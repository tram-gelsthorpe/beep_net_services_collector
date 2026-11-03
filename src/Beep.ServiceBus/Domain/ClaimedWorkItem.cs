using System;

namespace WorkerFramework.Domain
{
	/// <summary>
	/// Internal implementation of IClaimedWorkItem for claim results.
	/// </summary>
	public sealed record ClaimedWorkItem(
		Guid WorkItemId,
		Guid DomainId,
		string DomainType,
		Guid LeaseToken,
		int AttemptNumber,
		int MaxAttempts) : IClaimedWorkItem;
}
