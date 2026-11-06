using System.Reflection;
using System.Runtime.InteropServices;

namespace ProjectManagement.Utilities
{
    // ===========================
    // APP BUILD INFO
    // What the running build is, read once from the assembly the SDK stamped at build time:
    // the informational version carries "+<commit sha>" (Source Link), the file time is when
    // it was built. Shown on About PRISM (/Developer) so users can quote it to the administrator.
    // ===========================
    public sealed record AppBuildInfo(
        string AssemblyVersion,
        string? Commit,
        DateTimeOffset? BuiltUtc,
        string Runtime,
        DateTimeOffset StartedUtc)
    {
        private static readonly Lazy<AppBuildInfo> CurrentLazy = new(Read);

        public static AppBuildInfo Current => CurrentLazy.Value;

        /// <summary>The first 7 characters of the commit, as git shows it; null when the build was not stamped.</summary>
        public string? ShortCommit => Commit is { Length: > 7 } ? Commit[..7] : Commit;

        /// <summary>Splits "1.0.0+e5b656d…" into the version and the commit sha (lower case, hex only).</summary>
        public static (string Version, string? Commit) ParseInformationalVersion(string? informational)
        {
            if (string.IsNullOrWhiteSpace(informational))
            {
                return ("1.0", null);
            }

            var plus = informational.IndexOf('+');
            var version = (plus >= 0 ? informational[..plus] : informational).Trim();
            if (version.Length == 0)
            {
                version = "1.0";
            }

            if (plus < 0)
            {
                return (version, null);
            }

            var metadata = informational[(plus + 1)..].Trim();
            var isSha = metadata.Length >= 7 && metadata.All(Uri.IsHexDigit);
            return (version, isSha ? metadata.ToLowerInvariant() : null);
        }

        /// <summary>"1.0.0" reads as "1.0"; anything with a non-zero patch keeps it.</summary>
        public static string FriendlyVersion(string version)
        {
            var parts = version.Split('.');
            return parts.Length == 3 && parts[2] == "0" ? $"{parts[0]}.{parts[1]}" : version;
        }

        private static AppBuildInfo Read()
        {
            var assembly = typeof(AppBuildInfo).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var (version, commit) = ParseInformationalVersion(informational);

            DateTimeOffset? built = null;
            try
            {
                if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
                {
                    built = new DateTimeOffset(File.GetLastWriteTimeUtc(assembly.Location), TimeSpan.Zero);
                }
            }
            catch (IOException)
            {
                // The build time is a nicety; the page shows "Not recorded" without it.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return new AppBuildInfo(FriendlyVersion(version), commit, built, RuntimeInformation.FrameworkDescription, ProcessStartedUtc());
        }

        // "Running since" is the process start, not the moment this page was first opened.
        private static DateTimeOffset ProcessStartedUtc()
        {
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                return DateTimeOffset.UtcNow;
            }
        }
    }
}
