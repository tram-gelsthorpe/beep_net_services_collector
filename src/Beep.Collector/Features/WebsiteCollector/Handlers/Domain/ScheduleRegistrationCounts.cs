namespace Beep.Collector;

/// <summary>What one run of <see cref="WebsiteCollectorScheduleHandler"/> did, for its log line.</summary>
internal sealed class ScheduleRegistrationCounts
{
	public int Total, Registered, AlreadyQueued, FailedWorkItems;
	public int EndpointsChanged, EndpointsDisabled, FailedEndpoints;

	public override string ToString() =>
		$"Registered: {Registered}, Already queued: {AlreadyQueued}, Failed: {FailedWorkItems}, Total: {Total}; " +
		$"Endpoints changed: {EndpointsChanged}, disabled: {EndpointsDisabled}, failed: {FailedEndpoints}";
}
