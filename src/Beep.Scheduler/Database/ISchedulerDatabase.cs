namespace Beep.Scheduler;

public interface ISchedulerDatabase
{
	Task Create(CancellationToken cancellationToken = default);
}
