using Beep.Scheduler;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker;

/// <summary>
/// Initializes schedules for the WebsiteCollector worker at startup
/// </summary>
public class ScheduleInitializationService
{
	private readonly IScheduleService _scheduleService;
	private readonly SchedulerOption _scheduleOption;
	private readonly ILogger<ScheduleInitializationService> _logger;

	public ScheduleInitializationService(
		IScheduleService scheduleService,
		SchedulerOption scheduleOption,
		ILogger<ScheduleInitializationService> logger)
	{
		_scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));

		_scheduleOption = scheduleOption ?? throw new ArgumentNullException(nameof(scheduleOption));
		_scheduleOption.Validate();

		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>
	/// Initialize default schedules for WebsiteCollector
	/// </summary>
	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			_logger.LogInformation("Initializing WebsiteCollector schedules");

			// Create or update the daily WebsiteCollector schedule
			await _scheduleService.CreateOrUpdateScheduleAsync(
				workerType: _scheduleOption.WorkerType,
				description: _scheduleOption.Description,
				frequency: _scheduleOption.Frequency,
				startTime: _scheduleOption.StartTime!.Value,
				isActive: true,
				timeZone: _scheduleOption.TimeZone,
				dayOfWeek: _scheduleOption.DayOfWeek,
				dayOfMonth: _scheduleOption.DayOfMonth,
				dayOfYear: _scheduleOption.DayOfYear,
				cancellationToken: cancellationToken);

			_logger.LogInformation("WebsiteCollector schedules initialized successfully");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error initializing WebsiteCollector schedules");
			throw;
		}
	}
}
