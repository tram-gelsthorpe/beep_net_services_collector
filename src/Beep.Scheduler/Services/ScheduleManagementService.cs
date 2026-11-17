using System.Data;
using System.Data.SqlClient;
using Beep.Scheduler.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace Beep.Scheduler.Services;

/// <summary>
/// Implementation of schedule management service using SQL Server
/// </summary>
public class ScheduleManagementService : IScheduleService
{
	private readonly SchedulerDatabase _database;
	private readonly ILogger<ScheduleManagementService> _logger;

	public ScheduleManagementService(
		SchedulerDatabase database,
		ILogger<ScheduleManagementService> logger)
	{
		_database = database ?? throw new ArgumentNullException(nameof(database));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task<int> CreateOrUpdateScheduleAsync(
		string workerType,
		string description,
		string frequency,
		TimeOnly startTime,
		bool isActive = true,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		if (string.IsNullOrWhiteSpace(frequency))
			throw new ArgumentException("Frequency cannot be empty", nameof(frequency));

		try
		{
			_logger.LogInformation(
				"Creating or updating schedule for worker type {WorkerType} with frequency {Frequency} at {StartTime}",
				workerType, frequency, startTime);

			await _database.CreateOrUpdateScheduleAsync(
				workerType,
				description,
				frequency,
				startTime,
				isActive,
				cancellationToken);

			_logger.LogInformation(
				"Schedule for worker type {WorkerType} created/updated",
				workerType);

			return 1; // Success indicator
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error creating or updating schedule for worker type {WorkerType}",
				workerType);
			throw;
		}
	}

	public async Task<bool> ScheduleExistsAsync(
		string workerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		try
		{
			var schedule = await _database.GetScheduleByWorkerTypeAsync(workerType, cancellationToken);
			return schedule != null;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error checking if schedule exists for worker type {WorkerType}",
				workerType);
			throw;
		}
	}

	public async Task<ScheduleInfo?> GetScheduleAsync(
		string workerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		try
		{
			var schedule = await _database.GetScheduleByWorkerTypeAsync(workerType, cancellationToken);

			if (schedule == null)
				return null;

			return new ScheduleInfo
			{
				Id = 0,
				WorkerType = schedule.WorkerType,
				Description = "", // Not retrieved from basic schedule
				Frequency = schedule.Frequency,
				StartTime = TimeOnly.FromTimeSpan(schedule.StartTime),
				IsActive = true, // Default based on schedule existence
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = null,
				LastExecutedAt = schedule.LastRunAt?.DateTime
			};
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error retrieving schedule for worker type {WorkerType}",
				workerType);
			throw;
		}
	}

	public async Task UpdateScheduleStatusAsync(
		string workerType,
		bool isActive,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		try
		{
			await _database.UpdateScheduleStatusAsync(workerType, isActive, cancellationToken);

			_logger.LogInformation(
				"Schedule status for worker type {WorkerType} updated to {IsActive}",
				workerType, isActive);
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error updating schedule status for worker type {WorkerType}",
				workerType);
			throw;
		}
	}

	public async Task<bool> DeleteScheduleAsync(
		string workerType,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(workerType))
			throw new ArgumentException("Worker type cannot be empty", nameof(workerType));

		try
		{
			await _database.DeleteScheduleAsync(workerType, cancellationToken);

			_logger.LogInformation(
				"Schedule for worker type {WorkerType} deleted",
				workerType);

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error deleting schedule for worker type {WorkerType}",
				workerType);
			throw;
		}
	}
}
