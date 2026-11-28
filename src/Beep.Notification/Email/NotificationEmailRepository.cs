using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.Notification;

public sealed class NotificationEmailRepository : INotificationEmailRepository
{
	private readonly string _connectionString;
	private readonly Guid _createdBy;

	/// <param name="connectionString">A_1_Beep_Global_Default (<c>ConnectionStrings:Default</c>)</param>
	/// <param name="createdBy">User recorded in CreatedBy / ModifiedBy</param>
	public NotificationEmailRepository(string connectionString, Guid createdBy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		_connectionString = connectionString;
		_createdBy = createdBy;
	}

	public async Task<IReadOnlyList<Guid>> AddAsync(
		IReadOnlyCollection<Guid> subscriptionIds, NotificationEmailKind kind, string? data, CancellationToken cancellationToken)
	{
		if (subscriptionIds.Count == 0)
			return [];
		await using var connection = new SqlConnection(_connectionString);
		// The ids as a JSON array: no limit on how many subscriptions (unlike one parameter per id).
		var ids = await connection.QueryAsync<Guid>(new CommandDefinition("""
			INSERT INTO dbo.User_Notification_Email (Created, CreatedBy, User_Notification_Id, Kind, Data)
			OUTPUT INSERTED.Id
			SELECT SYSDATETIMEOFFSET(), @createdBy, n.Id, @kind, @data
			FROM dbo.User_Notification n
			WHERE n.Id IN (SELECT CAST([value] AS uniqueidentifier) FROM OPENJSON(@ids))
			""", new { createdBy = _createdBy, kind = (byte)kind, data, ids = JsonSerializer.Serialize(subscriptionIds) },
			cancellationToken: cancellationToken));
		return ids.ToList();
	}

	public async Task<QueuedNotificationEmail?> GetAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		// A deleted user counts as unsubscribed.
		var rows = await connection.QueryAsync<EmailRow, Subscription, QueuedNotificationEmail>(new CommandDefinition("""
			SELECT e.Id AS EmailId, e.Kind, e.Data, e.SentAt,
			       n.Id, n.User_Id AS UserId, u.Login, u.EmailAddress AS Email, RTRIM(u.CultureInfoUI) AS Culture,
			       n.Website, n.NotificationType, n.Filter, n.IsConfirmed, n.Token,
			       CAST(CASE WHEN n.IsDeleted = 1 OR u.IsDeleted = 1 THEN 1 ELSE 0 END AS bit) AS IsDeleted, n.LastSentAt
			FROM dbo.User_Notification_Email e
			JOIN dbo.User_Notification n ON n.Id = e.User_Notification_Id
			JOIN dbo.[User] u ON u.Id = n.User_Id
			WHERE e.Id = @id
			""", new { id }, cancellationToken: cancellationToken),
			(email, subscription) => new QueuedNotificationEmail(
				email.EmailId, (NotificationEmailKind)email.Kind, email.Data, email.SentAt, subscription),
			splitOn: "Id");
		return rows.SingleOrDefault();
	}

	public async Task MarkSentAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = new SqlConnection(_connectionString);
		await connection.ExecuteAsync(new CommandDefinition("""
			SET XACT_ABORT ON;
			BEGIN TRANSACTION;
			DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();
			UPDATE dbo.User_Notification_Email
			SET SentAt = @now, Modified = @now, ModifiedBy = @modifiedBy, Version = Version + 1
			WHERE Id = @id;
			UPDATE n SET LastSentAt = @now
			FROM dbo.User_Notification n
			JOIN dbo.User_Notification_Email e ON e.User_Notification_Id = n.Id
			WHERE e.Id = @id;
			COMMIT;
			""", new { id, modifiedBy = _createdBy }, cancellationToken: cancellationToken));
	}

	private sealed record EmailRow(Guid EmailId, byte Kind, string? Data, DateTimeOffset? SentAt);
}
