# LoomKit.Jobs

A background job scheduling library for .NET: jobs are placed on named **queues**, picked up by **consumers**, and processed through an extensible **middleware pipeline** — with support for one-shot, delayed, and recurring cron jobs. This is the deferred-execution sibling of [LoomKit.Requests](https://github.com/andreafoorlan/LoomKit.Requests) and [LoomKit.Notifications](https://github.com/andreafoorlan/LoomKit.Notifications): same dependency-injection and middleware shape, adapted to work run later instead of work run now.

> **Status:** early stage. The public API may still change between versions — pin a commit/tag if you depend on it.

## Features

- Named queues and consumers, wired together and started/stopped as a single `IHostedService`
- One-shot (`ScheduleNowAsync`), delayed (`ScheduleAtAsync`), and recurring cron (`ScheduleCronAsync`) scheduling
- Handlers resolved from your DI container (`Microsoft.Extensions.DependencyInjection`)
- An optional middleware pipeline per consumer, configured at startup — built-in retry and cron-reschedule middleware included
- Every job carries a `JobStatus` (progress, timing, and — for jobs that produce one — a typed response) that's visible while the job runs, not only after it finishes
- Built-in tracing via `System.Diagnostics.ActivitySource` (OpenTelemetry-compatible)
- Lifecycle events (`JobScheduled`/`JobStarted`/`JobEnded`/`JobException`) on the scheduler
- `CancellationToken` propagated end-to-end, from the consumer through every middleware down to the handler
- Extensible: bring your own `IJobQueue`, `IJobConsumer`, or `IJobScheduler` implementation if the defaults don't fit

## Requirements

- .NET 10 or later
- Runtime dependencies: `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, and [`Cronos`](https://github.com/HangfireIO/Cronos) for cron expression parsing

## Installation

### Via NuGet (recommended)

```bash
dotnet add package LoomKit.Jobs
```

Available on [nuget.org](https://www.nuget.org/packages/LoomKit.Jobs) once the first tagged release (`v1.0.0`) has been published — a package version is published automatically for every `vX.Y.Z` tag pushed to this repo.

If you'd rather build against the source directly instead (e.g. to track `main`, or to debug/modify the library alongside your app), two options:

### As a git submodule

```bash
git submodule add https://github.com/andreafoorlan/LoomKit.Jobs.git external/LoomKit.Jobs
cd external/LoomKit.Jobs
git checkout v1.0.0
cd ../..
git add external/LoomKit.Jobs
git commit -m "Add LoomKit.Jobs submodule pinned to v1.0.0"
```

Then reference the project from your solution/project:

```xml
<ProjectReference Include="..\external\LoomKit.Jobs\src\LoomKit.Jobs.csproj" />
```

When cloning a repository that already has this submodule:

```bash
git clone --recurse-submodules <your-repo-url>
# or, on an existing clone:
git submodule update --init --recursive
```

To move to a newer release later:

```bash
cd external/LoomKit.Jobs
git fetch --tags
git checkout v1.1.0
cd ../..
git add external/LoomKit.Jobs
git commit -m "Bump LoomKit.Jobs submodule to v1.1.0"
```

(`git submodule add -b <tag>` doesn't pin reliably since submodules track branches, not tags — `checkout` inside the submodule plus committing the resulting gitlink in the parent repo is what actually pins the commit.)

### Plain project reference

If you're vendoring the source directly instead of using a submodule:

```xml
<ProjectReference Include="..\path\to\LoomKit.Jobs\src\LoomKit.Jobs.csproj" />
```

## Core concepts

| Type | Purpose |
|---|---|
| `IJob` | Non-generic marker every job implements — lets a queue hold many different job types at once. |
| `IJob<TJobStatus>` | The contract a job actually declares, tying it to the `JobStatus` type it carries. Use `IJob<JobStatus>` for a job with no typed response, `IJob<JobStatus<TResponse>>` for one that produces a `TResponse`. |
| `JobStatus` / `JobStatus<TResponse>` | Runtime state attached to every scheduled job (queue/consumer name, timing, progress) — mutable, and readable at any point while the job runs, not just after it completes. |
| `IJobHandler<TJob, TJobStatus>` | Implement one per job type — this is where the actual logic lives. |
| `IJobQueue` | Stores and delivers `JobSchedule` instances. `InProcessJobQueue` is the built-in in-memory implementation. |
| `IJobConsumer` | Background worker that dequeues jobs from a queue and dispatches them to handlers via a middleware pipeline. |
| `IJobScheduler` | The entry point your application code calls to enqueue jobs — also the `IHostedService` that owns every queue and consumer. |
| `JobMiddleware<TJob, TJobStatus>` | Optional cross-cutting behavior wrapped around a handler (logging, retry, cron reschedule, ...). |
| `IJobSchedulerSeeder` | Hook called once at scheduler startup to pre-enqueue jobs. |

## Quick start

### 1. Define jobs and handlers

```csharp
// A job with no typed response
public sealed class SendEmailJob : IJob<JobStatus>
{
    public required string To { get; init; }
    public required string Subject { get; init; }
}

public sealed class SendEmailHandler : IJobHandler<SendEmailJob, JobStatus>
{
    private readonly IEmailService _email;

    public SendEmailHandler(IEmailService email) => _email = email;

    public Task HandleAsync(SendEmailJob job, JobSchedule jobSchedule, JobStatus jobStatus, CancellationToken cancellationToken = default)
        => _email.SendAsync(job.To, job.Subject, cancellationToken);
}

// A job that produces a response
public sealed class FetchExchangeRateJob : IJob<JobStatus<decimal>>
{
    public required string Currency { get; init; }
}

public sealed class FetchExchangeRateHandler : IJobHandler<FetchExchangeRateJob, JobStatus<decimal>>
{
    private readonly IRatesService _rates;

    public FetchExchangeRateHandler(IRatesService rates) => _rates = rates;

    public async Task HandleAsync(FetchExchangeRateJob job, JobSchedule jobSchedule, JobStatus<decimal> jobStatus, CancellationToken cancellationToken = default)
    {
        // written onto the status as soon as it's known - not returned - see "Why JobStatus<TResponse>
        // instead of IJob<TResponse>?" below
        jobStatus.JobResponse = await _rates.GetRateAsync(job.Currency, cancellationToken);
    }
}
```

### 2. Register handlers, queues, and consumers in DI

Handlers are plain DI services. Register them one by one:

```csharp
services.AddScoped<IJobHandler<SendEmailJob, JobStatus>, SendEmailHandler>();
services.AddScoped<IJobHandler<FetchExchangeRateJob, JobStatus<decimal>>, FetchExchangeRateHandler>();
```

...or scan one or more assemblies for every closed `IJobHandler<,>` implementation and register them all at once — no extra package required, this is built in:

```csharp
services.AddJobHandlersFromAssemblies(ServiceLifetime.Scoped, typeof(Program).Assembly);
```

Calling it more than once, or passing overlapping assemblies, won't produce duplicate registrations. Note it only picks up **closed, concrete** handler classes — an open-generic handler isn't discovered and must still be registered by hand.

Then wire up the scheduler itself — its queues, its consumers, and (optionally) a startup seeder:

```csharp
services.AddDefaultJobScheduler(builder => builder
    .UseQueue<InProcessJobQueue>("email", q =>
    {
        q.MaxJobRetries = 3;
        q.JobAwaitCheckInterval = 500;    // ms between queue polls
        q.JobRetryInterval = 10_000;      // ms before re-enqueuing a failed job
    })
    .UseConsumer<JobConsumer>("email-consumer", "email", c =>
    {
        c.UseScopedServiceProvider = true;                // one DI scope per job
        c.UseJobMiddleware(typeof(JobRetryMiddleware<,>));
    })
    .UseJobSchedulerSeeder<StartupJobSeeder>());
```

`AddDefaultJobScheduler` registers `IJobScheduler` as a singleton and as an `IHostedService` (not configurable — see [Extensibility](#extensibility-custom-queues-consumers-and-schedulers)), backed by the default `JobScheduler`, `InProcessJobQueue`, and `JobConsumer`.

### 3. Schedule jobs

Inject `IJobScheduler` and use the convenience extension methods:

```csharp
public sealed class NotificationService(IJobScheduler scheduler)
{
    public Task SendWelcomeEmailAsync(string email, CancellationToken cancellationToken = default)
        => scheduler.ScheduleNowAsync("email", new SendEmailJob { To = email, Subject = "Welcome!" }, cancellationToken: cancellationToken);

    public Task ScheduleReminderAsync(string email, DateTime remindAt, CancellationToken cancellationToken = default)
        => scheduler.ScheduleAtAsync("email", remindAt, new SendEmailJob { To = email, Subject = "Reminder" }, cancellationToken: cancellationToken);

    public Task StartDailyReportAsync(CancellationToken cancellationToken = default)
        => scheduler.ScheduleCronAsync(
            queueName: "reports",
            cronExpression: "0 8 * * *",  // every day at 08:00
            cronStartAt: DateTime.UtcNow,
            cronEndAt: null,
            job: new DailyReportJob(),
            cancellationToken: cancellationToken);
}
```

## Middleware pipeline

A middleware wraps the next handler in the chain and decides whether/when to call it — same idea as ASP.NET Core middleware, but per consumer.

> **You don't register middleware classes in DI.** The pipeline constructs them directly via `ActivatorUtilities`, passing the next handler explicitly and resolving any other constructor parameter (like `ILogger<>` below) from the container. All you register in DI are the middleware's *own* dependencies, if any — the middleware type itself is only ever passed to `UseJobMiddleware`, never to `services.Add...`.

### Built-in middleware

| Middleware | Description |
|---|---|
| `JobRetryMiddleware<TJob, TJobStatus>` | Catches exceptions and re-enqueues the job with `RetriesLeft - 1`. Rethrows once retries are exhausted. |
| `CronJobRescheduleMiddleware<TJob, TJobStatus>` | After successful execution of a `CronJobSchedule`, computes the next occurrence and re-enqueues. |

### Custom middleware

Extend `JobMiddleware<TJob, TJobStatus>`:

```csharp
public sealed class LoggingMiddleware<TJob, TJobStatus> : JobMiddleware<TJob, TJobStatus>
    where TJob : IJob<TJobStatus>
    where TJobStatus : JobStatus, new()
{
    private readonly ILogger<LoggingMiddleware<TJob, TJobStatus>> _logger;

    public LoggingMiddleware(IJobHandler<TJob, TJobStatus> nextHandler, ILogger<LoggingMiddleware<TJob, TJobStatus>> logger)
        : base(nextHandler)
    {
        _logger = logger;
    }

    public override async Task HandleAsync(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting job {JobType} ({JobScheduleId})", typeof(TJob).Name, jobSchedule.JobScheduleId);

        await _nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);

        _logger.LogInformation("Completed job {JobType} in {Elapsed}ms", typeof(TJob).Name, (jobStatus.EndedAt - jobStatus.StartedAt)?.TotalMilliseconds);
    }
}
```

Register it as an **open generic type**:

```csharp
c.UseJobMiddleware(typeof(LoggingMiddleware<,>));
```

**Execution order:** middlewares run in the order they're registered — the first one registered is the outermost, so it runs first on the way in and last on the way out (a normal "onion" pipeline):

```csharp
c.UseJobMiddleware(typeof(LoggingMiddleware<,>))   // runs 1st, then last
 .UseJobMiddleware(typeof(JobRetryMiddleware<,>));  // runs 2nd, then first
