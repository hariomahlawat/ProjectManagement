namespace ProjectManagement.Services.Compendiums;

/// <summary>
/// Resolves semantic cover-image slots without allowing an edit to one slot to
/// reshuffle unrelated slots. The policy is deliberately deterministic and is
/// shared by cover authoring, readiness evaluation and final PDF composition.
/// </summary>
public static class CompendiumCoverSlotAssignmentPolicy
{
    /// <summary>
    /// Preserves valid automatic assignments as sticky publication state and
    /// allocates only unresolved automatic slots. Explicit assignments are
    /// reserved before automatic work so a manual choice always wins.
    /// </summary>
    public static IReadOnlyList<CompendiumCoverImageSlot> Resolve(
        CompendiumFrontCoverTemplate frontTemplate,
        CompendiumBackCoverTemplate backTemplate,
        IEnumerable<CompendiumCoverImageSlot>? configuredSlots,
        IEnumerable<CompendiumCoverAutomaticImagePolicy.Candidate>? rankedCandidates,
        IReadOnlySet<(int ProjectId, int PhotoId)> usablePhotos)
    {
        ArgumentNullException.ThrowIfNull(usablePhotos);

        var slots = NormaliseSlots(configuredSlots).ToList();
        var activeRequirements = CompendiumCoverTemplatePolicy
            .ResolveSlots(frontTemplate, backTemplate)
            .ToArray();

        foreach (var requirement in activeRequirements)
        {
            if (FindSlotIndex(slots, requirement.Surface, requirement.SlotKey) >= 0)
            {
                continue;
            }

            slots.Add(new CompendiumCoverImageSlot(
                requirement.Surface,
                requirement.SlotKey,
                CompendiumCoverImageMode.Automatic,
                null,
                null,
                .5d,
                .5d,
                CompendiumCoverTemplatePolicy.NormalizeFitMode(
                    requirement.Surface,
                    frontTemplate,
                    CompendiumImageFitMode.Fill)));
        }

        var candidates = (rankedCandidates ?? Array.Empty<CompendiumCoverAutomaticImagePolicy.Candidate>())
            .Where(candidate => candidate.ProjectId > 0
                                && candidate.PhotoId > 0
                                && usablePhotos.Contains((candidate.ProjectId, candidate.PhotoId)))
            .GroupBy(candidate => (candidate.ProjectId, candidate.PhotoId))
            .Select(group => group.First())
            .ToArray();

        foreach (var surface in Enum.GetValues<CompendiumCoverSurface>())
        {
            ResolveSurface(
                surface,
                frontTemplate,
                activeRequirements.Where(requirement => requirement.Surface == surface).ToArray(),
                slots,
                candidates,
                usablePhotos);
        }

        return slots;
    }

