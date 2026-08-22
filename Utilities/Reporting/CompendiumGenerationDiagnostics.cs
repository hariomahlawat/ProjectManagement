using System.Text;
using System.Text.Json;

namespace ProjectManagement.Utilities.Reporting;

/// <summary>
/// Best-effort durable diagnostics for an air-gapped IIS deployment. This writer is
/// intentionally independent of the configured logging provider so a PDF failure still
/// leaves a correlation record when only console logging was configured.
/// </summary>
public static class CompendiumGenerationDiagnostics
{
    public const string DirectoryEnvironmentVariable = "PRISM_COMPENDIUM_DIAGNOSTICS_DIR";

    private static readonly object WriteGate = new();
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryWrite(
        Exception exception,
        string operation,
        string traceIdentifier,
        out string? diagnosticPath,
        out Exception? writeFailure)
    {
        ArgumentNullException.ThrowIfNull(exception);
        diagnosticPath = null;
        writeFailure = null;

        try
        {
            var directory = ResolveDirectory();
            Directory.CreateDirectory(directory);
            diagnosticPath = Path.Combine(
                directory,
                $"compendium-generation-{DateTime.UtcNow:yyyyMMdd}.jsonl");

            var generation = exception as CompendiumPdfGenerationException;
            var payload = new
            {
                occurredAtUtc = DateTimeOffset.UtcNow,
                build = CompendiumBuildIdentity.BuildStamp,
                reviewContract = CompendiumBuildIdentity.ReviewContract,
                pdfContract = CompendiumBuildIdentity.PdfContract,
                operation,
                traceIdentifier,
                stage = generation?.Stage.ToString() ?? "Unhandled",
                plannedPhysicalPage = generation?.PlannedPhysicalPage,
                pageKind = generation?.PageKind?.ToString(),
                projectId = generation?.ProjectId,
                projectName = generation?.ProjectName,
                exceptionType = exception.GetType().FullName,
                message = Limit(exception.Message, 4000),
                exceptionChain = ExceptionChain(exception)
            };

            var line = JsonSerializer.Serialize(payload, JsonOptions) + Environment.NewLine;
            lock (WriteGate)
            {
                File.AppendAllText(diagnosticPath, line, Utf8WithoutBom);
            }

            return true;
        }
        catch (Exception failure)
        {
            diagnosticPath = null;
            writeFailure = failure;
            return false;
        }
    }

    public static string ResolveDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "logs", "compendium")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
    }

    private static IReadOnlyList<object> ExceptionChain(Exception exception)
    {
        var chain = new List<object>(4);
        for (Exception? current = exception; current is not null && chain.Count < 4; current = current.InnerException)
        {
            chain.Add(new
            {
                type = current.GetType().FullName,
                message = Limit(current.Message, 4000),
                stackTrace = Limit(current.StackTrace, 12000)
            });
        }

        return chain;
    }

    private static string? Limit(string? value, int maximumLength)
        => string.IsNullOrEmpty(value) || value.Length <= maximumLength
            ? value
            : value[..maximumLength];
}
