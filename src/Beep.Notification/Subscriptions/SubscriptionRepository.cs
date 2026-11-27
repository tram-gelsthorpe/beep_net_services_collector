using System.Data;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Notification;

public sealed class SubscriptionRepository : ISubscriptionRepository
{
	private const int LoginAttempts = 10;

	private const string SelectSql = """
		SELECT n.Id, n.User_Id AS UserId, u.Login, u.EmailAddress AS Email, RTRIM(u.CultureInfoUI) AS Culture,
		       n.Website, n.NotificationType, n.Filter, n.IsConfirmed, n.Token, n.IsDeleted, n.LastSentAt
		FROM dbo.User_Notification n
		JOIN dbo.[User] u ON u.Id = n.User_Id
		""";

	private readonly string _connectionString;
	private readonly Guid _licenceId;
	private readonly Guid _createdBy;

	/// <param name="connectionString">A_1_Beep_Global_Default (<c>ConnectionStrings:Default</c>)</param>
	/// <param name="licenceId">Licence new users are created under</param>
	/// <param name="createdBy">User recorded in CreatedBy / ModifiedBy</param>
	public SubscriptionRepository(string connectionString, Guid licenceId, Guid createdBy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_connectionString = connectionString;
		_licenceId = licenceId;
		_createdBy = createdBy;
	}

