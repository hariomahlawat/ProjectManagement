using Microsoft.AspNetCore.Identity;

namespace ProjectManagement.Infrastructure;

public static class IdentityPasswordPolicy
{
    public static int SuggestedGeneratedLength(PasswordOptions options) =>
        Math.Max(16, Math.Max(options.RequiredLength, options.RequiredUniqueChars) + 4);

    public static string Describe(PasswordOptions options)
    {
        var requirements = new List<string>
        {
            $"at least {options.RequiredLength} characters"
        };

        if (options.RequireLowercase)
        {
            requirements.Add("a lowercase letter");
        }

        if (options.RequireUppercase)
        {
            requirements.Add("an uppercase letter");
        }

        if (options.RequireDigit)
        {
            requirements.Add("a digit");
        }

        if (options.RequireNonAlphanumeric)
        {
            requirements.Add("a symbol");
        }

        if (options.RequiredUniqueChars > 1)
        {
            requirements.Add($"{options.RequiredUniqueChars} unique characters");
        }

        return "Password must contain " + JoinRequirements(requirements) + ".";
    }

    private static string JoinRequirements(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + $", and {items[^1]}"
        };
    }
}
