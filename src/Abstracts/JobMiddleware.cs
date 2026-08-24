using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Abstracts;

public abstract class JobMiddleware<TJob, TJobStatus> : IJobHandler<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>
    where TJobStatus : JobStatus, new()
{
    protected readonly IJobHandler<TJob, TJobStatus> _nextHandler;

    public JobMiddleware(IJobHandler<TJob, TJobStatus> nextHandler)
    {
        // deps
        _nextHandler = nextHandler;
    }

    public abstract Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default);
}
