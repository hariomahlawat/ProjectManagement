using ProjectManagement.Features.MediaLibrary.Options;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaLibraryOptionsValidatorTests
{
    [Fact]
    public void Validate_RejectsDuplicateEnabledSourceKeys()
    {
        var options = new MediaLibraryOptions
        {
            Sources =
            {
                new MediaSourceOptions { Key = "archive", Name = "One", RootPath = Path.GetTempPath(), Enabled = true },
                new MediaSourceOptions { Key = "ARCHIVE", Name = "Two", RootPath = Path.GetTempPath(), Enabled = true }
            }
        };

        var result = new MediaLibraryOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }
}
