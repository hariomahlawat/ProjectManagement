using System;
using System.Linq;
using System.Text;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

// -----------------------------------------------------------------------------
// One rule for how roster text is stored and shown, so "cFN sURYAMAN sHUKLA" or
// "LT COL" typed with Caps Lock reads as "Cfn Suryaman Shukla" / "Lt Col".
// Used when a roster is saved and when existing rows are read, so older entries
// show tidily without a data migration.
// -----------------------------------------------------------------------------
public static class TrainingRosterText
{
    // Army numbers: no inner spaces, upper case ("17032478k" → "17032478K").
    public static string? ArmyNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    // Ranks and names: single spaces; words capitalised unless the text is already in
    // ordinary mixed case ("Hav", "Nb Sub", "McGregor" are left as typed).
    public static string Rank(string? value) => Words(value);

    public static string Name(string? value) => Words(value);

    // Units keep their case ("3 GUARDS", "475 Engr Bde"); only the spacing is tidied.
    public static string Unit(string? value) => CollapseSpaces(value);

    private static string Words(string? value)
    {
        var text = CollapseSpaces(value);
        if (text.Length == 0 || LooksDeliberate(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var startOfWord = true;
        foreach (var c in text)
        {
            if (char.IsLetter(c))
            {
                builder.Append(startOfWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                startOfWord = false;
            }
            else
            {
                builder.Append(c);
                // A new word starts after a space, slash, hyphen, apostrophe or full stop ("2/Lt", "D'Souza").
                startOfWord = !char.IsLetterOrDigit(c);
            }
        }

        return builder.ToString();
    }

    // Every word starts with a capital (or a digit) and the text has some lower case:
    // the person typed it on purpose, so keep it.
    private static bool LooksDeliberate(string text)
    {
        if (!text.Any(char.IsLower))
        {
            return false;
        }

        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => !char.IsLetter(word[0]) || char.IsUpper(word[0]));
    }

    private static string CollapseSpaces(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
