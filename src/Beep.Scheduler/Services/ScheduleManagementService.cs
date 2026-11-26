using Microsoft.Extensions.Logging;

namespace Beep.Scheduler;

/// <summary>
/// Implementation of schedule management service using SQL Server
/// </summary>
public class ScheduleManagementService : IScheduleService
{
	private readonly SchedulerRepository _database;
	private readonly IReadOnlyList<string> _handlerNames;
	private readonly ILogger<ScheduleManagementService> _logger;

	public ScheduleManagementService(
		SchedulerRepository database,
		IEnumerable<IScheduleHandler> handlers,
		ILogger<ScheduleManagementService> logger)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		ArgumentNullException.ThrowIfNull(handlers);
		_handlerNames = [.. handlers.Select(h => h.HandlerType)];
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<int> CreateOrUpdateScheduleAsync(
		string handlerType,
		string description,
		string frequency,
		IReadOnlyCollection<TimeOnly> startTimes,
		bool isActive = true,
		string? timeZone = null,
		int? dayOfWeek = null,
		int? dayOfMonth = null,
		int? dayOfYear = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(handlerType))
			throw new ArgumentException("HandlerType cannot be empty", nameof(handlerType));

		if (string.IsNullOrWhiteSpace(frequency))
			throw new ArgumentException("Frequency cannot be empty", nameof(frequency));

		ArgumentNullException.ThrowIfNull(startTimes);

		// A schedule for a handler that is not registered would be skipped at every run.
		if (!_handlerNames.Contains(handlerType, StringComparer.Ordinal))
			throw new InvalidOperationException(
				$"No schedule handler with HandlerType '{handlerType}' is registered. Registered: " +
				(_handlerNames.Count == 0 ? "none" : string.Join(", ", _handlerNames.Select(n => $"'{n}'"))) + ".");

		try
		{
			var timeZoneId = ScheduleTimeZone.Resolve(timeZone);

			_logger.LogInformation(
				"Creating or updating schedule for handler type {HandlerType} with frequency {Frequency} at {StartTimes} ({TimeZone})",
				handlerType, frequency, string.Join(", ", startTimes.Order().Select(t => t.ToString("HH:mm"))), timeZoneId);

			await _database.CreateOrUpdateScheduleAsync(
				handlerType,
				description,
				frequency,
				startTimes,
				isActive,
				cancellationToken,
				timeZoneId,
				dayOfWeek,
				dayOfMonth,
				dayOfYear);

			_logger.LogInformation(
				"Schedule for handler type {HandlerType} created/updated",
				handlerType);

			return 1; // Success indicator
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error creating or updating schedule for handler type {HandlerType}",
				handlerType);
			throw;
		}
	}

	public async Task<bool> ScheduleExistsAsync(
		string handlerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(handlerType))
			throw new ArgumentException("HandlerType cannot be empty", nameof(handlerType));

		try
		{
			var schedule = await _database.GetScheduleByHandlerTypeAsync(handlerType, cancellationToken);
			return schedule != null;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error checking if schedule exists for handler type {HandlerType}",
				handlerType);
			throw;
		}
	}

	public async Task<ScheduleInfo?> GetScheduleAsync(
		string handlerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(handlerType))
			throw new ArgumentException("HandlerType cannot be empty", nameof(handlerType));

		try
		{
			var schedule = await _database.GetScheduleByHandlerTypeAsync(handlerType, cancellationToken);

			if (schedule == null)
				return null;

			return new ScheduleInfo
			{
				Id = schedule.Id,
				HandlerType = schedule.HandlerType,
				Description = schedule.Description ?? string.Empty,
				Frequency = schedule.Frequency,
				StartTimes = schedule.StartTimes,
				TimeZone = schedule.TimeZone,
				IsActive = schedule.IsActive,
				CreatedAt = schedule.CreatedAt,
				UpdatedAt = schedule.UpdatedAt,
				LastExecutedAt = schedule.LastRunAt
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error retrieving schedule for handler type {HandlerType}",
				handlerType);
			throw;
		}
	}

	public async Task UpdateScheduleStatusAsync(
		string handlerType,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(handlerType))
			throw new ArgumentException("HandlerType cannot be empty", nameof(handlerType));

		try
		{
			await _database.UpdateScheduleStatusAsync(handlerType, isActive, cancellationToken);

			_logger.LogInformation(
				"Schedule status for handler type {HandlerType} updated to {IsActive}",
				handlerType, isActive);
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error updating schedule status for handler type {HandlerType}",
				handlerType);
			throw;
		}
	}

	public async Task<bool> DeleteScheduleAsync(
		string handlerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(handlerType))
			throw new ArgumentException("HandlerType cannot be empty", nameof(handlerType));

		try
		{
			await _database.DeleteScheduleAsync(handlerType, cancellationToken);

			_logger.LogInformation(
				"Schedule for handler type {HandlerType} deleted",
				handlerType);

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error deleting schedule for handler type {HandlerType}",
				handlerType);
			throw;
		}
	}
}
