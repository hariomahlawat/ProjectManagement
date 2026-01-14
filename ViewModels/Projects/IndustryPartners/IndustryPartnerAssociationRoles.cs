using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public static class IndustryPartnerAssociationRoles
    {
        // Section: Role values
        public const string DevelopmentPartner = "DevelopmentPartner";
        public const string Jdp = "JDP";
        public const string Oem = "OEM";
        public const string TotRecipient = "ToTRecipient";
        public const string Support = "Support";

        // Section: Role options
        public static readonly IReadOnlyList<IndustryPartnerAssociationRoleOption> Options = new List<IndustryPartnerAssociationRoleOption>
        {
            new(DevelopmentPartner, "Development Partner"),
            new(Jdp, "JDP"),
            new(Oem, "OEM"),
            new(TotRecipient, "ToT Recipient"),
            new(Support, "Support")
        };

        // Section: Legacy mappings
        private static readonly IReadOnlyDictionary<string, string> LegacyRoleMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Development Partner", DevelopmentPartner },
            { "ToT Recipient", TotRecipient }
        };

        // Section: Validation
        public static bool IsValid(string? role)
        {
            var normalized = Normalize(role);
            return !string.IsNullOrWhiteSpace(normalized)
                && Options.Any(option => string.Equals(option.Value, normalized, StringComparison.Ordinal));
        }

        // Section: Normalization
        public static string Normalize(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return string.Empty;
            }

            return LegacyRoleMap.TryGetValue(role, out var normalized) ? normalized : role;
        }

        // Section: Equivalent roles
        public static IReadOnlyCollection<string> GetEquivalentRoles(string? role)
        {
            var normalized = Normalize(role);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Array.Empty<string>();
            }

            var equivalents = new List<string> { normalized };
            foreach (var legacy in LegacyRoleMap.Where(item => string.Equals(item.Value, normalized, StringComparison.Ordinal)))
            {
                equivalents.Add(legacy.Key);
            }

            return equivalents.Distinct(StringComparer.Ordinal).ToArray();
        }

        // Section: Label lookup
        public static string GetLabel(string? role)
        {
            var normalized = Normalize(role);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            return Options.FirstOrDefault(option => string.Equals(option.Value, normalized, StringComparison.Ordinal))?.Label
                ?? normalized;
        }
    }

    public sealed record IndustryPartnerAssociationRoleOption(string Value, string Label);
}
