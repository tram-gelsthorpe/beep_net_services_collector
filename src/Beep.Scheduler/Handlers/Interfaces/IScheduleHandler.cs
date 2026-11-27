namespace Beep.Scheduler;

/// <summary>
/// Interface for schedule handlers that register work items
/// </summary>
/// <remarks>
/// Register implementations with <c>services.RegisterScheduleHandler&lt;THandler&gt;()</c>.
/// The scheduler resolves handlers from a new DI scope for every execution, so a handler
/// may depend on scoped services directly.
///
/// Example implementation:
/// <code>
/// public class WebsiteCollectorScheduleHandler(IWorkItemRepository workItems) : IScheduleHandler
/// {
///     public string HandlerType => "WebsiteCollector";
///     public Guid WorkerId => Guid.Empty;
///
///     public async Task&lt;ScheduleHandlerResult&gt; RegisterItemsAsync(
///         WorkerSchedule schedule,
///         CancellationToken cancellationToken)
///     {
///         // The ServiceBus worker type of the work this schedule creates
///         var inserted = await workItems.EnqueueBatchAsync(
///             "WebsiteCollector", "WebsiteSelector", [new WorkItemRequest(domainId)], cancellationToken);
///         return new ScheduleHandlerResult { ItemsRegistered = inserted };
///     }
/// }
/// </code>
/// </remarks>
public interface IScheduleHandler
{
	/// <summary>
	/// Identifies the handler; schedules refer to it (<see cref="WorkerSchedule.HandlerType"/>, the <c>Scheduler:HandlerType</c>
	/// setting), e.g. "WebsiteCollector". Not a ServiceBus worker type.
	/// </summary>
	string HandlerType { get; }

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
