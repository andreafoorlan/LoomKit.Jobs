using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Tests.Fixtures;

public sealed class PingJob : IJob<JobStatus>, ITraceable
{
    public List<string> Trace { get; } = [];

    public CancellationToken ObservedToken { get; set; }
}

public sealed class PingJobHandler : IJobHandler<PingJob, JobStatus>
{
    public Task HandleAsync(PingJob job, JobSchedule jobSchedule, JobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        job.Trace.Add("handler");
        job.ObservedToken = cancellationToken;

        return Task.CompletedTask;
    }
}