    private static void ResolveSurface(
        CompendiumCoverSurface surface,
        CompendiumFrontCoverTemplate frontTemplate,
        IReadOnlyList<CompendiumCoverTemplatePolicy.Slot> requirements,
        List<CompendiumCoverImageSlot> slots,
        IReadOnlyList<CompendiumCoverAutomaticImagePolicy.Candidate> candidates,
        IReadOnlySet<(int ProjectId, int PhotoId)> usablePhotos)
    {
        if (requirements.Count == 0)
        {
            return;
        }

        var strictDistinct = surface == CompendiumCoverSurface.Front
                             && frontTemplate == CompendiumFrontCoverTemplate.PortfolioQuartet;
        var explicitPhotos = new HashSet<(int ProjectId, int PhotoId)>();
        var explicitProjects = new HashSet<int>();

        // Pass 1: reserve every manual assignment before considering any
        // automatic slot. This makes the outcome independent of slot order.
        foreach (var requirement in requirements)
        {
            var slot = GetSlot(slots, requirement.Surface, requirement.SlotKey);
            if (slot.ImageMode != CompendiumCoverImageMode.Explicit
                || !TryReference(slot, out var reference))
            {
                continue;
            }

            explicitPhotos.Add(reference);
            explicitProjects.Add(reference.ProjectId);
        }

        var usedPhotos = new HashSet<(int ProjectId, int PhotoId)>(explicitPhotos);
        var usedProjects = new HashSet<int>(explicitProjects);
        var unresolved = new List<(CompendiumCoverTemplatePolicy.Slot Requirement, int SlotIndex)>();

        // Pass 2: retain every valid sticky automatic assignment. Only a
        // conflict with an explicit choice (or Quartet distinctness) releases
        // the automatic slot for reallocation.
        foreach (var requirement in requirements)
        {
            var index = FindSlotIndex(slots, requirement.Surface, requirement.SlotKey);
            var slot = slots[index] with
            {
                FitMode = CompendiumCoverTemplatePolicy.NormalizeFitMode(
                    requirement.Surface,
                    frontTemplate,
                    slots[index].FitMode)
            };

            if (slot.ImageMode == CompendiumCoverImageMode.None)
            {
                slots[index] = slot with { ProjectId = null, PhotoId = null };
                continue;
            }

            if (slot.ImageMode == CompendiumCoverImageMode.Explicit)
            {
                slots[index] = slot;
                continue;
            }

            var stickyIsUsable = TryReference(slot, out var sticky)
                                 && usablePhotos.Contains(sticky)
                                 && !explicitPhotos.Contains(sticky)
                                 && (!strictDistinct || !usedPhotos.Contains(sticky));

            if (stickyIsUsable)
            {
                slots[index] = slot;
                usedPhotos.Add(sticky);
                usedProjects.Add(sticky.ProjectId);
                continue;
            }

            slots[index] = slot with
            {
                ProjectId = null,
                PhotoId = null,
                FocalX = .5d,
                FocalY = .5d
            };
            unresolved.Add((requirement, index));
        }

        // Pass 3: allocate only the automatic slots that remain unresolved.
        // Prefer a different project, then a different photo. Non-Quartet
        // layouts may reuse an automatic photo only as a last resort; an
        // explicit photo is never reused by an automatic slot.
        foreach (var item in unresolved)
        {
            var sequence = candidates
                .Where(candidate => !usedPhotos.Contains((candidate.ProjectId, candidate.PhotoId))
                                    && !usedProjects.Contains(candidate.ProjectId))
                .Concat(candidates.Where(candidate =>
                    !usedPhotos.Contains((candidate.ProjectId, candidate.PhotoId))))
                .Concat(strictDistinct
                    ? Array.Empty<CompendiumCoverAutomaticImagePolicy.Candidate>()
                    : candidates.Where(candidate =>
                        !explicitPhotos.Contains((candidate.ProjectId, candidate.PhotoId))))
                .GroupBy(candidate => (candidate.ProjectId, candidate.PhotoId))
                .Select(group => group.First());

            var resolved = sequence.FirstOrDefault();
            if (resolved is null)
            {
                continue;
            }

            var slot = slots[item.SlotIndex];
            slots[item.SlotIndex] = slot with
            {
                ImageMode = CompendiumCoverImageMode.Automatic,
                ProjectId = resolved.ProjectId,
                PhotoId = resolved.PhotoId,
                FocalX = ClampFocal(resolved.FocalX),
                FocalY = ClampFocal(resolved.FocalY)
            };
            usedPhotos.Add((resolved.ProjectId, resolved.PhotoId));
            usedProjects.Add(resolved.ProjectId);
        }
    }

    private static IEnumerable<CompendiumCoverImageSlot> NormaliseSlots(
        IEnumerable<CompendiumCoverImageSlot>? configuredSlots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in configuredSlots ?? Array.Empty<CompendiumCoverImageSlot>())
        {
            if (string.IsNullOrWhiteSpace(slot.SlotKey))
            {
                continue;
            }

            var surface = Enum.IsDefined(slot.Surface) ? slot.Surface : CompendiumCoverSurface.Front;
            var key = SlotIdentity(surface, slot.SlotKey);
            if (!seen.Add(key))
            {
                continue;
            }

            var mode = Enum.IsDefined(slot.ImageMode)
                ? slot.ImageMode
                : CompendiumCoverImageMode.Automatic;
            var hasCompleteReference = slot.ProjectId is > 0 && slot.PhotoId is > 0;
            yield return slot with
            {
                Surface = surface,
                SlotKey = slot.SlotKey.Trim(),
                ImageMode = mode,
                ProjectId = mode != CompendiumCoverImageMode.None && hasCompleteReference
                    ? slot.ProjectId
                    : null,
                PhotoId = mode != CompendiumCoverImageMode.None && hasCompleteReference
                    ? slot.PhotoId
                    : null,
                FocalX = ClampFocal(slot.FocalX),
                FocalY = ClampFocal(slot.FocalY),
                FitMode = Enum.IsDefined(slot.FitMode) ? slot.FitMode : CompendiumImageFitMode.Fill
            };
        }
    }

    private static CompendiumCoverImageSlot GetSlot(
        IReadOnlyList<CompendiumCoverImageSlot> slots,
        CompendiumCoverSurface surface,
        string slotKey)
        => slots[FindSlotIndex(slots, surface, slotKey)];

    private static int FindSlotIndex(
        IReadOnlyList<CompendiumCoverImageSlot> slots,
        CompendiumCoverSurface surface,
        string slotKey)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index].Surface == surface
                && string.Equals(slots[index].SlotKey, slotKey, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryReference(
        CompendiumCoverImageSlot slot,
        out (int ProjectId, int PhotoId) reference)
    {
        if (slot.ProjectId is > 0 && slot.PhotoId is > 0)
        {
            reference = (slot.ProjectId.Value, slot.PhotoId.Value);
            return true;
        }

        reference = default;
        return false;
    }

    private static string SlotIdentity(CompendiumCoverSurface surface, string slotKey)
        => $"{surface}:{slotKey.Trim()}";

    private static double ClampFocal(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : .5d;
}
