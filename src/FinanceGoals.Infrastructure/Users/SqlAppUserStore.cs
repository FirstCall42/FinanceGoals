using FinanceGoals.Application.Users;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Users;

/// <summary>Reads and writes app.AppUser with the API's own connection (the app user).</summary>
internal sealed class SqlAppUserStore : IAppUserStore
{
    // Update-or-insert under a key-range lock, so two first sign-ins at once can't both insert.
    private const string _sql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        UPDATE app.AppUser WITH (UPDLOCK, SERIALIZABLE)
        SET Email = @Email, Name = @Name, LastSignInAt = SYSUTCDATETIME()
        WHERE GoogleSubject = @GoogleSubject;
        IF @@ROWCOUNT = 0
            INSERT INTO app.AppUser (GoogleSubject, Email, Name) VALUES (@GoogleSubject, @Email, @Name);
        SELECT Id, Email, Name FROM app.AppUser WHERE GoogleSubject = @GoogleSubject;
        COMMIT TRANSACTION;
        """;

    private readonly IConfiguration _configuration;

    public SqlAppUserStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<User> SignInAsync(string googleSubject, string email, string? name, CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_sql, connection);
        command.Parameters.Add(new SqlParameter("@GoogleSubject", System.Data.SqlDbType.VarChar, 255) { Value = googleSubject });
        command.Parameters.Add(new SqlParameter("@Email", System.Data.SqlDbType.NVarChar, 320) { Value = email });
        command.Parameters.Add(new SqlParameter("@Name", System.Data.SqlDbType.NVarChar, 200) { Value = (object?)name ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The signed-in user was not stored.");
        }

        return new User(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }
}
