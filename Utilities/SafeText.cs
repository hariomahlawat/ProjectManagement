using System.Text.RegularExpressions;

namespace ProjectManagement.Utilities
{
    public static class SafeText
    {
        private static readonly Regex Newlines = new(@"\r\n|\r|\n", RegexOptions.Compiled);

        public static string ToSafeHtml(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var encoded = System.Net.WebUtility.HtmlEncode(input);
            return Newlines.Replace(encoded, "<br>");
        }
    }
}
