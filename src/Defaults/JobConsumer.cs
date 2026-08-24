using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Internal;
using LoomKit.Jobs.Models;
using LoomKit.Jobs.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoomKit.Jobs.Defaults;

public class JobConsumer : IJobConsumer
{
    // Closed MethodInfo for InnerJobHandleAsync<TJob, TJobStatus>, and the pipeline "plan" (which
    // handler/middleware types to close and how to build them), only depend on the job's runtime
    // type (which determines TJobStatus too, see JobStatusTypeResolver) and on the consumer
    // options instance, never on a specific job value - so both are computed once and reused
    // instead of re-running reflection (MakeGenericMethod/MakeGenericType) on every dispatch.
    private static readonly MethodInfo _innerJobHandleAsyncDefinition =
        typeof(JobConsumer).GetMethod(nameof(InnerJobHandleAsync), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Could not locate method {nameof(InnerJobHandleAsync)}.");

    private static readonly ConcurrentDictionary<Type, MethodInfo> _innerJobHandleAsyncMethodCache = new();
    private static readonly ConcurrentDictionary<(JobConsumerOptions Options, Type JobType), JobDispatchPlan> _dispatchPlanCache = new();

    public string? JobConsumerName => _jobConsumerOptions.JobConsumerName;
    public string? JobQueueName => _jobConsumerOptions.JobQueueName;
    public JobConsumerOptions? JobConsumerOptions => _jobConsumerOptions;
    public bool? IsStarted => _consumerTask is not null && !_consumerTask.IsCompleted;

    public JobSchedule? CurrentJobSchedule => _currentJobSchedule;

    private readonly JobConsumerOptions _jobConsumerOptions;
    private readonly IJobQueue _jobQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobConsumer> _logger;
    private CancellationTokenSource? _linkedCancellationTokenSource;
    private Task? _consumerTask;
    private JobSchedule? _currentJobSchedule;

    private readonly object _startStopLock;

    public JobConsumer(JobConsumerOptions jobConsumerOptions, IJobQueue jobQueue, IServiceProvider serviceProvider, ILogger<JobConsumer> logger)
    {
        // deps
        _jobConsumerOptions = jobConsumerOptions;
        _jobQueue = jobQueue;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // inits
        _startStopLock = new object();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // ensure only one start/stop at a time
        lock (_startStopLock)
        {
            // check if consumer task already running
            if (_consumerTask != null && !_consumerTask.IsCompleted)
            {
                _logger.LogWarning("[{jobConsumerName}] StartAsync called but consumer already running", _jobConsumerOptions.JobConsumerName);
                return Task.CompletedTask;
            }

            //
            _linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            //
            _consumerTask = Task.Run(async () =>
            {
                while (!_linkedCancellationTokenSource.Token.IsCancellationRequested)
                {
                    //
                    var jobSchedule = await _jobQueue.AwaitForJobScheduleAsync(_jobConsumerOptions.JobConsumerName, _linkedCancellationTokenSource.Token);

                    //
                    if (jobSchedule is null)
                        continue;

                    // trigger queue job started event
                    await _jobQueue.NotifyJobStarted(_jobConsumerOptions.JobConsumerName, jobSchedule);

                    //
                    _logger.LogTrace("[{jobConsumerName}] HANDLING job schedule {jobScheduleId}", _jobConsumerOptions.JobConsumerName, jobSchedule.JobScheduleId);

                    try
                    {
                        //
                        await HandleJobScheduleAsync(jobSchedule, _linkedCancellationTokenSource.Token);

                        // trigger queue job finished event
                        await _jobQueue.NotifyJobEnded(_jobConsumerOptions.JobConsumerName, jobSchedule);

                        //
                        _logger.LogTrace("[{jobConsumerName}] HANDLED job schedule {jobScheduleId}", _jobConsumerOptions.JobConsumerName, jobSchedule.JobScheduleId);
                    }
                    catch (OperationCanceledException)
                        when (_linkedCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // shutdown requested
                        break;
                    }
                    catch (Exception exception)
                    {
                        // trigger queue job finished event
                        await _jobQueue.NotifyJobException(_jobConsumerOptions.JobConsumerName, jobSchedule, exception);

                        //
                        _logger.LogTrace("[{jobConsumerName}] EXCEPTION handling job schedule {jobScheduleId}: {exceptionMessage}", _jobConsumerOptions.JobConsumerName, jobSchedule.JobScheduleId, exception.Message);
                    }
                }
            });

            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // ensure only one start/stop at a time
        lock (_startStopLock)
        {
            if (_linkedCancellationTokenSource is null)
                return;

            // call cancel on linked token source
            _linkedCancellationTokenSource.Cancel();
        }

        try
        {
            // wait for consumer task to finish with a timeout
            var task = _consumerTask ?? Task.CompletedTask;
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));

            if (completed != task)
            {
                _logger.LogWarning("[{jobConsumerName}] Consumer did not stop within timeout", _jobConsumerOptions.JobConsumerName);
            }
            else
            {
                // propagate any exceptions from the consumer task
                await task;
            }
        }
        catch (OperationCanceledException operationCanceledException)
        {
            //
            _logger.LogError(operationCanceledException, "[{jobConsumerName}] Operation canceled during StopAsync", _jobConsumerOptions.JobConsumerName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "[{jobConsumerName}] Error during StopAsync", _jobConsumerOptions.JobConsumerName);
        }
        finally
        {
            // cleanup
            _linkedCancellationTokenSource?.Dispose();
            _linkedCancellationTokenSource = null;
            _consumerTask = null;
        }
    }