```

`ClearJobMiddlewares()` resets the pipeline built so far if you need to override it conditionally.

## Why `JobStatus<TResponse>` instead of `IJob<TResponse>`?

`LoomKit.Requests` has `IRequest`/`IRequest<TResponse>`, with the response returned from `HandleAsync`. Jobs deliberately don't mirror that shape. A request is a synchronous call — the caller is still there, awaiting, when the response comes back. A job is not: the caller that scheduled it has long since moved on by the time a consumer picks it up, so "return the response from `HandleAsync`" has nowhere useful to go — the only place a response can live is the job's `JobStatus`, the same place its progress and timing already live. `JobStatus<TResponse>` makes that the actual contract: handlers write the response onto `jobStatus.JobResponse` as soon as they know it (even progressively, for a long-running job), and anything holding the `JobSchedule` — an event handler, a query — can read it at any point, not only after the job finishes.

## Extensibility: custom queues, consumers, and schedulers

Implement `IJobQueue` and pass the type to `UseQueue<T>`. The constructor receives the queue's `JobQueueOptions` and any other services registered in DI, injected automatically via `ActivatorUtilities`:

```csharp
public sealed class RedisJobQueue : IJobQueue
{
    public RedisJobQueue(IJobScheduler scheduler, JobQueueOptions options, IConnectionMultiplexer redis) { /* ... */ }
    // implement the rest of IJobQueue
}

