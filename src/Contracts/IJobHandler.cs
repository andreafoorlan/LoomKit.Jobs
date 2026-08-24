using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Contracts;

// Single handler shape for every job, with or without a typed response - the response (if any)
// is written onto the strongly-typed jobStatus as the handler runs, not returned at the end.
// See JobStatus<TResponse> in Models for the typed-response case.
public interface IJobHandler<in TJob, TJobStatus>
    where TJob : IJob<TJobStatus>
    where TJobStatus : JobStatus, new()
{
    Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default);
}
