using System.Text.RegularExpressions;

namespace ProjectManagement.Utilities
{
    public static class FileNameSanitizer
    {
        private static readonly Regex Allowed = new(@"[^A-Za-z0-9\.\-_]+", RegexOptions.Compiled);

        public static string Sanitize(string original)
        {
            var name = string.IsNullOrWhiteSpace(original) ? "file" : original.Trim();
            name = Allowed.Replace(name, "_");
            return name.Length > 100 ? name[..100] : name;
        }
    }
}
