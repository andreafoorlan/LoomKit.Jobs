namespace LoomKit.Jobs.Models;

public class JobStatus
{
    public string? QueueName { get; set; }
    public string? ConsumerName { get; set; }
    public string? TaskDescription { get; set; }
    public long TaskProgress { get; set; }
    public long TaskTotal { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public JobStatus? JobSubStatus { get; set; }

    // Untyped storage so a queue holding heterogeneous job types can still carry a response of
    // some kind. Handlers of a job declared IJob<JobStatus<TResponse>> get a typed view of this
    // same slot through JobStatus<TResponse>.JobResponse below - reading/writing either one reads
    // or writes the same value.
    public virtual object? JobResponse { get; set; }
}

// Typed view of JobStatus for jobs that produce a response. Handlers receive this concrete type
// (via IJobHandler<TJob, JobStatus<TResponse>>) and can read/write JobResponse at any point during
// execution - not just once at the end - without casting.
public class JobStatus<TResponse> : JobStatus
{
    public new TResponse? JobResponse
    {
        get => base.JobResponse is TResponse response ? response : default;
        set => base.JobResponse = value;
    }
}
