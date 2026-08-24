using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Tests.Fixtures;

public sealed class TraceJob : IJob<JobStatus<List<string>>>, ITraceable
{
    public List<string> Trace { get; } = [];

    public CancellationToken ObservedToken { get; set; }
}

public sealed class TraceJobHandler : IJobHandler<TraceJob, JobStatus<List<string>>>
{
    public Task HandleAsync(TraceJob job, JobSchedule jobSchedule, JobStatus<List<string>> jobStatus, CancellationToken cancellationToken = default)
    {
        job.Trace.Add("handler");
        job.ObservedToken = cancellationToken;

        // The core behavior of the IJob<TJobStatus> redesign: the response is written onto the
        // typed status as the handler runs, not returned at the end - it's visible on JobStatus
        // to anything holding the JobSchedule (events, queries) as soon as it's set, not only
        // after the whole pipeline completes.
        jobStatus.JobResponse = job.Trace;

        return Task.CompletedTask;
    }
}
