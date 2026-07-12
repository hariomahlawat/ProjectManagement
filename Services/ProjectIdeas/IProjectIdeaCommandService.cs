using ProjectManagement.Models.ProjectIdeas;

namespace ProjectManagement.Services.ProjectIdeas;

/// <summary>
/// Application command contract for Project Idea lifecycle and typed discussion writes.
/// Cross-module workflows use this contract so Project Ideas remain the sole owner of
/// their persistence and invariants.
/// </summary>
public interface IProjectIdeaCommandService
{
    Task<ProjectIdea> CreateAsync(
        ProjectIdea idea,
        CancellationToken cancellationToken = default);

    Task<ProjectIdeaComment> AddConferenceCommentAsync(
        ProjectIdea idea,
        string text,
        string userId,
        string actorRole,
        CancellationToken cancellationToken = default);
}
