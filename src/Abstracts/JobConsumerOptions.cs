namespace LoomKit.Jobs.Abstracts;

public class JobConsumerOptions
{
    public required string JobConsumerName { get; init; }
    public required Type JobConsumerType { get; init; }

    public required string JobQueueName { get; init; }

    public bool UseScopedServiceProvider { get; init; }

    // Single unified pipeline - the void/response split that used to exist here is gone now
    // that every job carries its result (if any) on its JobStatus instead of a handler return
    // value; see IJobHandler<TJob, TJobStatus>.
    public required IReadOnlyList<Type> JobMiddlewares { get; init; }
}
