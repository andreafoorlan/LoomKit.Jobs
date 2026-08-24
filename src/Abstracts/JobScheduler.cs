using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Events;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Abstracts;

public abstract class JobScheduler<TJobSchedulerOptions> : IJobScheduler
    where TJobSchedulerOptions : JobSchedulerOptions
{
    protected readonly TJobSchedulerOptions _jobSchedulerOptions;

    public JobScheduler(TJobSchedulerOptions jobSchedulerOptions)
    {
        // deps
        ArgumentNullException.ThrowIfNull(jobSchedulerOptions);
        _jobSchedulerOptions = jobSchedulerOptions;
    }

    public abstract Task StartAsync(CancellationToken cancellationToken);
    public abstract Task StopAsync(CancellationToken cancellationToken);

    public abstract Task<Dictionary<string, IJobQueue>> ListQueuesAsync(CancellationToken cancellationToken = default);
    public abstract Task<Dictionary<string, IJobConsumer>> ListConsumersAsync(CancellationToken cancellationToken = default);

    public abstract Task EnqueueJobScheduleAsync(string queueName, JobSchedule jobSchedule, CancellationToken cancellationToken = default);

    public abstract Task<Dictionary<string, List<JobSchedule>>> ListQueuedJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken);
    public abstract Task<Dictionary<string, List<JobSchedule>>> ListQueuedJobSchedulesAsync(string queueName, Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken);

    public abstract Task<JobQueueOptions?> GetJobQueueOptionsAsync(string queueName, CancellationToken cancellationToken = default);
    public abstract Task<JobConsumerOptions?> GetJobConsumerOptionsAsync(string consumerName, CancellationToken cancellationToken = default);

    public abstract Task<Dictionary<string, long>> RemoveQueuedJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default);
    public abstract Task<Dictionary<string, long>> RemoveQueuedJobSchedulesAsync(string queueName, Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default);

    public abstract Task<List<JobSchedule>> ListConsumingJobSchedulesAsync(CancellationToken cancellationToken = default);

    public abstract Task NotifyJobScheduled(string queueName, JobSchedule jobSchedule);
    public abstract Task NotifyJobStarted(string queueName, string consumerName, JobSchedule jobSchedule);
    public abstract Task NotifyJobEnded(string queueName, string consumerName, JobSchedule jobSchedule);
    public abstract Task NotifyJobException(string queueName, string consumerName, JobSchedule jobSchedule, Exception exception);

    public abstract event EventHandler<JobScheduledEventArgs> JobScheduled;
    public abstract event EventHandler<JobStartedEventArgs> JobStarted;
    public abstract event EventHandler<JobEndedEventArgs> JobEnded;
    public abstract event EventHandler<JobExceptionEventArgs> JobException;
}
