using ProjectManagement.Services.Projects;

namespace ProjectManagement.Tests.Fakes;

/// <summary>
/// Test double used by page-model tests that do not exercise project-content commands.
/// Any unexpected content save fails immediately so the tests cannot silently bypass behavior.
/// </summary>
public sealed class ThrowingProjectContentService : IProjectContentService
{
    private const string UnexpectedCallMessage =
        "Project content commands are outside the scope of this test.";

    public Task<ProjectContentSaveResult> SaveBriefAsync(
        int projectId,
        string? brief,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(UnexpectedCallMessage);

    public Task<ProjectContentSaveResult> SaveCapabilitiesAsync(
        int projectId,
        IReadOnlyList<string?> statements,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(UnexpectedCallMessage);


    public Task<ProjectContentSaveResult> SaveTechnicalSpecificationsAsync(
        int projectId,
        IReadOnlyList<string?> items,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(UnexpectedCallMessage);

    public Task<ProjectContentSaveResult> SaveDescriptionAsync(
        int projectId,
        string? description,
        string rowVersion,
        string userId,
        string userDisplay,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(UnexpectedCallMessage);
}
