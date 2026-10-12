using System.Data;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KiComLibBackend.Services
{
    public class CommitService
    {
        private readonly ApplicationDbContext _db;
        private readonly IBlobStore _blobStore;
        private readonly ProjectRematchScheduler _rematch;

        public CommitService(ApplicationDbContext db, IBlobStore blobStore, ProjectRematchScheduler rematch)
        {
            _db = db;
            _blobStore = blobStore;
            _rematch = rematch;
        }

        public async Task<CommitOutcome> ApplyAsync(CommitRequest request, string? accountAuthor, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return CommitOutcome.BadRequest("message is required");
            }

            string author = !string.IsNullOrWhiteSpace(accountAuthor)
                ? accountAuthor.Trim()
                : (request.Author ?? "").Trim();
            if (string.IsNullOrWhiteSpace(author))
            {
                return CommitOutcome.BadRequest("author is required");
            }

            List<CommitChange> ordered = OrderChanges(request.Changes);

            // Pre-resolve model3d UUIDs created in this commit
            Dictionary<Guid, int> uuidToModel3dId = new();

            List<string> missingBlobs = new();
            List<Guid> unknownModels = new();

            // Collect referenced blobs / models for validation (pass 1 – read only, outside txn)
            foreach (CommitChange change in ordered)
            {
                string op = change.Op.ToLowerInvariant();
                string entity = change.Entity.ToLowerInvariant();

                if (op is "create" or "update")
                {
                    CollectBlob(change.BlobHash, missingBlobs);
                    CollectBlob(change.PreviewBlobHash, missingBlobs);
                }

                if (entity == "footprint" && change.Models != null)
                {
                    foreach (FootprintModelChange model in change.Models)
                    {
                        if (!model.Model3dUuid.HasValue)
                        {
                            continue;
                        }

                        Guid mid = model.Model3dUuid.Value;
                        bool createdHere = ordered.Any(c =>
                            c.Entity.Equals("model3d", StringComparison.OrdinalIgnoreCase)
                            && c.Op.Equals("create", StringComparison.OrdinalIgnoreCase)
                            && c.Uuid == mid);
                        if (createdHere)
                        {
                            continue;
                        }

                        bool exists = await _db.Model3D.AnyAsync(m => m.Uuid == mid && m.DeletedAt == null, cancellationToken);
                        if (!exists)
                        {
                            unknownModels.Add(mid);
                        }
                    }
                }
            }

            if (missingBlobs.Count > 0 || unknownModels.Count > 0)
            {
                List<string> reallyMissing = (await _blobStore.FindMissingAsync(missingBlobs, cancellationToken)).ToList();
                if (reallyMissing.Count > 0 || unknownModels.Count > 0)
                {
                    return CommitOutcome.Missing(reallyMissing, unknownModels.Distinct().ToList());
                }
            }

            // UUID required checks (cheap, before txn)
            foreach (CommitChange change in ordered)
            {
                string op = change.Op.ToLowerInvariant();
                if (op is "update" or "rename" or "delete")
                {
                    if (!change.Uuid.HasValue)
                    {
                        return CommitOutcome.BadRequest($"uuid is required for {op} on {change.Entity}");
                    }
                }
            }

            // Conflict / exists checks + writes run inside a Serializable transaction so two
            // concurrent commits against the same head cannot both succeed (TOCTOU-safe).
            await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            try
            {
                List<ConflictItem> conflicts = new();
                foreach (CommitChange change in ordered)
                {
                    string op = change.Op.ToLowerInvariant();
                    string entity = change.Entity.ToLowerInvariant();

                    if (op is "update" or "rename" or "delete")
                    {
                        ConflictItem? conflict = await CheckExpectedHashAsync(entity, change.Uuid!.Value, change.ExpectedHash, cancellationToken);
                        if (conflict != null)
                        {
                            conflicts.Add(conflict);
                        }
                    }

                    if (op == "create")
                    {
                        ConflictItem? exists = await CheckCreateExistsAsync(entity, change, cancellationToken);
                        if (exists != null)
                        {
                            conflicts.Add(exists);
                        }
                    }
                }

                if (conflicts.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CommitOutcome.Conflict(conflicts);
                }

                List<InUseItem> inUse = await FindInUseDeletesAsync(ordered, cancellationToken);
                if (inUse.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CommitOutcome.StillInUse(inUse);
                }

                int? parentCommitId = await _db.Commits.OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(cancellationToken);

                Commit commit = new Commit
                {
                    Message = request.Message,
                    Author = author,
                    Client = request.Client,
                    ParentCommitId = parentCommitId
                };
                _db.Commits.Add(commit);
                await _db.SaveChangesAsync(cancellationToken);

                List<CommitResultItem> results = new();

                foreach (CommitChange change in ordered)
                {
                    string entity = change.Entity.ToLowerInvariant();
                    CommitResultItem result = entity switch
                    {
                        "model3d" => await ApplyModel3DAsync(commit.Id!.Value, change, uuidToModel3dId, cancellationToken),
                        "datasheet" => await ApplyDatasheetAsync(commit.Id!.Value, change, cancellationToken),
                        "template" => await ApplyTemplateAsync(commit.Id!.Value, change, cancellationToken),
                        "symbol" => await ApplySymbolAsync(commit.Id!.Value, change, cancellationToken),
                        "footprint" => await ApplyFootprintAsync(commit.Id!.Value, change, uuidToModel3dId, cancellationToken),
                        _ => throw new InvalidOperationException($"Unknown entity '{change.Entity}'")
                    };
                    results.Add(result);
                }

                await transaction.CommitAsync(cancellationToken);
                await NotifyRematchAsync(cancellationToken);
                return CommitOutcome.Ok(new CommitResponse { CommitId = commit.Id!.Value, Results = results });
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                return CommitOutcome.Conflict(new List<ConflictItem>
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

        private async Task NotifyRematchAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _rematch.NotifyCatalogChangedAsync(cancellationToken);
            }
            catch (Exception)
            {
                // The catalog commit is already stored. Scheduling a rematch must not fail it.
            }
        }

        private static bool IsSerializationFailure(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is PostgresException pg && pg.SqlState == PostgresErrorCodes.SerializationFailure)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<CommitChange> OrderChanges(IEnumerable<CommitChange> changes)
        {
            int Rank(string entity) => entity.ToLowerInvariant() switch
            {
                "model3d" => 0,
                "datasheet" => 1,
                "template" => 2,
                "symbol" => 3,
                "footprint" => 4,
                _ => 5
            };

            return changes.OrderBy(c => Rank(c.Entity)).ToList();
        }

        private static void CollectBlob(string? hash, List<string> missingBlobs)
        {
            if (!string.IsNullOrWhiteSpace(hash))
            {
                missingBlobs.Add(DbBlobStore.NormalizeHash(hash));
            }
        }

        private async Task<List<InUseItem>> FindInUseDeletesAsync(List<CommitChange> changes, CancellationToken cancellationToken)
        {
            HashSet<Guid> deletedFootprints = changes
                .Where(c => Eq(c.Entity, "footprint") && Eq(c.Op, "delete") && c.Uuid.HasValue)
                .Select(c => c.Uuid!.Value)
                .ToHashSet();

            // Footprint updates that replace the model list drop references that are not in the new list.
            Dictionary<Guid, HashSet<Guid>> footprintModelsAfter = new();
            foreach (CommitChange change in changes)
            {
                if (!Eq(change.Entity, "footprint") || change.Models == null || !change.Uuid.HasValue)
                {
                    continue;
                }

                if (!Eq(change.Op, "update") && !Eq(change.Op, "rename"))
                {
                    continue;
                }

                footprintModelsAfter[change.Uuid.Value] = change.Models
                    .Where(m => m.Model3dUuid.HasValue)
                    .Select(m => m.Model3dUuid!.Value)
                    .ToHashSet();
            }

            List<InUseItem> blocked = new();
            foreach (CommitChange change in changes.Where(c => Eq(c.Op, "delete") && c.Uuid.HasValue))
            {
                string entity = change.Entity.ToLowerInvariant();
                InUseItem? item = entity switch
                {
                    "symbol" => await SymbolInUseAsync(change.Uuid!.Value, cancellationToken),
                    "footprint" => await FootprintInUseAsync(change.Uuid!.Value, cancellationToken),
                    "datasheet" => await DatasheetInUseAsync(change.Uuid!.Value, cancellationToken),
                    "model3d" => await Model3DInUseAsync(change.Uuid!.Value, deletedFootprints, footprintModelsAfter, cancellationToken),
                    _ => null
                };
                if (item != null)
                {
                    blocked.Add(item);
                }
            }

            return blocked;
        }

        private async Task<InUseItem?> SymbolInUseAsync(Guid uuid, CancellationToken cancellationToken)
        {
            Symbol? symbol = await _db.Symbol.FirstOrDefaultAsync(s => s.Uuid == uuid && s.DeletedAt == null, cancellationToken);
            if (symbol?.Id == null)
            {
                return null;
            }

            List<InUseReference> usedBy = await _db.Part
                .Where(p => p.SymbolId == symbol.Id)
                .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                .ToListAsync(cancellationToken);
            return usedBy.Count == 0 ? null : InUse("symbol", uuid, symbol.Name, usedBy);
        }

        private async Task<InUseItem?> FootprintInUseAsync(Guid uuid, CancellationToken cancellationToken)
        {
            Footprint? footprint = await _db.Footprint.FirstOrDefaultAsync(f => f.Uuid == uuid && f.DeletedAt == null, cancellationToken);
            if (footprint?.Id == null)
            {
                return null;
            }

            List<InUseReference> usedBy = await _db.Part
                .Where(p => p.FootprintId == footprint.Id)
                .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                .ToListAsync(cancellationToken);
            return usedBy.Count == 0 ? null : InUse("footprint", uuid, footprint.Name, usedBy);
        }

        private async Task<InUseItem?> DatasheetInUseAsync(Guid uuid, CancellationToken cancellationToken)
        {
            Datasheet? datasheet = await _db.Datasheet.FirstOrDefaultAsync(d => d.Uuid == uuid && d.DeletedAt == null, cancellationToken);
            if (datasheet?.Id == null)
            {
                return null;
            }

            string name = string.IsNullOrEmpty(datasheet.Directory)
                ? datasheet.Filename
                : datasheet.Directory.TrimEnd('/') + "/" + datasheet.Filename;
            List<InUseReference> usedBy = (await PartDatasheetUsage.FindPartsAsync(_db, datasheet.Id.Value, cancellationToken))
                .Select(p => new InUseReference { Entity = "part", Id = p.Id, Name = p.Name })
                .ToList();
            return usedBy.Count == 0 ? null : InUse("datasheet", uuid, name, usedBy);
        }

        private async Task<InUseItem?> Model3DInUseAsync(
            Guid uuid,
            HashSet<Guid> deletedFootprints,
            Dictionary<Guid, HashSet<Guid>> footprintModelsAfter,
            CancellationToken cancellationToken)
        {
            Model3D? model = await _db.Model3D.FirstOrDefaultAsync(m => m.Uuid == uuid && m.DeletedAt == null, cancellationToken);
            if (model?.Id == null)
            {
                return null;
            }

            var footprints = await _db.Footprint
                .Where(fp => fp.DeletedAt == null && fp.HeadRevisionId != null)
                .Where(fp => _db.FootprintRevisionModel3Ds.Any(link =>
                    link.FootprintRevisionId == fp.HeadRevisionId && link.Model3dId == model.Id))
                .Select(fp => new { fp.Id, fp.Uuid, fp.Name })
                .ToListAsync(cancellationToken);

            List<InUseReference> usedBy = new();
            foreach (var fp in footprints)
            {
                if (fp.Uuid is Guid fpUuid && deletedFootprints.Contains(fpUuid))
                {
                    continue;
                }

                if (fp.Uuid is Guid fpUuid2
                    && footprintModelsAfter.TryGetValue(fpUuid2, out HashSet<Guid>? kept)
                    && !kept.Contains(uuid))
                {
                    continue;
                }

                usedBy.Add(new InUseReference { Entity = "footprint", Id = fp.Id, Name = fp.Name });
            }

            if (usedBy.Count == 0)
            {
                return null;
            }

            string name = string.IsNullOrEmpty(model.Directory)
                ? model.Filename
                : model.Directory.TrimEnd('/') + "/" + model.Filename;
            return InUse("model3d", uuid, name, usedBy);
        }

        private static InUseItem InUse(string entity, Guid uuid, string name, List<InUseReference> usedBy) =>
            new() { Entity = entity, Uuid = uuid, Name = name, UsedBy = usedBy };

        private static bool Eq(string? value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

        private async Task<ConflictItem?> CheckExpectedHashAsync(string entity, Guid uuid, string? expectedHash, CancellationToken cancellationToken)
        {
            string? actual = await GetHeadBlobHashAsync(entity, uuid, cancellationToken);
            int? headRevisionId = await GetHeadRevisionIdAsync(entity, uuid, cancellationToken);
            string? expected = string.IsNullOrWhiteSpace(expectedHash) ? null : DbBlobStore.NormalizeHash(expectedHash);
            string? actualNorm = string.IsNullOrWhiteSpace(actual) ? null : DbBlobStore.NormalizeHash(actual);

            if (!string.Equals(expected, actualNorm, StringComparison.Ordinal))
            {
                return new ConflictItem
                {
                    Entity = entity,
                    Uuid = uuid,
                    ExpectedHash = expected,
                    ActualHash = actualNorm,
                    HeadRevisionId = headRevisionId
                };
            }

            return null;
        }

        private async Task<ConflictItem?> CheckCreateExistsAsync(string entity, CommitChange change, CancellationToken cancellationToken)
        {
            switch (entity)
            {
                case "symbol":
                    if (!change.LibraryFileId.HasValue || string.IsNullOrWhiteSpace(change.Name))
                    {
                        return null;
                    }

                    Symbol? existingSym = await _db.Symbol.FirstOrDefaultAsync(
                        s => s.LibraryFileId == change.LibraryFileId && s.Name == change.Name && s.DeletedAt == null,
                        cancellationToken);
                    if (existingSym != null)
                    {
                        return new ConflictItem { Entity = entity, Uuid = existingSym.Uuid, Reason = "exists", HeadRevisionId = existingSym.HeadRevisionId };
                    }

                    break;
                case "footprint":
                    if (!change.LibraryFileId.HasValue || string.IsNullOrWhiteSpace(change.Name))
                    {
                        return null;
                    }

                    Footprint? existingFp = await _db.Footprint.FirstOrDefaultAsync(
                        s => s.LibraryFileId == change.LibraryFileId && s.Name == change.Name && s.DeletedAt == null,
                        cancellationToken);
                    if (existingFp != null)
                    {
                        return new ConflictItem { Entity = entity, Uuid = existingFp.Uuid, Reason = "exists", HeadRevisionId = existingFp.HeadRevisionId };
                    }

                    break;
                case "model3d":
                case "datasheet":
                case "template":
                    {
                        string directory = change.Directory ?? "";
                        string filename = change.Filename ?? "";
                        if (entity == "model3d")
                        {
                            Model3D? existing = await _db.Model3D.FirstOrDefaultAsync(
                                m => m.Directory == directory && m.Filename == filename && m.DeletedAt == null, cancellationToken);
                            if (existing != null)
                            {
                                return new ConflictItem { Entity = entity, Uuid = existing.Uuid, Reason = "exists", HeadRevisionId = existing.HeadRevisionId };
                            }
                        }
                        else if (entity == "datasheet")
                        {
                            Datasheet? existing = await _db.Datasheet.FirstOrDefaultAsync(
                                m => m.Directory == directory && m.Filename == filename && m.DeletedAt == null, cancellationToken);
                            if (existing != null)
                            {
                                return new ConflictItem { Entity = entity, Uuid = existing.Uuid, Reason = "exists", HeadRevisionId = existing.HeadRevisionId };
                            }
                        }
                        else
                        {
                            Template? existing = await _db.Template.FirstOrDefaultAsync(
                                m => m.Directory == directory && m.Filename == filename && m.DeletedAt == null, cancellationToken);
                            if (existing != null)
                            {
                                return new ConflictItem { Entity = entity, Uuid = existing.Uuid, Reason = "exists", HeadRevisionId = existing.HeadRevisionId };
                            }
                        }

                        break;
                    }
            }

            return null;
        }

        private async Task<string?> GetHeadBlobHashAsync(string entity, Guid uuid, CancellationToken cancellationToken)
        {
            return entity switch
            {
                "symbol" => await (
                    from s in _db.Symbol
                    join r in _db.SymbolRevisions on s.HeadRevisionId equals r.Id
                    where s.Uuid == uuid
                    select r.BlobHash).FirstOrDefaultAsync(cancellationToken),
                "footprint" => await (
                    from s in _db.Footprint
                    join r in _db.FootprintRevisions on s.HeadRevisionId equals r.Id
                    where s.Uuid == uuid
                    select r.BlobHash).FirstOrDefaultAsync(cancellationToken),
                "model3d" => await (
                    from s in _db.Model3D
                    join r in _db.Model3DRevisions on s.HeadRevisionId equals r.Id
                    where s.Uuid == uuid
                    select r.BlobHash).FirstOrDefaultAsync(cancellationToken),
                "datasheet" => await (
                    from s in _db.Datasheet
                    join r in _db.DatasheetRevisions on s.HeadRevisionId equals r.Id
                    where s.Uuid == uuid
                    select r.BlobHash).FirstOrDefaultAsync(cancellationToken),
                "template" => await (
                    from s in _db.Template
                    join r in _db.TemplateRevisions on s.HeadRevisionId equals r.Id
                    where s.Uuid == uuid
                    select r.BlobHash).FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        private async Task<int?> GetHeadRevisionIdAsync(string entity, Guid uuid, CancellationToken cancellationToken)
        {
            return entity switch
            {
                "symbol" => await _db.Symbol.Where(s => s.Uuid == uuid).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "footprint" => await _db.Footprint.Where(s => s.Uuid == uuid).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "model3d" => await _db.Model3D.Where(s => s.Uuid == uuid).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "datasheet" => await _db.Datasheet.Where(s => s.Uuid == uuid).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                "template" => await _db.Template.Where(s => s.Uuid == uuid).Select(s => s.HeadRevisionId).FirstOrDefaultAsync(cancellationToken),
                _ => null
            };
        }

        private async Task<CommitResultItem> ApplySymbolAsync(int commitId, CommitChange change, CancellationToken cancellationToken)
        {
            string op = change.Op.ToLowerInvariant();
            if (op == "create")
            {
                Guid uuid = change.Uuid ?? Guid.NewGuid();
                Symbol head = new Symbol
                {
                    Uuid = uuid,
                    LibraryFileId = change.LibraryFileId!.Value,
                    Name = change.Name!
                };
                _db.Symbol.Add(head);
                await _db.SaveChangesAsync(cancellationToken);

                SymbolRevision rev = new SymbolRevision
                {
                    SymbolId = head.Id!.Value,
                    CommitId = commitId,
                    RevisionNo = 1,
                    LibraryFileId = change.LibraryFileId!.Value,
                    Name = change.Name!,
                    BlobHash = Norm(change.BlobHash),
                    PreviewBlobHash = Norm(change.PreviewBlobHash),
                    Reference = change.Reference,
                    InBom = change.InBom ?? true,
                    OnBoard = change.OnBoard ?? true,
                    PinNumbersHidden = change.PinNumbersHidden ?? false,
                    PinNamesHidden = change.PinNamesHidden ?? false,
                    PinNamesOffset = change.PinNamesOffset,
                    Description = change.Description
                };
                _db.SymbolRevisions.Add(rev);
                await _db.SaveChangesAsync(cancellationToken);
                head.HeadRevisionId = rev.Id;
                head.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                return Result("symbol", uuid, head.Id!.Value, rev.Id!.Value, 1, rev.BlobHash);
            }

            Symbol existing = await _db.Symbol.Include(s => s.HeadRevision)
                .FirstAsync(s => s.Uuid == change.Uuid, cancellationToken);
            SymbolRevision prev = existing.HeadRevision!;
            int nextNo = prev.RevisionNo + 1;

            if (op == "delete")
            {
                SymbolRevision del = CloneSymbolRevision(prev, commitId, nextNo);
                del.IsDeleted = true;
                del.BlobHash = null;
                del.PreviewBlobHash = null;
                _db.SymbolRevisions.Add(del);
                await _db.SaveChangesAsync(cancellationToken);
                existing.HeadRevisionId = del.Id;
                existing.DeletedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return Result("symbol", existing.Uuid!.Value, existing.Id!.Value, del.Id!.Value, nextNo, null);
            }

            SymbolRevision next = CloneSymbolRevision(prev, commitId, nextNo);
            if (op == "rename")
            {
                if (!string.IsNullOrWhiteSpace(change.Name))
                {
                    next.Name = change.Name!;
                    existing.Name = change.Name!;
                }

                if (change.LibraryFileId.HasValue)
                {
                    next.LibraryFileId = change.LibraryFileId.Value;
                    existing.LibraryFileId = change.LibraryFileId.Value;
                }
            }
            else // update
            {
                if (change.LibraryFileId.HasValue)
                {
                    next.LibraryFileId = change.LibraryFileId.Value;
                    existing.LibraryFileId = change.LibraryFileId.Value;
                }

                if (!string.IsNullOrWhiteSpace(change.Name))
                {
                    next.Name = change.Name!;
                    existing.Name = change.Name!;
                }

                next.BlobHash = Norm(change.BlobHash) ?? next.BlobHash;
                next.PreviewBlobHash = Norm(change.PreviewBlobHash) ?? next.PreviewBlobHash;
                if (change.Reference != null) next.Reference = change.Reference;
                if (change.InBom.HasValue) next.InBom = change.InBom.Value;
                if (change.OnBoard.HasValue) next.OnBoard = change.OnBoard.Value;
                if (change.PinNumbersHidden.HasValue) next.PinNumbersHidden = change.PinNumbersHidden.Value;
                if (change.PinNamesHidden.HasValue) next.PinNamesHidden = change.PinNamesHidden.Value;
                if (change.PinNamesOffset.HasValue) next.PinNamesOffset = change.PinNamesOffset;
                if (change.Description != null) next.Description = change.Description;
            }

            _db.SymbolRevisions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);
            existing.HeadRevisionId = next.Id;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return Result("symbol", existing.Uuid!.Value, existing.Id!.Value, next.Id!.Value, nextNo, next.BlobHash);
        }

        private static SymbolRevision CloneSymbolRevision(SymbolRevision prev, int commitId, int revisionNo) => new SymbolRevision
        {
            SymbolId = prev.SymbolId,
            CommitId = commitId,
            RevisionNo = revisionNo,
            LibraryFileId = prev.LibraryFileId,
            Name = prev.Name,
            BlobHash = prev.BlobHash,
            PreviewBlobHash = prev.PreviewBlobHash,
            Reference = prev.Reference,
            InBom = prev.InBom,
            OnBoard = prev.OnBoard,
            PinNumbersHidden = prev.PinNumbersHidden,
            PinNamesHidden = prev.PinNamesHidden,
            PinNamesOffset = prev.PinNamesOffset,
            Description = prev.Description
        };

        private async Task<CommitResultItem> ApplyFootprintAsync(
            int commitId,
            CommitChange change,
            Dictionary<Guid, int> uuidToModel3dId,
            CancellationToken cancellationToken)
        {
            string op = change.Op.ToLowerInvariant();
            if (op == "create")
            {
                Guid uuid = change.Uuid ?? Guid.NewGuid();
                Footprint head = new Footprint
                {
                    Uuid = uuid,
                    LibraryFileId = change.LibraryFileId!.Value,
                    Name = change.Name!
                };
                _db.Footprint.Add(head);
                await _db.SaveChangesAsync(cancellationToken);

                FootprintRevision rev = new FootprintRevision
                {
                    FootprintId = head.Id!.Value,
                    CommitId = commitId,
                    RevisionNo = 1,
                    LibraryFileId = change.LibraryFileId!.Value,
                    Name = change.Name!,
                    BlobHash = Norm(change.BlobHash),
                    PreviewBlobHash = Norm(change.PreviewBlobHash),
                    Type = change.Type ?? 0,
                    BoardOnly = change.BoardOnly ?? false,
                    ExcludeFromPosFiles = change.ExcludeFromPosFiles ?? false,
                    ExcludeFromBom = change.ExcludeFromBom ?? false,
                    Height = change.Height ?? 0,
                    Layer = change.Layer ?? "F.Cu",
                    Description = change.Description,
                    Tags = change.Tags,
                    Property = change.Property
                };
                _db.FootprintRevisions.Add(rev);
                await _db.SaveChangesAsync(cancellationToken);
                await ReplaceModelsAsync(rev.Id!.Value, change.Models, uuidToModel3dId, cancellationToken);
                head.HeadRevisionId = rev.Id;
                head.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return Result("footprint", uuid, head.Id!.Value, rev.Id!.Value, 1, rev.BlobHash);
            }

            Footprint existing = await _db.Footprint.Include(s => s.HeadRevision)
                .FirstAsync(s => s.Uuid == change.Uuid, cancellationToken);
            FootprintRevision prev = existing.HeadRevision!;
            int nextNo = prev.RevisionNo + 1;

            if (op == "delete")
            {
                FootprintRevision del = CloneFootprintRevision(prev, commitId, nextNo);
                del.IsDeleted = true;
                del.BlobHash = null;
                del.PreviewBlobHash = null;
                _db.FootprintRevisions.Add(del);
                await _db.SaveChangesAsync(cancellationToken);
                existing.HeadRevisionId = del.Id;
                existing.DeletedAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return Result("footprint", existing.Uuid!.Value, existing.Id!.Value, del.Id!.Value, nextNo, null);
            }

            FootprintRevision next = CloneFootprintRevision(prev, commitId, nextNo);
            if (op == "rename")
            {
                if (!string.IsNullOrWhiteSpace(change.Name))
                {
                    next.Name = change.Name!;
                    existing.Name = change.Name!;
                }

                if (change.LibraryFileId.HasValue)
                {
                    next.LibraryFileId = change.LibraryFileId.Value;
                    existing.LibraryFileId = change.LibraryFileId.Value;
                }
            }
            else
            {
                if (change.LibraryFileId.HasValue)
                {
                    next.LibraryFileId = change.LibraryFileId.Value;
                    existing.LibraryFileId = change.LibraryFileId.Value;
                }

                if (!string.IsNullOrWhiteSpace(change.Name))
                {
                    next.Name = change.Name!;
                    existing.Name = change.Name!;
                }

                next.BlobHash = Norm(change.BlobHash) ?? next.BlobHash;
                next.PreviewBlobHash = Norm(change.PreviewBlobHash) ?? next.PreviewBlobHash;
                if (change.Type.HasValue) next.Type = change.Type.Value;
                if (change.BoardOnly.HasValue) next.BoardOnly = change.BoardOnly.Value;
                if (change.ExcludeFromPosFiles.HasValue) next.ExcludeFromPosFiles = change.ExcludeFromPosFiles.Value;
                if (change.ExcludeFromBom.HasValue) next.ExcludeFromBom = change.ExcludeFromBom.Value;
                if (change.Height.HasValue) next.Height = change.Height.Value;
                if (change.Layer != null) next.Layer = change.Layer;
                if (change.Description != null) next.Description = change.Description;
                if (change.Tags != null) next.Tags = change.Tags;
                if (change.Property != null) next.Property = change.Property;
            }

            _db.FootprintRevisions.Add(next);
            await _db.SaveChangesAsync(cancellationToken);

            // Models == null → carry over previous revision's models (update and rename).
            // Models is a list (possibly empty) → replace.
            if (change.Models != null)
            {
                await ReplaceModelsAsync(next.Id!.Value, change.Models, uuidToModel3dId, cancellationToken);
            }
            else
            {
                await CopyModelsFromPreviousAsync(prev.Id!.Value, next.Id!.Value, cancellationToken);
            }

            existing.HeadRevisionId = next.Id;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return Result("footprint", existing.Uuid!.Value, existing.Id!.Value, next.Id!.Value, nextNo, next.BlobHash);
        }

        private static FootprintRevision CloneFootprintRevision(FootprintRevision prev, int commitId, int revisionNo) => new FootprintRevision
        {
            FootprintId = prev.FootprintId,
            CommitId = commitId,
            RevisionNo = revisionNo,
            LibraryFileId = prev.LibraryFileId,
            Name = prev.Name,
            BlobHash = prev.BlobHash,
            PreviewBlobHash = prev.PreviewBlobHash,
            Type = prev.Type,
            BoardOnly = prev.BoardOnly,
            ExcludeFromPosFiles = prev.ExcludeFromPosFiles,
            ExcludeFromBom = prev.ExcludeFromBom,
            Height = prev.Height,
            Layer = prev.Layer,
            Description = prev.Description,
            Tags = prev.Tags,
            Property = prev.Property
        };

        private async Task CopyModelsFromPreviousAsync(
            int previousRevisionId,
            int newRevisionId,
            CancellationToken cancellationToken)
        {
            List<FootprintRevisionModel3D> prevModels = await _db.FootprintRevisionModel3Ds
                .Where(m => m.FootprintRevisionId == previousRevisionId)
                .ToListAsync(cancellationToken);
            foreach (FootprintRevisionModel3D m in prevModels)
            {
                _db.FootprintRevisionModel3Ds.Add(new FootprintRevisionModel3D
                {
                    FootprintRevisionId = newRevisionId,
                    Position = m.Position,
                    Model3dId = m.Model3dId,
                    OffsetX = m.OffsetX,
                    OffsetY = m.OffsetY,
                    OffsetZ = m.OffsetZ,
                    ScaleX = m.ScaleX,
                    ScaleY = m.ScaleY,
                    ScaleZ = m.ScaleZ,
                    RotateX = m.RotateX,
                    RotateY = m.RotateY,
                    RotateZ = m.RotateZ,
                    Opacity = m.Opacity,
                    Hide = m.Hide
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task ReplaceModelsAsync(
            int revisionId,
            List<FootprintModelChange>? models,
            Dictionary<Guid, int> uuidToModel3dId,
            CancellationToken cancellationToken)
        {
            if (models == null)
            {
                return;
            }

            foreach (FootprintModelChange model in models)
            {
                if (!model.Model3dUuid.HasValue)
                {
                    continue;
                }

                int model3dId;
                if (uuidToModel3dId.TryGetValue(model.Model3dUuid.Value, out int mapped))
                {
                    model3dId = mapped;
                }
                else
                {
                    model3dId = await _db.Model3D
                        .Where(m => m.Uuid == model.Model3dUuid.Value)
                        .Select(m => m.Id!.Value)
                        .FirstAsync(cancellationToken);
                }

                float[] offset = model.Offset ?? new float[] { 0, 0, 0 };
                float[] scale = model.Scale ?? new float[] { 1, 1, 1 };
                float[] rotate = model.Rotate ?? new float[] { 0, 0, 0 };

                _db.FootprintRevisionModel3Ds.Add(new FootprintRevisionModel3D
                {
                    FootprintRevisionId = revisionId,
                    Position = model.Position,
                    Model3dId = model3dId,
                    OffsetX = At(offset, 0),
                    OffsetY = At(offset, 1),
                    OffsetZ = At(offset, 2),
                    ScaleX = At(scale, 0, 1),
                    ScaleY = At(scale, 1, 1),
                    ScaleZ = At(scale, 2, 1),
                    RotateX = At(rotate, 0),
                    RotateY = At(rotate, 1),
                    RotateZ = At(rotate, 2),
                    Opacity = model.Opacity,
                    Hide = model.Hide
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static float At(float[] arr, int index, float fallback = 0) =>
            arr.Length > index ? arr[index] : fallback;

        private async Task<CommitResultItem> ApplyModel3DAsync(
            int commitId,
            CommitChange change,
            Dictionary<Guid, int> uuidToModel3dId,
            CancellationToken cancellationToken)
        {
            return await ApplyFileEntityAsync(
                commitId,
                change,
                "model3d",
                async (uuid, directory, filename) =>
                {
                    Model3D head = new Model3D { Uuid = uuid, Directory = directory, Filename = filename };
                    _db.Model3D.Add(head);
                    await _db.SaveChangesAsync(cancellationToken);
                    uuidToModel3dId[uuid] = head.Id!.Value;
                    return (head.Id!.Value, (Action<int>)(rid => { head.HeadRevisionId = rid; head.UpdatedAt = DateTime.UtcNow; }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async uuid =>
                {
                    Model3D head = await _db.Model3D.Include(m => m.HeadRevision).FirstAsync(m => m.Uuid == uuid, cancellationToken);
                    uuidToModel3dId[uuid] = head.Id!.Value;
                    return (head, head.HeadRevision!, (Action<int, string, string>)((rid, d, f) =>
                    {
                        head.HeadRevisionId = rid;
                        head.Directory = d;
                        head.Filename = f;
                        head.UpdatedAt = DateTime.UtcNow;
                    }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async (entityId, revNo, directory, filename, comment, blobHash, size, isDeleted) =>
                {
                    Model3DRevision rev = new Model3DRevision
                    {
                        Model3dId = entityId,
                        CommitId = commitId,
                        RevisionNo = revNo,
                        IsDeleted = isDeleted,
                        Directory = directory,
                        Filename = filename,
                        Comment = comment,
                        BlobHash = blobHash,
                        Size = size
                    };
                    _db.Model3DRevisions.Add(rev);
                    await _db.SaveChangesAsync(cancellationToken);
                    return rev.Id!.Value;
                },
                cancellationToken);
        }

        private async Task<CommitResultItem> ApplyDatasheetAsync(int commitId, CommitChange change, CancellationToken cancellationToken)
        {
            return await ApplyFileEntityAsync(
                commitId,
                change,
                "datasheet",
                async (uuid, directory, filename) =>
                {
                    Datasheet head = new Datasheet { Uuid = uuid, Directory = directory, Filename = filename };
                    _db.Datasheet.Add(head);
                    await _db.SaveChangesAsync(cancellationToken);
                    return (head.Id!.Value, (Action<int>)(rid => { head.HeadRevisionId = rid; head.UpdatedAt = DateTime.UtcNow; }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async uuid =>
                {
                    Datasheet head = await _db.Datasheet.Include(m => m.HeadRevision).FirstAsync(m => m.Uuid == uuid, cancellationToken);
                    return (head, head.HeadRevision!, (Action<int, string, string>)((rid, d, f) =>
                    {
                        head.HeadRevisionId = rid;
                        head.Directory = d;
                        head.Filename = f;
                        head.UpdatedAt = DateTime.UtcNow;
                    }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async (entityId, revNo, directory, filename, comment, blobHash, size, isDeleted) =>
                {
                    DatasheetRevision rev = new DatasheetRevision
                    {
                        DatasheetId = entityId,
                        CommitId = commitId,
                        RevisionNo = revNo,
                        IsDeleted = isDeleted,
                        Directory = directory,
                        Filename = filename,
                        Comment = comment,
                        BlobHash = blobHash,
                        Size = size
                    };
                    _db.DatasheetRevisions.Add(rev);
                    await _db.SaveChangesAsync(cancellationToken);
                    return rev.Id!.Value;
                },
                cancellationToken);
        }

        private async Task<CommitResultItem> ApplyTemplateAsync(int commitId, CommitChange change, CancellationToken cancellationToken)
        {
            return await ApplyFileEntityAsync(
                commitId,
                change,
                "template",
                async (uuid, directory, filename) =>
                {
                    Template head = new Template { Uuid = uuid, Directory = directory, Filename = filename };
                    _db.Template.Add(head);
                    await _db.SaveChangesAsync(cancellationToken);
                    return (head.Id!.Value, (Action<int>)(rid => { head.HeadRevisionId = rid; head.UpdatedAt = DateTime.UtcNow; }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async uuid =>
                {
                    Template head = await _db.Template.Include(m => m.HeadRevision).FirstAsync(m => m.Uuid == uuid, cancellationToken);
                    return (head, head.HeadRevision!, (Action<int, string, string>)((rid, d, f) =>
                    {
                        head.HeadRevisionId = rid;
                        head.Directory = d;
                        head.Filename = f;
                        head.UpdatedAt = DateTime.UtcNow;
                    }), (Action)(() => { head.DeletedAt = DateTime.UtcNow; head.UpdatedAt = DateTime.UtcNow; }));
                },
                async (entityId, revNo, directory, filename, comment, blobHash, size, isDeleted) =>
                {
                    TemplateRevision rev = new TemplateRevision
                    {
                        TemplateId = entityId,
                        CommitId = commitId,
                        RevisionNo = revNo,
                        IsDeleted = isDeleted,
                        Directory = directory,
                        Filename = filename,
                        Comment = comment,
                        BlobHash = blobHash,
                        Size = size
                    };
                    _db.TemplateRevisions.Add(rev);
                    await _db.SaveChangesAsync(cancellationToken);
                    return rev.Id!.Value;
                },
                cancellationToken);
        }

        private async Task<CommitResultItem> ApplyFileEntityAsync(
            int commitId,
            CommitChange change,
            string entity,
            Func<Guid, string, string, Task<(int Id, Action<int> SetHead, Action MarkDeleted)>> createHead,
            Func<Guid, Task<(object Head, dynamic PrevRev, Action<int, string, string> UpdateHead, Action MarkDeleted)>> loadHead,
            Func<int, int, string, string, string?, string?, long?, bool, Task<int>> insertRevision,
            CancellationToken cancellationToken)
        {
            string op = change.Op.ToLowerInvariant();
            string directory = change.Directory ?? "";
            string filename = change.Filename ?? "";

            if (op == "create")
            {
                Guid uuid = change.Uuid ?? Guid.NewGuid();
                (int id, Action<int> setHead, _) = await createHead(uuid, directory, filename);
                long? size = null;
                string? hash = Norm(change.BlobHash);
                if (hash != null)
                {
                    Blob? blob = await _blobStore.GetAsync(hash, cancellationToken);
                    size = blob?.Size;
                }

                int revId = await insertRevision(id, 1, directory, filename, change.Comment, hash, size, false);
                setHead(revId);
                await _db.SaveChangesAsync(cancellationToken);
                return Result(entity, uuid, id, revId, 1, hash);
            }

            var loaded = await loadHead(change.Uuid!.Value);
            int entityId = (int)loaded.Head.GetType().GetProperty("Id")!.GetValue(loaded.Head)!;
            Guid entityUuid = (Guid)loaded.Head.GetType().GetProperty("Uuid")!.GetValue(loaded.Head)!;
            int prevNo = (int)loaded.PrevRev.RevisionNo;
            string prevDir = (string)loaded.PrevRev.Directory;
            string prevFile = (string)loaded.PrevRev.Filename;
            string? prevComment = loaded.PrevRev.Comment;
            string? prevHash = loaded.PrevRev.BlobHash;
            long? prevSize = loaded.PrevRev.Size;
            int nextNo = prevNo + 1;

            if (op == "delete")
            {
                int revId = await insertRevision(entityId, nextNo, prevDir, prevFile, prevComment, null, null, true);
                loaded.UpdateHead(revId, prevDir, prevFile);
                loaded.MarkDeleted();
                await _db.SaveChangesAsync(cancellationToken);
                return Result(entity, entityUuid, entityId, revId, nextNo, null);
            }

            string nextDir = op == "rename" && change.Directory != null ? change.Directory : (change.Directory ?? prevDir);
            string nextFile = op == "rename" && change.Filename != null ? change.Filename : (change.Filename ?? prevFile);
            if (op == "rename")
            {
                if (change.Directory != null) nextDir = change.Directory;
                if (change.Filename != null) nextFile = change.Filename;
            }

            string? nextHash = op == "update" ? (Norm(change.BlobHash) ?? prevHash) : prevHash;
            string? nextComment = op == "update" && change.Comment != null ? change.Comment : prevComment;
            long? nextSize = prevSize;
            if (op == "update" && nextHash != null)
            {
                Blob? blob = await _blobStore.GetAsync(nextHash, cancellationToken);
                nextSize = blob?.Size ?? prevSize;
            }

            int newRevId = await insertRevision(entityId, nextNo, nextDir, nextFile, nextComment, nextHash, nextSize, false);
            loaded.UpdateHead(newRevId, nextDir, nextFile);
            await _db.SaveChangesAsync(cancellationToken);
            return Result(entity, entityUuid, entityId, newRevId, nextNo, nextHash);
        }

        private static string? Norm(string? hash) =>
            string.IsNullOrWhiteSpace(hash) ? null : DbBlobStore.NormalizeHash(hash);

        private static CommitResultItem Result(string entity, Guid uuid, int id, int revisionId, int revisionNo, string? hash) =>
            new CommitResultItem
            {
                Entity = entity,
                Uuid = uuid,
                Id = id,
                RevisionId = revisionId,
                RevisionNo = revisionNo,
                Hash = hash == null ? null : DbBlobStore.NormalizeHash(hash)
            };
    }

    public class CommitOutcome
    {
        public int StatusCode { get; init; }
        public CommitResponse? Response { get; init; }
        public List<ConflictItem>? Conflicts { get; init; }
        public List<InUseItem>? InUse { get; init; }
        public List<string>? MissingBlobs { get; init; }
        public List<Guid>? UnknownModels { get; init; }
        public string? Error { get; init; }

        public static CommitOutcome Ok(CommitResponse response) => new() { StatusCode = 201, Response = response };
        public static CommitOutcome Conflict(List<ConflictItem> conflicts) => new() { StatusCode = 409, Conflicts = conflicts };
        public static CommitOutcome StillInUse(List<InUseItem> inUse) =>
            new() { StatusCode = 409, Error = "Delete rejected: still in use", InUse = inUse };
        public static CommitOutcome Missing(List<string> missingBlobs, List<Guid> unknownModels) =>
            new() { StatusCode = 400, MissingBlobs = missingBlobs, UnknownModels = unknownModels };
        public static CommitOutcome BadRequest(string error) => new() { StatusCode = 400, Error = error };
    }
}
