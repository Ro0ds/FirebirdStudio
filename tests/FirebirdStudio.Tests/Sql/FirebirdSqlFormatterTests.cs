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
    public void EmptyInputRemainsEmpty()
    {
        Assert.Equal(string.Empty, FirebirdSqlFormatter.Format("  \r\n "));
    }
}
