namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Single source of truth for Compendium cover image-slot requirements, geometry and template safeguards.
/// Browser bootstrap, readiness and export consume this policy so authoring and final composition cannot drift.
/// </summary>
public static class CompendiumCoverTemplatePolicy
{
    public sealed record Slot(
        CompendiumCoverSurface Surface,
        string SlotKey,
        int TargetWidth,
        int TargetHeight,
        bool Required,
        bool FillOnly);

    public sealed record TemplateContract(
        string Template,
        string Surface,
        IReadOnlyList<string> Slots,
        IReadOnlyList<string> RequiredSlots,
        int MinimumDistinctImages,
        bool FillOnly);

    public static IReadOnlyList<Slot> ResolveSlots(
        CompendiumFrontCoverTemplate front,
        CompendiumBackCoverTemplate back)
    {
        var slots = new List<Slot>();
        AddFront(slots, front);
        AddBack(slots, back);
        return slots;
    }

    public static IReadOnlyList<string> RequiredSlotKeys(
        CompendiumCoverSurface surface,
        CompendiumFrontCoverTemplate front,
        CompendiumBackCoverTemplate back)
        => ResolveSlots(front, back)
            .Where(slot => slot.Surface == surface && slot.Required)
            .Select(slot => slot.SlotKey)
            .ToArray();

    public static int MinimumDistinctImages(CompendiumFrontCoverTemplate front)
        => front == CompendiumFrontCoverTemplate.PortfolioQuartet ? 4 : 0;

    public static bool IsFillOnly(CompendiumCoverSurface surface, CompendiumFrontCoverTemplate front)
        => surface == CompendiumCoverSurface.Front && front == CompendiumFrontCoverTemplate.PortfolioQuartet;

    public static CompendiumImageFitMode NormalizeFitMode(
        CompendiumCoverSurface surface,
        CompendiumFrontCoverTemplate front,
        CompendiumImageFitMode fitMode)
        => IsFillOnly(surface, front) ? CompendiumImageFitMode.Fill : fitMode;

    public static (int Width, int Height) ResolveGeometry(
        CompendiumFrontCoverTemplate front,
        CompendiumBackCoverTemplate back,
        CompendiumCoverSurface surface,
        string slotKey)
    {
        var slot = ResolveSlots(front, back).FirstOrDefault(item =>
            item.Surface == surface && string.Equals(item.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase));
        return slot is null
            ? (CompendiumCoverImagePolicy.RenderWidthPixels, CompendiumCoverImagePolicy.RenderHeightPixels)
            : (slot.TargetWidth, slot.TargetHeight);
    }

    public static object BuildClientContract()
    {
        static TemplateContract Contract(
            string template,
            string surface,
            IReadOnlyList<Slot> slots,
            int minimum,
            bool fillOnly)
            => new(
                template,
                surface,
                slots.Select(slot => slot.SlotKey).ToArray(),
                slots.Where(slot => slot.Required).Select(slot => slot.SlotKey).ToArray(),
                minimum,
                fillOnly);

        return new
        {
            front = Enum.GetValues<CompendiumFrontCoverTemplate>()
                .Select(template => Contract(
                    template.ToString(),
                    "front",
                    ResolveSlots(template, CompendiumBackCoverTemplate.MinimalInstitutional)
                        .Where(slot => slot.Surface == CompendiumCoverSurface.Front)
                        .ToArray(),
                    MinimumDistinctImages(template),
                    IsFillOnly(CompendiumCoverSurface.Front, template)))
                .ToArray(),
            back = Enum.GetValues<CompendiumBackCoverTemplate>()
                .Select(template => Contract(
                    template.ToString(),
                    "back",
                    ResolveSlots(CompendiumFrontCoverTemplate.Minimal, template)
                        .Where(slot => slot.Surface == CompendiumCoverSurface.Back)
                        .ToArray(),
                    0,
                    false))
                .ToArray()
        };
    }

    private static void AddFront(List<Slot> slots, CompendiumFrontCoverTemplate template)
    {
        switch (template)
        {
            case CompendiumFrontCoverTemplate.Minimal:
                return;
            case CompendiumFrontCoverTemplate.FullBleedHero:
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Hero", 1800, 2546, true, false));
                return;
            case CompendiumFrontCoverTemplate.EditorialSplit:
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Hero", 1400, 1700, true, false));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary1", 700, 1700, false, false));
                return;
            case CompendiumFrontCoverTemplate.Triptych:
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Hero", 700, 1500, true, false));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary1", 700, 1500, false, false));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary2", 700, 1500, false, false));
                return;
            case CompendiumFrontCoverTemplate.PortfolioQuartet:
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Hero", 1400, 1600, true, true));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary1", 720, 540, true, true));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary2", 720, 540, true, true));
                slots.Add(new Slot(CompendiumCoverSurface.Front, "Secondary3", 720, 540, true, true));
                return;
            default:
                slots.Add(new Slot(
                    CompendiumCoverSurface.Front,
                    "Hero",
                    CompendiumCoverImagePolicy.RenderWidthPixels,
                    CompendiumCoverImagePolicy.RenderHeightPixels,
                    true,
                    false));
                return;
        }
    }

    private static void AddBack(List<Slot> slots, CompendiumBackCoverTemplate template)
    {
        switch (template)
        {
            case CompendiumBackCoverTemplate.ImageEcho:
                slots.Add(new Slot(CompendiumCoverSurface.Back, "Hero", 1800, 1800, true, false));
                break;
            case CompendiumBackCoverTemplate.PortfolioStrip:
                slots.Add(new Slot(CompendiumCoverSurface.Back, "Hero", 700, 1100, true, false));
                slots.Add(new Slot(CompendiumCoverSurface.Back, "Secondary1", 700, 1100, false, false));
                slots.Add(new Slot(CompendiumCoverSurface.Back, "Secondary2", 700, 1100, false, false));
                break;
        }
    }
}
