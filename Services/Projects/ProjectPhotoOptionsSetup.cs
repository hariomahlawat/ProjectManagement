using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Services.Projects;

public sealed class ProjectPhotoOptionsSetup : IConfigureOptions<ProjectPhotoOptions>
{
    private readonly IWebHostEnvironment _environment;

    public ProjectPhotoOptionsSetup(IWebHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public void Configure(ProjectPhotoOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(options.StorageRoot))
        {
            return;
        }

        var basePath = !string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? _environment.WebRootPath!
            : _environment.ContentRootPath;

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        options.StorageRoot = Path.Combine(basePath, "uploads");
    }
}
