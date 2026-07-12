using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

/// <summary>
/// Narrow command contract used by cross-module workflows that add typed discussion entries.
/// </summary>
public interface IProjectIdeaCommandService
{
    Task<ProjectIdeaComment> AddConferenceCommentAsync(
        ProjectIdea idea,
        string text,
        string userId,
        string actorRole,
        CancellationToken cancellationToken = default);
}
