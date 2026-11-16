using System.Data;
using System.Data.SqlClient;
using Beep.Scheduler.Domain;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler.Infrastructure.Database;

/// <summary>
/// Database operations for the scheduler
/// </summary>
public class SchedulerDatabase
{
	private readonly string _connectionString;
	private readonly ILogger<SchedulerDatabase> _logger;

	public SchedulerDatabase(string connectionString, ILogger<SchedulerDatabase> logger)
	{
		_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Get all schedules that are due to run
	/// </summary>
	public async Task<List<WorkerSchedule>> GetDueSchedulesAsync(CancellationToken cancellationToken)
	{
		var schedules = new List<WorkerSchedule>();

		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand("Scheduler.GetDueSchedules", connection)
			{
				CommandType = CommandType.StoredProcedure,
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);

			using var reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				schedules.Add(new WorkerSchedule
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
				});
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving due schedules from database");
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
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand(
				"SELECT Id, WorkerType, Frequency, StartTime, DayOfWeek, DayOfMonth, " +
				"DayOfYear, IsActive, MaxConcurrentRegistrations, CreatedAt, UpdatedAt, LastRunAt " +
				"FROM Scheduler.WorkerSchedules WHERE Id = @Id",
				connection)
			{
				CommandTimeout = 30
			};

			command.Parameters.AddWithValue("@Id", scheduleId);

			using var reader = await command.ExecuteReaderAsync(cancellationToken);
			if (await reader.ReadAsync(cancellationToken))
			{
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
					LastRunAt = reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11)
				};
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving schedule {ScheduleId} from database", scheduleId);
			throw;
		}

		return null;
	}

	/// <summary>
	/// Log a schedule execution
	/// </summary>
	public async Task LogExecutionAsync(
		Guid scheduleId,
		string status,
		int itemsRegistered,
		string? error,
		string? handlerName,
		DateTimeOffset startedAt,
		CancellationToken cancellationToken)
	{
		try
		{
			using var connection = new SqlConnection(_connectionString);
			await connection.OpenAsync(cancellationToken);

			using var command = new SqlCommand("Scheduler.LogScheduleExecution", connection)
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

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error logging schedule execution for schedule {ScheduleId}", scheduleId);
			throw;
		}
	}
}
