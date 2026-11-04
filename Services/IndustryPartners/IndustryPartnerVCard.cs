using System.Text;

namespace ProjectManagement.Services.IndustryPartners;

// SECTION: Contact cards (vCard 3.0) for the Industry Directory
// One card per contact, so a phone or Outlook can import an organisation's people in one go.
// The contact's single name field (often "Name, Designation") becomes the formatted name;
// the organisation goes in ORG.
public static class IndustryPartnerVCard
{
    public const string ContentType = "text/vcard; charset=utf-8";

    private const int MaxLineOctets = 75;

    public static string Build(string organisationName, IEnumerable<IndustryPartnerContactDto> contacts)
    {
        ArgumentNullException.ThrowIfNull(contacts);

        var builder = new StringBuilder();
        foreach (var contact in contacts)
        {
            var name = string.IsNullOrWhiteSpace(contact.Name)
                ? organisationName
                : contact.Name.Trim();

            AppendLine(builder, "BEGIN:VCARD");
            AppendLine(builder, "VERSION:3.0");
            AppendLine(builder, $"FN:{Escape(name)}");
            AppendLine(builder, $"N:{Escape(name)};;;;");
            AppendLine(builder, $"ORG:{Escape(organisationName)}");
            if (!string.IsNullOrWhiteSpace(contact.Phone))
            {
                AppendLine(builder, $"TEL;TYPE=WORK,VOICE:{Escape(contact.Phone.Trim())}");
            }

            if (!string.IsNullOrWhiteSpace(contact.Email))
            {
                AppendLine(builder, $"EMAIL;TYPE=INTERNET:{Escape(contact.Email.Trim())}");
            }

            AppendLine(builder, "END:VCARD");
        }

        return builder.ToString();
    }

    /// <summary>A safe download name such as "aravalli-defence-systems-pvt-ltd-contacts.vcf".</summary>
    public static string FileName(string organisationName, string? contactName = null)
    {
        static string Slug(string value)
        {
            var slug = new StringBuilder();
            foreach (var character in value.Trim().ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(character))
                {
                    slug.Append(character);
                }
                else if (slug.Length > 0 && slug[^1] != '-')
                {
                    slug.Append('-');
                }
            }

            var text = slug.ToString().Trim('-');
            return text.Length > 60 ? text[..60].TrimEnd('-') : text;
        }

        var organisation = Slug(organisationName);
        var basis = string.IsNullOrWhiteSpace(contactName)
            ? $"{organisation}-contacts"
            : $"{Slug(contactName)}-{organisation}";
        basis = basis.Trim('-');
        return (string.IsNullOrEmpty(basis) ? "contacts" : basis) + ".vcf";
    }

    // RFC 2426: backslash, comma, semicolon and newlines are escaped in text values.
    internal static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

    // Lines are folded at 75 octets (UTF-8), continuing with a single leading space, and
    // never split inside a multi-byte character.
    private static void AppendLine(StringBuilder builder, string line)
    {
        var octets = 0;
        var limit = MaxLineOctets;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                builder.Append("\r\n ");
                octets = 1;
                limit = MaxLineOctets;
            }

            builder.Append(rune.ToString());
            octets += size;
        }

        builder.Append("\r\n");
    }
}
