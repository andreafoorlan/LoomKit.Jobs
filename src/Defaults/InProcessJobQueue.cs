using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Defaults;

public class InProcessJobQueue : IJobQueue
{
    private readonly IJobScheduler _jobScheduler;
    private readonly JobQueueOptions _jobQueueOptions;
    private readonly ILogger<InProcessJobQueue> _logger;
    private readonly SemaphoreSlim _scheduleJobSemaphore;
    private readonly SemaphoreSlim _awaitJobSemaphore;
    private readonly List<JobSchedule> _jobSchedules;

    private bool _jobQueueStarted;

    public string JobQueueName => _jobQueueOptions.JobQueueName;
    public JobQueueOptions JobQueueOptions => _jobQueueOptions;

    public InProcessJobQueue(
        IJobScheduler jobScheduler,
        JobQueueOptions jobQueueOptions,
        ILogger<InProcessJobQueue> logger)
    {
        // deps
        _jobScheduler = jobScheduler;
        _jobQueueOptions = jobQueueOptions;
        _logger = logger;

        // init
        _scheduleJobSemaphore = new SemaphoreSlim(1, 1);
        _awaitJobSemaphore = new SemaphoreSlim(1, 1);
        _jobSchedules = new List<JobSchedule>();
        _jobQueueStarted = false;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        //
        _jobQueueStarted = true;

        //
        _logger.LogInformation("[{methodName}] Job queue {jobQueueName} started", nameof(StartAsync), _jobQueueOptions.JobQueueName);

        //
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        //
        _jobQueueStarted = false;

        //
        _logger.LogInformation("[{methodName}] Job queue {jobQueueName} stopped", nameof(StopAsync), _jobQueueOptions.JobQueueName);

        //
        return Task.CompletedTask;
    }

    public async Task EnqueueJobScheduleAsync(JobSchedule jobSchedule, CancellationToken cancellationToken = default)
    {
        // wait and lock to add
        await _scheduleJobSemaphore.WaitAsync(cancellationToken);

        // set job schedule status queue name
        jobSchedule.JobStatus.QueueName = _jobQueueOptions.JobQueueName;

        // add job schedule to list
        _jobSchedules.Add(jobSchedule);

        //
        _logger.LogInformation("[{queueName}] Scheduled job schedule {jobScheduleId} at {nextAt}", _jobQueueOptions.JobQueueName, jobSchedule.JobScheduleId, jobSchedule.NextAt);

        // release lock
        _scheduleJobSemaphore.Release();

        //
        await _jobScheduler.NotifyJobScheduled(_jobQueueOptions.JobQueueName, jobSchedule);
    }

    public async Task<List<JobSchedule>> ListJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken)
    {
        // get queryable
        var queryable = _jobSchedules.AsQueryable<JobSchedule>();

        // apply builder to queryable
        queryable = queryBuilder(queryable);

        //
        await _scheduleJobSemaphore.WaitAsync(cancellationToken);

        // get list of job schedules from queryable
        var jobSchedules = queryable.ToList();

        //
        _scheduleJobSemaphore.Release();

        //
        return jobSchedules;
    }

    public async Task<long> RemoveJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default)
    {
        //
        await _scheduleJobSemaphore.WaitAsync(cancellationToken);

        //
        var removedCount = _jobSchedules.RemoveAll(predicate);

        //
        _scheduleJobSemaphore.Release();

        //
        return removedCount;
    }

    public async Task<JobSchedule?> AwaitForJobScheduleAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        // init
        JobSchedule? candidateJobSchedule = default;

        // wait and lock to dequeue
        await _awaitJobSemaphore.WaitAsync(cancellationToken);

        //
        do
        {
            // perform check only if queue is started
            if (_jobQueueStarted)
            {
                // lock scheduling
                await _scheduleJobSemaphore.WaitAsync(cancellationToken);

                // get the first candidate job schedule
                candidateJobSchedule = _jobSchedules
                    .Where(js => js.NextAt <= DateTime.UtcNow)
                    .MinBy(js => js.NextAt);

                // unlock scheduling
                _scheduleJobSemaphore.Release();

                //
                _logger.LogTrace("[{consumerName}] Waiting for a job schedule, currently scheduled {jobSchedulesCount} jobs", consumerName, _jobSchedules.Count);
            }
            else
            {
                //
                _logger.LogTrace("[{consumerName}] Queue not started, currently scheduled {jobSchedulesCount} jobs", consumerName, _jobSchedules.Count);
            }

            // loop delay - honors cancellation so shutdown doesn't have to wait out a full
            // JobAwaitCheckInterval (the original version didn't pass the token here)
            try
            {
                await Task.Delay(_jobQueueOptions.JobAwaitCheckInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

        } while (candidateJobSchedule is null && !cancellationToken.IsCancellationRequested);

        //
        _logger.LogTrace("[{consumerName}] Handling job schedule {candidateJobScheduleId}, scheduled at {candidateNextJobScheduleAt}", consumerName, candidateJobSchedule?.JobScheduleId, candidateJobSchedule?.NextAt.ToLongTimeString());

        // remove from schedules queue - the candidate is already in hand, no need to re-scan the
        // list by predicate the way RemoveJobSchedulesAsync does
        if (candidateJobSchedule is not null)
        {
            await _scheduleJobSemaphore.WaitAsync(cancellationToken);
            _jobSchedules.Remove(candidateJobSchedule);
            _scheduleJobSemaphore.Release();
        }

        // release lock
        _awaitJobSemaphore.Release();

        //
        return candidateJobSchedule;
    }

    public async Task NotifyJobStarted(string consumerName, JobSchedule jobSchedule)
    {
        //
        await _jobScheduler.NotifyJobStarted(_jobQueueOptions.JobQueueName, consumerName, jobSchedule);
    }

    public async Task NotifyJobEnded(string consumerName, JobSchedule jobSchedule)
    {
        //
        await _jobScheduler.NotifyJobEnded(_jobQueueOptions.JobQueueName, consumerName, jobSchedule);
    }

    public async Task NotifyJobException(string consumerName, JobSchedule jobSchedule, Exception exception)
    {
        //
        await _jobScheduler.NotifyJobException(_jobQueueOptions.JobQueueName, consumerName, jobSchedule, exception);
    }
}
