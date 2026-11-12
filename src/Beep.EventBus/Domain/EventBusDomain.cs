using System;
using System.Data;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace Beep.EventBus;

public sealed record EventBusDomain(
	Guid Id,
	Guid EventBusOwnerId,
	string EventBusOwnerUrl,
	bool IsEnabled,
	int TimeoutSeconds,
	int MaxRetryAttempts);
