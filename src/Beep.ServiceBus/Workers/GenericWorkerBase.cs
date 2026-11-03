using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerFramework.Configuration;
using WorkerFramework.Leasing;
using WorkerFramework.Processing;

namespace WorkerFramework.Workers;

/// <summary>
/// Generic background worker that implements the claim/release pattern for any work item type.
/// Handles polling, concurrency management, lease renewal, and graceful shutdown.
/// </summary>
/// <typeparam name="T">The type of work item to process</typeparam>
public abstract class GenericWorkerBase<T> : BackgroundService
{
	private readonly Guid _workerId = Guid.NewGuid();
	protected readonly ILeaseManager<T> LeaseManager;
	protected readonly IWorkProcessor<T> WorkProcessor;
	protected readonly IWorkerOption WorkderOption;
	protected readonly ILogger Logger;

	protected abstract int PollIntervalSeconds { get; }
	protected abstract int MaxConcurrentItems { get; }
	protected abstract int LeaseSeconds { get; }

	protected GenericWorkerBase(
		ILeaseManager<T> leaseManager,
		IWorkProcessor<T> processor,
		ILogger logger)
	{
		LeaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
		WorkProcessor = processor ?? throw new ArgumentNullException(nameof(processor));
		Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		WorkderOption = null!; // Will be set from properties
	}

	protected GenericWorkerBase(
		ILeaseManager<T> leaseManager,
		IWorkProcessor<T> processor,
		IWorkerOption configuration,
		ILogger logger)
	{
		LeaseManager = leaseManager ?? throw new ArgumentNullException(nameof(leaseManager));
		WorkProcessor = processor ?? throw new ArgumentNullException(nameof(processor));
		WorkderOption = configuration ?? throw new ArgumentNullException(nameof(configuration));
		Logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		Logger.LogInformation("Worker started");
		var running = new List<Task>();

		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					// Clean up completed tasks
					foreach (var completed in running.Where(t => t.IsCompleted).ToArray())
					{
						try
						{
							await completed;
						}
						catch (Exception ex)
						{
							Logger.LogError(ex, "Task failed unexpectedly");
						}
						running.Remove(completed);
					}

					// Claim new work if we have capacity
					var available = MaxConcurrentItems - running.Count;
					if (available > 0)
					{
						var items = await LeaseManager.ClaimAsync(_workerId, available, LeaseSeconds, stoppingToken);
						foreach (var claimed in items)
						{
							running.Add(ProcessItemAsync(claimed.Item, claimed.LeaseToken, stoppingToken));
						}
					}

					// Wait before next poll
					await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					Logger.LogError(ex, "Polling failed");
					await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
				}
			}
		}
		finally
		{
			// Wait for all in-flight items to complete
			try
			{
				await Task.WhenAll(running);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "One or more in-flight items failed during shutdown");
			}
			Logger.LogInformation("Worker stopped");
		}
	}

	private async Task ProcessItemAsync(T item, Guid leaseToken, CancellationToken shutdownToken)
	{
		using var leaseLostSource = new CancellationTokenSource();
		using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, leaseLostSource.Token);
		var processingToken = linkedTokenSource.Token;

		Task heartbeat = Task.CompletedTask;

		try
		{
			// Initialize processing
			await WorkProcessor.InitializeAsync(item, leaseToken, _workerId, processingToken);

			// Start heartbeat task
			heartbeat = HeartbeatAsync(item, leaseToken, leaseLostSource, processingToken);

			// Process the item
			var result = await WorkProcessor.ProcessAsync(item, leaseToken, processingToken);

			// Complete processing
			await WorkProcessor.CompleteAsync(item, leaseToken, result, processingToken);

			if (result.Success)
			{
				Logger.LogInformation("Item processed successfully");
			}
			else
			{
				Logger.LogWarning("Item processing failed: {Error}", result.ErrorMessage);
			}
		}
		catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested || leaseLostSource.Token.IsCancellationRequested)
		{
			Logger.LogWarning("Item processing cancelled due to shutdown or lost lease");
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Item processing failed unexpectedly");
			try
			{
				var failureResult = new ProcessingResult { Success = false, ErrorMessage = GetErrorMessage(ex) };
				await WorkProcessor.CompleteAsync(item, leaseToken, failureResult, processingToken);
			}
			catch (Exception completeEx)
			{
				Logger.LogError(completeEx, "Could not record processing failure");
			}
		}
		finally
		{
			await leaseLostSource.CancelAsync();
			try
			{
				await heartbeat;
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
			}
			catch (Exception ex)
			{
				Logger.LogWarning(ex, "Heartbeat ended unexpectedly");
			}
		}
	}

	private async Task HeartbeatAsync(
		T item,
		Guid leaseToken,
		CancellationTokenSource leaseLostSource,
		CancellationToken processingToken)
	{
		try
		{
			var interval = Math.Max(10, WorkProcessor.HeartbeatIntervalSeconds);

			while (!processingToken.IsCancellationRequested)
			{
				await Task.Delay(TimeSpan.FromSeconds(interval), processingToken);

				if (!await LeaseManager.RenewAsync(item, leaseToken, LeaseSeconds, processingToken))
				{
					Logger.LogWarning("Lease renewal rejected or lost");
					await leaseLostSource.CancelAsync();
					return;
				}
			}
		}
		catch (OperationCanceledException) when (processingToken.IsCancellationRequested)
		{
			// Expected cancellation
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, "Lease heartbeat failed");
			await leaseLostSource.CancelAsync();
		}
	}

	private static string GetErrorMessage(Exception ex)
	{
		var msg = ex.Message;
		return msg.Length > 2000 ? msg[..2000] : msg;
	}
}
