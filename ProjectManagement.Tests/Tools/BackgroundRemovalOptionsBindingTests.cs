using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.PhotoTools;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// The settings as PRISM really reads them: through the configuration binder, from the shipped
/// appsettings.json, not from options built by hand.
/// </summary>
public sealed class BackgroundRemovalOptionsBindingTests
{
    [Fact]
    public void ShippedSettings_BindToExactlyThreeChannelsEach()
    {
        var model = Bind(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ProjectRoot(), "appsettings.json"), optional: false)
            .Build()).Model;

        Assert.Equal(new[] { 0.485f, 0.456f, 0.406f }, model.ChannelMean());
        Assert.Equal(new[] { 0.229f, 0.224f, 0.225f }, model.ChannelStd());
    }

    [Fact]
    public void ConfiguredChannels_ReplaceTheDefaults()
    {
        var model = Bind(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PhotoTools:BackgroundRemoval:Model:Mean:0"] = "0.5",
            ["PhotoTools:BackgroundRemoval:Model:Mean:1"] = "0.5",
            ["PhotoTools:BackgroundRemoval:Model:Mean:2"] = "0.5",
            ["PhotoTools:BackgroundRemoval:Model:Std:0"] = "1",
            ["PhotoTools:BackgroundRemoval:Model:Std:1"] = "1",
            ["PhotoTools:BackgroundRemoval:Model:Std:2"] = "1"
        }).Build()).Model;

        Assert.Equal(new[] { 0.5f, 0.5f, 0.5f }, model.ChannelMean());
        Assert.Equal(new[] { 1f, 1f, 1f }, model.ChannelStd());
    }

    [Fact]
    public void UnsetChannels_UseTheImageNetDefaults()
    {
        var model = Bind(new ConfigurationBuilder().Build()).Model;
        Assert.Equal(new[] { 0.485f, 0.456f, 0.406f }, model.ChannelMean());
        Assert.Equal(new[] { 0.229f, 0.224f, 0.225f }, model.ChannelStd());
    }

    [Fact]
    public void ChannelsOfTheWrongCount_AreRefusedWhenTheModelLoads()
    {
        var model = new BackgroundModelOptions { Mean = new[] { 0.5f, 0.5f }, Std = new[] { 1f, 1f, 1f } };
        Assert.Throws<InvalidDataException>(() => BackgroundModelContract.CheckChannels(model));
        var zero = new BackgroundModelOptions { Std = new[] { 1f, 0f, 1f } };
        Assert.Throws<InvalidDataException>(() => BackgroundModelContract.CheckChannels(zero));
        BackgroundModelContract.CheckChannels(new BackgroundModelOptions());
    }

    private static BackgroundRemovalOptions Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddPhotoTools(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<BackgroundRemovalOptions>>().Value;
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root could not be located.");
    }
}
