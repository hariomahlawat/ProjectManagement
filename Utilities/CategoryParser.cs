using System;
using System.Collections.Generic;
using ProjectManagement.Models;

namespace ProjectManagement.Utilities
{
    public static class CategoryParser
    {
        private static readonly Dictionary<string, EventCategory> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["visit"] = EventCategory.Visit,
                ["vis"] = EventCategory.Visit,

                ["insp"] = EventCategory.Insp,
                ["inspection"] = EventCategory.Insp,

                ["conf"] = EventCategory.Conference,
                ["conference"] = EventCategory.Conference,
                ["training"] = EventCategory.Conference,
                ["townhall"] = EventCategory.Conference,

                ["holiday"] = EventCategory.Other,
                ["hiring"]  = EventCategory.Other,
                ["other"]   = EventCategory.Other
            };

        public static EventCategory ParseOrOther(string? value)
            => value is not null && Map.TryGetValue(value.Trim(), out var cat)
                ? cat
                : EventCategory.Other;
    }
}
