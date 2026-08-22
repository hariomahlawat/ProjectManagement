using ProjectManagement.Services.Compendiums;
using Xunit;

namespace ProjectManagement.Tests.Publications;

public sealed class CompendiumPhase42SlotStabilityTests
{
    [Fact]
    public void ChangingSecondary1_PreservesHeroAndSecondary2()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.Explicit, 4, 404),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 3, 303)
        };

        var resolved = ResolveTriptych(slots, Candidates(
            (1, 101), (2, 202), (3, 303), (4, 404)));

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Explicit, 4, 404);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
    }

    [Fact]
    public void ExplicitAssignmentWinsEvenWhenItsSlotAppearsAfterAutomaticSlot()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 4, 404),
            Slot("Secondary1", CompendiumCoverImageMode.Explicit, 4, 404),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 3, 303)
        };

        var resolved = ResolveTriptych(slots, Candidates(
            (1, 101), (2, 202), (3, 303), (4, 404)));

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Explicit, 4, 404);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
    }

    [Fact]
    public void LegacyUnresolvedAutomaticSlots_AreAllocatedDeterministically()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, null, null),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, null, null),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, null, null)
        };

        var resolved = ResolveTriptych(slots, Candidates(
            (1, 101), (2, 202), (3, 303)));

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Automatic, 2, 202);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
    }

    [Fact]
    public void FrontAndBackAssignments_AreIndependent()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101, CompendiumCoverSurface.Front),
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101, CompendiumCoverSurface.Back)
        };
        var candidates = Candidates((1, 101), (2, 202));
        var usable = candidates.Select(item => (item.ProjectId, item.PhotoId)).ToHashSet();

        var resolved = CompendiumCoverSlotAssignmentPolicy.Resolve(
            CompendiumFrontCoverTemplate.InstitutionalHero,
            CompendiumBackCoverTemplate.ImageEcho,
            slots,
            candidates,
            usable);

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101, CompendiumCoverSurface.Front);
        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101, CompendiumCoverSurface.Back);
    }

    [Fact]
    public void PortfolioQuartet_RepairsOnlyConflictingAutomaticAssignment()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, 2, 202),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 2, 202),
            Slot("Secondary3", CompendiumCoverImageMode.Automatic, 4, 404)
        };
        var candidates = Candidates((1, 101), (2, 202), (3, 303), (4, 404));
        var usable = candidates.Select(item => (item.ProjectId, item.PhotoId)).ToHashSet();

        var resolved = CompendiumCoverSlotAssignmentPolicy.Resolve(
            CompendiumFrontCoverTemplate.PortfolioQuartet,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            slots,
            candidates,
            usable);

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Automatic, 2, 202);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
        AssertReference(resolved, "Secondary3", CompendiumCoverImageMode.Automatic, 4, 404);
        Assert.Equal(4, resolved
            .Where(item => item.Surface == CompendiumCoverSurface.Front)
            .Select(item => (item.ProjectId, item.PhotoId))
            .Distinct()
            .Count());
        Assert.All(resolved.Where(item => item.Surface == CompendiumCoverSurface.Front),
            item => Assert.Equal(CompendiumImageFitMode.Fill, item.FitMode));
    }

    [Fact]
    public void HiddenSupportingSelection_IsPreservedAcrossTemplateChanges()
    {
        var hidden = Slot("Secondary3", CompendiumCoverImageMode.Automatic, 9, 909);
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, 2, 202),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 3, 303),
            hidden
        };

        var resolved = ResolveTriptych(slots, Candidates((1, 101), (2, 202), (3, 303)));

        AssertReference(resolved, "Secondary3", CompendiumCoverImageMode.Automatic, 9, 909);
    }

    [Fact]
    public void SettingOneOptionalSlotToNone_PreservesSiblingAssignments()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.None, 2, 202),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 3, 303)
        };

        var resolved = ResolveTriptych(slots, Candidates((1, 101), (2, 202), (3, 303)));

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        var disabled = Assert.Single(resolved.Where(item => item.Surface == CompendiumCoverSurface.Front
                                                            && item.SlotKey == "Secondary1"));
        Assert.Equal(CompendiumCoverImageMode.None, disabled.ImageMode);
        Assert.Null(disabled.ProjectId);
        Assert.Null(disabled.PhotoId);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
    }

    [Fact]
    public void StaleAutomaticReference_RepairsOnlyItsOwnSlot()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, 2, 202),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, 3, 303)
        };
        var candidates = Candidates((1, 101), (4, 404), (3, 303));
        var usable = candidates.Select(item => (item.ProjectId, item.PhotoId)).ToHashSet();

        var resolved = CompendiumCoverSlotAssignmentPolicy.Resolve(
            CompendiumFrontCoverTemplate.Triptych,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            slots,
            candidates,
            usable);

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Automatic, 4, 404);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 3, 303);
    }

    [Fact]
    public void NonQuartet_ReusesAutomaticPhotoOnlyWhenNoUnusedPhotoExists()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Automatic, null, null),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, null, null),
            Slot("Secondary2", CompendiumCoverImageMode.Automatic, null, null)
        };

        var resolved = ResolveTriptych(slots, Candidates((1, 101)));

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary1", CompendiumCoverImageMode.Automatic, 1, 101);
        AssertReference(resolved, "Secondary2", CompendiumCoverImageMode.Automatic, 1, 101);
    }

    [Fact]
    public void AutomaticFallback_NeverConsumesExplicitlyReservedPhoto()
    {
        var slots = new[]
        {
            Slot("Hero", CompendiumCoverImageMode.Explicit, 1, 101),
            Slot("Secondary1", CompendiumCoverImageMode.Automatic, null, null)
        };
        var candidates = Candidates((1, 101));
        var usable = candidates.Select(item => (item.ProjectId, item.PhotoId)).ToHashSet();

        var resolved = CompendiumCoverSlotAssignmentPolicy.Resolve(
            CompendiumFrontCoverTemplate.EditorialSplit,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            slots,
            candidates,
            usable);

        AssertReference(resolved, "Hero", CompendiumCoverImageMode.Explicit, 1, 101);
        var automatic = Assert.Single(resolved.Where(item => item.Surface == CompendiumCoverSurface.Front
                                                             && item.SlotKey == "Secondary1"));
        Assert.Equal(CompendiumCoverImageMode.Automatic, automatic.ImageMode);
        Assert.Null(automatic.ProjectId);
        Assert.Null(automatic.PhotoId);
    }

    private static IReadOnlyList<CompendiumCoverImageSlot> ResolveTriptych(
        IReadOnlyList<CompendiumCoverImageSlot> slots,
        IReadOnlyList<CompendiumCoverAutomaticImagePolicy.Candidate> candidates)
    {
        var usable = candidates.Select(item => (item.ProjectId, item.PhotoId)).ToHashSet();
        foreach (var slot in slots.Where(item => item.ProjectId is > 0 && item.PhotoId is > 0))
        {
            usable.Add((slot.ProjectId!.Value, slot.PhotoId!.Value));
        }

        return CompendiumCoverSlotAssignmentPolicy.Resolve(
            CompendiumFrontCoverTemplate.Triptych,
            CompendiumBackCoverTemplate.MinimalInstitutional,
            slots,
            candidates,
            usable);
    }

    private static IReadOnlyList<CompendiumCoverAutomaticImagePolicy.Candidate> Candidates(
        params (int ProjectId, int PhotoId)[] references)
        => references.Select((reference, index) => new CompendiumCoverAutomaticImagePolicy.Candidate(
            reference.ProjectId,
            reference.PhotoId,
            .5d,
            .5d,
            10_000 - index)).ToArray();

    private static CompendiumCoverImageSlot Slot(
        string slotKey,
        CompendiumCoverImageMode mode,
        int? projectId,
        int? photoId,
        CompendiumCoverSurface surface = CompendiumCoverSurface.Front)
        => new(surface, slotKey, mode, projectId, photoId, .5d, .5d, CompendiumImageFitMode.Fill);

    private static void AssertReference(
        IEnumerable<CompendiumCoverImageSlot> slots,
        string slotKey,
        CompendiumCoverImageMode mode,
        int projectId,
        int photoId,
        CompendiumCoverSurface surface = CompendiumCoverSurface.Front)
    {
        var slot = Assert.Single(slots.Where(item => item.Surface == surface
                                                     && string.Equals(item.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(mode, slot.ImageMode);
        Assert.Equal(projectId, slot.ProjectId);
        Assert.Equal(photoId, slot.PhotoId);
    }
}
