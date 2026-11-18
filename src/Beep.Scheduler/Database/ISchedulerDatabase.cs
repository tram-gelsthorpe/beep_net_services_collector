namespace Beep.Scheduler;

public interface ISchedulerDatabase
{
	Task Create(string schema, CancellationToken cancellationToken = default);
}