    private async Task HandleJobScheduleAsync(JobSchedule jobSchedule, CancellationToken cancellationToken = default)
    {
        // check job schedule has job and job status
        var job = jobSchedule.Job ?? throw new InvalidOperationException("Job is null.");
        var jobStatus = jobSchedule.JobStatus ?? throw new InvalidOperationException("JobStatus is null.");

        // save job schedule
        _currentJobSchedule = jobSchedule;

        // update job status
        jobStatus.ConsumerName = _jobConsumerOptions.JobConsumerName;
        jobStatus.StartedAt = DateTime.UtcNow;

        // every job implements exactly one closed IJob<TJobStatus> - resolve and cache the
        // matching closed InnerJobHandleAsync<TJob, TJobStatus> method for this job's runtime type
        var jobType = job.GetType();

        var closedMethodInfo = _innerJobHandleAsyncMethodCache.GetOrAdd(jobType, static type =>
        {
            var jobStatusType = JobStatusTypeResolver.ResolveJobStatusType(type);
            return _innerJobHandleAsyncDefinition.MakeGenericMethod(type, jobStatusType);
        });

        // create scope if requested in consumer options and ensure disposal
        IServiceProvider serviceProviderToUse = _serviceProvider;
        IServiceScope? scope = null;

        if (_jobConsumerOptions.UseScopedServiceProvider)
        {
            scope = _serviceProvider.CreateScope();
            serviceProviderToUse = scope.ServiceProvider;
        }

        try
        {
            // invoke the closed method
            var handleTask = (Task)closedMethodInfo.Invoke(this, [job, jobSchedule, jobStatus, serviceProviderToUse, cancellationToken])!;

            // await the task
            await handleTask;
        }
        finally
        {
            // update job status
            jobStatus.EndedAt = DateTime.UtcNow;

            // clear current job schedule
            _currentJobSchedule = null;

            // dispose scope if created
            scope?.Dispose();
        }
    }

    protected virtual async Task InnerJobHandleAsync<TJob, TJobStatus>(TJob job, JobSchedule jobSchedule, TJobStatus jobStatus, IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        where TJob : IJob<TJobStatus>
        where TJobStatus : JobStatus, new()
    {
        // get job type
        var jobType = typeof(TJob);

        // get (or build once) the plan describing which handler/middleware types to close and how to construct them
        var plan = _dispatchPlanCache.GetOrAdd((_jobConsumerOptions, jobType), static key =>
        {
            var (options, type) = key;
            var jobStatusType = JobStatusTypeResolver.ResolveJobStatusType(type);
            var handlerType = typeof(IJobHandler<,>).MakeGenericType(type, jobStatusType);

            // built in reverse so the first-registered middleware ends up outermost (executes first)
            var middlewareFactories = options.JobMiddlewares
                .Reverse()
                .Select(middlewareType => ActivatorUtilities.CreateFactory(middlewareType.MakeGenericType(type, jobStatusType), [handlerType]))
                .ToArray();

            return new JobDispatchPlan(handlerType, middlewareFactories);
        });

        // get job handler
        var jobHandlerClosedInstance = (IJobHandler<TJob, TJobStatus>)serviceProvider.GetRequiredService(plan.HandlerType);

        // create the middleware pipeline from the cached factories
        var currentJobHandler = jobHandlerClosedInstance;

        using var activity = JobsActivitySource.Source.StartActivity($"job.execute {jobType.Name}", ActivityKind.Internal);
        activity?.SetTag("job.type", jobType.FullName);
        activity?.SetTag("job.status_type", typeof(TJobStatus).FullName);
        activity?.SetTag("job.schedule_id", jobSchedule.JobScheduleId);
        activity?.SetTag("job.group_id", jobSchedule.JobGroupId);
        activity?.SetTag("job.retries_left", jobSchedule.RetriesLeft);
        activity?.SetTag("job.queue_name", _jobConsumerOptions.JobQueueName);
        activity?.SetTag("job.consumer_name", _jobConsumerOptions.JobConsumerName);

        foreach (var middlewareFactory in plan.MiddlewareFactories)
        {
            currentJobHandler = (IJobHandler<TJob, TJobStatus>)middlewareFactory(serviceProvider, [currentJobHandler]);
        }

        try
        {
            await currentJobHandler.HandleAsync(job, jobSchedule, jobStatus, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }

    private sealed record JobDispatchPlan(Type HandlerType, ObjectFactory[] MiddlewareFactories);
}
