using System.Data;
using System.Text.Json;
using KiComLibBackend.Data;
using KiComLibBackend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace KiComLibBackend.Services
{
    public static class CatalogKinds
    {
        public static readonly string[] All = { "part", "manufacturer", "distributor", "partnumber", "ordernumber" };

        public static bool IsCatalog(string? entity) =>
            entity != null && All.Contains(entity, StringComparer.OrdinalIgnoreCase);

        public static string Normalize(string entity) => entity.ToLowerInvariant();

        public static string? Table(string entity) => Normalize(entity) switch
        {
            "part" => "part",
            "manufacturer" => "manufacturer",
            "distributor" => "distributor",
            "partnumber" => "part_number",
            "ordernumber" => "order_number",
            _ => null
        };

        /// <summary>
        /// History author is the account behind the session or API token.
        /// A name sent by the client is only used when nobody is signed in.
        /// </summary>
        public static string Author(HttpRequest request)
        {
            string? account = CallerContext.Current(request.HttpContext).Username;
            if (!string.IsNullOrWhiteSpace(account))
            {
                return account.Trim();
            }

            string? value = request.Headers["X-Commit-Author"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(value) ? "web-ui" : value.Trim();
        }

        public static string Message(HttpRequest request, string fallback)
        {
            string? value = request.Headers["X-Commit-Message"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    /// <summary>
    /// Writes immutable JSON snapshots of catalog rows into the shared commit history.
    /// </summary>
    public class CatalogRevisionService
    {
        private readonly ApplicationDbContext _db;
        private readonly DiffService _diff;
        private readonly ProjectRematchScheduler _rematch;
        private int _commitsWritten;

        public CatalogRevisionService(ApplicationDbContext db, DiffService diff, ProjectRematchScheduler rematch)
        {
            _db = db;
            _diff = diff;
            _rematch = rematch;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            int writtenBefore = _commitsWritten;
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                T result = await action();
                await transaction.CommitAsync(cancellationToken);
                if (_commitsWritten != writtenBefore)
                {
                    try
                    {
                        await _rematch.NotifyCatalogChangedAsync(cancellationToken);
                    }
                    catch (Exception)
                    {
                        // The catalog change is committed. Rematch scheduling must not fail the request.
                    }
                }

                return result;
            }
            catch
            {
                try { await transaction.RollbackAsync(cancellationToken); } catch { /* ignore */ }
                throw;
            }
        }

        public async Task RecordAsync(
            string entity,
            int id,
            string op,
            string author,
            string message,
            string? client,
            CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            op = op.ToLowerInvariant();
            LiveRow? live = await ReadLiveAsync(entity, id, cancellationToken);
            if (live == null)
            {
                return;
            }

            if (op == "update")
            {
                CatalogRevision? head = await HeadAsync(entity, id, cancellationToken);
                if (head != null && !head.IsDeleted && head.Snapshot == live.Snapshot)
                {
                    return;
                }
            }

            await WriteCommitAsync(
                message,
                author,
                client,
                new[] { new PendingChange(entity, id, live.Uuid, op == "delete", live.Name, live.Snapshot) },
                cancellationToken);
        }

        /// <summary>
        /// Snapshots the row and every catalog child that a database cascade would remove.
        /// Call this before deleting the root row, inside the same transaction.
        /// </summary>
        public async Task RecordDeleteAsync(
            string entity,
            int id,
            string author,
            string message,
            string? client,
            CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            List<(string Entity, int Id)> nodes = await ExpandDeleteAsync(entity, id, cancellationToken);
            List<PendingChange> changes = new();
            foreach ((string nodeEntity, int nodeId) in nodes)
            {
                LiveRow? live = await ReadLiveAsync(nodeEntity, nodeId, cancellationToken);
                if (live == null)
                {
                    continue;
                }

                changes.Add(new PendingChange(nodeEntity, nodeId, live.Uuid, true, live.Name, live.Snapshot));
            }

            if (changes.Count == 0)
            {
                return;
            }

            await WriteCommitAsync(message, author, client, changes, cancellationToken);
        }

        public async Task<bool> KnownAsync(string entity, int id, CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            if (await _db.CatalogRevisions.AnyAsync(r => r.Entity == entity && r.EntityId == id, cancellationToken))
            {
                return true;
            }

            return await ReadLiveAsync(entity, id, cancellationToken) != null;
        }

        public async Task<List<object>> HistoryAsync(string entity, int id, CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            var rows = await (
                from r in _db.CatalogRevisions
                join c in _db.Commits on r.CommitId equals c.Id
                where r.Entity == entity && r.EntityId == id
                orderby r.RevisionNo
                select new
                {
                    revisionId = r.Id,
                    revisionNo = r.RevisionNo,
                    isDeleted = r.IsDeleted,
                    name = r.Name,
                    hash = (string?)null,
                    commitId = c.Id,
                    commitUuid = c.Uuid,
                    message = c.Message,
                    author = c.Author,
                    client = c.Client,
                    createdAt = r.CreatedAt
                }).ToListAsync(cancellationToken);

            return rows.Cast<object>().ToList();
        }

        public async Task<object?> RevisionAsync(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            CatalogRevision? rev = await _db.CatalogRevisions.FirstOrDefaultAsync(
                r => r.Entity == entity && r.EntityId == id && r.RevisionNo == revisionNo,
                cancellationToken);
            if (rev == null)
            {
                return null;
            }

            return new
            {
                id,
                revisionId = rev.Id,
                revisionNo = rev.RevisionNo,
                isDeleted = rev.IsDeleted,
                name = rev.Name,
                uuid = rev.EntityUuid,
                snapshot = ParseSnapshot(rev.Snapshot)
            };
        }

        public async Task<object?> DiffAsync(string entity, int id, int fromRev, int toRev, CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            string? fromText = await FormatRevisionAsync(entity, id, fromRev, cancellationToken);
            string? toText = await FormatRevisionAsync(entity, id, toRev, cancellationToken);
            if (fromText == null && toText == null)
            {
                return null;
            }

            string diff = _diff.BuildUnifiedDiff(
                fromText ?? "",
                toText ?? "",
                $"{entity}/{id}@r{fromRev}",
                $"{entity}/{id}@r{toRev}");
            return new { format = "unified", diff };
        }

        public async Task<int> CountForCommitAsync(int commitId, CancellationToken cancellationToken) =>
            await _db.CatalogRevisions.CountAsync(r => r.CommitId == commitId, cancellationToken);

        public async Task<List<object>> ChangesForCommitAsync(int commitId, CancellationToken cancellationToken)
        {
            List<CatalogRevision> rows = await _db.CatalogRevisions
                .Where(r => r.CommitId == commitId)
                .ToListAsync(cancellationToken);

            List<object> list = new();
            foreach (CatalogRevision row in rows)
            {
                CatalogRevision? prev = await _db.CatalogRevisions.FirstOrDefaultAsync(
                    x => x.Entity == row.Entity && x.EntityId == row.EntityId && x.RevisionNo == row.RevisionNo - 1,
                    cancellationToken);
                int? headNo = await _db.CatalogRevisions
                    .Where(x => x.Entity == row.Entity && x.EntityId == row.EntityId)
                    .MaxAsync(x => (int?)x.RevisionNo, cancellationToken);
                string op = row.IsDeleted ? "delete" : row.RevisionNo == 1 || prev == null ? "create" : "update";
                list.Add(new
                {
                    entity = row.Entity,
                    uuid = row.EntityUuid,
                    id = row.EntityId,
                    name = row.Name,
                    op,
                    revisionId = row.Id,
                    revisionNo = row.RevisionNo,
                    previousRevisionId = prev?.Id,
                    isHead = headNo == row.RevisionNo
                });
            }

            return list;
        }

        public async Task<List<object>> DiffsForCommitAsync(int commitId, CancellationToken cancellationToken)
        {
            List<CatalogRevision> rows = await _db.CatalogRevisions
                .Where(r => r.CommitId == commitId)
                .ToListAsync(cancellationToken);
            List<object> diffs = new();
            foreach (CatalogRevision row in rows)
            {
                object? diff = await DiffAsync(row.Entity, row.EntityId, row.RevisionNo - 1, row.RevisionNo, cancellationToken);
                if (diff != null)
                {
                    diffs.Add(new { entity = row.Entity, id = row.EntityId, revisionNo = row.RevisionNo, diff });
                }
            }

            return diffs;
        }

        public static string DisplayName(string entity, string snapshot)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(snapshot);
                JsonElement root = doc.RootElement;
                string? Pick(string key) =>
                    root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : null;

                string? name = CatalogKinds.Normalize(entity) switch
                {
                    "partnumber" => Pick("manufacturer_part_number"),
                    "ordernumber" => Pick("order_number"),
                    _ => Pick("name")
                };
                return string.IsNullOrWhiteSpace(name) ? entity : name;
            }
            catch (JsonException)
            {
                return entity;
            }
        }

        public static object ParseSnapshot(string snapshot)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(snapshot);
            }
            catch (JsonException)
            {
                return snapshot;
            }
        }

        private async Task<string?> FormatRevisionAsync(string entity, int id, int revisionNo, CancellationToken cancellationToken)
        {
            if (revisionNo < 1)
            {
                return "";
            }

            CatalogRevision? rev = await _db.CatalogRevisions.FirstOrDefaultAsync(
                r => r.Entity == entity && r.EntityId == id && r.RevisionNo == revisionNo,
                cancellationToken);
            if (rev == null)
            {
                return null;
            }

            string pretty;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rev.Snapshot) ? "{}" : rev.Snapshot);
                pretty = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                pretty = rev.Snapshot;
            }

            return (rev.IsDeleted ? "deleted: true\n" : "deleted: false\n") + pretty;
        }

        private async Task WriteCommitAsync(
            string message,
            string author,
            string? client,
            IReadOnlyList<PendingChange> changes,
            CancellationToken cancellationToken)
        {
            int? parentCommitId = await _db.Commits.OrderByDescending(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync(cancellationToken);
            Commit commit = new Commit
            {
                Message = message,
                Author = string.IsNullOrWhiteSpace(author) ? "web-ui" : author.Trim(),
                Client = client,
                ParentCommitId = parentCommitId
            };
            _db.Commits.Add(commit);
            await _db.SaveChangesAsync(cancellationToken);

            foreach (PendingChange change in changes)
            {
                int nextNo = (await _db.CatalogRevisions
                    .Where(r => r.Entity == change.Entity && r.EntityId == change.Id)
                    .MaxAsync(r => (int?)r.RevisionNo, cancellationToken) ?? 0) + 1;

                _db.CatalogRevisions.Add(new CatalogRevision
                {
                    Entity = change.Entity,
                    EntityId = change.Id,
                    EntityUuid = change.Uuid,
                    CommitId = commit.Id!.Value,
                    RevisionNo = nextNo,
                    IsDeleted = change.IsDeleted,
                    Name = change.Name,
                    Snapshot = change.Snapshot
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            _commitsWritten++;
        }

        private async Task<List<(string Entity, int Id)>> ExpandDeleteAsync(string entity, int id, CancellationToken cancellationToken)
        {
            List<(string Entity, int Id)> nodes = new();
            switch (entity)
            {
                case "part":
                    {
                        List<int> partNumberIds = await _db.PartNumber
                            .Where(p => p.PartId == id && p.Id != null)
                            .Select(p => p.Id!.Value)
                            .ToListAsync(cancellationToken);
                        await AddOrderNumbersAsync(nodes, partNumberIds, cancellationToken);
                        foreach (int partNumberId in partNumberIds)
                        {
                            nodes.Add(("partnumber", partNumberId));
                        }

                        break;
                    }
                case "partnumber":
                    await AddOrderNumbersAsync(nodes, new[] { id }, cancellationToken);
                    break;
                case "manufacturer":
                    {
                        List<int> partNumberIds = await _db.PartNumber
                            .Where(p => p.ManufacturerId == id && p.Id != null)
                            .Select(p => p.Id!.Value)
                            .ToListAsync(cancellationToken);
                        await AddOrderNumbersAsync(nodes, partNumberIds, cancellationToken);
                        foreach (int partNumberId in partNumberIds)
                        {
                            nodes.Add(("partnumber", partNumberId));
                        }

                        break;
                    }
                case "distributor":
                    await AddDistributorOrdersAsync(nodes, id, cancellationToken);
                    break;
            }

            nodes.Add((entity, id));
            return nodes;
        }

        private async Task AddOrderNumbersAsync(List<(string Entity, int Id)> nodes, IReadOnlyCollection<int> partNumberIds, CancellationToken cancellationToken)
        {
            if (partNumberIds.Count == 0)
            {
                return;
            }

            List<int> orderIds = await _db.OrderNumber
                .Where(o => o.PartNumberId != null && partNumberIds.Contains(o.PartNumberId.Value) && o.Id != null)
                .Select(o => o.Id!.Value)
                .ToListAsync(cancellationToken);
            foreach (int orderId in orderIds)
            {
                nodes.Add(("ordernumber", orderId));
            }
        }

        private async Task AddDistributorOrdersAsync(List<(string Entity, int Id)> nodes, int distributorId, CancellationToken cancellationToken)
        {
            List<int> orderIds = await _db.OrderNumber
                .Where(o => o.DistributorId == distributorId && o.Id != null)
                .Select(o => o.Id!.Value)
                .ToListAsync(cancellationToken);
            foreach (int orderId in orderIds)
            {
                nodes.Add(("ordernumber", orderId));
            }
        }

        private async Task<CatalogRevision?> HeadAsync(string entity, int id, CancellationToken cancellationToken) =>
            await _db.CatalogRevisions
                .Where(r => r.Entity == entity && r.EntityId == id)
                .OrderByDescending(r => r.RevisionNo)
                .FirstOrDefaultAsync(cancellationToken);

        internal async Task ApplySnapshotAsync(string entity, int id, Guid uuid, string snapshot, CancellationToken cancellationToken)
        {
            entity = CatalogKinds.Normalize(entity);
            string? table = CatalogKinds.Table(entity);
            if (table == null || !Columns.TryGetValue(entity, out Col[]? cols))
            {
                throw new InvalidOperationException($"Unknown catalog entity '{entity}'");
            }

            using JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(snapshot) ? "{}" : snapshot);
            JsonElement root = doc.RootElement;
            bool exists = await ReadLiveAsync(entity, id, cancellationToken) != null;
            List<NpgsqlParameter> parameters = new() { new NpgsqlParameter("id", id) };
            if (!exists)
            {
                parameters.Add(new NpgsqlParameter("uuid", uuid));
            }

            foreach (Col col in cols)
            {
                parameters.Add(new NpgsqlParameter(col.Name, ValueOf(root, col)));
            }

            string assignments = string.Join(", ", cols.Select(c => c.Kind == "json" ? $"{c.Name} = CAST(@{c.Name} AS json)" : $"{c.Name} = @{c.Name}"));
            string sql;
            if (exists)
            {
                sql = $"UPDATE public.{table} SET {assignments} WHERE id = @id";
            }
            else
            {
                string names = string.Join(", ", cols.Select(c => c.Name));
                string values = string.Join(", ", cols.Select(c => c.Kind == "json" ? $"CAST(@{c.Name} AS json)" : $"@{c.Name}"));
                sql = $"INSERT INTO public.{table} (id, uuid, {names}) VALUES (@id, @uuid, {values})";
            }

            await ExecAsync(sql, parameters, cancellationToken);
            if (!exists)
            {
                await ExecAsync(
                    $"SELECT setval(pg_get_serial_sequence('public.{table}', 'id'), (SELECT COALESCE(MAX(id), 1) FROM public.{table}))",
                    Array.Empty<NpgsqlParameter>(),
                    cancellationToken);
            }
        }

        internal async Task DeleteRowAsync(string entity, int id, CancellationToken cancellationToken)
        {
            string? table = CatalogKinds.Table(entity);
            if (table == null)
            {
                throw new InvalidOperationException($"Unknown catalog entity '{entity}'");
            }

            await ExecAsync($"DELETE FROM public.{table} WHERE id = @id", new[] { new NpgsqlParameter("id", id) }, cancellationToken);
        }

        private async Task ExecAsync(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken)
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            NpgsqlConnection connection = (NpgsqlConnection)_db.Database.GetDbConnection();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            if (_db.Database.CurrentTransaction != null)
            {
                command.Transaction = (NpgsqlTransaction)_db.Database.CurrentTransaction.GetDbTransaction();
            }

            foreach (NpgsqlParameter parameter in parameters)
            {
                command.Parameters.Add(parameter);
            }

            await command.ExecuteScalarAsync(cancellationToken);
        }

        private static object ValueOf(JsonElement root, Col col)
        {
            if (!root.TryGetProperty(col.Name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (col.Name == "lifecycle_status")
                {
                    return LegacyLifecycleStatus(root);
                }

                return col.Fallback ?? DBNull.Value;
            }

            switch (col.Kind)
            {
                case "bool":
                    if (value.ValueKind == JsonValueKind.True) return true;
                    if (value.ValueKind == JsonValueKind.False) return false;
                    return col.Fallback ?? false;
                case "int":
                    if (value.TryGetInt32(out int number)) return number;
                    return col.Fallback ?? DBNull.Value;
                case "json":
                    return value.GetRawText();
                default:
                    return value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? (object)DBNull.Value
                        : value.GetRawText();
            }
        }

        /// <summary>Snapshots taken before lifecycle_status stored only is_end_of_life.</summary>
        private static string LegacyLifecycleStatus(JsonElement root)
        {
            if (root.TryGetProperty("is_end_of_life", out JsonElement legacy) && legacy.ValueKind == JsonValueKind.True)
            {
                return "eol";
            }

            return "active";
        }

        private static readonly Dictionary<string, Col[]> Columns = new()
        {
            ["part"] = new[]
            {
                new Col("name", "text"),
                new Col("comment", "text"),
                new Col("datasheet_id", "int"),
                new Col("datasheet", "text"),
                new Col("datasheet_ids", "json", "[]"),
                new Col("library_id", "int"),
                new Col("symbol_id", "int"),
                new Col("footprint_id", "int"),
                new Col("lifecycle_status", "text", "active"),
                new Col("keywords", "text"),
                new Col("value", "text"),
                new Col("properties", "json"),
                new Col("alternatives", "json"),
                new Col("links", "json", "[]")
            },
            ["manufacturer"] = new[]
            {
                new Col("name", "text"),
                new Col("url", "text"),
                new Col("trustedparts_id", "text")
            },
            ["distributor"] = new[]
            {
                new Col("name", "text"),
                new Col("url", "text"),
                new Col("trustedparts_id", "text"),
                new Col("is_favorite", "bool", false)
            },
            ["partnumber"] = new[]
            {
                new Col("part_id", "int"),
                new Col("manufacturer_id", "int"),
                new Col("manufacturer_part_number", "text"),
                new Col("quantity", "int", 1),
                new Col("package_type", "text")
            },
            ["ordernumber"] = new[]
            {
                new Col("order_number", "text"),
                new Col("part_number_id", "int"),
                new Col("distributor_id", "int"),
                new Col("quantity_min", "int", 1),
                new Col("quantity_steps", "int", 1)
            }
        };

        internal async Task<LiveRow?> ReadLiveAsync(string entity, int id, CancellationToken cancellationToken)
        {
            string? table = CatalogKinds.Table(entity);
            if (table == null)
            {
                return null;
            }

            string sql = $"SELECT uuid::text, (to_jsonb(t) - 'id' - 'uuid' - 'created_at' - 'updated_at')::text FROM public.{table} t WHERE id = @id";
            await _db.Database.OpenConnectionAsync(cancellationToken);
            NpgsqlConnection connection = (NpgsqlConnection)_db.Database.GetDbConnection();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            if (_db.Database.CurrentTransaction != null)
            {
                command.Transaction = (NpgsqlTransaction)_db.Database.CurrentTransaction.GetDbTransaction();
            }

            command.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
            {
                return null;
            }

            Guid uuid = Guid.Parse(reader.GetString(0));
            string snapshot = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
            return new LiveRow(uuid, snapshot, DisplayName(entity, snapshot));
        }

        internal sealed record LiveRow(Guid Uuid, string Snapshot, string Name);

        private sealed record PendingChange(string Entity, int Id, Guid Uuid, bool IsDeleted, string Name, string Snapshot);

        private sealed record Col(string Name, string Kind, object? Fallback = null);
    }
}
