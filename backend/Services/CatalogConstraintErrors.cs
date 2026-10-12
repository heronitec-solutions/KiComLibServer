using System.Text.RegularExpressions;
using Npgsql;

namespace KiComLibBackend.Services
{
    /// <summary>
    /// Turns a PostgreSQL constraint failure into field names and reasons the editor can show.
    /// </summary>
    public static class CatalogConstraintErrors
    {
        private static readonly Regex DetailColumns = new(@"Key \((?<columns>[^)]+)\)", RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> ColumnFields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name",
            ["value"] = "value",
            ["comment"] = "comment",
            ["datasheet"] = "datasheet",
            ["datasheet_id"] = "datasheetId",
            ["library_id"] = "libraryId",
            ["symbol_id"] = "symbolId",
            ["footprint_id"] = "footprintId",
            ["keywords"] = "keywords",
            ["manufacturer_part_number"] = "manufacturerPartNumber",
            ["package_type"] = "packageType",
            ["quantity"] = "quantity",
            ["order_number"] = "orderNumberValue",
            ["quantity_min"] = "quantityMin",
            ["quantity_steps"] = "quantitySteps",
            ["manufacturer_id"] = "manufacturerId",
            ["distributor_id"] = "distributorId",
            ["part_number_id"] = "partNumberId",
            ["part_id"] = "partId",
            ["uuid"] = "uuid"
        };

        private static readonly Dictionary<string, string> ConstraintFields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["part_name_key"] = "name",
            ["part_uuid_key"] = "uuid",
            ["part_library_id_foreign"] = "libraryId",
            ["part_symbol_id_foreign"] = "symbolId",
            ["part_footprint_id_foreign"] = "footprintId",
            ["part_datasheet_id_foreign"] = "datasheetId",
            ["part_manufacturer_id_foreign"] = "manufacturerId",
            ["part_id_foreign"] = "partId",
            ["part_number_id_foreign"] = "partNumberId",
            ["distributor_id_foreign"] = "distributorId"
        };

        public static Dictionary<string, string[]>? FromException(Exception exception)
        {
            PostgresException? postgres = FindPostgres(exception);
            if (postgres == null)
            {
                return null;
            }

            return Describe(
                postgres.SqlState,
                postgres.ConstraintName,
                postgres.ColumnName,
                postgres.Detail,
                postgres.MessageText);
        }

        public static Dictionary<string, string[]>? Describe(
            string sqlState,
            string? constraintName,
            string? columnName,
            string? detail,
            string? messageText)
        {
            if (!IsDataConstraint(sqlState))
            {
                return null;
            }

            List<string> fields = Fields(columnName, constraintName, detail);
            Dictionary<string, List<string>> grouped = new(StringComparer.Ordinal);
            if (fields.Count == 0)
            {
                Add(grouped, "", Reason(sqlState, "", detail, messageText));
            }
            else
            {
                foreach (string field in fields)
                {
                    Add(grouped, field, Reason(sqlState, field, detail, messageText));
                }
            }

            return grouped.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        private static List<string> Fields(string? columnName, string? constraintName, string? detail)
        {
            List<string> columns = new();
            if (!string.IsNullOrWhiteSpace(columnName))
            {
                columns.Add(columnName.Trim().Trim('"'));
            }
            else
            {
                columns.AddRange(ColumnsFromDetail(detail));
            }

            List<string> fields = new();
            foreach (string column in columns)
            {
                fields.Add(ColumnFields.TryGetValue(column, out string? mapped) ? mapped : column);
            }

            if (fields.Count == 0
                && !string.IsNullOrWhiteSpace(constraintName)
                && ConstraintFields.TryGetValue(constraintName, out string? fromConstraint))
            {
                fields.Add(fromConstraint);
            }

            return fields;
        }

        private static IEnumerable<string> ColumnsFromDetail(string? detail)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                yield break;
            }

            Match match = DetailColumns.Match(detail);
            if (!match.Success)
            {
                yield break;
            }

            foreach (string column in match.Groups["columns"].Value.Split(','))
            {
                string name = column.Trim().Trim('"');
                if (name.Length > 0)
                {
                    yield return name;
                }
            }
        }

        private static string Reason(string sqlState, string field, string? detail, string? messageText)
        {
            switch (sqlState)
            {
                case PostgresErrorCodes.UniqueViolation:
                    return field switch
                    {
                        "name" => "This name is already used by another part.",
                        "uuid" => "This identifier is already used.",
                        _ => "This value is already used."
                    };
                case PostgresErrorCodes.ForeignKeyViolation:
                    return field switch
                    {
                        "libraryId" => "The selected library does not exist.",
                        "symbolId" => "The selected symbol does not exist.",
                        "footprintId" => "The selected footprint does not exist.",
                        "datasheetId" => "The selected datasheet does not exist.",
                        "manufacturerId" => "The selected manufacturer does not exist.",
                        "distributorId" => "The selected distributor does not exist.",
                        "partId" => "The selected part does not exist.",
                        "partNumberId" => "The selected part number does not exist.",
                        _ => "The selected value does not exist."
                    };
                case PostgresErrorCodes.NotNullViolation:
                    return "This field is required.";
                case PostgresErrorCodes.StringDataRightTruncation:
                    return LengthReason(messageText);
                case PostgresErrorCodes.CheckViolation:
                    return FirstText(detail, messageText) ?? "This value is not allowed.";
                default:
                    return FirstText(detail, messageText) ?? "This value is not valid.";
            }
        }

        private static string LengthReason(string? messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                return "This value is too long.";
            }

            Match match = Regex.Match(messageText, @"character varying\((?<length>\d+)\)", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return $"This value is too long (maximum {match.Groups["length"].Value} characters).";
            }

            return "This value is too long.";
        }

        private static bool IsDataConstraint(string sqlState)
        {
            return sqlState is PostgresErrorCodes.UniqueViolation
                or PostgresErrorCodes.ForeignKeyViolation
                or PostgresErrorCodes.NotNullViolation
                or PostgresErrorCodes.StringDataRightTruncation
                or PostgresErrorCodes.CheckViolation;
        }

        private static PostgresException? FindPostgres(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is PostgresException postgres)
                {
                    return postgres;
                }
            }

            return null;
        }

        private static void Add(Dictionary<string, List<string>> grouped, string field, string reason)
        {
            if (!grouped.TryGetValue(field, out List<string>? reasons))
            {
                reasons = new List<string>();
                grouped[field] = reasons;
            }

            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }

        private static string? FirstText(string? detail, string? messageText)
        {
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail.Trim();
            }

            if (!string.IsNullOrWhiteSpace(messageText))
            {
                return messageText.Trim();
            }

            return null;
        }
    }
}
