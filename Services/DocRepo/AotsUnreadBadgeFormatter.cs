namespace ProjectManagement.Services.DocRepo;

public static class AotsUnreadBadgeFormatter
{
    // SECTION: Compact badge text formatter
    public static string Format(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return count > 9 ? "9+" : count.ToString();
    }
}
