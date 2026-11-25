using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Database operations for the scheduler
/// </summary>
public sealed class SchedulerRepository
{
	private readonly string _connectionString;
	private readonly string _schema;
	private readonly ILogger<SchedulerRepository> _logger;

	public SchedulerRepository(string connectionString, string schema, ILogger<SchedulerRepository> logger)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		_schema = schema ?? throw new ArgumentNullException(nameof(schema));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Get the schedules that are due to run now (database clock). Read-only: the caller
	/// enqueues a ServiceBus work item per schedule, and the ServiceBus makes sure only one
	/// worker runs it.
	/// </summary>
	public async Task<List<WorkerSchedule>> GetDueSchedulesAsync(CancellationToken cancellationToken)
	{
		var due = await QueryDueSchedulesAsync(null, cancellationToken);
		return [.. due.Select(d => d.Schedule)];
	}

	/// <summary>
	/// Check whether one schedule is still due. Returns the database time of the check (used as
	/// the run's start time for <see cref="LogExecutionAsync"/>), or null when it is not due:
	/// inactive, deleted, or already run this period.
	/// </summary>
	public async Task<DateTimeOffset?> CheckDueAsync(Guid scheduleId, CancellationToken cancellationToken)
	{
		var due = await QueryDueSchedulesAsync(scheduleId, cancellationToken);
		return due.Count == 0 ? null : due[0].CheckedAt;
	}

	private async Task<List<(WorkerSchedule Schedule, DateTimeOffset CheckedAt)>> QueryDueSchedulesAsync(
		Guid? scheduleId, CancellationToken cancellationToken)
	{
		var schedules = new List<(WorkerSchedule, DateTimeOffset)>();

		try
		{
			await using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			await using var command = new SqlCommand($"{_schema}.GetDueSchedules", connection)
			{
				CommandType = CommandType.StoredProcedure,
				CommandTimeout = 30
			};
			command.Parameters.AddWithValue("@ScheduleId", (object?)scheduleId ?? DBNull.Value);

			await using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				schedules.Add((new WorkerSchedule
				{
					Id = reader.GetGuid(0),
					WorkerType = reader.GetString(1),
					Frequency = reader.GetString(2),
					StartTime = reader.GetTimeSpan(3),
					DayOfWeek = reader.IsDBNull(4) ? null : reader.GetInt32(4),
					DayOfMonth = reader.IsDBNull(5) ? null : reader.GetInt32(5),
					DayOfYear = reader.IsDBNull(6) ? null : reader.GetInt32(6),
					MaxConcurrentRegistrations = reader.GetInt32(7),
					LastRunAt = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8)
				}, reader.GetDateTimeOffset(9)));
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Error reading due schedules");
			throw;
		}

