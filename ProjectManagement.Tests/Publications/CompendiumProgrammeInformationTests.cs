using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumProgrammeInformationTests
{
    [Fact]
    public void Resolve_UsesStablePublicationOrderAndColouredIconKeys()
    {
        var modules = CompendiumProgrammeInformation.Resolve(
            "Infantry Directorate",
            "₹28 lakh",
            new[] { new CompendiumIprCredentialDto("Patent", "Granted", 2026) },
            new CompendiumTechnologyTransferDto("Completed", 2026));

        Assert.Collection(
            modules,
            module => AssertModule(module, CompendiumProgrammeModuleKind.ArmsServices, "Arms / Services", "arms-services", "maroon"),
            module => AssertModule(module, CompendiumProgrammeModuleKind.ProliferationCost, "Proliferation cost", "proliferation-cost", "green"),
            module => AssertModule(module, CompendiumProgrammeModuleKind.Ipr, "IPR", "ipr-granted", "gold"),
            module => AssertModule(module, CompendiumProgrammeModuleKind.TechnologyTransfer, "Technology transfer", "technology-transfer", "blue"));

        Assert.Equal("Infantry Directorate", modules[0].Value);
    }

    [Fact]
    public void Resolve_DistinguishesFiledGrantedAndMixedIpr()
    {
        var filed = ResolveIpr(new CompendiumIprCredentialDto("Patent", "Filed", 2025));
        var granted = ResolveIpr(new CompendiumIprCredentialDto("Patent", "Granted", 2026));
        var mixed = ResolveIpr(
            new CompendiumIprCredentialDto("Patent", "Filed", 2025),
            new CompendiumIprCredentialDto("Patent", "Granted", 2026));

        Assert.Equal(CompendiumIprVisualState.Filed, filed.IprState);
        Assert.Equal("ipr-filed", filed.IconKey);
        Assert.Contains("Filed", filed.Value, StringComparison.Ordinal);

        Assert.Equal(CompendiumIprVisualState.Granted, granted.IprState);
        Assert.Equal("ipr-granted", granted.IconKey);
        Assert.Contains("Granted", granted.Value, StringComparison.Ordinal);

        Assert.Equal(CompendiumIprVisualState.Mixed, mixed.IprState);
        Assert.Equal("ipr-mixed", mixed.IconKey);
        Assert.Contains("1 granted", mixed.Value, StringComparison.Ordinal);
        Assert.Contains("1 filed", mixed.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_PreservesPatentAndCopyrightAsSeparateLines()
    {
        var module = ResolveIpr(
            new CompendiumIprCredentialDto("Patent", "Granted", 2026),
            new CompendiumIprCredentialDto("Copyright", "Filed", 2025));

        Assert.Contains("Patent · Granted · 2026", module.Value, StringComparison.Ordinal);
        Assert.Contains("Copyright · Filed · 2025", module.Value, StringComparison.Ordinal);
        Assert.Contains('\n', module.Value);
    }

    [Fact]
    public void Resolve_OmitsEmptyOptionalModules()
    {
        var modules = CompendiumProgrammeInformation.Resolve(
            " ",
            "Not recorded",
            Array.Empty<CompendiumIprCredentialDto>(),
            null);

        Assert.Empty(modules);
    }

    [Fact]
    public void Resolve_ShowsTechnologyTransferYearOnlyWhenCompleted()
    {
        var inProgress = CompendiumProgrammeInformation.Resolve(
            null,
            null,
            null,
            new CompendiumTechnologyTransferDto("In Progress", 2026)).Single();
        var completed = CompendiumProgrammeInformation.Resolve(
            null,
            null,
            null,
            new CompendiumTechnologyTransferDto("Completed", 2026)).Single();

        Assert.Equal("In Progress", inProgress.Value);
        Assert.Equal("Completed · 2026", completed.Value);
    }

    private static CompendiumProgrammeModuleDto ResolveIpr(params CompendiumIprCredentialDto[] credentials)
        => CompendiumProgrammeInformation.Resolve(null, null, credentials, null).Single();

    private static void AssertModule(
        CompendiumProgrammeModuleDto module,
        CompendiumProgrammeModuleKind kind,
        string label,
        string iconKey,
        string tone)
    {
        Assert.Equal(kind, module.Kind);
        Assert.Equal(label, module.Label);
        Assert.Equal(iconKey, module.IconKey);
        Assert.Equal(tone, module.Tone);
    }
}
