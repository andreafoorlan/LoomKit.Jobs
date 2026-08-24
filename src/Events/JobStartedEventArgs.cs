using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Events;

public class JobStartedEventArgs : EventArgs
{
    public required string QueueName { get; set; }
    public required string ConsumerName { get; set; }
    public required JobSchedule JobSchedule { get; set; }
}
