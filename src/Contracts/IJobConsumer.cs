using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Contracts;

public interface IJobConsumer
{
    public string JobConsumerName { get; }
    public string JobQueueName { get; }
    public JobConsumerOptions JobConsumerOptions { get; }
    public bool IsStarted { get; }
    public JobSchedule? CurrentJobSchedule { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
