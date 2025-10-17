using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ProjectManagement.Tests.Fakes;

public sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ProjectManagement.Tests";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public string EnvironmentName { get; set; } = "Development";

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

    private string _webRootPath = Path.Combine(Path.GetTempPath(), "ProjectManagement", "wwwroot");

    public string WebRootPath
    {
        get => _webRootPath;
        set => _webRootPath = value ?? throw new ArgumentNullException(nameof(value));
    }
}
