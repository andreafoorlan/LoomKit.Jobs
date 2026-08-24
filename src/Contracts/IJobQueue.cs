using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Contracts;

public interface IJobQueue
{
    public string? JobQueueName { get; }
    public JobQueueOptions? JobQueueOptions { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task EnqueueJobScheduleAsync(JobSchedule jobSchedule, CancellationToken cancellationToken = default);

    Task<List<JobSchedule>> ListJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken);
    Task<long> RemoveJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default);

    Task<JobSchedule?> AwaitForJobScheduleAsync(string consumerName, CancellationToken cancellationToken = default);

    Task NotifyJobStarted(string consumerName, JobSchedule jobSchedule);
    Task NotifyJobEnded(string consumerName, JobSchedule jobSchedule);

    Task NotifyJobException(string consumerName, JobSchedule jobSchedule, Exception exception);
}
