using ProjectManagement.Services.Admin;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class AdminClientDescriptorServiceTests
{
    private readonly AdminClientDescriptorService _service = new();

    [Fact]
    public void Describe_ChromeOnWindows_ReturnsConciseDescriptor()
    {
        var result = _service.Describe("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/149.0.0.0 Safari/537.36");

        Assert.Equal("Chrome 149", result.Browser);
        Assert.Equal("Windows", result.OperatingSystem);
        Assert.Equal("Desktop", result.DeviceClass);
        Assert.Equal("Chrome 149 · Windows · Desktop", result.Summary);
    }

    [Fact]
    public void Describe_BlankValue_ReturnsSafeFallback()
    {
        var result = _service.Describe(" ");

        Assert.Equal("Client not reported", result.Summary);
        Assert.Null(result.RawUserAgent);
    }
}