services.AddDefaultJobScheduler(builder => builder.UseQueue<RedisJobQueue>("email", _ => { }));
```

`IJobConsumer` works the same way via `UseConsumer<T>`.

If you need different behavior at the scheduler level itself, derive from `JobScheduler<TOptions>` and register it with the generic overload — unlike `AddRequestSender`/`AddNotificationDispatcher`, there's no `WithLifetime`: an `IJobScheduler` is always registered as a singleton `IHostedService`, since that's the only lifetime the host manages hosted services with.

```csharp
services.AddJobScheduler<MyJobScheduler, MyJobSchedulerOptionsBuilder, MyJobSchedulerOptions>(options => { });
```

## Cron scheduling and seeding

`ScheduleCronAsync` parses the cron expression (via [`Cronos`](https://github.com/HangfireIO/Cronos), seconds-precision) and enqueues the next occurrence. Pair it with `CronJobRescheduleMiddleware<,>` so each successful run schedules the next one:

```csharp
c.UseJobMiddleware(typeof(CronJobRescheduleMiddleware<,>));
```

To enqueue jobs once at startup (e.g. to (re-)establish a recurring cron job when the app boots), implement `IJobSchedulerSeeder` and register it with `UseJobSchedulerSeeder<T>()`:

```csharp
public sealed class StartupJobSeeder : IJobSchedulerSeeder
{
    public Task SeedJobs(IJobScheduler jobScheduler)
        => jobScheduler.ScheduleCronAsync(
            queueName: "reports",
            cronExpression: "0 * * * *",
            cronStartAt: DateTime.UtcNow,
            cronEndAt: null,
            job: new DailyReportJob());
}
```

## Lifecycle events

Subscribe to lifecycle events on `IJobScheduler`:

```csharp
scheduler.JobScheduled += (_, e) => Console.WriteLine($"Queued  {e.JobSchedule.JobScheduleId} -> {e.QueueName}");
scheduler.JobStarted   += (_, e) => Console.WriteLine($"Started {e.JobSchedule.JobScheduleId} on {e.ConsumerName}");
scheduler.JobEnded     += (_, e) => Console.WriteLine($"Ended   {e.JobSchedule.JobScheduleId}");
scheduler.JobException += (_, e) => Console.WriteLine($"Error   {e.JobSchedule.JobScheduleId}: {e.Exception.Message}");
```

`JobEnded` fires once a job's pipeline completes without throwing — including a job that failed and was retried internally by `JobRetryMiddleware<,>`, since a retry re-enqueues and returns normally rather than propagating the exception. `JobException` fires only when an exception escapes the whole pipeline (no middleware caught it, or retries were exhausted).

## Cancellation

The token observed by a handler comes from the consumer's own lifecycle (linked to the token passed to the scheduler's `StartAsync`, cancelled when `StopAsync` is called) — not from the caller of `ScheduleNowAsync`/`ScheduleAtAsync`/`ScheduleCronAsync`, which only governs the enqueue operation itself. This is the one place Jobs' cancellation model differs from Requests/Notifications: a scheduled job outlives the call that scheduled it, so there's no caller-side token to carry forward once it's queued. Make sure any custom middleware you write forwards the token it receives to `_nextHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken)` instead of dropping it.

## Observability

Every job dispatch starts a `job.execute {JobTypeName}` `Activity` on an `ActivitySource` named after the assembly (`LoomKit.Jobs`), tagged with `job.type`, `job.status_type`, `job.schedule_id`, `job.group_id`, `job.retries_left`, `job.queue_name`, and `job.consumer_name`. `JobRetryMiddleware<,>` and `CronJobRescheduleMiddleware<,>` add `job.retried`/`job.retries_exhausted`/`job.rescheduled` events on the current activity. If a handler or middleware throws, the exception is recorded on the activity via `Activity.AddException` (standard OpenTelemetry semantic conventions), including its message and stack trace.

⚠️ If your tracing backend doesn't have the same access controls as your application logs, avoid throwing exceptions from handlers whose `Message` carries secrets or personal data — they will flow into your trace exporter as-is.

## License

[MIT](LICENSE)
