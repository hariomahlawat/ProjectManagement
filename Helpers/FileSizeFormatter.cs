using System;
using System.Globalization;

namespace ProjectManagement.Helpers;

public static class FileSizeFormatter
{
    public static string FormatFileSize(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes < 1024)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} B", bytes);
        }

        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            unitIndex == 0 ? "{0} {1}" : "{0:0.#} {1}",
            value,
            units[unitIndex]);
    }
}