		return schedules;
	}

	/// <summary>
	/// Get a single schedule by ID
	/// </summary>
	public async Task<WorkerSchedule?> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken)
	{
		try
		{
			return await QueryScheduleAsync("Id = @Value", scheduleId, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving schedule {ScheduleId} from database", scheduleId);
			throw;
		}
	}

	private async Task<WorkerSchedule?> QueryScheduleAsync(string whereClause, object value, CancellationToken cancellationToken)
	{
		using var connection = new SqlConnection(_connectionString);
		await connection.OpenAsync(cancellationToken);

		using var command = new SqlCommand(
			$"SELECT Id, WorkerType, Frequency, StartTime, DayOfWeek, DayOfMonth, " +
			$"DayOfYear, IsActive, MaxConcurrentRegistrations, CreatedAt, UpdatedAt, LastRunAt, Description " +
			$"FROM {_schema}.WorkerSchedules WHERE {whereClause}",
			connection)
		{
			CommandTimeout = 30
		};

		command.Parameters.AddWithValue("@Value", value);

		using var reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
			return null;

		return new WorkerSchedule
		{
			Id = reader.GetGuid(0),
			WorkerType = reader.GetString(1),
			Frequency = reader.GetString(2),
			StartTime = reader.GetTimeSpan(3),
			DayOfWeek = reader.IsDBNull(4) ? null : reader.GetInt32(4),
			DayOfMonth = reader.IsDBNull(5) ? null : reader.GetInt32(5),
			DayOfYear = reader.IsDBNull(6) ? null : reader.GetInt32(6),
			IsActive = reader.GetBoolean(7),
			MaxConcurrentRegistrations = reader.GetInt32(8),
			CreatedAt = reader.GetDateTimeOffset(9),
			UpdatedAt = reader.GetDateTimeOffset(10),
			LastRunAt = reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11),
			Description = reader.IsDBNull(12) ? null : reader.GetString(12)
		};
	}

	/// <summary>
	/// Log a schedule execution
	/// </summary>
	/// <param name="completesRun">
	/// True when the run is finished (succeeded, skipped, or failed on its last attempt): LastRunAt is
	/// set to <paramref name="startedAt"/> and the schedule waits for its next period. False for a
	/// failed attempt the ServiceBus will retry.
	/// </param>
	public async Task LogExecutionAsync(
		Guid scheduleId,
		string status,
		int itemsRegistered,
		string? error,
		string? handlerName,
		DateTimeOffset startedAt,
		CancellationToken cancellationToken,
		bool completesRun = true)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand($"{_schema}.LogScheduleExecution", connection)
			{
				CommandType = CommandType.StoredProcedure,
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@ScheduleId", scheduleId);
			command.Parameters.AddWithValue("@Status", status);
			command.Parameters.AddWithValue("@ItemsRegistered", itemsRegistered);
			command.Parameters.AddWithValue("@Error", error ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@HandlerName", handlerName ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@StartedAt", startedAt);
			command.Parameters.AddWithValue("@CompletesRun", completesRun);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error logging schedule execution for schedule {ScheduleId}", scheduleId);
			throw;
		}
	}

	/// <summary>
	/// Get a schedule by worker type name
	/// </summary>
	public async Task<WorkerSchedule?> GetScheduleByWorkerTypeAsync(string workerType, CancellationToken cancellationToken)
	{
		try
		{
			return await QueryScheduleAsync("WorkerType = @Value", workerType, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving schedule by worker type {WorkerType}", workerType);
			throw;
		}
	}

	/// <summary>
	/// Create or update a schedule for a worker type
	/// </summary>
	public async Task CreateOrUpdateScheduleAsync(
		string workerType,
		string description,
		string frequency,
		TimeOnly startTime,
		bool isActive,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			// UPDLOCK + SERIALIZABLE holds a key-range lock on WorkerType until commit, so two
			// instances starting at the same time cannot both take the INSERT branch.
			using var command = new SqlCommand($"""
				SET XACT_ABORT ON;
				BEGIN TRANSACTION;

				UPDATE {_schema}.WorkerSchedules WITH (UPDLOCK, SERIALIZABLE)
				SET Description = @Description, Frequency = @Frequency, StartTime = @StartTime,
					IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
				WHERE WorkerType = @WorkerType;

				IF @@ROWCOUNT = 0
					INSERT INTO {_schema}.WorkerSchedules (WorkerType, Description, Frequency, StartTime, IsActive)
					VALUES (@WorkerType, @Description, @Frequency, @StartTime, @IsActive);

				COMMIT TRANSACTION;
				""",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@WorkerType", workerType);
			command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@Frequency", frequency);
			command.Parameters.AddWithValue("@StartTime", startTime.ToTimeSpan());
			command.Parameters.AddWithValue("@IsActive", isActive);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating or updating schedule for worker type {WorkerType}", workerType);
			throw;
		}
	}

	/// <summary>
	/// Update the active status of a schedule
	/// </summary>
	public async Task UpdateScheduleStatusAsync(
		string workerType,
		bool isActive,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand(
				$"UPDATE {_schema}.WorkerSchedules SET IsActive = @IsActive WHERE WorkerType = @WorkerType",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@WorkerType", workerType);
			command.Parameters.AddWithValue("@IsActive", isActive);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error updating schedule status for worker type {WorkerType}", workerType);
			throw;
		}
	}

	/// <summary>
	/// Delete a schedule
	/// </summary>
	public async Task DeleteScheduleAsync(
		string workerType,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand(
				$"DELETE FROM {_schema}.WorkerSchedules WHERE WorkerType = @WorkerType",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@WorkerType", workerType);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error deleting schedule for worker type {WorkerType}", workerType);
			throw;
		}
	}
}

