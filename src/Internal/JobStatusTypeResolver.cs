using System.Collections.Concurrent;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;

namespace LoomKit.Jobs.Internal;

// Shared reflection helper for resolving a job's concrete TJobStatus (from the single closed
// IJob<TJobStatus> it implements) and for constructing a fresh instance of it. Used at every
// point that creates a brand new JobSchedule for a job (ScheduleNowAsync/ScheduleAtAsync/
// ScheduleCronAsync) and by the consumer's dispatch pipeline, which only knows the job as
// `object` at that point. Resolved once per job type and cached - not recomputed per call.
internal static class JobStatusTypeResolver
{
    private static readonly ConcurrentDictionary<Type, Type> _jobStatusTypeCache = new();

    public static Type ResolveJobStatusType(Type jobType)
    {
        return _jobStatusTypeCache.GetOrAdd(jobType, static type =>
        {
            var jobInterface = type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJob<>));

            if (jobInterface is null)
                throw new InvalidOperationException($"Type {type.FullName} does not implement IJob<TJobStatus>.");

            return jobInterface.GetGenericArguments()[0];
        });
    }

    public static JobStatus CreateJobStatus(Type jobType)
    {
        var jobStatusType = ResolveJobStatusType(jobType);

        return (JobStatus)(Activator.CreateInstance(jobStatusType)
            ?? throw new InvalidOperationException($"Could not construct an instance of {jobStatusType.FullName}."));
    }
}
