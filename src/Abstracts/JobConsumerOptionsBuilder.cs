using System.Collections.Immutable;

namespace LoomKit.Jobs.Abstracts;

public class JobConsumerOptionsBuilder
{
    private readonly LinkedList<Type> _jobMiddlewares;

    public required string JobConsumerName { get; init; }
    public required Type JobConsumerType { get; init; }
    public required string JobQueueName { get; init; }
    public bool UseScopedServiceProvider { get; set; }

    public JobConsumerOptionsBuilder()
    {
        // inits
        _jobMiddlewares = [];
    }

    public JobConsumerOptionsBuilder ClearJobMiddlewares()
    {
        // clear
        _jobMiddlewares.Clear();

        return this;
    }

    public JobConsumerOptionsBuilder UseJobMiddleware(Type jobMiddlewareType)
    {
        // check type is null
        if (jobMiddlewareType is null)
            throw new ArgumentNullException(nameof(jobMiddlewareType));

        // check if jobMiddlewareType is open generic type
        if (!jobMiddlewareType.IsGenericTypeDefinition)
            throw new ArgumentException("Middleware type must be an open generic type", nameof(jobMiddlewareType));

        // check if jobMiddlewareType derives from JobMiddleware<,>
        if (!DerivesFromOpenGeneric(jobMiddlewareType, typeof(JobMiddleware<,>)))
            throw new ArgumentException("Middleware type must derive from JobMiddleware<,>", nameof(jobMiddlewareType));

        // add middleware type
        _jobMiddlewares.AddLast(jobMiddlewareType);

        return this;
    }

    public JobConsumerOptions Build()
    {
        return new JobConsumerOptions()
        {
            JobConsumerName = JobConsumerName,
            JobConsumerType = JobConsumerType,
            JobQueueName = JobQueueName,
            UseScopedServiceProvider = UseScopedServiceProvider,
            JobMiddlewares = _jobMiddlewares.ToImmutableList(),
        };
    }

    // Walks the base-type chain (not GetInterfaces(), since JobMiddleware<,> is an abstract
    // class, not an interface) looking for a base type closing the given open generic definition.
    private static bool DerivesFromOpenGeneric(Type type, Type openGenericBaseType)
    {
        for (var currentBaseType = type.BaseType; currentBaseType is not null; currentBaseType = currentBaseType.BaseType)
        {
            if (currentBaseType.IsGenericType && currentBaseType.GetGenericTypeDefinition() == openGenericBaseType)
                return true;
        }

        return false;
    }
}
