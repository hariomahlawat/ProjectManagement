using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ProjectManagement.Infrastructure;

/// <summary>
/// Best-effort diagnostic writer for failures that occur before ASP.NET Core can begin
/// serving requests. It never suppresses or replaces the original startup exception.
/// </summary>
public static class StartupFailureReporter
{
    public static string? TryWrite(
        IConfiguration configuration,
        IHostEnvironment environment,
        string phase,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var configuredDataRoot = configuration["Storage:DataRoot"]?.Trim();
            var root = !string.IsNullOrWhiteSpace(configuredDataRoot)
                       && Path.IsPathFullyQualified(configuredDataRoot)
                ? configuredDataRoot
                : environment.ContentRootPath;

            var directory = Path.Combine(root, "startup-diagnostics");
            Directory.CreateDirectory(directory);

            var now = DateTimeOffset.UtcNow;
            var path = Path.Combine(
                directory,
                $"startup-failure-{now:yyyyMMdd-HHmmssfff}-pid{Environment.ProcessId}.log");

            var report = new StringBuilder()
                .AppendLine("PRISM ERP startup failure")
                .AppendLine($"UTC: {now:O}")
                .AppendLine($"Environment: {environment.EnvironmentName}")
                .AppendLine($"Phase: {phase}")
                .AppendLine($"Machine: {Environment.MachineName}")
                .AppendLine($"ProcessId: {Environment.ProcessId}")
                .AppendLine($"ContentRoot: {environment.ContentRootPath}")
                .AppendLine()
                .AppendLine(exception.ToString())
                .ToString();

            File.WriteAllText(path, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.Error.WriteLine($"PRISM startup diagnostic written to: {path}");
            return path;
        }
        catch (Exception diagnosticException)
        {
            Console.Error.WriteLine(
                $"PRISM could not write a startup diagnostic for phase '{phase}': " +
                diagnosticException.Message);
            return null;
        }
    }
}
