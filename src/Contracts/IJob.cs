using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Contracts;

// True common marker for every job, regardless of its status type - kept non-generic so that
// JobSchedule.Job can hold any job uniformly (a single queue mixes many job/status types).
public interface IJob { }

// TJobStatus must be constructible: a fresh instance is created for every new JobSchedule
// (ScheduleNowAsync/ScheduleAtAsync/ScheduleCronAsync) and for every retry re-enqueue.
public interface IJob<TJobStatus> : IJob
    where TJobStatus : JobStatus, new()
{ }
