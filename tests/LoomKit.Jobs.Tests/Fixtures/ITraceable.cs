namespace LoomKit.Jobs.Tests.Fixtures;

// Shared by all job fixtures below so a single generic FirstMiddleware<,>/SecondMiddleware<,>
// pair (see JobMiddlewares.cs) can record execution order for both PingJob and TraceJob -
// something the old void/response-split JobMiddleware<> and JobMiddleware<,> could not do.
public interface ITraceable
{
    List<string> Trace { get; }
}
