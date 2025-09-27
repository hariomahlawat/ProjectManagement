namespace ProjectManagement.Models.Scheduling;

public static class NextStageStartPolicies
{
    public const string SameDay = "SameDay";
    public const string NextWorkingDay = "NextWorkingDay";

    public static bool IsValid(string? value) =>
        value is SameDay or NextWorkingDay;
}
