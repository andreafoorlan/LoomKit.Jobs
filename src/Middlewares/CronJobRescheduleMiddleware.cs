using System.Diagnostics;
using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Extensions;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Middlewares;

public class CronJobRescheduleMiddleware<TJob, TJobStatus> : JobMiddleware<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>
    where TJobStatus : JobStatus, new()
{
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<CronJobRescheduleMiddleware<TJob, TJobStatus>> _logger;

    public CronJobRescheduleMiddleware(IJobHandler<TJob, TJobStatus> nextHandler, IJobScheduler jobScheduler, ILogger<CronJobRescheduleMiddleware<TJob, TJobStatus>> logger)
        : base(nextHandler)
    {
        // deps
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    public override async Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        // do stuff
        await _nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);

        // check if job schedule is a cron job schedule
        // (note: the original void/response variants disagreed on whether to also require
        // RetriesLeft > 0 here - that check belongs to the retry budget of a single occurrence,
        // not to whether the recurring series continues, so it's dropped for both cases)
        if (jobSchedule is CronJobSchedule cronJobSchedule
            && !string.IsNullOrWhiteSpace(jobStatus.QueueName))
        {
            //
            _logger.LogDebug("Trying to reschedule job schedule with id {jobScheduleId}", jobSchedule.JobScheduleId);

            //
            await _jobScheduler.ScheduleCronAsync(
                jobStatus.QueueName,
                cronJobSchedule.CronExpression,
                cronJobSchedule.CronStartAt,
                cronJobSchedule.CronEndAt,
                job,
                cronJobSchedule.JobScheduleId,
                cronJobSchedule.JobGroupId,
                cancellationToken);

            Activity.Current?.AddEvent(new ActivityEvent("job.rescheduled", tags: new ActivityTagsCollection
            {
                ["cron.expression"] = cronJobSchedule.CronExpression
            }));
        }
    }
}
