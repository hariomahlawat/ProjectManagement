using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProjectManagement.Infrastructure;
using Xunit;

namespace ProjectManagement.Tests;

/// <summary>
/// Disabling, resetting or demoting a user rotates their security stamp. Open sessions only
/// notice at the next stamp check, which Identity runs every 30 minutes by default; PRISM
/// checks every minute so the change reaches open sessions promptly.
/// </summary>
public sealed class SessionRevalidationTests
{
    [Fact]
    public void TheApplicationChecksSecurityStampsEveryMinuteByDefault()
    {
        using var factory = CreateFactory(setting: null);

        Assert.Equal(TimeSpan.FromMinutes(1), ResolveRegisteredInterval(factory));
    }

    [Fact]
    public void TheIntervalCanBeConfigured()
    {
        using var factory = CreateFactory(setting: "5");

        Assert.Equal(TimeSpan.FromMinutes(5), ResolveRegisteredInterval(factory));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("0", 1)]
    [InlineData("-3", 1)]
    [InlineData("1", 1)]
    [InlineData("10", 10)]
    [InlineData("30", 30)]
    [InlineData("240", 30)]
    public void OutOfRangeValuesAreClampedSoATypoCannotStopStartup(string? configured, int expectedMinutes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SessionRevalidation.ConfigurationKey] = configured
            })
            .Build();

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), SessionRevalidation.ResolveInterval(configuration));
    }

    [Theory]
    [InlineData(1, "within a minute")]
    [InlineData(5, "within 5 minutes")]
    public void TheDelayIsDescribedForAdministrators(int minutes, string expected)
    {
        Assert.Equal(expected, SessionRevalidation.DescribeDelay(TimeSpan.FromMinutes(minutes)));
    }

    private static WebApplicationFactory<Program> CreateFactory(string? setting) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UsePrismTestInfrastructure($"session-revalidation-{setting ?? "default"}");
            if (setting is not null)
            {
                builder.UseSetting(SessionRevalidation.ConfigurationKey, setting);
            }
        });

    private static TimeSpan ResolveRegisteredInterval(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<IOptions<SecurityStampValidatorOptions>>()
            .Value
            .ValidationInterval;
    }
}
