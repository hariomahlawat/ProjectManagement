namespace ProjectManagement.Services.Projects;

public static class ProjectValidationMessages
{
    public const string DuplicateCaseFileNumber = "Case file number already exists.";

    public const string InactiveCategory = "Selected category is no longer active.";

    public const string InactiveTechnicalCategory = "Selected technical category is no longer active.";

    public const string InactiveProjectType = "Selected project type is no longer active.";

    public const string InactiveSponsoringUnit = "Selected sponsoring unit is no longer active.";

    public const string InactiveLineDirectorate = "Selected line directorate is no longer active.";

    /// <summary>
    /// The request proposes a list item that has since been deleted (change requests name the
    /// item inside their payload, with no foreign key, so the delete could not see it).
    /// </summary>
    public static string DeletedProposedValue(string label) =>
        $"The {label} this request proposes has been deleted, so it cannot be approved. Reject it and ask for a new request.";

    public const string ProposedValueNoLongerAvailable =
        "A value this request proposes has just been deleted, so it cannot be approved. Reject it and ask for a new request.";

    public const string PendingMetaChangeRequest = "A meta change request is already pending for this project.";
}
