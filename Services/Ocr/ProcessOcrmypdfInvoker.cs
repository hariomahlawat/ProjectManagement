using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectManagement.Services.Ocr;

// SECTION: Default process-based ocrmypdf invoker
public sealed class ProcessOcrmypdfInvoker : IOcrmypdfInvoker
{
    public async Task<OcrmypdfProcessResult> RunAsync(string executable, string args, string workingDirectory, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ocrmypdf process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new OcrmypdfProcessResult(process.ExitCode, stdout, stderr);
    }
}
