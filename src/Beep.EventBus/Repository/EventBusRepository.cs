using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Beep.ServiceBus;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Beep.EventBus;

public sealed class EventBusRepository : IEventBusRepository
{
	private const int MaxUrlLength = 2048;

	private readonly string _connectionString;
	private readonly string _schema;

	public EventBusRepository(string connectionString, string schema)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		_schema = schema ?? throw new ArgumentNullException(nameof(schema));
	}

	private SqlConnection Open() => new(_connectionString);

	public async Task<EndpointRegistrationResult> RegisterEndpointsAsync(
		IReadOnlyCollection<EventBusEndpoint> endpoints,
		CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(endpoints);
		if (endpoints.Count == 0)
			return new EndpointRegistrationResult(0, 0);

		// Normalise in C#: invalid URLs mean "no endpoint", settings are clamped to the table's CHECKs.
		var json = JsonSerializer.Serialize(endpoints.Select(e => new
		{
			e.OwnerId,
			Url = IsValidUrl(e.DeliveryUrl) ? e.DeliveryUrl : null,
			TimeoutSeconds = Math.Clamp(e.TimeoutSeconds, 5, 300),
			MaxAttempts = Math.Clamp(e.MaxAttempts, 1, 10)
		}));

		var sql = $"""
			SET XACT_ABORT ON;
			BEGIN TRANSACTION;

			DECLARE @items TABLE (
				OwnerId uniqueidentifier NOT NULL,
				Url nvarchar({MaxUrlLength}) NULL,
				UrlHash binary(32) NULL,
				TimeoutSeconds int NOT NULL,
				MaxAttempts int NOT NULL);

			INSERT INTO @items (OwnerId, Url, UrlHash, TimeoutSeconds, MaxAttempts)
			SELECT OwnerId, Url, CONVERT(binary(32), HASHBYTES('SHA2_256', Url)), MAX(TimeoutSeconds), MAX(MaxAttempts)
			FROM OPENJSON(@Endpoints) WITH (
				OwnerId uniqueidentifier '$.OwnerId',
				Url nvarchar({MaxUrlLength}) '$.Url',
				TimeoutSeconds int '$.TimeoutSeconds',
				MaxAttempts int '$.MaxAttempts')
			GROUP BY OwnerId, Url;

			-- Endpoints of the listed owners that are no longer listed
			UPDATE e
			SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME()
			FROM {_schema}.EventBus e WITH (UPDLOCK, HOLDLOCK)
			WHERE e.IsEnabled = 1
				AND e.EventBusOwnerId IN (SELECT OwnerId FROM @items)
				AND NOT EXISTS (
					SELECT 1 FROM @items i
					WHERE i.OwnerId = e.EventBusOwnerId AND i.UrlHash = e.EventBusOwnerUrlHash);
			DECLARE @disabled int = @@ROWCOUNT;

			-- Existing endpoints whose settings changed or that were disabled
			UPDATE e
			SET IsEnabled = 1, TimeoutSeconds = i.TimeoutSeconds, MaxAttempts = i.MaxAttempts, UpdatedAt = SYSUTCDATETIME()
			FROM {_schema}.EventBus e WITH (UPDLOCK, HOLDLOCK)
			JOIN @items i ON i.OwnerId = e.EventBusOwnerId AND i.UrlHash = e.EventBusOwnerUrlHash
			WHERE e.IsEnabled = 0 OR e.TimeoutSeconds <> i.TimeoutSeconds OR e.MaxAttempts <> i.MaxAttempts;
			DECLARE @changed int = @@ROWCOUNT;

			-- New endpoints
			INSERT INTO {_schema}.EventBus (EventBusOwnerId, EventBusOwnerDeliveryUrl, IsEnabled, TimeoutSeconds, MaxAttempts)
			SELECT i.OwnerId, i.Url, 1, i.TimeoutSeconds, i.MaxAttempts
			FROM @items i
			WHERE i.Url IS NOT NULL
				AND NOT EXISTS (
					SELECT 1 FROM {_schema}.EventBus e WITH (UPDLOCK, HOLDLOCK)
					WHERE e.EventBusOwnerId = i.OwnerId AND e.EventBusOwnerUrlHash = i.UrlHash);
			SET @changed += @@ROWCOUNT;

			COMMIT TRANSACTION;

			SELECT @changed AS Changed, @disabled AS Disabled;
			""";

		await using var connection = Open();
		return await connection.QuerySingleAsync<EndpointRegistrationResult>(
			new CommandDefinition(sql, new { Endpoints = new DbString { Value = json, Length = -1 } }, commandTimeout: 60, cancellationToken: ct));
	}

	public async Task<IReadOnlyList<WorkItemRequest>> CreateDeliveriesAsync(
		Guid ownerId,
		Guid eventId,
		string payload,
		CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(payload);

		// Re-publishing an event does not create a second delivery (UQ_EventBusDeliveries_Event),
		// but it does return deliveries that were never delivered, so their work items are
		// enqueued again if a previous publish failed between the two databases.
		var sql = $"""
			INSERT INTO {_schema}.EventBusDeliveries (EventBusId, EventId, Payload)
			SELECT e.Id, @EventId, @Payload
			FROM {_schema}.EventBus e
			WHERE e.EventBusOwnerId = @OwnerId
				AND e.IsEnabled = 1
				AND NOT EXISTS (
					SELECT 1 FROM {_schema}.EventBusDeliveries d WITH (UPDLOCK, HOLDLOCK)
					WHERE d.EventBusId = e.Id AND d.EventId = @EventId);

			SELECT d.Id AS DomainId, e.MaxAttempts
			FROM {_schema}.EventBusDeliveries d
			JOIN {_schema}.EventBus e ON e.Id = d.EventBusId
			WHERE d.EventId = @EventId
				AND e.EventBusOwnerId = @OwnerId
				AND e.IsEnabled = 1
				AND d.DeliveredAt IS NULL;
			""";

		await using var connection = Open();
		var rows = await connection.QueryAsync<WorkItemRequest>(new CommandDefinition(
			sql,
			new { OwnerId = ownerId, EventId = eventId, Payload = new DbString { Value = payload, Length = -1 } },
			cancellationToken: ct));
		return rows.AsList();
	}

	public async Task<EventBusDelivery?> GetDeliveryAsync(Guid deliveryId, CancellationToken ct)
	{
		var sql = $"""
			SELECT
				d.Id AS DeliveryId,
				d.EventBusId,
				d.EventId,
				e.EventBusOwnerId AS OwnerId,
				e.EventBusOwnerDeliveryUrl AS DeliveryUrl,
				e.TimeoutSeconds,
				e.IsEnabled,
				d.Payload,
				d.DeliveredAt
			FROM {_schema}.EventBusDeliveries d
			JOIN {_schema}.EventBus e ON e.Id = d.EventBusId
			WHERE d.Id = @DeliveryId
			""";

		await using var connection = Open();
		return await connection.QuerySingleOrDefaultAsync<EventBusDelivery>(
			new CommandDefinition(sql, new { DeliveryId = deliveryId }, cancellationToken: ct));
	}

	public async Task RecordAttemptAsync(Guid deliveryId, DeliveryOutcome outcome, CancellationToken ct)
	{
		ArgumentNullException.ThrowIfNull(outcome);

		var sql = $"""
			UPDATE {_schema}.EventBusDeliveries
			SET HttpStatusCode = @HttpStatusCode,
				ErrorMessage = @Error,
				LastAttemptAt = SYSUTCDATETIME(),
				DeliveredAt = CASE WHEN @Delivered = 1 THEN SYSUTCDATETIME() ELSE DeliveredAt END
			WHERE Id = @DeliveryId
			""";

		await using var connection = Open();
		await connection.ExecuteAsync(new CommandDefinition(sql, new
		{
			DeliveryId = deliveryId,
			outcome.Delivered,
			outcome.HttpStatusCode,
			Error = outcome.Error is { Length: > 2000 } e ? e[..2000] : outcome.Error
		}, cancellationToken: ct));
	}

	private static bool IsValidUrl(string? url) =>
		url is { Length: > 0 and <= MaxUrlLength }
		&& Uri.TryCreate(url, UriKind.Absolute, out var uri)
		&& (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
