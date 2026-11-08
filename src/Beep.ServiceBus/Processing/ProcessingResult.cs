namespace Beep.ServiceBus;

/// <summary>
/// Represents the result of processing a work item.
/// </summary>
public class ProcessingResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public object? ResultData { get; set; }
}
