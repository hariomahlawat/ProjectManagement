namespace ProjectManagement.Services.Arpp;

public sealed class ArppManagedIpaException : InvalidOperationException
{
    public ArppManagedIpaException(int projectId)
        : base($"The IPA position for project {projectId} is managed through ARPP and cannot be edited as a legacy project fact.")
    {
        ProjectId = projectId;
    }

    public int ProjectId { get; }
}
