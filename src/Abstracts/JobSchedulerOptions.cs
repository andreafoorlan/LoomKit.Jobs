namespace LoomKit.Jobs.Abstracts;

// No ServiceLifetime here unlike RequestSenderOptions/NotificationDispatcherOptions in the sibling
// libraries: IJobScheduler is always an IHostedService, and the host only manages hosted services
// as singletons, so a configurable lifetime would not make sense for the scheduler itself.
public abstract class JobSchedulerOptions
{
    public Dictionary<string, JobQueueOptions> JobQueueOptions { get; init; } = new();
    public Dictionary<string, JobConsumerOptions> JobConsumerOptions { get; init; } = new();

    // Not `required`: a type with required members can't satisfy the new() constraint that
    // JobSchedulerOptionsBuilder<T>.Build() relies on (see sibling RequestSenderOptions, which
    // avoids `required` for the same reason).
    public List<Type> JobSchedulerSeeders { get; init; } = [];
}
