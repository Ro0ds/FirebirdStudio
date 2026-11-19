using FirebirdStudio.Application.Sql;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Tests.Sql;

public sealed class FirebirdDdlPreviewTests
{
    [Fact]
    public void BuildsTablePreviewWithConsistentIndentation()
    {
        DatabaseMetadataObject[] fields =
        [
            new("Column", "Amount", "Orders", "type code: 7; subtype: 1; precision: 2; scale: 0; not null"),
            new("Column", "Label", "Orders", "type code: 37; character length: 30; charset: UTF8")
        ];
        DatabaseMetadataObject[] constraints =
        [
            new("Constraint", "FK_Orders", "Orders", "Type: FOREIGN KEY; ON DELETE CASCADE",
                [new("Key column", "Amount", null, "references: Parent.Amount"),
                 new("Key column", "Label", null, "references: Parent.Label")])
        ];

        var preview = FirebirdDdlPreview.BuildTableDdl("Orders", fields, constraints).ReplaceLineEndings("\n");

        Assert.StartsWith(
            """
            CREATE TABLE "Orders" (
                "Amount" NUMERIC(2, 0) NOT NULL,
                "Label" VARCHAR(30) CHARACTER SET "UTF8",
                CONSTRAINT "FK_Orders" FOREIGN KEY ("Amount", "Label") REFERENCES "Parent" ("Amount", "Label") ON DELETE CASCADE
            );

            -- Generated from the metadata currently exposed by Firebird Studio.
            """, preview);
    }

    [Fact]
    public void FormatsViewSourceWithAliasesAndSubquery()
    {
        const string source = "select c.id as \"Order Id\", (select max(o.total) from orders o where o.customer_id=c.id) as total from customers c";

        var preview = FirebirdDdlPreview.FormatSource("View source", source).ReplaceLineEndings("\n");

        Assert.Equal(
            "SELECT c.id AS \"Order Id\",\n    (SELECT max(o.total) FROM orders o WHERE o.customer_id = c.id) AS total\nFROM customers c\n",
            preview);
    }

    [Theory]
    [InlineData("Trigger source")]
    [InlineData("Procedure source")]
    public void AlignsClausesWithinCatalogSourceBlocks(string kind)
    {
        const string source = "as declare variable x integer; begin if (x=0) then begin " +
                              "select case when id=1 then 'A  B' else 'C' end from customer where id=:action into :x; " +
                              "-- preserve this comment\nend end";

        var preview = FirebirdDdlPreview.FormatSource(kind, source).ReplaceLineEndings("\n");

        Assert.StartsWith("AS\nDECLARE variable x INTEGER;\nBEGIN\n    IF", preview);
        Assert.Contains("\n        SELECT CASE\n                WHEN id = 1 THEN 'A  B'\n                ELSE 'C'\n            END", preview);
        Assert.Contains("\n        FROM customer\n        WHERE id = :action INTO :x;", preview);
        Assert.Contains("\n        -- preserve this comment\n    END\nEND\n", preview);
    }

    [Theory]
    [InlineData("Domain default", "DEFAULT 'a  b'")]
    [InlineData("Domain validation", "CHECK (VALUE > 0)")]
    public void PreservesCatalogDomainExpressions(string kind, string source)
    {
        Assert.Equal(source, FirebirdDdlPreview.FormatSource(kind, source));
    }
}
