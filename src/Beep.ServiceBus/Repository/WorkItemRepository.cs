using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Beep.ServiceBus;

/// <summary>
/// SQL Server implementation of IWorkItemRepository.
/// Uses stored procedures defined in WorkerFramework schema.
/// </summary>
public class WorkItemRepository : IWorkItemRepository
{
	private readonly string _connectionString;
	private readonly ILogger<WorkItemRepository> _logger;

	public WorkItemRepository(string connectionString, ILogger<WorkItemRepository> logger)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<IReadOnlyList<IClaimedWorkItem>> ClaimAsync(
		string workerType,
		Guid workerId,
		int batchSize,
		int leaseSeconds,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.ClaimWorkItems";
			cmd.CommandTimeout = 30;

			cmd.Parameters.AddWithValue("@WorkerType", workerType);
			cmd.Parameters.AddWithValue("@WorkerId", workerId);
			cmd.Parameters.AddWithValue("@BatchSize", batchSize);
			cmd.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);

			var items = new List<IClaimedWorkItem>();

			using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				items.Add(new ClaimedWorkItem(
					WorkItemId: reader.GetGuid(0),
					DomainId: reader.GetGuid(1),
					DomainType: reader.GetString(2),
					LeaseToken: reader.GetGuid(3),
					AttemptNumber: reader.GetInt32(4),
					MaxAttempts: reader.GetInt32(5)
				));
			}

			_logger.LogDebug("Claimed {Count} work items for worker type {WorkerType}", items.Count, workerType);
			return items;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to claim work items for worker type {WorkerType}", workerType);
			throw;
		}
	}

	public async Task<bool> RenewLeaseAsync(
		Guid workItemId,
		Guid leaseToken,
		int leaseSeconds,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.RenewWorkItemLease";
			cmd.CommandTimeout = 10;

			cmd.Parameters.AddWithValue("@WorkItemId", workItemId);
			cmd.Parameters.AddWithValue("@LeaseToken", leaseToken);
			cmd.Parameters.AddWithValue("@LeaseSeconds", leaseSeconds);

			using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
			if (await reader.ReadAsync(cancellationToken))
			{
				var success = reader.GetBoolean(0);
				if (success)
					_logger.LogDebug("Renewed lease for work item {WorkItemId}", workItemId);
				else
					_logger.LogWarning("Failed to renew lease for work item {WorkItemId} - lease expired or invalid", workItemId);
				return success;
			}

			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error renewing lease for work item {WorkItemId}", workItemId);
			throw;
		}
	}

	public async Task<bool> ReleaseLeaseAsync(
		Guid workItemId,
		Guid leaseToken,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.ReleaseWorkItemLease";
			cmd.CommandTimeout = 10;

			cmd.Parameters.AddWithValue("@WorkItemId", workItemId);
			cmd.Parameters.AddWithValue("@LeaseToken", leaseToken);

			using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
			if (await reader.ReadAsync(cancellationToken))
			{
				bool success = reader.GetBoolean(0);
				if (success)
					_logger.LogDebug("Released lease for work item {WorkItemId}", workItemId);
				return success;
			}

			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error releasing lease for work item {WorkItemId}", workItemId);
			throw;
		}
	}

	public async Task<bool> CompleteAsync(
		Guid workItemId,
		Guid leaseToken,
		bool succeeded,
		string? error = null,
		string? resultData = null,
		string backoffStrategy = "exponential",
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.CompleteWorkItem";
			cmd.CommandTimeout = 30;

			cmd.Parameters.AddWithValue("@WorkItemId", workItemId);
			cmd.Parameters.AddWithValue("@LeaseToken", leaseToken);
			cmd.Parameters.AddWithValue("@Succeeded", succeeded);
			cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@ResultData", (object?)resultData ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@BackoffStrategyName", backoffStrategy);

			await cmd.ExecuteNonQueryAsync(cancellationToken);

			_logger.LogInformation(
				"Completed work item {WorkItemId}: Success={Success}, Backoff={Backoff}",
				workItemId,
				succeeded,
				backoffStrategy);

			return true;
		}
		catch (SqlException sqlEx) when (sqlEx.Number == 50002)
		{
			// Specific error: lease invalid
			_logger.LogWarning("Cannot complete work item {WorkItemId} - lease invalid or expired", workItemId);
			return false;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error completing work item {WorkItemId}", workItemId);
			throw;
		}
	}

	public async Task RecordAttemptAsync(
		Guid workItemId,
		int attemptNumber,
		string statusSnapshot,
		string? errorMessage = null,
		string? resultData = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.RecordWorkItemAttempt";
			cmd.CommandTimeout = 10;

			cmd.Parameters.AddWithValue("@WorkItemId", workItemId);
			cmd.Parameters.AddWithValue("@AttemptNumber", attemptNumber);
			cmd.Parameters.AddWithValue("@StatusSnapshot", statusSnapshot);
			cmd.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
			cmd.Parameters.AddWithValue("@ResultData", (object?)resultData ?? DBNull.Value);

			await cmd.ExecuteNonQueryAsync(cancellationToken);

			_logger.LogDebug("Recorded attempt {Attempt} for work item {WorkItemId}: {Status}", 
				attemptNumber, workItemId, statusSnapshot);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error recording attempt for work item {WorkItemId}", workItemId);
			throw;
		}
	}

	public async Task<Guid> EnqueueAsync(
		string workerType,
		Guid domainId,
		string domainType,
		int maxAttempts = 5,
		bool scheduleForNow = true,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		if (string.IsNullOrWhiteSpace(domainType))
			throw new ArgumentException("Domain type cannot be empty", nameof(domainType));

		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandText = @"
				DECLARE @workItemId uniqueidentifier = NEWID();
				INSERT INTO dbo.WorkItems (Id, WorkerType, DomainId, DomainType, StatusId, MaxAttempts, NextRunAt)
				VALUES (@workItemId, @WorkerType, @DomainId, @DomainType, 1, @MaxAttempts, @NextRunAt);
				SELECT @workItemId;
			";
			cmd.CommandTimeout = 10;

			cmd.Parameters.AddWithValue("@WorkerType", workerType);
			cmd.Parameters.AddWithValue("@DomainId", domainId);
			cmd.Parameters.AddWithValue("@DomainType", domainType);
			cmd.Parameters.AddWithValue("@MaxAttempts", maxAttempts);
			cmd.Parameters.AddWithValue("@NextRunAt", scheduleForNow ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.AddMinutes(1));

			var result = await cmd.ExecuteScalarAsync(cancellationToken);
			var workItemId = (Guid)(result ?? Guid.Empty);

			_logger.LogInformation(
				"Enqueued work item {WorkItemId} for domain {DomainType}:{DomainId}",
				workItemId, domainType, domainId);

			return workItemId;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error enqueuing work item for {DomainType}:{DomainId}", domainType, domainId);
			throw;
		}
	}

	public async Task<WorkItemDetail?> GetDetailAsync(
		Guid workItemId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var cmd = connection.CreateCommand();
			cmd.CommandType = CommandType.StoredProcedure;
			cmd.CommandText = "dbo.GetWorkItem";
			cmd.CommandTimeout = 10;

			cmd.Parameters.AddWithValue("@WorkItemId", workItemId);

			using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

			// First result set: work item header
			if (!await reader.ReadAsync(cancellationToken))
				return null;

			var attempts = new List<WorkItemAttempt>();
			var detail = new WorkItemDetail(
				Id: reader.GetGuid(0),
				WorkerType: reader.GetString(1),
				DomainId: reader.GetGuid(2),
				DomainType: reader.GetString(3),
				Status: reader.GetString(4),
				LeaseOwner: reader.IsDBNull(5) ? null : reader.GetGuid(5),
				AttemptCount: reader.GetInt32(6),
				MaxAttempts: reader.GetInt32(7),
				NextRunAt: reader.GetDateTimeOffset(8),
				LastError: reader.IsDBNull(9) ? null : reader.GetString(9),
				CreatedAt: reader.GetDateTimeOffset(10),
				StartedAt: reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11),
				CompletedAt: reader.IsDBNull(12) ? null : reader.GetDateTimeOffset(12),
				Attempts: attempts.AsReadOnly()
			);

			// Move to second result set: attempt history
			await reader.NextResultAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				attempts.Add(new WorkItemAttempt(
					AttemptNumber: reader.GetInt32(0),
					StartedAt: reader.GetDateTimeOffset(1),
					FinishedAt: reader.IsDBNull(2) ? null : reader.GetDateTimeOffset(2),
					Status: reader.IsDBNull(3) ? null : reader.GetString(3),
					ErrorMessage: reader.IsDBNull(4) ? null : reader.GetString(4)
				));
			}

			return detail with { Attempts = attempts.AsReadOnly() };
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error getting work item detail {WorkItemId}", workItemId);
			throw;
		}
	}

	
}
