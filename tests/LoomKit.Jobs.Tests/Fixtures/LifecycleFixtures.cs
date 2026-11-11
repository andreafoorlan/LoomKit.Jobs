using System.Collections.Concurrent;
using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Tests.Fixtures;

// Counts how many times a scope-owned service was disposed asynchronously.
public sealed class DisposalLog
{
    private int _asyncDisposals;

    public int AsyncDisposals => Volatile.Read(ref _asyncDisposals);

    public void RecordAsyncDisposal() => Interlocked.Increment(ref _asyncDisposals);
}

// Implements ONLY IAsyncDisposable (no IDisposable), like a pooled DbContext lease or an async
// client. The DI container can only dispose it through DisposeAsync on the scope: a synchronous
// scope.Dispose() throws InvalidOperationException for such a service.
public sealed class AsyncOnlyDisposable(DisposalLog log) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        log.RecordAsyncDisposal();

        return ValueTask.CompletedTask;
    }
}

public sealed class ScopedDependencyJob : IJob<JobStatus>
{
    private int _handled;

    public bool Handled => Volatile.Read(ref _handled) == 1;

    public void MarkHandled() => Volatile.Write(ref _handled, 1);
}

public sealed class ScopedDependencyJobHandler(AsyncOnlyDisposable dependency) : IJobHandler<ScopedDependencyJob, JobStatus>
{
    // held only so the scope creates (and therefore owns) the service
    private readonly AsyncOnlyDisposable _dependency = dependency;

    public Task HandleAsync(ScopedDependencyJob job, JobSchedule jobSchedule, JobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        job.MarkHandled();

        return Task.CompletedTask;
    }
}

// A queue that, like a database-backed one, honours the cancellation token by letting the
// OperationCanceledException escape from AwaitForJobScheduleAsync (InProcessJobQueue swallows it).
public sealed class CancellationThrowingJobQueue(JobQueueOptions jobQueueOptions) : IJobQueue
{
    public string JobQueueName => jobQueueOptions.JobQueueName;

    public JobQueueOptions JobQueueOptions => jobQueueOptions;

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EnqueueJobScheduleAsync(JobSchedule jobSchedule, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<List<JobSchedule>> ListJobSchedulesAsync(Func<IQueryable<JobSchedule>, IQueryable<JobSchedule>> queryBuilder, CancellationToken cancellationToken)
        => Task.FromResult(new List<JobSchedule>());

    public Task<long> RemoveJobSchedulesAsync(Predicate<JobSchedule> predicate, CancellationToken cancellationToken = default) => Task.FromResult(0L);

    public async Task<JobSchedule?> AwaitForJobScheduleAsync(string consumerName, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);

        return null;
    }

    public Task NotifyJobStarted(string consumerName, JobSchedule jobSchedule) => Task.CompletedTask;

    public Task NotifyJobEnded(string consumerName, JobSchedule jobSchedule) => Task.CompletedTask;

    public Task NotifyJobException(string consumerName, JobSchedule jobSchedule, Exception exception) => Task.CompletedTask;
}

// Collects every log entry so a test can assert on what was (not) logged.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue((logLevel, $"{category}: {formatter(state, exception)}"));
    }
}
