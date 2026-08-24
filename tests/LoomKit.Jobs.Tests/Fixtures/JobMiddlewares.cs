using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Tests.Fixtures;

// A single generic pair, usable for any ITraceable job regardless of its TJobStatus - the void/
// response split that used to force two separate middleware pairs per behavior is gone.
public sealed class FirstMiddleware<TJob, TJobStatus> : JobMiddleware<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>, ITraceable
    where TJobStatus : JobStatus, new()
{
    public FirstMiddleware(IJobHandler<TJob, TJobStatus> nextHandler) : base(nextHandler) { }

    public override async Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        job.Trace.Add("first:before");

        await _nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);

        job.Trace.Add("first:after");
    }
}

public sealed class SecondMiddleware<TJob, TJobStatus> : JobMiddleware<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>, ITraceable
    where TJobStatus : JobStatus, new()
{
    public SecondMiddleware(IJobHandler<TJob, TJobStatus> nextHandler) : base(nextHandler) { }

    public override async Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        job.Trace.Add("second:before");

        await _nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);

        job.Trace.Add("second:after");
    }
}
