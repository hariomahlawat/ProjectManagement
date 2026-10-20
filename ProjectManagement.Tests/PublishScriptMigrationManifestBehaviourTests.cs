using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace ProjectManagement.Tests;

// Executes Assert-ApplicationMigrationManifest from ops/publish/create-publish-folder.ps1 against
// temporary fixture manifests and migration directories (hariomahlawat/ProjectManagement#2683).
// Only the script's function definitions are loaded; nothing is published. Each scenario runs
// under every available PowerShell host: pwsh, and Windows PowerShell 5.1 on Windows.
public sealed class PublishScriptMigrationManifestBehaviourTests : IDisposable
{
    private const string First = "20250101000000_First";
    private const string Second = "20250102000000_Second";
    private const string Third = "20250103000000_Third";

    // Loads only the function definitions from the publish script, then calls the validator.
    private const string Harness = """
        param([string] $ScriptPath, [string] $ManifestPath, [string] $MigrationsDirectory)
        $ErrorActionPreference = "Stop"
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $null, [ref] $parseErrors)
        if ($parseErrors) { Write-Output "PARSE:$($parseErrors -join '; ')"; exit 2 }
        $functions = $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)
        foreach ($function in $functions) { . ([scriptblock]::Create($function.Extent.Text)) }
        try {
            $ids = Assert-ApplicationMigrationManifest $ManifestPath $MigrationsDirectory
            Write-Output "OK:$($ids.Count):$($ids[-1])"
            exit 0
        }
        catch {
            Write-Output "ERR:$($_.Exception.Message)"
            exit 1
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "prism-manifest-" + Guid.NewGuid().ToString("N"));
    private readonly string _migrations;
    private readonly string _manifest;

    public PublishScriptMigrationManifestBehaviourTests()
    {
        _migrations = Path.Combine(_root, "Migrations");
        _manifest = Path.Combine(_migrations, "immutable-migration-ids.txt");
        Directory.CreateDirectory(_migrations);
    }

    public static IEnumerable<object[]> Shells()
    {
        foreach (var shell in new[] { "pwsh", "powershell.exe" })
        {
            if (shell == "powershell.exe" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                continue;
            }

            if (IsAvailable(shell))
            {
                yield return new object[] { shell };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void ValidOrderedManifestMatchingSources_Passes(string shell)
    {
        WriteMigrations(First, Second, Third);
        WriteManifest("# immutable", First, Second, "", Third);

        var result = Run(shell);

        Assert.True(result.Passed, result.Output);
        Assert.Equal($"OK:3:{Third}", result.Output);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void SingleMigrationSource_Passes(string shell)
    {
        WriteMigrations(First);
        WriteManifest(First);

        var result = Run(shell);

        Assert.True(result.Passed, result.Output);
        Assert.Equal($"OK:1:{First}", result.Output);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void TwoSourceFilesDeclaringTheSameId_Fails(string shell)
    {
        WriteMigrations(First, Second);
        WriteMigrationFile("Duplicate.cs", Second);
        WriteManifest(First, Second);

        AssertFails(shell, $"Duplicate [Migration] IDs: {Second}");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void DuplicateManifestEntries_Fails(string shell)
    {
        WriteMigrations(First, Second);
        WriteManifest(First, Second, Second);

        AssertFails(shell, $"Duplicate IDs: {Second}");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void MissingFinalMigrationId_Fails(string shell)
    {
        WriteMigrations(First, Second, Third);
        WriteManifest(First, Second);

        AssertFails(shell, $"Missing from manifest: [{Third}]");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void ManifestEntryWithoutMigrationSource_Fails(string shell)
    {
        WriteMigrations(First, Second);
        WriteManifest(First, Second, Third);

        AssertFails(shell, $"Without migration source: [{Third}]");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void OutOfOrderManifestEntries_Fails(string shell)
    {
        WriteMigrations(First, Second, Third);
        WriteManifest(First, Third, Second);

        AssertFails(shell, $"'{Second}' is out of order after '{Third}'");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void ManifestDifferingOnlyByCase_Fails(string shell)
    {
        WriteMigrations(First, Second);
        WriteManifest(First, Second.ToLowerInvariant());

        AssertFails(shell, $"Missing from manifest: [{Second}]");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void EmptyManifest_Fails(string shell)
    {
        WriteMigrations(First);
        WriteManifest("# no migrations yet");

        AssertFails(shell, "contains no migration IDs");
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void EmptyMigrationSourceDirectory_Fails(string shell)
    {
        WriteManifest(First);

        AssertFails(shell, "Application migration sources are missing");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void AssertFails(string shell, string expectedMessage)
    {
        var result = Run(shell);

        Assert.False(result.Passed, result.Output);
        Assert.StartsWith("ERR:", result.Output, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, result.Output, StringComparison.Ordinal);
    }

    private void WriteMigrations(params string[] ids)
    {
        foreach (var id in ids)
        {
            WriteMigrationFile(id + ".cs", id);
        }
    }

    private void WriteMigrationFile(string fileName, string id)
    {
        File.WriteAllText(
            Path.Combine(_migrations, fileName),
            $$"""
            namespace ProjectManagement.Migrations
            {
                [Migration("{{id}}")]
                public partial class M{{Guid.NewGuid():N}} { }
            }
            """);
    }

    private void WriteManifest(params string[] lines) => File.WriteAllLines(_manifest, lines);

    private (bool Passed, string Output) Run(string shell)
    {
        var harnessPath = Path.Combine(_root, "harness.ps1");
        File.WriteAllText(harnessPath, Harness);

        var startInfo = new ProcessStartInfo(shell)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", harnessPath,
                     FindRepoFile("ops", "publish", "create-publish-folder.ps1"), _manifest, _migrations
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{shell} did not finish the manifest harness.");
        }

        var output = stdout.Result.Trim();
        Assert.True(
            process.ExitCode is 0 or 1,
            $"{shell} harness exited with {process.ExitCode}: {output} {stderr.Result}");
        return (process.ExitCode == 0, output);
    }

    private static bool IsAvailable(string shell)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(shell, "-NoProfile -NonInteractive -Command exit 0")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            return process is not null && process.WaitForExit(60_000) && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static string FindRepoFile(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, Path.Combine(relativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(relativePath)}");
    }
}
