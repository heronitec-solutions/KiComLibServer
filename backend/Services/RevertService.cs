using System.Data;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KiComLibBackend.Services
{
    public class RevertOutcome
    {
        public int StatusCode { get; init; }
        public int? CommitId { get; init; }
        public string? Error { get; init; }
        public List<ConflictItem>? Conflicts { get; init; }
        public List<InUseItem>? InUse { get; init; }

        public static RevertOutcome Ok(int commitId) => new() { StatusCode = 201, CommitId = commitId };
        public static RevertOutcome NotFound(string error) => new() { StatusCode = 404, Error = error };
        public static RevertOutcome BadRequest(string error) => new() { StatusCode = 400, Error = error };
        public static RevertOutcome Conflict(string error, List<ConflictItem>? conflicts = null) =>
            new() { StatusCode = 409, Error = error, Conflicts = conflicts };
        public static RevertOutcome StillInUse(List<InUseItem> inUse) =>
            new() { StatusCode = 409, Error = "still in use", InUse = inUse };
    }

    /// <summary>
    /// Appends a new commit that restores an older state. History is never rewritten.
    /// </summary>
    public class RevertService
    {
        private static readonly HashSet<string> BlobEntities = new(StringComparer.OrdinalIgnoreCase)
        {
            "symbol", "footprint", "model3d", "datasheet", "template"
        };

        private readonly ApplicationDbContext _db;
        private readonly CatalogRevisionService _catalog;
        private readonly ProjectRematchScheduler _rematch;

        public RevertService(ApplicationDbContext db, CatalogRevisionService catalog, ProjectRematchScheduler rematch)
        {
            _db = db;
            _catalog = catalog;
            _rematch = rematch;
        }

        public async Task<RevertOutcome> RevertCommitAsync(int commitId, RevertRequest? request, CancellationToken cancellationToken)
        {
            Commit? commit = await _db.Commits.FirstOrDefaultAsync(c => c.Id == commitId, cancellationToken);
            if (commit == null)
            {
                return RevertOutcome.NotFound("Commit not found.");
            }

            if (IsSystemCommit(commit))
            {
                return RevertOutcome.BadRequest("System import commits cannot be reverted. Restore a single revision from the element history instead.");
            }

            List<ChangeRef> changes = await LoadCommitChangesAsync(commitId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(request?.Entity) || request?.Id != null)
            {
                if (string.IsNullOrWhiteSpace(request?.Entity) || request?.Id == null)
                {
                    return RevertOutcome.BadRequest("entity and id are both required to revert a single change.");
                }

                string entity = request.Entity.ToLowerInvariant();
                changes = changes.Where(c => c.Entity == entity && c.EntityId == request.Id.Value).ToList();
                if (changes.Count == 0)
                {
                    return RevertOutcome.NotFound("That change is not part of this commit.");
                }
            }

            if (changes.Count == 0)
            {
                return RevertOutcome.BadRequest("This commit has no changes to revert.");
            }

            List<ConflictItem> superseded = new();
            foreach (ChangeRef change in changes)
            {
                if (!await IsHeadAsync(change, cancellationToken))
                {
                    superseded.Add(new ConflictItem
                    {
                        Entity = change.Entity,
                        Uuid = change.Uuid == Guid.Empty ? null : change.Uuid,
                        Reason = "superseded",
                        HeadRevisionId = await CurrentHeadRevisionIdAsync(change, cancellationToken)
                    });
                }
            }

            if (superseded.Count > 0)
            {
                return RevertOutcome.Conflict(
                    "Later changes exist. Revert those first, or restore a revision from the element history.",
                    superseded);
            }

            List<InUseItem> inUse = await FindUndoCreateBlocksAsync(changes, cancellationToken);
            if (inUse.Count > 0)
            {
                return RevertOutcome.StillInUse(inUse);
            }

            string author = string.IsNullOrWhiteSpace(request?.Author) ? "web-ui" : request.Author.Trim();
            string message = string.IsNullOrWhiteSpace(request?.Message)
                ? changes.Count == 1
                    ? $"Revert {changes[0].Entity} '{changes[0].Name}' from commit #{commitId}"
                    : $"Revert commit #{commitId}: {commit.Message}"
                : request.Message.Trim();

            return await RunAsync(message, author, "web-ui", async newCommitId =>
            {
                foreach (ChangeRef change in OrderInverses(changes))
                {
                    await ApplyInverseAsync(newCommitId, change, cancellationToken);
                }
            }, cancellationToken);
        }

        public async Task<RevertOutcome> RestoreAsync(string entity, int id, int revisionNo, RevertRequest? request, CancellationToken cancellationToken)
        {
            entity = entity.ToLowerInvariant();
            if (!BlobEntities.Contains(entity) && !CatalogKinds.IsCatalog(entity))
            {
                return RevertOutcome.NotFound($"Unknown entity '{entity}'.");
            }

            string author = string.IsNullOrWhiteSpace(request?.Author) ? "web-ui" : request.Author.Trim();

            if (revisionNo <= 0)
            {
                ChangeRef? head = await LoadHeadAsync(entity, id, cancellationToken);
                if (head == null)
                {
                    return RevertOutcome.NotFound("No revision to undo.");
                }

                if (head.RevisionNo != 1 || head.IsDeleted)
                {
                    return RevertOutcome.Conflict("Only the current create revision can be undone this way. Restore an older revision instead.");
                }

                List<InUseItem> blocked = await FindUndoCreateBlocksAsync(new[] { head }, cancellationToken);
                if (blocked.Count > 0)
                {
                    return RevertOutcome.StillInUse(blocked);
                }

                string undoMessage = string.IsNullOrWhiteSpace(request?.Message)
                    ? $"Revert create of {entity} '{head.Name}'"
                    : request.Message.Trim();
                return await RunAsync(undoMessage, author, "web-ui", async newCommitId =>
                {
                    await ApplyInverseAsync(newCommitId, head, cancellationToken);
                }, cancellationToken);
            }

            ChangeRef? source = await LoadRevisionAsync(entity, id, revisionNo, cancellationToken);
            if (source == null)
            {
                return RevertOutcome.NotFound("Revision not found.");
            }

            if (await IsHeadAsync(source, cancellationToken))
            {
                return RevertOutcome.BadRequest("This revision is already current.");
            }

            if (source.IsDeleted)
            {
                InUseItem? blocked = await InUseAsync(
                    source,
                    new HashSet<int>(),
                    new HashSet<int>(),
                    new HashSet<int>(),
                    new HashSet<Guid>(),
                    cancellationToken);
                if (blocked != null)
                {
                    return RevertOutcome.StillInUse(new List<InUseItem> { blocked });
                }
            }

            string message = string.IsNullOrWhiteSpace(request?.Message)
                ? $"Restore {entity} '{source.Name}' to revision {revisionNo}"
                : request.Message.Trim();

            return await RunAsync(message, author, "web-ui", async newCommitId =>
            {
                if (source.IsCatalog)
                {
                    await WriteCatalogStateAsync(newCommitId, source, source.IsDeleted, source.Snapshot, cancellationToken);
                }
                else
                {
                    await CopyBlobAsync(newCommitId, source.Entity, source.EntityId, source.RevisionNo, forceDeleted: false, cancellationToken);
                }
            }, cancellationToken);
        }

        private async Task<RevertOutcome> RunAsync(
            string message,
            string author,
            string client,
            Func<int, Task> apply,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                int? parentCommitId = await _db.Commits.OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(cancellationToken);
                Commit commit = new Commit
                {
                    Message = message,
                    Author = author,
                    Client = client,
                    ParentCommitId = parentCommitId
                };
                _db.Commits.Add(commit);
                await _db.SaveChangesAsync(cancellationToken);
                await apply(commit.Id!.Value);
                await transaction.CommitAsync(cancellationToken);
                try
                {
                    await _rematch.NotifyCatalogChangedAsync(cancellationToken);
                }
                catch (Exception)
                {
                    // The revert commit is stored. Rematch scheduling must not fail it.
                }

                return RevertOutcome.Ok(commit.Id!.Value);
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                return RevertOutcome.Conflict("A live record already uses this name.");
            }
            catch (Exception ex) when (IsForeignKeyViolation(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                return RevertOutcome.Conflict("A related record is missing, so this change cannot be restored.");
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                return RevertOutcome.Conflict("The library changed while reverting. Try again.", new List<ConflictItem>
                {
                    new ConflictItem { Reason = "serialization_failure" }
                });
            }
            catch
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                throw;
            }
        }

        private async Task ApplyInverseAsync(int commitId, ChangeRef change, CancellationToken cancellationToken)
        {
            if (change.RevisionNo == 1 && !change.IsDeleted)
            {
                await WriteDeletedAsync(commitId, change, cancellationToken);
                return;
            }

            if (change.IsCatalog)
            {
                string snapshot = change.IsDeleted
                    ? change.Snapshot
                    : (await LoadRevisionAsync(change.Entity, change.EntityId, change.RevisionNo - 1, cancellationToken))?.Snapshot ?? change.Snapshot;
                await WriteCatalogStateAsync(commitId, change, deleted: false, snapshot, cancellationToken);
                return;
            }

            await CopyBlobAsync(commitId, change.Entity, change.EntityId, change.RevisionNo - 1, forceDeleted: false, cancellationToken);
        }

        private async Task WriteDeletedAsync(int commitId, ChangeRef change, CancellationToken cancellationToken)
        {
            if (change.IsCatalog)
            {
                CatalogRevisionService.LiveRow? live = await _catalog.ReadLiveAsync(change.Entity, change.EntityId, cancellationToken);
                string snapshot = live?.Snapshot ?? change.Snapshot;
                string name = live?.Name ?? change.Name;
                Guid uuid = live?.Uuid ?? change.Uuid;
                await AddCatalogRevisionAsync(commitId, change.Entity, change.EntityId, uuid, name, snapshot, true, cancellationToken);
                await _catalog.DeleteRowAsync(change.Entity, change.EntityId, cancellationToken);
                return;
            }

            await CopyBlobAsync(commitId, change.Entity, change.EntityId, change.RevisionNo, forceDeleted: true, cancellationToken);
        }

        private async Task WriteCatalogStateAsync(
            int commitId,
            ChangeRef change,
            bool deleted,
            string snapshot,
            CancellationToken cancellationToken)
        {
            if (deleted)
            {
                await WriteDeletedAsync(commitId, change, cancellationToken);
                return;
            }

            await _catalog.ApplySnapshotAsync(change.Entity, change.EntityId, change.Uuid, snapshot, cancellationToken);
            CatalogRevisionService.LiveRow? live = await _catalog.ReadLiveAsync(change.Entity, change.EntityId, cancellationToken);
            if (live == null)
            {
                throw new InvalidOperationException($"Restored {change.Entity} {change.EntityId} is missing.");
            }

            await AddCatalogRevisionAsync(commitId, change.Entity, change.EntityId, live.Uuid, live.Name, live.Snapshot, false, cancellationToken);
        }

        private async Task AddCatalogRevisionAsync(
            int commitId,
            string entity,
            int id,
            Guid uuid,
            string name,
            string snapshot,
            bool isDeleted,
            CancellationToken cancellationToken)
        {
            int nextNo = (await _db.CatalogRevisions
                .Where(r => r.Entity == entity && r.EntityId == id)
                .MaxAsync(r => (int?)r.RevisionNo, cancellationToken) ?? 0) + 1;

            _db.CatalogRevisions.Add(new CatalogRevision
            {
                Entity = entity,
                EntityId = id,
                EntityUuid = uuid,
                CommitId = commitId,
                RevisionNo = nextNo,
                IsDeleted = isDeleted,
                Name = name,
                Snapshot = snapshot
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task CopyBlobAsync(int commitId, string entity, int id, int sourceRevisionNo, bool forceDeleted, CancellationToken cancellationToken)
        {
            switch (entity)
            {
                case "symbol":
                    await CopySymbolAsync(commitId, id, sourceRevisionNo, forceDeleted, cancellationToken);
                    break;
                case "footprint":
                    await CopyFootprintAsync(commitId, id, sourceRevisionNo, forceDeleted, cancellationToken);
                    break;
                case "model3d":
                    await CopyFileAsync(commitId, "model3d", id, sourceRevisionNo, forceDeleted, cancellationToken);
                    break;
                case "datasheet":
                    await CopyFileAsync(commitId, "datasheet", id, sourceRevisionNo, forceDeleted, cancellationToken);
                    break;
                case "template":
                    await CopyFileAsync(commitId, "template", id, sourceRevisionNo, forceDeleted, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown entity '{entity}'");
            }
        }

        private async Task CopySymbolAsync(int commitId, int id, int sourceRevisionNo, bool forceDeleted, CancellationToken cancellationToken)
        {
            Symbol head = await _db.Symbol.FirstAsync(s => s.Id == id, cancellationToken);
            SymbolRevision source = await _db.SymbolRevisions.FirstAsync(
                r => r.SymbolId == id && r.RevisionNo == sourceRevisionNo, cancellationToken);
            int nextNo = await _db.SymbolRevisions.Where(r => r.SymbolId == id).MaxAsync(r => r.RevisionNo, cancellationToken) + 1;
            bool deleted = forceDeleted || source.IsDeleted;
            SymbolRevision next = new SymbolRevision
            {
                SymbolId = id,
                CommitId = commitId,
                RevisionNo = nextNo,
                IsDeleted = deleted,
                LibraryFileId = source.LibraryFileId,
                Name = source.Name,
                BlobHash = deleted ? null : source.BlobHash,
                PreviewBlobHash = deleted ? null : source.PreviewBlobHash,
                Reference = source.Reference,
                InBom = source.InBom,
                OnBoard = source.OnBoard,
                PinNumbersHidden = source.PinNumbersHidden,
                PinNamesHidden = source.PinNamesHidden,
                PinNamesOffset = source.PinNamesOffset,
                Description = source.Description
            };
            _db.SymbolRevisions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            head.HeadRevisionId = next.Id;
            head.Name = source.Name;
            head.LibraryFileId = source.LibraryFileId;
            head.DeletedAt = deleted ? DateTime.UtcNow : null;
            head.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task CopyFootprintAsync(int commitId, int id, int sourceRevisionNo, bool forceDeleted, CancellationToken cancellationToken)
        {
            Footprint head = await _db.Footprint.FirstAsync(s => s.Id == id, cancellationToken);
            FootprintRevision source = await _db.FootprintRevisions.FirstAsync(
                r => r.FootprintId == id && r.RevisionNo == sourceRevisionNo, cancellationToken);
            int nextNo = await _db.FootprintRevisions.Where(r => r.FootprintId == id).MaxAsync(r => r.RevisionNo, cancellationToken) + 1;
            bool deleted = forceDeleted || source.IsDeleted;
            FootprintRevision next = new FootprintRevision
            {
                FootprintId = id,
                CommitId = commitId,
                RevisionNo = nextNo,
                IsDeleted = deleted,
                LibraryFileId = source.LibraryFileId,
                Name = source.Name,
                BlobHash = deleted ? null : source.BlobHash,
                PreviewBlobHash = deleted ? null : source.PreviewBlobHash,
                Type = source.Type,
                BoardOnly = source.BoardOnly,
                ExcludeFromPosFiles = source.ExcludeFromPosFiles,
                ExcludeFromBom = source.ExcludeFromBom,
                Height = source.Height,
                Layer = source.Layer,
                Description = source.Description,
                Tags = source.Tags,
                Property = source.Property
            };
            _db.FootprintRevisions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            if (!deleted)
            {
                List<FootprintRevisionModel3D> models = await _db.FootprintRevisionModel3Ds
                    .Where(m => m.FootprintRevisionId == source.Id)
                    .ToListAsync(cancellationToken);
                foreach (FootprintRevisionModel3D model in models)
                {
                    _db.FootprintRevisionModel3Ds.Add(new FootprintRevisionModel3D
                    {
                        FootprintRevisionId = next.Id!.Value,
                        Position = model.Position,
                        Model3dId = model.Model3dId,
                        OffsetX = model.OffsetX,
                        OffsetY = model.OffsetY,
                        OffsetZ = model.OffsetZ,
                        ScaleX = model.ScaleX,
                        ScaleY = model.ScaleY,
                        ScaleZ = model.ScaleZ,
                        RotateX = model.RotateX,
                        RotateY = model.RotateY,
                        RotateZ = model.RotateZ,
                        Opacity = model.Opacity,
                        Hide = model.Hide
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

            head.HeadRevisionId = next.Id;
            head.Name = source.Name;
            head.LibraryFileId = source.LibraryFileId;
            head.DeletedAt = deleted ? DateTime.UtcNow : null;
            head.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task CopyFileAsync(int commitId, string entity, int id, int sourceRevisionNo, bool forceDeleted, CancellationToken cancellationToken)
        {
            if (entity == "model3d")
            {
                Model3D head = await _db.Model3D.FirstAsync(s => s.Id == id, cancellationToken);
                Model3DRevision source = await _db.Model3DRevisions.FirstAsync(
                    r => r.Model3dId == id && r.RevisionNo == sourceRevisionNo, cancellationToken);
                int nextNo = await _db.Model3DRevisions.Where(r => r.Model3dId == id).MaxAsync(r => r.RevisionNo, cancellationToken) + 1;
                bool deleted = forceDeleted || source.IsDeleted;
                Model3DRevision next = new Model3DRevision
                {
                    Model3dId = id,
                    CommitId = commitId,
                    RevisionNo = nextNo,
                    IsDeleted = deleted,
                    Directory = source.Directory,
                    Filename = source.Filename,
                    Comment = source.Comment,
                    BlobHash = deleted ? null : source.BlobHash,
                    Size = deleted ? null : source.Size
                };
                _db.Model3DRevisions.Add(next);
                await _db.SaveChangesAsync(cancellationToken);
                head.HeadRevisionId = next.Id;
                head.Directory = source.Directory;
                head.Filename = source.Filename;
                head.DeletedAt = deleted ? DateTime.UtcNow : null;
                head.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (entity == "datasheet")
            {
                Datasheet head = await _db.Datasheet.FirstAsync(s => s.Id == id, cancellationToken);
                DatasheetRevision source = await _db.DatasheetRevisions.FirstAsync(
                    r => r.DatasheetId == id && r.RevisionNo == sourceRevisionNo, cancellationToken);
                int nextNo = await _db.DatasheetRevisions.Where(r => r.DatasheetId == id).MaxAsync(r => r.RevisionNo, cancellationToken) + 1;
                bool deleted = forceDeleted || source.IsDeleted;
                DatasheetRevision next = new DatasheetRevision
                {
                    DatasheetId = id,
                    CommitId = commitId,
                    RevisionNo = nextNo,
                    IsDeleted = deleted,
                    Directory = source.Directory,
                    Filename = source.Filename,
                    Comment = source.Comment,
                    BlobHash = deleted ? null : source.BlobHash,
                    Size = deleted ? null : source.Size
                };
                _db.DatasheetRevisions.Add(next);
                await _db.SaveChangesAsync(cancellationToken);
                head.HeadRevisionId = next.Id;
                head.Directory = source.Directory;
                head.Filename = source.Filename;
                head.DeletedAt = deleted ? DateTime.UtcNow : null;
                head.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            Template template = await _db.Template.FirstAsync(s => s.Id == id, cancellationToken);
            TemplateRevision templateSource = await _db.TemplateRevisions.FirstAsync(
                r => r.TemplateId == id && r.RevisionNo == sourceRevisionNo, cancellationToken);
            int templateNext = await _db.TemplateRevisions.Where(r => r.TemplateId == id).MaxAsync(r => r.RevisionNo, cancellationToken) + 1;
            bool templateDeleted = forceDeleted || templateSource.IsDeleted;
            TemplateRevision templateNextRev = new TemplateRevision
            {
                TemplateId = id,
                CommitId = commitId,
                RevisionNo = templateNext,
                IsDeleted = templateDeleted,
                Directory = templateSource.Directory,
                Filename = templateSource.Filename,
                Comment = templateSource.Comment,
                BlobHash = templateDeleted ? null : templateSource.BlobHash,
                Size = templateDeleted ? null : templateSource.Size
            };
            _db.TemplateRevisions.Add(templateNextRev);
            await _db.SaveChangesAsync(cancellationToken);
            template.HeadRevisionId = templateNextRev.Id;
            template.Directory = templateSource.Directory;
            template.Filename = templateSource.Filename;
            template.DeletedAt = templateDeleted ? DateTime.UtcNow : null;
            template.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task<List<InUseItem>> FindUndoCreateBlocksAsync(IReadOnlyList<ChangeRef> changes, CancellationToken cancellationToken)
        {
            HashSet<int> deletedParts = Ids(changes, "part");
            HashSet<int> deletedPartNumbers = Ids(changes, "partnumber");
            HashSet<int> deletedOrders = Ids(changes, "ordernumber");
            HashSet<Guid> deletedFootprints = changes
                .Where(c => c.IsUndoCreate && c.Entity == "footprint")
                .Select(c => c.Uuid)
                .ToHashSet();

            List<InUseItem> blocked = new();
            foreach (ChangeRef change in changes.Where(c => c.IsUndoCreate))
            {
                InUseItem? item = await InUseAsync(change, deletedParts, deletedPartNumbers, deletedOrders, deletedFootprints, cancellationToken);
                if (item != null)
                {
                    blocked.Add(item);
                }
            }

            return blocked;
        }

        private async Task<InUseItem?> InUseAsync(
            ChangeRef change,
            HashSet<int> deletedParts,
            HashSet<int> deletedPartNumbers,
            HashSet<int> deletedOrders,
            HashSet<Guid> deletedFootprints,
            CancellationToken cancellationToken)
        {
            List<InUseReference> usedBy = change.Entity switch
            {
                "symbol" => await _db.Part
                    .Where(p => p.SymbolId == change.EntityId && p.Id != null && !deletedParts.Contains(p.Id.Value))
                    .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                    .ToListAsync(cancellationToken),
                "footprint" => await _db.Part
                    .Where(p => p.FootprintId == change.EntityId && p.Id != null && !deletedParts.Contains(p.Id.Value))
                    .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                    .ToListAsync(cancellationToken),
                "datasheet" => (await PartDatasheetUsage.FindPartsAsync(_db, change.EntityId, cancellationToken))
                    .Where(p => p.Id != null && !deletedParts.Contains(p.Id.Value))
                    .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                    .ToList(),
                "part" => await _db.PartNumber
                    .Where(p => p.PartId == change.EntityId && p.Id != null && !deletedPartNumbers.Contains(p.Id.Value))
                    .Select(p => new InUseReference { Entity = "partnumber", Id = p.Id, Name = p.ManufacturerPartNumber ?? "" })
                    .ToListAsync(cancellationToken),
                "manufacturer" => await _db.PartNumber
                    .Where(p => p.ManufacturerId == change.EntityId && p.Id != null && !deletedPartNumbers.Contains(p.Id.Value))
                    .Select(p => new InUseReference { Entity = "partnumber", Id = p.Id, Name = p.ManufacturerPartNumber ?? "" })
                    .ToListAsync(cancellationToken),
                "partnumber" => await _db.OrderNumber
                    .Where(o => o.PartNumberId == change.EntityId && o.Id != null && !deletedOrders.Contains(o.Id.Value))
                    .Select(o => new InUseReference { Entity = "ordernumber", Id = o.Id, Name = o.OrderNumberValue })
                    .ToListAsync(cancellationToken),
                "distributor" => await _db.OrderNumber
                    .Where(o => o.DistributorId == change.EntityId && o.Id != null && !deletedOrders.Contains(o.Id.Value))
                    .Select(o => new InUseReference { Entity = "ordernumber", Id = o.Id, Name = o.OrderNumberValue })
                    .ToListAsync(cancellationToken),
                "model3d" => await ModelInUseAsync(change, deletedFootprints, cancellationToken),
                _ => new List<InUseReference>()
            };

            if (usedBy.Count == 0)
            {
                return null;
            }

            return new InUseItem
            {
                Entity = change.Entity,
                Uuid = change.Uuid,
                Name = change.Name,
                UsedBy = usedBy
            };
        }

        private async Task<List<InUseReference>> ModelInUseAsync(ChangeRef change, HashSet<Guid> deletedFootprints, CancellationToken cancellationToken)
        {
            var footprints = await _db.Footprint
                .Where(fp => fp.DeletedAt == null && fp.HeadRevisionId != null)
                .Where(fp => _db.FootprintRevisionModel3Ds.Any(link =>
                    link.FootprintRevisionId == fp.HeadRevisionId && link.Model3dId == change.EntityId))
                .Select(fp => new { fp.Id, fp.Uuid, fp.Name })
                .ToListAsync(cancellationToken);

            return footprints
                .Where(fp => fp.Uuid == null || !deletedFootprints.Contains(fp.Uuid.Value))
                .Select(fp => new InUseReference { Entity = "footprint", Id = fp.Id, Name = fp.Name })
                .ToList();
        }

        private static HashSet<int> Ids(IReadOnlyList<ChangeRef> changes, string entity) =>
            changes.Where(c => c.IsUndoCreate && c.Entity == entity).Select(c => c.EntityId).ToHashSet();

        private async Task<bool> IsHeadAsync(ChangeRef change, CancellationToken cancellationToken)
        {
            int? headId = await CurrentHeadRevisionIdAsync(change, cancellationToken);
            return headId == change.RevisionId;
        }

        private async Task<int?> CurrentHeadRevisionIdAsync(ChangeRef change, CancellationToken cancellationToken)
        {
            if (change.IsCatalog)
            {
                return await _db.CatalogRevisions
                    .Where(r => r.Entity == change.Entity && r.EntityId == change.EntityId)
                    .OrderByDescending(r => r.RevisionNo)
                    .Select(r => (int?)r.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            return change.Entity switch
            {
                "symbol" => await _db.Symbol.Where(s => s.Id == change.EntityId).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "footprint" => await _db.Footprint.Where(s => s.Id == change.EntityId).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "model3d" => await _db.Model3D.Where(s => s.Id == change.EntityId).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "datasheet" => await _db.Datasheet.Where(s => s.Id == change.EntityId).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "template" => await _db.Template.Where(s => s.Id == change.EntityId).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        private async Task<List<ChangeRef>> LoadCommitChangesAsync(int commitId, CancellationToken cancellationToken)
        {
            List<ChangeRef> changes = new();
            changes.AddRange(await _db.SymbolRevisions.Where(r => r.CommitId == commitId)
                .Join(_db.Symbol, r => r.SymbolId, s => s.Id, (r, s) => new ChangeRef
                {
                    Entity = "symbol",
                    EntityId = r.SymbolId,
                    Uuid = s.Uuid ?? Guid.Empty,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Name
                }).ToListAsync(cancellationToken));
            changes.AddRange(await _db.FootprintRevisions.Where(r => r.CommitId == commitId)
                .Join(_db.Footprint, r => r.FootprintId, s => s.Id, (r, s) => new ChangeRef
                {
                    Entity = "footprint",
                    EntityId = r.FootprintId,
                    Uuid = s.Uuid ?? Guid.Empty,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Name
                }).ToListAsync(cancellationToken));
            changes.AddRange(await _db.Model3DRevisions.Where(r => r.CommitId == commitId)
                .Join(_db.Model3D, r => r.Model3dId, s => s.Id, (r, s) => new ChangeRef
                {
                    Entity = "model3d",
                    EntityId = r.Model3dId,
                    Uuid = s.Uuid ?? Guid.Empty,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Filename
                }).ToListAsync(cancellationToken));
            changes.AddRange(await _db.DatasheetRevisions.Where(r => r.CommitId == commitId)
                .Join(_db.Datasheet, r => r.DatasheetId, s => s.Id, (r, s) => new ChangeRef
                {
                    Entity = "datasheet",
                    EntityId = r.DatasheetId,
                    Uuid = s.Uuid ?? Guid.Empty,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Filename
                }).ToListAsync(cancellationToken));
            changes.AddRange(await _db.TemplateRevisions.Where(r => r.CommitId == commitId)
                .Join(_db.Template, r => r.TemplateId, s => s.Id, (r, s) => new ChangeRef
                {
                    Entity = "template",
                    EntityId = r.TemplateId,
                    Uuid = s.Uuid ?? Guid.Empty,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Filename
                }).ToListAsync(cancellationToken));
            changes.AddRange(await _db.CatalogRevisions.Where(r => r.CommitId == commitId)
                .Select(r => new ChangeRef
                {
                    Entity = r.Entity,
                    EntityId = r.EntityId,
                    Uuid = r.EntityUuid,
                    RevisionId = r.Id ?? 0,
                    RevisionNo = r.RevisionNo,
                    IsDeleted = r.IsDeleted,
                    Name = r.Name,
                    IsCatalog = true,
                    Snapshot = r.Snapshot
                }).ToListAsync(cancellationToken));
            return changes;
        }

        private async Task<ChangeRef?> LoadHeadAsync(string entity, int id, CancellationToken cancellationToken)
        {
            if (CatalogKinds.IsCatalog(entity))
            {
                CatalogRevision? rev = await _db.CatalogRevisions
                    .Where(r => r.Entity == entity && r.EntityId == id)
                    .OrderByDescending(r => r.RevisionNo)
                    .FirstOrDefaultAsync(cancellationToken);
                return rev == null ? null : ToCatalogRef(rev);
            }

            return entity switch
            {
                "symbol" => await LoadSymbolHeadAsync(id, cancellationToken),
                "footprint" => await LoadFootprintHeadAsync(id, cancellationToken),
                "model3d" => await LoadModelHeadAsync(id, cancellationToken),
                "datasheet" => await LoadDatasheetHeadAsync(id, cancellationToken),
                "template" => await LoadTemplateHeadAsync(id, cancellationToken),
                _ => null
            };
        }

        private async Task<ChangeRef?> LoadRevisionAsync(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            if (revisionNo < 1)
            {
                return null;
            }

            if (CatalogKinds.IsCatalog(entity))
            {
                CatalogRevision? rev = await _db.CatalogRevisions.FirstOrDefaultAsync(
                    r => r.Entity == entity && r.EntityId == id && r.RevisionNo == revisionNo,
                    cancellationToken);
                return rev == null ? null : ToCatalogRef(rev);
            }

            return entity switch
            {
                "symbol" => await (
                    from r in _db.SymbolRevisions
                    join s in _db.Symbol on r.SymbolId equals s.Id
                    where r.SymbolId == id && r.RevisionNo == revisionNo
                    select new ChangeRef
                    {
                        Entity = "symbol",
                        EntityId = id,
                        Uuid = s.Uuid ?? Guid.Empty,
                        RevisionId = r.Id ?? 0,
                        RevisionNo = r.RevisionNo,
                        IsDeleted = r.IsDeleted,
                        Name = r.Name
                    }).FirstOrDefaultAsync(cancellationToken),
                "footprint" => await (
                    from r in _db.FootprintRevisions
                    join s in _db.Footprint on r.FootprintId equals s.Id
                    where r.FootprintId == id && r.RevisionNo == revisionNo
                    select new ChangeRef
                    {
                        Entity = "footprint",
                        EntityId = id,
                        Uuid = s.Uuid ?? Guid.Empty,
                        RevisionId = r.Id ?? 0,
                        RevisionNo = r.RevisionNo,
                        IsDeleted = r.IsDeleted,
                        Name = r.Name
                    }).FirstOrDefaultAsync(cancellationToken),
                "model3d" => await (
                    from r in _db.Model3DRevisions
                    join s in _db.Model3D on r.Model3dId equals s.Id
                    where r.Model3dId == id && r.RevisionNo == revisionNo
                    select new ChangeRef
                    {
                        Entity = "model3d",
                        EntityId = id,
                        Uuid = s.Uuid ?? Guid.Empty,
                        RevisionId = r.Id ?? 0,
                        RevisionNo = r.RevisionNo,
                        IsDeleted = r.IsDeleted,
                        Name = r.Filename
                    }).FirstOrDefaultAsync(cancellationToken),
                "datasheet" => await (
                    from r in _db.DatasheetRevisions
                    join s in _db.Datasheet on r.DatasheetId equals s.Id
                    where r.DatasheetId == id && r.RevisionNo == revisionNo
                    select new ChangeRef
                    {
                        Entity = "datasheet",
                        EntityId = id,
                        Uuid = s.Uuid ?? Guid.Empty,
                        RevisionId = r.Id ?? 0,
                        RevisionNo = r.RevisionNo,
                        IsDeleted = r.IsDeleted,
                        Name = r.Filename
                    }).FirstOrDefaultAsync(cancellationToken),
                "template" => await (
                    from r in _db.TemplateRevisions
                    join s in _db.Template on r.TemplateId equals s.Id
                    where r.TemplateId == id && r.RevisionNo == revisionNo
                    select new ChangeRef
                    {
                        Entity = "template",
                        EntityId = id,
                        Uuid = s.Uuid ?? Guid.Empty,
                        RevisionId = r.Id ?? 0,
                        RevisionNo = r.RevisionNo,
                        IsDeleted = r.IsDeleted,
                        Name = r.Filename
                    }).FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        private async Task<ChangeRef?> LoadSymbolHeadAsync(int id, CancellationToken cancellationToken)
        {
            Symbol? head = await _db.Symbol.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (head?.HeadRevisionId == null) return null;
            SymbolRevision? rev = await _db.SymbolRevisions.FirstOrDefaultAsync(r => r.Id == head.HeadRevisionId, cancellationToken);
            return rev == null ? null : new ChangeRef
            {
                Entity = "symbol",
                EntityId = id,
                Uuid = head.Uuid ?? Guid.Empty,
                RevisionId = rev.Id ?? 0,
                RevisionNo = rev.RevisionNo,
                IsDeleted = rev.IsDeleted,
                Name = rev.Name
            };
        }

        private async Task<ChangeRef?> LoadFootprintHeadAsync(int id, CancellationToken cancellationToken)
        {
            Footprint? head = await _db.Footprint.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (head?.HeadRevisionId == null) return null;
            FootprintRevision? rev = await _db.FootprintRevisions.FirstOrDefaultAsync(r => r.Id == head.HeadRevisionId, cancellationToken);
            return rev == null ? null : new ChangeRef
            {
                Entity = "footprint",
                EntityId = id,
                Uuid = head.Uuid ?? Guid.Empty,
                RevisionId = rev.Id ?? 0,
                RevisionNo = rev.RevisionNo,
                IsDeleted = rev.IsDeleted,
                Name = rev.Name
            };
        }

        private async Task<ChangeRef?> LoadModelHeadAsync(int id, CancellationToken cancellationToken)
        {
            Model3D? head = await _db.Model3D.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (head?.HeadRevisionId == null) return null;
            Model3DRevision? rev = await _db.Model3DRevisions.FirstOrDefaultAsync(r => r.Id == head.HeadRevisionId, cancellationToken);
            return rev == null ? null : new ChangeRef
            {
                Entity = "model3d",
                EntityId = id,
                Uuid = head.Uuid ?? Guid.Empty,
                RevisionId = rev.Id ?? 0,
                RevisionNo = rev.RevisionNo,
                IsDeleted = rev.IsDeleted,
                Name = rev.Filename
            };
        }

        private async Task<ChangeRef?> LoadDatasheetHeadAsync(int id, CancellationToken cancellationToken)
        {
            Datasheet? head = await _db.Datasheet.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (head?.HeadRevisionId == null) return null;
            DatasheetRevision? rev = await _db.DatasheetRevisions.FirstOrDefaultAsync(r => r.Id == head.HeadRevisionId, cancellationToken);
            return rev == null ? null : new ChangeRef
            {
                Entity = "datasheet",
                EntityId = id,
                Uuid = head.Uuid ?? Guid.Empty,
                RevisionId = rev.Id ?? 0,
                RevisionNo = rev.RevisionNo,
                IsDeleted = rev.IsDeleted,
                Name = rev.Filename
            };
        }

        private async Task<ChangeRef?> LoadTemplateHeadAsync(int id, CancellationToken cancellationToken)
        {
            Template? head = await _db.Template.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (head?.HeadRevisionId == null) return null;
            TemplateRevision? rev = await _db.TemplateRevisions.FirstOrDefaultAsync(r => r.Id == head.HeadRevisionId, cancellationToken);
            return rev == null ? null : new ChangeRef
            {
                Entity = "template",
                EntityId = id,
                Uuid = head.Uuid ?? Guid.Empty,
                RevisionId = rev.Id ?? 0,
                RevisionNo = rev.RevisionNo,
                IsDeleted = rev.IsDeleted,
                Name = rev.Filename
            };
        }

        private static ChangeRef ToCatalogRef(CatalogRevision rev) => new()
        {
            Entity = rev.Entity,
            EntityId = rev.EntityId,
            Uuid = rev.EntityUuid,
            RevisionId = rev.Id ?? 0,
            RevisionNo = rev.RevisionNo,
            IsDeleted = rev.IsDeleted,
            Name = rev.Name,
            IsCatalog = true,
            Snapshot = rev.Snapshot
        };

        private static List<ChangeRef> OrderInverses(IEnumerable<ChangeRef> changes)
        {
            return changes
                .OrderBy(c => c.IsUndoCreate ? 1 : 0)
                .ThenBy(c => c.IsUndoCreate ? -Rank(c.Entity) : Rank(c.Entity))
                .ToList();
        }

        private static int Rank(string entity) => entity switch
        {
            "manufacturer" or "distributor" or "model3d" or "datasheet" or "template" or "symbol" => 0,
            "part" or "footprint" => 1,
            "partnumber" => 2,
            "ordernumber" => 3,
            _ => 4
        };

        private static bool IsSystemCommit(Commit commit) =>
            string.Equals(commit.Author, "system", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commit.Client, "init", StringComparison.OrdinalIgnoreCase);

        private static bool IsUniqueViolation(Exception ex) => HasSqlState(ex, PostgresErrorCodes.UniqueViolation);

        private static bool IsForeignKeyViolation(Exception ex) => HasSqlState(ex, PostgresErrorCodes.ForeignKeyViolation);

        private static bool IsSerializationFailure(Exception ex) => HasSqlState(ex, PostgresErrorCodes.SerializationFailure);

        private static bool HasSqlState(Exception ex, string sqlState)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is PostgresException pg && pg.SqlState == sqlState)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ChangeRef
        {
            public string Entity { get; init; } = "";
            public int EntityId { get; init; }
            public Guid Uuid { get; init; }
            public int RevisionId { get; init; }
            public int RevisionNo { get; init; }
            public bool IsDeleted { get; init; }
            public string Name { get; init; } = "";
            public bool IsCatalog { get; init; }
            public string Snapshot { get; init; } = "{}";
            public bool IsUndoCreate => RevisionNo == 1 && !IsDeleted;
        }
    }
}
