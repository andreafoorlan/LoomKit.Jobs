namespace LoomKit.Jobs.Contracts;

public interface IJobSchedulerSeeder
{
    Task SeedJobs(IJobScheduler jobScheduler);
}