	public async Task<SaveSubscriptionResult> SaveAsync(
		string email, string? culture, string website, string notificationType, string? filter,
		CancellationToken cancellationToken)
	{
		if (culture is { Length: > 5 })
			culture = culture[..5]; // dbo.User.CultureInfoUI is char(5)
		await using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

		// The licence's user with this address (deleted users are not reused).
		var userId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
			SELECT TOP (1) Id FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK)
			WHERE EmailAddress = @email AND Licence_Id = @licenceId AND IsDeleted = 0
			ORDER BY Created
			""", new { email, licenceId = _licenceId }, transaction, cancellationToken: cancellationToken));

		var isNewUser = userId is null;
		userId ??= await CreateUserAsync(connection, transaction, email, culture, cancellationToken);

		var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
			DECLARE @id uniqueidentifier = (SELECT Id FROM dbo.User_Notification WITH (UPDLOCK, HOLDLOCK)
			                               WHERE User_Id = @userId AND Website = @website AND NotificationType = @notificationType);
			IF @id IS NULL
			BEGIN
				DECLARE @inserted TABLE (Id uniqueidentifier);
				INSERT INTO dbo.User_Notification
					(Version, Created, CreatedBy, User_Id, Licence_Id, Website, NotificationType, Channel, Filter, Token)
				OUTPUT INSERTED.Id INTO @inserted
				VALUES (1, SYSDATETIMEOFFSET(), @createdBy, @userId, @licenceId, @website, @notificationType, 1, @filter, @token);
				SELECT Id FROM @inserted;
			END
			ELSE
			BEGIN
				UPDATE dbo.User_Notification
				SET Filter = @filter, IsDeleted = 0, Version = Version + 1,
				    Modified = SYSDATETIMEOFFSET(), ModifiedBy = @createdBy
				WHERE Id = @id;
				SELECT @id;
			END
			""", new
		{
			userId,
			website,
			notificationType,
			filter,
			licenceId = _licenceId,
			createdBy = _createdBy,
			token = NewToken(),
		}, transaction, cancellationToken: cancellationToken));

		// The language follows the latest subscription.
		if (!isNewUser && culture is not null)
			await connection.ExecuteAsync(new CommandDefinition(
				"UPDATE dbo.[User] SET CultureInfoUI = @culture WHERE Id = @userId AND ISNULL(CultureInfoUI, '') <> @culture",
				new { culture, userId }, transaction, cancellationToken: cancellationToken));

		var subscription = await connection.QuerySingleAsync<Subscription>(new CommandDefinition(
			SelectSql + " WHERE n.Id = @id", new { id }, transaction, cancellationToken: cancellationToken));

		await transaction.CommitAsync(cancellationToken);
		return new SaveSubscriptionResult(subscription, isNewUser);
	}

	// A user with a random 9-digit login (100000000–999999999), a random password nobody knows (subscribers
	// do not log in) and the email address as full name. A login already taken is drawn again.
	private async Task<Guid> CreateUserAsync(
		SqlConnection connection, SqlTransaction transaction, string email, string? culture, CancellationToken cancellationToken)
	{
		var password = BCrypt.Net.BCrypt.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

		for (var attempt = 1; ; attempt++)
		{
			var login = NewLogin();
			var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
				IF EXISTS (SELECT 1 FROM dbo.[User] WITH (UPDLOCK, HOLDLOCK) WHERE Login = @login)
				BEGIN
					SELECT CAST(NULL AS uniqueidentifier);
					RETURN;
				END
				DECLARE @inserted TABLE (Id uniqueidentifier);
				INSERT INTO dbo.[User] (Version, Created, CreatedBy, Licence_Id, Login, Password, FullName, EmailAddress, CultureInfoUI, User_Type, User_Level, AuthenticationMethod)
				OUTPUT INSERTED.Id INTO @inserted
				VALUES (1, SYSDATETIMEOFFSET(), @createdBy, @licenceId, @login, @password, @email, @email, @culture, 10, 0, 0);
				SELECT Id FROM @inserted;
				""", new { login, password, email, culture, licenceId = _licenceId, createdBy = _createdBy },
				transaction, cancellationToken: cancellationToken));

			if (id is { } created)
				return created;
			if (attempt == LoginAttempts)
				throw new InvalidOperationException($"No free 9-digit login found after {LoginAttempts} attempts.");
		}
	}

	/// <summary>A random 9-digit login: 100000000–999999999.</summary>
	public static string NewLogin() => RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>32 random bytes, URL-safe base64 (43 characters).</summary>
	public static string NewToken() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	public async Task<Subscription?> FindByTokenAsync(string token, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		return await connection.QuerySingleOrDefaultAsync<Subscription>(new CommandDefinition(
			SelectSql + " WHERE n.Token = @token", new { token }, cancellationToken: cancellationToken));
	}

	public Task<Subscription?> ConfirmAsync(string token, CancellationToken cancellationToken) =>
		UpdateByTokenAsync(token, """
			UPDATE dbo.User_Notification
			SET IsConfirmed = 1, ConfirmedAt = ISNULL(ConfirmedAt, SYSDATETIMEOFFSET()), IsDeleted = 0,
			    Version = Version + 1, Modified = SYSDATETIMEOFFSET(), ModifiedBy = @createdBy
			WHERE Token = @token AND (IsConfirmed = 0 OR IsDeleted = 1)
			""", cancellationToken);

	public Task<Subscription?> UnsubscribeAsync(string token, CancellationToken cancellationToken) =>
		UpdateByTokenAsync(token, """
			UPDATE dbo.User_Notification
			SET IsDeleted = 1, Version = Version + 1, Modified = SYSDATETIMEOFFSET(), ModifiedBy = @createdBy
			WHERE Token = @token AND IsDeleted = 0
			""", cancellationToken);

	private async Task<Subscription?> UpdateByTokenAsync(string token, string sql, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(token))
			return null;
		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync(new CommandDefinition(sql, new { token, createdBy = _createdBy }, cancellationToken: cancellationToken));
		return await connection.QuerySingleOrDefaultAsync<Subscription>(new CommandDefinition(
			SelectSql + " WHERE n.Token = @token", new { token }, cancellationToken: cancellationToken));
	}

	public async Task<IReadOnlyList<Subscription>> GetActiveAsync(string website, string notificationType, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		var rows = await connection.QueryAsync<Subscription>(new CommandDefinition(SelectSql + """
			 WHERE n.Website = @website AND n.NotificationType = @notificationType
			   AND n.IsConfirmed = 1 AND n.IsDeleted = 0
			   AND u.IsActivated = 1 AND u.IsDeleted = 0 AND u.EmailAddress IS NOT NULL
			""", new { website, notificationType }, cancellationToken: cancellationToken));
		return rows.ToList();
	}

	public async Task MarkSentAsync(IReadOnlyCollection<Guid> subscriptionIds, CancellationToken cancellationToken)
	{
		if (subscriptionIds.Count == 0)
			return;
		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync(new CommandDefinition(
			"UPDATE dbo.User_Notification SET LastSentAt = SYSDATETIMEOFFSET() WHERE Id IN @ids",
			new { ids = subscriptionIds }, cancellationToken: cancellationToken));
	}
}
