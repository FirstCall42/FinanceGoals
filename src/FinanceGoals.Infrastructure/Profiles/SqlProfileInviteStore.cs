using System.Data;
using FinanceGoals.Application.Profiles;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace FinanceGoals.Infrastructure.Profiles;

/// <summary>Reads and writes app.ProfileInvite with the API's own connection (the app user).</summary>
internal sealed class SqlProfileInviteStore : IProfileInviteStore
{
    private const string _createSql = """
        INSERT INTO app.ProfileInvite (ProfileId, Email, TokenHash, ExpiresAt, CreatedBy, UpdatedBy)
        OUTPUT inserted.Id, inserted.ExpiresAt
        VALUES (@ProfileId, @Email, @TokenHash, DATEADD(second, @LifetimeSeconds, SYSUTCDATETIME()), @UserId, @UserId);
        """;

    private const string _pendingSql = """
        SELECT Id, Email, ExpiresAt FROM app.ProfileInvite
        WHERE ProfileId = @ProfileId AND AcceptedAt IS NULL AND ExpiresAt > SYSUTCDATETIME()
        ORDER BY Id;
        """;

    // Locks the invite row, so the same link used twice at once adds the member once.
    private const string _acceptSql = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @UserId int = (SELECT Id FROM app.AppUser WHERE GoogleSubject = @GoogleSubject);
        DECLARE @InviteId int, @ProfileId int, @InviteEmail nvarchar(320);
        SELECT @InviteId = Id, @ProfileId = ProfileId, @InviteEmail = Email
        FROM app.ProfileInvite WITH (UPDLOCK, ROWLOCK)
        WHERE TokenHash = @TokenHash AND AcceptedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();
        IF @InviteId IS NULL OR @UserId IS NULL
            SELECT 0, CAST(NULL AS int);
        ELSE IF LOWER(@InviteEmail) <> LOWER(@Email)
            SELECT 2, CAST(NULL AS int);
        ELSE
        BEGIN
            UPDATE app.ProfileInvite
            SET AcceptedAt = SYSUTCDATETIME(), AcceptedBy = @UserId, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
            WHERE Id = @InviteId;
            IF NOT EXISTS (SELECT 1 FROM app.ProfileMember WHERE ProfileId = @ProfileId AND UserId = @UserId)
                INSERT INTO app.ProfileMember (ProfileId, UserId, Role, CreatedBy, UpdatedBy)
                VALUES (@ProfileId, @UserId, 'Member', @UserId, @UserId);
            SELECT 1, @ProfileId;
        END;
        COMMIT TRANSACTION;
        """;

    private readonly IConfiguration _configuration;

    public SqlProfileInviteStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<CreatedInvite> CreateAsync(int profileId, string email, int byUserId, CancellationToken cancellationToken)
    {
        var (token, hash) = InviteTokens.New();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_createSql, connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        command.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 320) { Value = email });
        command.Parameters.Add(new SqlParameter("@TokenHash", SqlDbType.Binary, 32) { Value = hash });
        command.Parameters.Add(new SqlParameter("@LifetimeSeconds", SqlDbType.Int) { Value = (int)InviteTokens.Lifetime.TotalSeconds });
        command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.Int) { Value = byUserId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CreatedInvite(reader.GetInt32(0), email, DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), token);
    }

    public async Task<IReadOnlyList<PendingInvite>> PendingAsync(int profileId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_pendingSql, connection);
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var invites = new List<PendingInvite>();
        while (await reader.ReadAsync(cancellationToken))
        {
            invites.Add(new PendingInvite(reader.GetInt32(0), reader.GetString(1), DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
        }

        return invites;
    }

    public async Task<bool> RevokeAsync(int profileId, int inviteId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE FROM app.ProfileInvite WHERE Id = @InviteId AND ProfileId = @ProfileId AND AcceptedAt IS NULL;",
            connection);
        command.Parameters.Add(new SqlParameter("@InviteId", SqlDbType.Int) { Value = inviteId });
        command.Parameters.Add(new SqlParameter("@ProfileId", SqlDbType.Int) { Value = profileId });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<(InviteOutcome Outcome, int? ProfileId)> AcceptAsync(string token, string googleSubject, string email, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(_acceptSql, connection);
        command.Parameters.Add(new SqlParameter("@TokenHash", SqlDbType.Binary, 32) { Value = InviteTokens.Hash(token) });
        command.Parameters.Add(new SqlParameter("@GoogleSubject", SqlDbType.VarChar, 255) { Value = googleSubject });
        command.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 320) { Value = email });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((InviteOutcome)reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

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
