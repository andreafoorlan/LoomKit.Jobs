namespace LoomKit.Jobs.Models;

public record CronJobSchedule : JobSchedule
{
    public DateTime? CronStartAt { get; init; }
    public DateTime? CronEndAt { get; init; }
    public required string CronExpression { get; init; }
}
