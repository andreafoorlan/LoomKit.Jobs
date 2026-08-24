using System.Diagnostics;
using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Middlewares;

public class JobRetryMiddleware<TJob, TJobStatus> : JobMiddleware<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>
    where TJobStatus : JobStatus, new()
{
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<JobRetryMiddleware<TJob, TJobStatus>> _logger;

    public JobRetryMiddleware(IJobHandler<TJob, TJobStatus> nextHandler, IJobScheduler jobScheduler, ILogger<JobRetryMiddleware<TJob, TJobStatus>> logger)
        : base(nextHandler)
    {
        // deps
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public override async Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        try
        {
            // do stuff
            await _nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);
        }
        catch (Exception exception)
        {
            //
            _logger.LogDebug("Exception in job schedule with id {jobScheduleId} of type {exceptionType} with message {exceptionMessage} ", jobSchedule.JobScheduleId, exception.GetType().AssemblyQualifiedName, exception.Message);

            // reschedule if any retry left
            if (jobSchedule.RetriesLeft > 1
                && !string.IsNullOrWhiteSpace(jobStatus.QueueName))
            {
                //
                _logger.LogDebug("Retrying job schedule with id {jobScheduleId}, {retriesLeft} retries left", jobSchedule.JobScheduleId, jobSchedule.RetriesLeft - 1);

                Activity.Current?.AddEvent(new ActivityEvent("job.retried", tags: new ActivityTagsCollection
                {
                    ["retries_left"] = jobSchedule.RetriesLeft - 1
                }));

                // fresh, correctly-typed status for the retried occurrence - TJobStatus is a
                // compile-time parameter here so no reflection is needed (contrast with
                // JobSchedulerExtensions, which only knows TJob and resolves TJobStatus reflectively)
                var retryJobSchedule = jobSchedule with
                {
                    RetriesLeft = jobSchedule.RetriesLeft - 1,
                    NextAt = DateTime.UtcNow,
                    JobStatus = new TJobStatus()
                };

                //
                await _jobScheduler.EnqueueJobScheduleAsync(
                    jobStatus.QueueName,
                    retryJobSchedule,
                    cancellationToken);
            }
            else
            {
                //
                _logger.LogDebug("No retries left for job schedule with id {jobScheduleId}", jobSchedule.JobScheduleId);

                Activity.Current?.AddEvent(new ActivityEvent("job.retries_exhausted", tags: new ActivityTagsCollection
                {
                    ["exception.type"] = exception.GetType().FullName!,
                    ["exception.message"] = exception.Message
                }));

                // rethrow for further analysis
                throw;
            }
        }
    }
}
