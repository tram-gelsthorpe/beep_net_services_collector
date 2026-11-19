using System;
namespace Beep.EventBus;

public sealed record EventBusDomain(
	Guid Id,
	Guid EventBusOwnerId,
	string EventBusOwnerUrl,
	bool IsEnabled,
	int TimeoutSeconds,
	int MaxAttempts);
