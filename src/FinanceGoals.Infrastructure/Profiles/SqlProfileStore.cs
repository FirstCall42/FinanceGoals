using System.Data;
using FinanceGoals.Application.Profiles;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Profiles;

/// <summary>Reads and writes app.Profile and app.ProfileMember with the API's own connection (the app user).</summary>
internal sealed class SqlProfileStore : IProfileStore
{
    // Creates the first profile under a lock on the user's memberships, so two first sign-ins at
    // once give the user one profile, not two.
    private const string _ensureSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        IF NOT EXISTS (SELECT 1 FROM app.ProfileMember WITH (UPDLOCK, SERIALIZABLE) WHERE UserId = @UserId)
        BEGIN
            DECLARE @ProfileId int;
            INSERT INTO app.Profile (Name, CreatedBy, UpdatedBy) VALUES (@DefaultName, @UserId, @UserId);
            SET @ProfileId = SCOPE_IDENTITY();
            INSERT INTO app.ProfileMember (ProfileId, UserId, Role, CreatedBy, UpdatedBy)
            VALUES (@ProfileId, @UserId, 'Owner', @UserId, @UserId);
        END;
        SELECT p.Id, p.Name, m.Role
        FROM app.ProfileMember m JOIN app.Profile p ON p.Id = m.ProfileId
        WHERE m.UserId = @UserId
        ORDER BY p.Id;
        COMMIT TRANSACTION;
        """;

    private const string _accessSql = """
        SELECT m.UserId, m.Role
        FROM app.ProfileMember m JOIN app.AppUser u ON u.Id = m.UserId
        WHERE u.GoogleSubject = @GoogleSubject AND m.ProfileId = @ProfileId;
        """;

    private readonly IConfiguration _configuration;

    public SqlProfileStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<ProfileSummary>> EnsureForUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_ensureSql, connection);
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
        command.Parameters.Add(new SqlParameter("@DefaultName", SqlDbType.NVarChar, 100) { Value = IProfileStore.DefaultName });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new List<ProfileSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new ProfileSummary(reader.GetInt32(0), reader.GetString(1), ParseRole(reader.GetString(2))));
        }

        return profiles;
    }

    public async Task<IReadOnlyList<ProfileSummary>> ForUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT p.Id, p.Name, m.Role
            FROM app.ProfileMember m JOIN app.Profile p ON p.Id = m.ProfileId
            WHERE m.UserId = @UserId
            ORDER BY p.Id;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = userId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new List<ProfileSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new ProfileSummary(reader.GetInt32(0), reader.GetString(1), ParseRole(reader.GetString(2))));
        }

        return profiles;
    }

    public async Task<ProfileAccess?> AccessAsync(string googleSubject, int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_accessSql, connection);
        command.Parameters.Add(new SqlParameter("@GoogleSubject", SqlDbType.VarChar, 255) { Value = googleSubject });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProfileAccess(profileId, reader.GetInt32(0), ParseRole(reader.GetString(1)))
            : null;
    }

    public async Task<string?> NameAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT Name FROM app.Profile WHERE Id = @ProfileId;", connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task RenameAsync(int profileId, string name, int byUserId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "UPDATE app.Profile SET Name = @Name, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId WHERE Id = @ProfileId;",
            connection);
        command.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 100) { Value = name });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProfileMember>> MembersAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT u.Id, u.Email, u.Name, m.Role
            FROM app.ProfileMember m JOIN app.AppUser u ON u.Id = m.UserId
            WHERE m.ProfileId = @ProfileId
            ORDER BY CASE m.Role WHEN 'Owner' THEN 0 ELSE 1 END, u.Email;
            """,
            connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var members = new List<ProfileMember>();
        while (await reader.ReadAsync(cancellationToken))
        {
            members.Add(new ProfileMember(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ParseRole(reader.GetString(3))));
        }

        return members;
    }

    private static ProfileRole ParseRole(string role) => Enum.Parse<ProfileRole>(role);

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
