using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Events;

public class JobScheduledEventArgs : EventArgs
{
    public required string QueueName { get; set; }
    public required JobSchedule JobSchedule { get; set; }
}
