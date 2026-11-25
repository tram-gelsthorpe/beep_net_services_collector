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
					HandlerType = reader.GetString(1),
					Frequency = reader.GetString(2),
					StartTime = reader.GetTimeSpan(3),
					DayOfWeek = reader.IsDBNull(4) ? null : reader.GetInt32(4),
					DayOfMonth = reader.IsDBNull(5) ? null : reader.GetInt32(5),
					DayOfYear = reader.IsDBNull(6) ? null : reader.GetInt32(6),
					MaxConcurrentRegistrations = reader.GetInt32(7),
					LastRunAt = reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
					TimeZone = reader.GetString(10)
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
			$"SELECT Id, HandlerType, Frequency, StartTime, DayOfWeek, DayOfMonth, " +
			$"DayOfYear, IsActive, MaxConcurrentRegistrations, CreatedAt, UpdatedAt, LastRunAt, Description, TimeZone " +
			$"FROM {_schema}.Schedules WHERE {whereClause}",
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
			HandlerType = reader.GetString(1),
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
			Description = reader.IsDBNull(12) ? null : reader.GetString(12),
			TimeZone = reader.GetString(13)
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
	/// Get a schedule by handler type
	/// </summary>
	public async Task<WorkerSchedule?> GetScheduleByHandlerTypeAsync(string handlerType, CancellationToken cancellationToken)
	{
		try
		{
			return await QueryScheduleAsync("HandlerType = @Value", handlerType, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving schedule by handler type {HandlerType}", handlerType);
			throw;
		}
	}

	/// <summary>
	/// Create or update a schedule for a handler type
	/// </summary>
	/// <param name="timeZone">
	/// Time zone of <paramref name="startTime"/> and the day fields: an IANA or Windows id; null means
	/// this machine's local time zone (see <see cref="ScheduleTimeZone.Resolve"/>).
	/// </param>
	public async Task CreateOrUpdateScheduleAsync(
		string handlerType,
		string description,
		string frequency,
		TimeOnly startTime,
		bool isActive,
		CancellationToken cancellationToken,
		string? timeZone = null,
		int? dayOfWeek = null,
		int? dayOfMonth = null,
		int? dayOfYear = null)
	{
		var timeZoneId = ScheduleTimeZone.Resolve(timeZone);

		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			// UPDLOCK + SERIALIZABLE holds a key-range lock on HandlerType until commit, so two
			// instances starting at the same time cannot both take the INSERT branch.
			using var command = new SqlCommand($"""
				SET XACT_ABORT ON;

				-- GetDueSchedules uses AT TIME ZONE, which fails for an id this server does not know.
				IF NOT EXISTS (SELECT 1 FROM sys.time_zone_info WHERE name = @TimeZone)
					THROW 50000, 'The time zone is not known to this SQL Server (sys.time_zone_info).', 1;

				BEGIN TRANSACTION;

				UPDATE {_schema}.Schedules WITH (UPDLOCK, SERIALIZABLE)
				SET Description = @Description, Frequency = @Frequency, StartTime = @StartTime,
					TimeZone = @TimeZone, DayOfWeek = @DayOfWeek, DayOfMonth = @DayOfMonth, DayOfYear = @DayOfYear,
					IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
				WHERE HandlerType = @HandlerType;

				IF @@ROWCOUNT = 0
					INSERT INTO {_schema}.Schedules
						(HandlerType, Description, Frequency, StartTime, TimeZone, DayOfWeek, DayOfMonth, DayOfYear, IsActive)
					VALUES
						(@HandlerType, @Description, @Frequency, @StartTime, @TimeZone, @DayOfWeek, @DayOfMonth, @DayOfYear, @IsActive);

				COMMIT TRANSACTION;
				""",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@HandlerType", handlerType);
			command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@Frequency", frequency);
			command.Parameters.AddWithValue("@StartTime", startTime.ToTimeSpan());
			command.Parameters.AddWithValue("@IsActive", isActive);
			command.Parameters.AddWithValue("@TimeZone", timeZoneId);
			command.Parameters.AddWithValue("@DayOfWeek", (object?)dayOfWeek ?? DBNull.Value);
			command.Parameters.AddWithValue("@DayOfMonth", (object?)dayOfMonth ?? DBNull.Value);
			command.Parameters.AddWithValue("@DayOfYear", (object?)dayOfYear ?? DBNull.Value);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (SqlException ex) when (ex.Number == 50000)
		{
			throw new ArgumentException(
				$"Time zone '{timeZoneId}' is not known to the SQL Server (sys.time_zone_info).", nameof(timeZone), ex);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating or updating schedule for handler type {HandlerType}", handlerType);
			throw;
		}
	}

	/// <summary>
	/// Update the active status of a schedule
	/// </summary>
	public async Task UpdateScheduleStatusAsync(
		string handlerType,
		bool isActive,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand(
				$"UPDATE {_schema}.Schedules SET IsActive = @IsActive WHERE HandlerType = @HandlerType",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@HandlerType", handlerType);
			command.Parameters.AddWithValue("@IsActive", isActive);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error updating schedule status for handler type {HandlerType}", handlerType);
			throw;
		}
	}

	/// <summary>
	/// Delete a schedule
	/// </summary>
	public async Task DeleteScheduleAsync(
		string handlerType,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand(
				$"DELETE FROM {_schema}.Schedules WHERE HandlerType = @HandlerType",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@HandlerType", handlerType);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error deleting schedule for handler type {HandlerType}", handlerType);
			throw;
		}
	}
}

