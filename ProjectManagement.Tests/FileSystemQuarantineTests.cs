using ProjectManagement.Services.Storage;

namespace ProjectManagement.Tests;

public sealed class FileSystemQuarantineTests
{
    [Fact]
    public void StageFileAndRestore_RoundTripsWithinConfiguredRoot()
    {
        using var temp = new TempDirectory();
        var original = Path.Combine(temp.Path, "documents", "file.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllText(original, "payload");

        var handle = FileSystemQuarantine.StageFile(original, temp.Path, "documents", "42");

        Assert.NotNull(handle);
        Assert.False(File.Exists(original));
        Assert.True(File.Exists(handle!.QuarantinePath));

        FileSystemQuarantine.Restore(handle);

        Assert.True(File.Exists(original));
        Assert.False(File.Exists(handle.QuarantinePath));
        Assert.Equal("payload", File.ReadAllText(original));
    }

    [Fact]
    public void StageDirectoryAndFinalize_RemovesQuarantinedAssets()
    {
        using var temp = new TempDirectory();
        var original = Path.Combine(temp.Path, "projects", "7");
        Directory.CreateDirectory(original);
        File.WriteAllText(Path.Combine(original, "asset.txt"), "payload");

        var handle = FileSystemQuarantine.StageDirectory(original, temp.Path, "projects", "7");

        Assert.NotNull(handle);
        Assert.False(Directory.Exists(original));
        Assert.True(Directory.Exists(handle!.QuarantinePath));

        FileSystemQuarantine.FinalizeDeletion(handle);

        Assert.False(Directory.Exists(handle.QuarantinePath));
    }

    [Fact]
    public void StageFile_RejectsPathOutsideConfiguredRoot()
    {
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var file = Path.Combine(outside.Path, "outside.txt");
        File.WriteAllText(file, "payload");

        Assert.Throws<InvalidOperationException>(() =>
            FileSystemQuarantine.StageFile(file, root.Path, "documents", "1"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "prism-quarantine-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
