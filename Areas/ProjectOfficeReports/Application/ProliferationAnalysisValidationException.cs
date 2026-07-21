namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

/// <summary>
/// Represents a user-correctable proliferation analysis request error.
/// Only this exception type is returned to the browser as a validation problem.
/// </summary>
public sealed class ProliferationAnalysisValidationException : Exception
{
    public ProliferationAnalysisValidationException(string message)
        : base(message)
    {
    }
}
