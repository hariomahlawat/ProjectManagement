using System;
using System.Linq;

namespace ProjectManagement.Services.Analytics
{
    /// <summary>
    /// AR/VR is a technical category, matched by name the way CoE project categories are matched.
    /// One rule for the CoE (AR/VR) mode's project lists and its proliferation and training figures.
    /// </summary>
    public static class ArVrTechnicalCategories
    {
        private static readonly string[] Keywords = { "ar/vr", "arvr", "ar&vr", "augmented", "virtualreality" };

        public static bool Matches(string? technicalCategoryName)
        {
            if (string.IsNullOrWhiteSpace(technicalCategoryName))
            {
                return false;
            }

            var compact = technicalCategoryName.Replace(" ", string.Empty).ToLowerInvariant();
            return Keywords.Any(keyword => compact.Contains(keyword, StringComparison.Ordinal));
        }
    }
}
