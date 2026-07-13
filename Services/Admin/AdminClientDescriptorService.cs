using System.Text.RegularExpressions;

namespace ProjectManagement.Services.Admin;

public sealed record AdminClientDescriptor(
    string Browser,
    string OperatingSystem,
    string DeviceClass,
    string Summary,
    string? RawUserAgent);

public interface IAdminClientDescriptorService
{
    AdminClientDescriptor Describe(string? userAgent);
}

/// <summary>
/// Produces concise, deterministic client descriptions without introducing a
/// third-party user-agent dependency. Raw values remain available for technical review.
/// </summary>
public sealed class AdminClientDescriptorService : IAdminClientDescriptorService
{
    private static readonly Regex EdgeRegex = new(@"\bEdg(?:A|iOS)?/(?<version>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ChromeRegex = new(@"\b(?:Chrome|CriOS)/(?<version>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FirefoxRegex = new(@"\b(?:Firefox|FxiOS)/(?<version>\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafariRegex = new(@"\bVersion/(?<version>\d+)(?:\.\d+)*.*\bSafari/", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public AdminClientDescriptor Describe(string? userAgent)
    {
        var raw = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
        if (raw is null)
        {
            return new("Unknown browser", "Unknown operating system", "Unknown device", "Client not reported", null);
        }

        var browser = ResolveBrowser(raw);
        var operatingSystem = ResolveOperatingSystem(raw);
        var device = ResolveDeviceClass(raw);
        return new(browser, operatingSystem, device, $"{browser} · {operatingSystem} · {device}", raw);
    }

    private static string ResolveBrowser(string value)
    {
        var edge = EdgeRegex.Match(value);
        if (edge.Success) return $"Edge {edge.Groups["version"].Value}";

        if (value.Contains("OPR/", StringComparison.OrdinalIgnoreCase))
        {
            var version = ReadVersion(value, "OPR/");
            return string.IsNullOrWhiteSpace(version) ? "Opera" : $"Opera {version}";
        }

        var chrome = ChromeRegex.Match(value);
        if (chrome.Success) return $"Chrome {chrome.Groups["version"].Value}";

        var firefox = FirefoxRegex.Match(value);
        if (firefox.Success) return $"Firefox {firefox.Groups["version"].Value}";

        var safari = SafariRegex.Match(value);
        if (safari.Success) return $"Safari {safari.Groups["version"].Value}";

        if (value.Contains("MSIE ", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Trident/", StringComparison.OrdinalIgnoreCase))
        {
            return "Internet Explorer";
        }

        return "Unknown browser";
    }

    private static string ResolveOperatingSystem(string value)
    {
        if (value.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (value.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (value.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || value.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || value.Contains("iPod", StringComparison.OrdinalIgnoreCase)) return "iOS/iPadOS";
        if (value.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "macOS";
        if (value.Contains("CrOS", StringComparison.OrdinalIgnoreCase)) return "ChromeOS";
        if (value.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        return "Unknown operating system";
    }

    private static string ResolveDeviceClass(string value)
    {
        if (value.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Tablet", StringComparison.OrdinalIgnoreCase)) return "Tablet";

        if (value.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || value.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Mobile";

        return "Desktop";
    }

    private static string? ReadVersion(string value, string token)
    {
        var start = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += token.Length;
        var end = start;
        while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.')) end++;
        if (end <= start) return null;

        var full = value[start..end];
        var dot = full.IndexOf('.');
        return dot < 0 ? full : full[..dot];
    }
}
