using FirebirdStudio.Application.Sql;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Tests.Application;

public sealed class SqlCompletionCatalogTests
{
    private static readonly IReadOnlyList<DatabaseMetadataObject> Schema =
    [
        new("Table", "CUSTOMER", null, string.Empty,
        [
            new DatabaseMetadataObject("Column", "CUSTOMER_ID", "CUSTOMER", string.Empty),
            new DatabaseMetadataObject("Column", "CUSTOMER_NAME", "CUSTOMER", string.Empty)
        ]),
        new("View", "ACTIVE_CUSTOMERS", null, string.Empty, []),
        new("Procedure", "P_CUSTOMER", null, string.Empty),
        new("Domain", "D_INTEGER", null, string.Empty)
    ];

    [Fact]
    public void AliasCompletionResolvesRelationDeclaredAfterCaret()
    {
        const string sql = "SELECT c.CUSTOMER_I FROM CUSTOMER c";
        var caret = sql.IndexOf("CUSTOMER_I", StringComparison.Ordinal) + "CUSTOMER_I".Length;

        var suggestions = SqlCompletionCatalog.Build(sql, caret, Schema);

        Assert.Contains(suggestions, item => item.Text == "CUSTOMER_ID" && item.Description.Contains("CUSTOMER"));
        Assert.DoesNotContain(suggestions, item => item.Text == "CUSTOMER_NAME");
    }

    [Theory]
    [InlineData("SELECT * FROM CUS", "CUSTOMER")]
    [InlineData("UPDATE CUS", "CUSTOMER")]
    [InlineData("DELETE FROM CUS", "CUSTOMER")]
    [InlineData("INSERT INTO CUS", "CUSTOMER")]
    public void RelationContextSuggestsTablesAndViews(string sql, string expected)
    {
        var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);

        Assert.Contains(suggestions, item => item.Text == expected);
    }

    [Fact]
    public void SelectFromCanSuggestSelectableProcedures()
    {
        const string sql = "SELECT * FROM P_CUS";
        var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);

        Assert.Contains(suggestions, item => item.Text == "P_CUSTOMER");
    }

    [Fact]
    public void UpdateAndDeleteDoNotSuggestProceduresAsTargetRelations()
    {
        foreach (var sql in new[] { "UPDATE P_CUS", "DELETE FROM P_CUS", "INSERT INTO P_CUS" })
        {
            var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);
            Assert.DoesNotContain(suggestions, item => item.Text == "P_CUSTOMER");
        }
    }

    [Theory]
    [InlineData("UPDATE CUSTOMER c SET c.CUSTOMER_I", "CUSTOMER_ID")]
    [InlineData("DELETE FROM CUSTOMER c WHERE c.CUSTOMER_I", "CUSTOMER_ID")]
    public void AliasesResolveForUpdateAndDeleteStatements(string sql, string expected)
    {
        var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);

        Assert.Contains(suggestions, item => item.Text == expected);
    }

    [Theory]
    [InlineData("SELECT CAST(1 AS D_INT")]
    [InlineData("CREATE TABLE CUSTOMER (CUSTOMER_ID D_INT")]
    [InlineData("ALTER TABLE CUSTOMER ADD CUSTOMER_ID D_INT")]
    public void TypeContextSuggestsDatabaseDomains(string sql)
    {
        var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);

        Assert.Contains(suggestions, item => item.Text == "D_INTEGER");
    }

    [Theory]
    [InlineData("SELECT CAST(1 AS ", "")]
    [InlineData("SELECT CAST(1 AS D_INT", "D_INT")]
    [InlineData("UPDATE CUSTOMER c SET c.CUSTOMER_I", "CUSTOMER_I")]
    public void PrefixIsReadFromTheCaretPosition(string sql, string expected)
    {
        Assert.Equal(expected, SqlCompletionCatalog.GetPrefix(sql, sql.Length));
    }

    [Theory]
    [InlineData("SELECT CAST(1 AS D_INT", true)]
    [InlineData("CREATE TABLE CUSTOMER (CUSTOMER_ID D_INT", true)]
    [InlineData("SELECT * FROM CUSTOMER", false)]
    public void DomainCatalogIsOnlyRequestedInTypeContexts(string sql, bool expected)
    {
        Assert.Equal(expected, SqlCompletionCatalog.NeedsDomainSuggestions(sql, sql.Length));
    }

    [Theory]
    [InlineData("CREATE TABLE CUSTOMER (ID INTEGER)", true)]
    [InlineData("ALTER TABLE CUSTOMER ADD NAME VARCHAR(50)", true)]
    [InlineData("DROP VIEW ACTIVE_CUSTOMERS", true)]
    [InlineData("SELECT * FROM CUSTOMER", false)]
    [InlineData("-- CREATE TABLE NOT_DDL\nSELECT 1 FROM RDB$DATABASE", false)]
    public void SchemaChangesInvalidateCompletionCache(string sql, bool expected)
    {
        Assert.Equal(expected, SqlCompletionCatalog.MayChangeSchema(sql));
    }

    [Fact]
    public void GeneralContextIncludesFirebirdKeywordsAndFunctions()
    {
        const string sql = "SELECT ";

        var suggestions = SqlCompletionCatalog.Build(sql, sql.Length, Schema);

        Assert.Contains(suggestions, item => item.Text == "SELECT");
        Assert.Contains(suggestions, item => item.Text == "COALESCE");
        Assert.Contains(suggestions, item => item.Text == "INTEGER");
    }
}
