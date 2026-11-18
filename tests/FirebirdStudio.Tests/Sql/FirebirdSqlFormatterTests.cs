using FirebirdStudio.Desktop.Sql;

namespace FirebirdStudio.Tests.Sql;

public sealed class FirebirdSqlFormatterTests
{
    [Fact]
    public void FormatsMinifiedSelectIntoClausesAndLines()
    {
        const string sql = "select c.name,c.email,o.total from customers c inner join orders o on o.customer_id=c.id where c.active=1 order by c.name;";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("SELECT c.name,\n    c.email,\n    o.total", formatted);
        Assert.Contains("\nFROM customers c", formatted);
        Assert.Contains("\nINNER JOIN orders o", formatted);
        Assert.Contains("\nWHERE c.active = 1", formatted);
        Assert.Contains("\nORDER BY c.name;", formatted);
    }

    [Theory]
    [InlineData("select e.* from empresa e", "SELECT e.*\nFROM empresa e")]
    [InlineData("select e.name from empresa e", "SELECT e.name\nFROM empresa e")]
    public void KeepsQualifiedColumnsAndWildcardsTogether(string sql, string expected)
    {
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains(expected, formatted);
        Assert.DoesNotContain("e. *", formatted);
        Assert.DoesNotContain("e. name", formatted);
    }

    [Fact]
    public void FormatsCreateTableColumnAndConstraintDefinitionsOnSeparateLines()
    {
        const string sql = "create table customer(id integer not null,name varchar(100),created_at timestamp,constraint pk_customer primary key(id));";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("CREATE TABLE customer (\n    id INTEGER NOT NULL,\n    name VARCHAR(100),\n    created_at TIMESTAMP,\n    CONSTRAINT pk_customer PRIMARY KEY (id)\n);", formatted);
    }

    [Fact]
    public void KeepsAllCreateTableColumnsAtTheSameIndentationWithCharacterSets()
    {
        const string sql = "CREATE TABLE \"EMPRESA\" (\"COD_EMP\" INTEGER NOT NULL,\"NOME\" VARCHAR(200) CHARACTER SET \"NONE\" NOT NULL,\"ENDERECO\" VARCHAR(60) CHARACTER SET \"NONE\",\"COD_CIDADE\" INTEGER,\"DDD_FONE\" NUMERIC(2,0),CONSTRAINT \"PK_EMPRESA\" PRIMARY KEY (\"COD_EMP\"));";
        var formatted = FirebirdSqlFormatter.Format(sql);
        var lines = formatted.Split('\n');

        Assert.Contains("    \"COD_EMP\" INTEGER NOT NULL,", lines);
        Assert.Contains("    \"NOME\" VARCHAR(200) CHARACTER SET \"NONE\" NOT NULL,", lines);
        Assert.Contains("    \"ENDERECO\" VARCHAR(60) CHARACTER SET \"NONE\",", lines);
        Assert.Contains("    \"COD_CIDADE\" INTEGER,", lines);
        Assert.Contains("    \"DDD_FONE\" NUMERIC(2,0),", lines);
        Assert.Contains("    CONSTRAINT \"PK_EMPRESA\" PRIMARY KEY (\"COD_EMP\")", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("        ", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepsMultiwordJoinModifiersTogether()
    {
        var formatted = FirebirdSqlFormatter.Format("select c.id from customer c left outer join orders o on o.customer_id=c.id;");

        Assert.Contains("LEFT OUTER JOIN orders o", formatted);
    }

    [Fact]
    public void PreservesExistingMultilineClauseStructure()
    {
        const string sql = "SELECT\n    ID,\n    NAME\nFROM CUSTOMER\nWHERE ACTIVE = 1;";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("SELECT\n    ID,\n    NAME\nFROM CUSTOMER\nWHERE ACTIVE = 1;", formatted);
    }

    [Fact]
    public void DoesNotRewriteStringLiteralsOrQuotedIdentifiers()
    {
        const string sql = "select \"select\", 'from  where  x', 'it''s ok' from customer where name='a  b';";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("\"select\"", formatted);
        Assert.Contains("'from  where  x'", formatted);
        Assert.Contains("'it''s ok'", formatted);
        Assert.Contains("'a  b'", formatted);
    }

    [Fact]
    public void KeepsCommentsAsCommentsAndOnTheirOwnLine()
    {
        const string sql = "select id from customer -- keep this\nwhere id=1 /* and this */;";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("-- keep this", formatted);
        Assert.Contains("/* and this */", formatted);
        Assert.Contains("\nWHERE id = 1", formatted);
    }

    [Fact]
    public void FormatsUpdateAssignmentsOnSeparateLines()
    {
        var formatted = FirebirdSqlFormatter.Format("update customer set name='A',active=1 where id=2;");

        Assert.Contains("UPDATE customer\nSET name = 'A',\n    active = 1\nWHERE id = 2;", formatted);
    }

    [Fact]
    public void HandlesProceduralBeginEndWithoutChangingLiterals()
    {
        const string sql = "execute block as begin if (1=1) then begin insert into log_table(message) values('begin end'); end end";
        var formatted = FirebirdSqlFormatter.Format(sql);

        Assert.Contains("BEGIN\n", formatted);
        Assert.Contains("'begin end'", formatted);
        Assert.Contains("END", formatted);
    }

    [Fact]
    public void PreservesNamedParameterTokens()
    {
        var formatted = FirebirdSqlFormatter.Format("select * from customer where id=:CUSTOMER_ID;");

        Assert.Contains("id = :CUSTOMER_ID", formatted);
    }

    [Fact]
    public void EmptyInputRemainsEmpty()
    {
        Assert.Equal(string.Empty, FirebirdSqlFormatter.Format("  \r\n "));
    }
}
