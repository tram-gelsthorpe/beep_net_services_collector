using System;

namespace Beep.ServiceBus;

/// <summary>
/// One work item to enqueue with <see cref="IWorkItemRepository.EnqueueBatchAsync"/>.
/// </summary>
/// <param name="DomainId">Domain entity ID (e.g., WebsiteSelectorsId).</param>
/// <param name="MaxAttempts">Maximum retries allowed.</param>
public sealed record WorkItemRequest(Guid DomainId, int MaxAttempts = 5);
