namespace FinanceGoals.Migrator.Tests;

/// <summary>The snapshot must notice every kind of change the scripts could make.</summary>
[TestClass]
public sealed class SchemaSnapshotTests
{
    [TestMethod]
    [DataRow("ALTER TABLE app.Currency ALTER COLUMN Name nvarchar(200) NOT NULL;")]
    [DataRow("ALTER TABLE app.Currency ADD Symbol nvarchar(5) NULL;")]
    [DataRow("CREATE INDEX IX_Currency_Name ON app.Currency (Name);")]
    [DataRow("ALTER TABLE app.Currency ADD CONSTRAINT CK_Currency_Code CHECK (LEN(Code) = 3);")]
    [DataRow("EXEC (N'CREATE VIEW app.CurrencyCodes AS SELECT Code FROM app.Currency');")]
    [DataRow("GRANT VIEW DEFINITION TO app_data;")]
    [DataRow("CREATE USER extra WITHOUT LOGIN;")]
    [DataRow("UPDATE app.Currency SET Name = N'Norwegian Krone' WHERE Code = 'NOK';")]
    [DataRow("DELETE FROM app.Currency WHERE Code = 'RON';")]
    [DataRow("DELETE FROM dbo.SeedVersions;")]
    public async Task Detects_a_change(string change)
    {
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);
        var before = await SchemaSnapshot.TakeAsync(database, includeTimestamps: false);

        await SqlServer.ExecuteAsync(database, change);

        (await SchemaSnapshot.TakeAsync(database, includeTimestamps: false)).ShouldNotBe(before);
    }
}
