using System.Diagnostics;
using System.Reflection;

namespace LoomKit.Jobs.Telemetry;

internal static class JobsActivitySource
{
    private static readonly AssemblyName AssemblyName = typeof(JobsActivitySource).Assembly.GetName();

    internal static readonly ActivitySource Source = new(
        AssemblyName.Name ?? "LoomKit.Jobs",
        AssemblyName.Version?.ToString() ?? "1.0.0");
}
