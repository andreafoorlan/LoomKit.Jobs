using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Events;

public class JobExceptionEventArgs : EventArgs
{
    public required string QueueName { get; set; }
    public required string ConsumerName { get; set; }
    public required JobSchedule JobSchedule { get; set; }
    public required Exception Exception { get; set; }
}
