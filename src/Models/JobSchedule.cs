using LoomKit.Jobs.Contracts;

namespace LoomKit.Jobs.Models;

public record JobSchedule
{
    public string JobScheduleId { get; init; }
            = Guid.NewGuid().ToString();

    public string JobGroupId { get; init; }
        = Guid.NewGuid().ToString();

    public required IJob Job { get; init; }

    public required DateTime NextAt { get; init; }
    public int RetriesLeft { get; init; }

    public required JobStatus JobStatus { get; init; }
}
