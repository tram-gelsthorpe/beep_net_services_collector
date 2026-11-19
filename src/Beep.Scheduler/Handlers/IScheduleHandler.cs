namespace Beep.Scheduler;

/// <summary>
/// Represents the result of a schedule handler execution
/// </summary>
public class ScheduleHandlerResult
{
	/// <summary>
	/// Number of items registered/created
	/// </summary>
	public int ItemsRegistered { get; set; }

	/// <summary>
	/// Whether the handler succeeded
	/// </summary>
	public bool Success { get; set; } = true;

	/// <summary>
	/// Error message if execution failed
	/// </summary>
	public string? Error { get; set; }

	/// <summary>
	/// Optional additional details
	/// </summary>
	public string? Details { get; set; }
}

/// <summary>
/// Interface for schedule handlers that register work items
/// </summary>
/// <remarks>
/// Implementations should be registered in dependency injection and will be
/// called by the scheduler service when their schedule is due.
/// 
/// Example implementation:
/// <code>
/// public class WebsiteCollectorScheduleHandler : IScheduleHandler
/// {
///     private readonly IServiceProvider _serviceProvider;
///     
///     public WebsiteCollectorScheduleHandler(IServiceProvider serviceProvider)
///     {
///         _serviceProvider = serviceProvider;
///     }
///     
///     public string WorkerType => "WebsiteCollector";
///     
///     public async Task&lt;ScheduleHandlerResult&gt; RegisterItemsAsync(
///         WorkerSchedule schedule, 
///         CancellationToken cancellationToken)
///     {
///         using var scope = _serviceProvider.CreateScope();
///         var repository = scope.ServiceProvider.GetRequiredService&lt;IWebsiteSelectorsRepository&gt;();
///         
///         // Create work items for all active selectors
///         var count = await repository.RegisterWorkItemsAsync(cancellationToken);
///         
///         return new ScheduleHandlerResult 
///         { 
///             ItemsRegistered = count,
///             Success = count > 0
///         };
///     }
/// }
/// </code>
/// </remarks>
public interface IScheduleHandler
{
	/// <summary>
	/// The worker type this handler manages (e.g., "WebsiteCollector")
	/// </summary>
	string WorkerType { get; }

	/// <summary>
	///	The unique ID of the worker instance that will process the work items created by this handler
	/// </summary>
	Guid WorkerId { get; }

	/// <summary>
	/// Register work items for the given schedule
	/// </summary>
	/// <param name="schedule">The schedule that is due to run</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>Result including number of items registered</returns>
	Task<ScheduleHandlerResult> RegisterItemsAsync(
		WorkerSchedule schedule,
		CancellationToken cancellationToken);
}
