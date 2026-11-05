using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace FinanceGoals.Migrator.Tests;

/// <summary>
/// One SQL Server container for the test run. Each test gets its own database, provisioned
/// the way an environment would be: a contained database whose schema owner is a contained
/// user in db_owner. The migrator then connects as that owner, never as sa.
/// </summary>
[TestClass]
public static class SqlServer
{
    private const string _image = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    private static MsSqlContainer? _container;

    [AssemblyInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _container = new MsSqlBuilder(_image).Build();
        await _container.StartAsync(context.CancellationToken);

        await ExecuteAsync(_container.GetConnectionString(), """
            EXEC sp_configure 'contained database authentication', 1;
            RECONFIGURE;
            """);
    }

    [AssemblyCleanup]
    public static async Task StopAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates a database and its schema owner; returns the owner's connection string.</summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var name = $"fg_{Guid.NewGuid():N}";
        var ownerPassword = NewPassword();

        await ExecuteAsync(Container.GetConnectionString(), $"CREATE DATABASE [{name}] CONTAINMENT = PARTIAL;");
        var database = WithDatabase(Container.GetConnectionString(), name);
        await ExecuteAsync(database, $"""
            CREATE USER schema_owner WITH PASSWORD = '{ownerPassword}';
            ALTER ROLE db_owner ADD MEMBER schema_owner;
            """);

        return As(database, "schema_owner", ownerPassword);
    }

    /// <summary>The same database, signed in as another contained user.</summary>
    public static string As(string connectionString, string user, string password) =>
        new SqlConnectionStringBuilder(connectionString) { UserID = user, Password = password }.ConnectionString;

    public static string NewPassword() => $"Pw1!{Guid.NewGuid():N}";

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        var rows = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join("|", values));
        }

        return rows;
    }

    private static MsSqlContainer Container =>
        _container ?? throw new InvalidOperationException("The SQL Server container has not started.");

    private static string WithDatabase(string connectionString, string database) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;
}
