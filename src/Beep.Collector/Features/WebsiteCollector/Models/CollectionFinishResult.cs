namespace Beep.Collector;

/// <summary>
/// Result of finishing a collection: whether the work item was completed (lease still held)
/// and the Id of the collected data row, if one was saved.
/// </summary>
public sealed record CollectionFinishResult(bool Completed, Guid? CollectedDataId);
