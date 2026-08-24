using System.Reflection;
using LoomKit.Jobs.Abstracts;
using LoomKit.Jobs.Contracts;
using LoomKit.Jobs.Defaults;
using Microsoft.Extensions.DependencyInjection;

namespace LoomKit.Jobs;

public static class DependencyInjection
{
    private static readonly Type _jobHandlerOpenGenericType = typeof(IJobHandler<,>);

    public static IServiceCollection AddJobScheduler<TJobScheduler, TJobSchedulerOptionsBuilder, TJobSchedulerOptions>(this IServiceCollection services, Action<TJobSchedulerOptionsBuilder> optionsBuilder)
        where TJobScheduler : JobScheduler<TJobSchedulerOptions>
        where TJobSchedulerOptionsBuilder : JobSchedulerOptionsBuilder<TJobSchedulerOptions>, new()
        where TJobSchedulerOptions : JobSchedulerOptions, new()
    {
        // create options builder
        var jobSchedulerOptionsBuilder = new TJobSchedulerOptionsBuilder();

        // invoke build action
        optionsBuilder.Invoke(jobSchedulerOptionsBuilder);

        // build scheduler options from builder
        var jobSchedulerOptions = jobSchedulerOptionsBuilder.Build();

        // add scheduler to DI - always a singleton (not configurable, unlike RequestSender/
        // NotificationDispatcher's ServiceLifetime): the host only manages hosted services as
        // singletons, and IJobScheduler is an IHostedService
        services.AddSingleton<IJobScheduler>(serviceProvider =>
            (IJobScheduler)ActivatorUtilities.CreateInstance(serviceProvider, typeof(TJobScheduler), jobSchedulerOptions));

        // scheduler as hosted service
        services.AddHostedService<IJobScheduler>(sp => sp.GetRequiredService<IJobScheduler>());

        //
        return services;
    }

    public static IServiceCollection AddDefaultJobScheduler(this IServiceCollection services, Action<DefaultJobSchedulerOptionsBuilder> optionsBuilder)
    {
        // call generic add method
        return AddJobScheduler<JobScheduler, DefaultJobSchedulerOptionsBuilder, DefaultJobSchedulerOptions>(services, optionsBuilder);
    }

    // Scans the given assemblies for concrete, closed IJobHandler<,> implementations and registers
    // each one against the interface it implements. Open-generic handlers (rare) are not
    // discovered - register those manually with services.AddScoped(typeof(IJobHandler<,>), typeof(MyHandler<,>)).
    public static IServiceCollection AddJobHandlersFromAssemblies(this IServiceCollection services, ServiceLifetime lifetime, params Assembly[] assemblies)
    {
        // check args
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));

        // track (interface, implementation) pairs already registered so overlapping assemblies,
        // or calling this method more than once against the same IServiceCollection, don't produce
        // duplicate registrations - seeded from what's already in `services` (including registrations
        // from an earlier call to this method, or a handler registered manually beforehand)
        var registered = new HashSet<(Type HandlerInterface, Type ImplementationType)>(
            services
                .Where(d => d.ImplementationType is not null)
                .Select(d => (d.ServiceType, d.ImplementationType!)));

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                // only concrete, closed classes can be instantiated as-is; open-generic handlers are out of scope here
                if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                foreach (var handlerInterface in GetClosedJobHandlerInterfaces(type))
                {
                    if (registered.Add((handlerInterface, type)))
                    {
                        services.Add(ServiceDescriptor.Describe(handlerInterface, type, lifetime));
                    }
                }
            }
        }

        //
        return services;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // some types in the assembly could not be loaded (e.g. missing dependency) - use the ones that could
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static IEnumerable<Type> GetClosedJobHandlerInterfaces(Type type)
    {
        return type
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == _jobHandlerOpenGenericType);
    }
}
