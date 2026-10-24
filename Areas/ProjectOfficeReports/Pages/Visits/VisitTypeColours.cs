using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Pages.Visits;

// Each visit type gets a colour slot (1–6) from its position by name, so a type keeps the
// same colour on the register, the record page, the type chart (--pm-chart-accent-N) and
// the Visit types page.
public static class VisitTypeColours
{
    public const int SlotCount = 6;

    public static IReadOnlyDictionary<Guid, int> SlotsFor(IEnumerable<VisitType> types)
        => SlotsFor(types.Select(t => (t.Id, t.Name)));

    public static IReadOnlyDictionary<Guid, int> SlotsFor(IEnumerable<(Guid Id, string Name)> types)
    {
        return types
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select((t, index) => (t.Id, Slot: (index % SlotCount) + 1))
            .ToDictionary(x => x.Id, x => x.Slot);
    }

    public static int SlotOf(IReadOnlyDictionary<Guid, int> slots, Guid visitTypeId)
        => slots.TryGetValue(visitTypeId, out var slot) ? slot : 1;
}
