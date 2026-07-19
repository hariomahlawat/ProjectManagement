using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services.Ffc;
using ProjectManagement.Services.Ffc.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class FfcPowerPointExportServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsPowerPointWithSafeContextualFileName()
    {
        var data = BuildData("Myanmar");
        var service = new FfcPowerPointExportService(
            new StubDataService(data),
            new StubComposer(new byte[] { 80, 75, 3, 4 }, 12));

        var result = await service.GenerateAsync(BuildRequest(
            scope: FfcExportScope.CurrentFilteredPortfolio,
            year: 2025));

        Assert.Equal(FfcPowerPointExportService.PowerPointContentType, result.ContentType);
        Assert.Equal("FFC_Portfolio_2025_Myanmar_2026-07-19.pptx", result.FileName);
        Assert.Equal(12, result.SlideCount);
        Assert.Equal(new byte[] { 80, 75, 3, 4 }, result.Content);
    }

    [Fact]
    public async Task GenerateAsync_RejectsSelectedScopeWithoutCountries()
    {
        var service = new FfcPowerPointExportService(
            new StubDataService(BuildData("Myanmar")),
            new StubComposer(Array.Empty<byte>(), 0));

        var request = BuildRequest(scope: FfcExportScope.SelectedCountries) with
        {
            SelectedCountryIds = Array.Empty<long>()
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.GenerateAsync(request));

        Assert.Contains("Select at least one country", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_RejectsEmptyResultWithoutCallingComposer()
    {
        var empty = BuildData(null);
        var composer = new StubComposer(Array.Empty<byte>(), 0);
        var service = new FfcPowerPointExportService(new StubDataService(empty), composer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GenerateAsync(BuildRequest()));

        Assert.Contains("No FFC records", exception.Message, StringComparison.Ordinal);
        Assert.False(composer.WasCalled);
    }

    private static FfcPowerPointExportRequest BuildRequest(
        FfcExportScope scope = FfcExportScope.CompletePortfolio,
        short? year = null)
        => new(
            scope,
            FfcPresentationType.FullPortfolio,
            year,
            null,
            null,
            scope == FfcExportScope.SelectedCountries ? new long[] { 1 } : Array.Empty<long>(),
            IncludeProjects: true,
            IncludeProgress: true,
            IncludeMilestoneRemarks: false,
            IncludeAttachmentRegister: false,
            Title: "FFC Global Portfolio",
            Subtitle: null,
            HandlingMarking: null,
            RequestedAt: new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero));

    private static FfcPresentationData BuildData(string? countryName)
    {
        var countries = countryName is null
            ? Array.Empty<FfcPresentationCountry>()
            : new[]
            {
                new FfcPresentationCountry(
                    1,
                    countryName,
                    "MMR",
                    1,
                    1,
                    0,
                    0,
                    1,
                    new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero),
                    Array.Empty<FfcPresentationRecord>())
            };

        return new FfcPresentationData(
            "FFC Global Portfolio",
            "Position as at 19 Jul 2026",
            null,
            new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero),
            FfcPresentationType.FullPortfolio,
            true,
            true,
            false,
            false,
            new FfcFootprintSummary(
                countries.Length,
                countries.Length,
                countries.Length,
                0,
                0,
                countries.Length),
            countries);
    }

    private sealed class StubDataService : IFfcPresentationDataService
    {
        private readonly FfcPresentationData _data;

        public StubDataService(FfcPresentationData data) => _data = data;

        public Task<FfcPresentationData> GetAsync(
            FfcPowerPointExportRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_data);
    }

    private sealed class StubComposer : IFfcSlideComposer
    {
        private readonly byte[] _content;
        private readonly int _slideCount;

        public StubComposer(byte[] content, int slideCount)
        {
            _content = content;
            _slideCount = slideCount;
        }

        public bool WasCalled { get; private set; }

        public (byte[] Content, int SlideCount) Compose(FfcPresentationData data)
        {
            WasCalled = true;
            return (_content, _slideCount);
        }
    }
}
