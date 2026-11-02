using System;

namespace WorkerFramework.Data;

/// <summary>
/// Standard result from claiming work items.
/// Represents a single claimed item ready for processing.
/// </summary>
public interface IClaimedWorkItem
{
	/// <summary>
	/// Unique ID of the work item in the framework.
	/// </summary>
	Guid WorkItemId { get; }

	/// <summary>
	/// Domain-specific ID (e.g., WebsiteSelectorsId, DeliveryId).
	/// Workers use this to fetch full domain data.
	/// </summary>
	Guid DomainId { get; }

	/// <summary>
	/// Type of domain entity (e.g., 'WebsiteSelector', 'WebhookDelivery').
	/// </summary>
	string DomainType { get; }

	/// <summary>
	/// proof of lease ownership. Must be provided on renew/complete.
	/// </summary>
	Guid LeaseToken { get; }

	/// <summary>
	/// Current attempt number (1-based).
	/// </summary>
	int AttemptNumber { get; }

	/// <summary>
	/// Maximum number of retries allowed. When AttemptNumber >= MaxAttempts, no more retries.
	/// </summary>
	int MaxAttempts { get; }
}
