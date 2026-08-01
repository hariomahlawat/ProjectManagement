using ProjectManagement.Services.Navigation;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class IprNavigationUrlTests
{
    [Fact]
    public void IprRecordView_UsesNeutralSelectionRoute()
    {
        var builder = new UrlBuilder();

        var url = builder.IprRecordView(42);

        Assert.Equal("/ProjectOfficeReports/Ipr?tab=records&selectedRecordId=42", url);
        Assert.DoesNotContain("mode=edit", url);
    }

    [Fact]
    public void IprRecordManage_KeepsSelectionAndEditStateSeparate()
    {
        var builder = new UrlBuilder();

        var url = builder.IprRecordManage(42);

        Assert.Equal("/ProjectOfficeReports/Ipr?tab=records&selectedRecordId=42&mode=edit&id=42", url);
    }
}
