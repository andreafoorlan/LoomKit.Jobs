using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Events;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.Hosting;

namespace LoomKit.Jobs.Contracts;

public interface IJobScheduler : IHostedService
{
    Task<Dictionary<string, IJobQueue>> ListQueuesAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, IJobConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default);

    Task EnqueueJobScheduleAsync(string queueName, JobSchedule jobSchedule, CancellationToken cancellationToken = default);

    Task<Dictionary<string, List<JobSchedule>>> ListQueuedJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken);
    Task<List<JobSchedule>> ListQueuedJobSchedulesAsync(string queueName, Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken);

    Task<JobQueueOptions> GetJobQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default);
    Task<JobConsumerOptions> GetJobConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default);

    Task<Dictionary<string, long>> RemoveQueuedJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default);
    Task<long> RemoveQueuedJobSchedulesAsync(string queueName, Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default);

    Task<List<JobSchedule>> ListConsumingJobSchedulesAsync(CancellationToken cancellationToken = default);

    Task NotifyJobScheduled(string queueName, JobSchedule jobSchedule);
    Task NotifyJobStarted(string queueName, string consumerName, JobSchedule jobSchedule);
    Task NotifyJobEnded(string queueName, string consumerName, JobSchedule jobSchedule);
    Task NotifyJobException(string queueName, string consumerName, JobSchedule jobSchedule, Exception exception);

    event EventHandler<JobScheduledEventArgs> JobScheduled;
    event EventHandler<JobStartedEventArgs> JobStarted;
    event EventHandler<JobEndedEventArgs> JobEnded;
    event EventHandler<JobExceptionEventArgs> JobException;
}
