using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Beep.ServiceBus;

/// <summary>
/// Retries a SQL operation that was chosen as a deadlock victim (error 1205).
/// </summary>
/// <remarks>
/// SQL Server rolls the victim's whole transaction back, so running it again is safe for operations
/// that are one transaction (or one statement) and idempotent, such as claiming, completing or the
/// enqueue that skips entities with an unfinished work item. Many workers enqueuing and completing at
/// the same time take overlapping key-range locks, so occasional deadlocks are expected under load;
/// without a retry each one would be an error, and a lost enqueue a lost delivery.
/// </remarks>
public static class SqlRetry
{
	public const int DeadlockErrorNumber = 1205;

	/// <summary>Attempts in all, the first included.</summary>
	public const int MaxAttempts = 5;

	public static async Task<T> OnDeadlockAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);

		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await operation();
			}
			catch (SqlException ex) when (ex.Number == DeadlockErrorNumber && attempt < MaxAttempts)
			{
				// Short, growing, jittered pause so the transactions that collided do not collide again.
				await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 50) * attempt), cancellationToken);
			}
		}
	}

	public static Task OnDeadlockAsync(Func<Task> operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		return OnDeadlockAsync(async () => { await operation(); return true; }, cancellationToken);
	}
}
