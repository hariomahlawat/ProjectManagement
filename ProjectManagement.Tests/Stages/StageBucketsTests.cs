using ProjectManagement.Models.Stages;
using Xunit;

namespace ProjectManagement.Tests.Stages;

public class StageBucketsTests
{
    // SECTION: Known code coverage
    [Theory]
    [InlineData(StageCodes.FS, StageBucket.Approval)]
    [InlineData(StageCodes.SOW, StageBucket.Approval)]
    [InlineData(StageCodes.IPA, StageBucket.Approval)]
    [InlineData(StageCodes.AON, StageBucket.Aon)]
    [InlineData(StageCodes.BID, StageBucket.Procurement)]
    [InlineData(StageCodes.TEC, StageBucket.Procurement)]
    [InlineData(StageCodes.BM, StageBucket.Procurement)]
    [InlineData(StageCodes.COB, StageBucket.Procurement)]
    [InlineData(StageCodes.PNC, StageBucket.Procurement)]
    [InlineData(StageCodes.EAS, StageBucket.Procurement)]
    [InlineData(StageCodes.SO, StageBucket.Procurement)]
    [InlineData(StageCodes.DEVP, StageBucket.Development)]
    [InlineData(StageCodes.ATP, StageBucket.Development)]
    [InlineData(StageCodes.PAYMENT, StageBucket.Development)]
    [InlineData(StageCodes.TOT, StageBucket.Development)]
    public void Of_KnownCodes_MapCorrectly(string code, StageBucket expected)
    {
        Assert.Equal(expected, StageBuckets.Of(code));
    }

    // SECTION: Unknown code coverage
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("XYZ")]
    public void Of_Unknown_ReturnsUnknown(string? code)
    {
        Assert.Equal(StageBucket.Unknown, StageBuckets.Of(code));
    }
}
