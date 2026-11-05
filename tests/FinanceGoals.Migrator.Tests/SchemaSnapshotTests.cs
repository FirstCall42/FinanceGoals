namespace FinanceGoals.Migrator.Tests;

/// <summary>The snapshot must notice every kind of change the scripts could make.</summary>
[TestClass]
public sealed class SchemaSnapshotTests
{
    private const string _setup = """
        CREATE TABLE app.Pair (A int NULL, B int NULL, Note varchar(20) NULL, Doc xml NULL);
        CREATE TABLE app.Empty (P int NULL, Q int NULL, S AS (P + Q));
        INSERT INTO app.Pair (A, B, Note, Doc) VALUES (1, 2, 'x', N'<a>1</a>');
        CREATE TABLE app.Account (Id int NOT NULL CONSTRAINT PK_Account PRIMARY KEY,
            CurrencyCode char(3) NOT NULL CONSTRAINT FK_Account_Currency REFERENCES app.Currency (Code),
            CONSTRAINT CK_Account_Id CHECK (Id > 0));
        EXEC (N'CREATE TRIGGER app.TR_Account ON app.Account AFTER INSERT AS SET NOCOUNT ON;');
        EXEC (N'CREATE SCHEMA other AUTHORIZATION dbo');
        EXEC (N'CREATE TABLE other.Account (Id int NULL)');
        CREATE USER reader WITHOUT LOGIN;
        GRANT SELECT ON app.Pair (A) TO reader;
        GRANT SELECT ON app.Account TO reader;
        """;

    [TestMethod]
    // Columns (app.Empty has no rows, so only its definition can differ)
    [DataRow("ALTER TABLE app.Currency ALTER COLUMN Name nvarchar(200) NOT NULL;")]
    [DataRow("ALTER TABLE app.Currency ADD Symbol nvarchar(5) NULL;")]
    [DataRow("ALTER TABLE app.Empty DROP COLUMN S; ALTER TABLE app.Empty ADD S AS (P - Q);")]
    [DataRow("ALTER TABLE app.Empty DROP COLUMN S, P; ALTER TABLE app.Empty ADD P int NULL; ALTER TABLE app.Empty ADD S AS (P + Q);")]
    [DataRow("ALTER TABLE app.Pair ADD CONSTRAINT DF_Pair_Note DEFAULT ('n') FOR Note;")]
    // Constraints, indexes, triggers and code
    [DataRow("CREATE INDEX IX_Currency_Name ON app.Currency (Name);")]
    [DataRow("ALTER TABLE app.Currency ADD CONSTRAINT CK_Currency_Code CHECK (LEN(Code) = 3);")]
    [DataRow("ALTER TABLE app.Account NOCHECK CONSTRAINT CK_Account_Id;")]
    [DataRow("ALTER TABLE app.Account NOCHECK CONSTRAINT FK_Account_Currency;")]
    [DataRow("ALTER TABLE app.Account DROP CONSTRAINT FK_Account_Currency; ALTER TABLE app.Account ADD CONSTRAINT FK_Account_Currency FOREIGN KEY (CurrencyCode) REFERENCES app.Currency (Code) ON UPDATE CASCADE;")]
    [DataRow("ALTER TABLE app.Account DROP CONSTRAINT FK_Account_Currency; ALTER TABLE app.Account WITH NOCHECK ADD CONSTRAINT FK_Account_Currency FOREIGN KEY (CurrencyCode) REFERENCES app.Currency (Code);")]
    [DataRow("DISABLE TRIGGER app.TR_Account ON app.Account;")]
    [DataRow("EXEC (N'ALTER TRIGGER app.TR_Account ON app.Account AFTER INSERT AS SET NOCOUNT OFF;');")]
    [DataRow("EXEC (N'CREATE VIEW app.CurrencyCodes AS SELECT Code FROM app.Currency');")]
    // Ownership and security
    [DataRow("ALTER AUTHORIZATION ON SCHEMA::other TO reader;")]
    [DataRow("ALTER AUTHORIZATION ON OBJECT::app.Empty TO reader;")]
    [DataRow("GRANT VIEW DEFINITION TO app_data;")]
    [DataRow("REVOKE SELECT ON app.Pair (A) FROM reader; GRANT SELECT ON app.Pair (B) TO reader;")]
    [DataRow("REVOKE SELECT ON app.Account FROM reader; GRANT SELECT ON other.Account TO reader;")]
    [DataRow("CREATE USER extra WITHOUT LOGIN;")]
    // Data and journals
    // Case only: the snapshot must see a change a case-insensitive comparison would miss.
    [DataRow("UPDATE app.Currency SET Name = N'Norwegian KRONE' WHERE Code = 'NOK';")]
    [DataRow("DELETE FROM app.Currency WHERE Code = 'RON';")]
    [DataRow("UPDATE app.Pair SET Note = 'x ';")]
    [DataRow("UPDATE app.Pair SET Doc = N'<a>2</a>';")]
    [DataRow("DELETE FROM dbo.SeedVersions;")]
    public async Task Detects_a_change(string change)
    {
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);
        await SqlServer.ExecuteAsync(database, _setup);
        var before = await SchemaSnapshot.TakeAsync(database, includeTimestamps: false);

        await SqlServer.ExecuteAsync(database, change);

        (await SchemaSnapshot.TakeAsync(database, includeTimestamps: false)).ShouldNotBe(before);
    }

    [TestMethod]
    public async Task Same_schema_built_by_different_steps_matches()
    {
        // An upgraded database can reach the fresh build's schema by a longer route; internal ids
        // such as a dropped column's gap must not count as a difference.
        var direct = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(direct, TextWriter.Null).Run(appUser: null);
        await SqlServer.ExecuteAsync(direct, "CREATE TABLE app.Pair (A int NULL, B int NULL);");

        var stepwise = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(stepwise, TextWriter.Null).Run(appUser: null);
        await SqlServer.ExecuteAsync(stepwise, """
            CREATE TABLE app.Scratch (Id int NULL);
            DROP TABLE app.Scratch;
            CREATE TABLE app.Pair (A int NULL, Gone int NULL, B int NULL);
            ALTER TABLE app.Pair DROP COLUMN Gone;
            """);

        (await SchemaSnapshot.TakeAsync(stepwise, includeTimestamps: false))
            .ShouldBe(await SchemaSnapshot.TakeAsync(direct, includeTimestamps: false));
    }
}
