namespace Beep.EventBus;

/// <summary>
/// Generic interface for EventBus payloads.
/// Implementations can define the specific payload structure for different event types.
/// </summary>
public interface IEventBusPayload
{
	string Serialize();
}
