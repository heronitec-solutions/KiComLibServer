using KiComLibBackend.Services;
using Npgsql;

namespace KiComLibBackend.Tests;

public class CatalogConstraintErrorsTests
{
    [Fact]
    public void UniqueName_NamesTheFieldAndWhy()
    {
        Dictionary<string, string[]>? errors = CatalogConstraintErrors.Describe(
            PostgresErrorCodes.UniqueViolation,
            "part_name_key",
            null,
            "Key (name)=(R1) already exists.",
            "duplicate key value violates unique constraint \"part_name_key\"");

        Assert.NotNull(errors);
        Assert.Equal("This name is already used by another part.", Assert.Single(errors["name"]));
    }

    [Fact]
    public void UniqueName_UsesTheConstraintWhenTheDetailHasNoColumn()
    {
        Dictionary<string, string[]>? errors = CatalogConstraintErrors.Describe(
            PostgresErrorCodes.UniqueViolation,
            "part_name_key",
            null,
            null,
            "duplicate key");

        Assert.NotNull(errors);
        Assert.Equal("This name is already used by another part.", Assert.Single(errors["name"]));
    }

    [Fact]
    public void ForeignKey_NamesTheMissingSymbol()
    {
        Dictionary<string, string[]>? errors = CatalogConstraintErrors.Describe(
            PostgresErrorCodes.ForeignKeyViolation,
            "part_symbol_id_foreign",
            null,
            "Key (symbol_id)=(5) is not present in table \"symbol\".",
            "insert or update on table \"part\" violates foreign key constraint \"part_symbol_id_foreign\"");

        Assert.NotNull(errors);
        Assert.Equal("The selected symbol does not exist.", Assert.Single(errors["symbolId"]));
    }

    [Fact]
    public void NotNull_SaysTheFieldIsRequired()
    {
        Dictionary<string, string[]>? errors = CatalogConstraintErrors.Describe(
            PostgresErrorCodes.NotNullViolation,
            null,
            "name",
            "Failing row contains (1, null).",
            "null value in column \"name\" of relation \"part\" violates not-null constraint");

        Assert.NotNull(errors);
        Assert.Equal("This field is required.", Assert.Single(errors["name"]));
    }

    [Fact]
    public void Truncation_IncludesTheMaximumLength()
    {
        Dictionary<string, string[]>? errors = CatalogConstraintErrors.Describe(
            PostgresErrorCodes.StringDataRightTruncation,
            null,
            "value",
            null,
            "value too long for type character varying(100)");

        Assert.NotNull(errors);
        Assert.Equal("This value is too long (maximum 100 characters).", Assert.Single(errors["value"]));
    }

    [Fact]
    public void UnrelatedSqlState_IsLeftUnhandled()
    {
        Assert.Null(CatalogConstraintErrors.Describe(
            PostgresErrorCodes.ConnectionFailure,
            null,
            null,
            null,
            "connection failed"));
        Assert.Null(CatalogConstraintErrors.FromException(new InvalidOperationException("save failed")));
    }
}
