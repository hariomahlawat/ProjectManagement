using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using DocRepoDocument = ProjectManagement.Data.DocRepo.Document;

namespace ProjectManagement.Services.Admin.Recovery;

/// <summary>
/// What is in each bin, written once (docs/ux/admin-review-2-fixes.md, PR 5). Home, Restore deleted
/// items and its tabs all count from these queries, so their figures always agree.
/// </summary>
public static class AdminBins
{
    /// <summary>Projects in Trash. Deleted projects are hidden by a query filter, so it is lifted here.</summary>
    public static IQueryable<Project> DeletedProjects(ApplicationDbContext db) =>
        db.Projects.IgnoreQueryFilters().AsNoTracking().Where(project => project.IsDeleted);

    /// <summary>Project documents in the recycle bin.</summary>
    public static IQueryable<ProjectDocument> DeletedProjectDocuments(ApplicationDbContext db) =>
        db.ProjectDocuments.AsNoTracking().Where(document => document.Status == ProjectDocumentStatus.SoftDeleted);

    /// <summary>Document Repository documents in its trash.</summary>
    public static IQueryable<DocRepoDocument> DeletedRepositoryDocuments(ApplicationDbContext db) =>
        db.Documents.AsNoTracking().Where(document => document.IsDeleted);

    /// <summary>Deleted calendar events. Deleted events are hidden by a query filter, so it is lifted here.</summary>
    public static IQueryable<Event> DeletedEvents(ApplicationDbContext db) =>
        db.Events.IgnoreQueryFilters().AsNoTracking().Where(calendarEvent => calendarEvent.IsDeleted);

    /// <summary>
    /// Briefing decks deleted in Studio that can still be restored. A deck past the briefing-deck
    /// service's window cannot come back, so it is not counted; the rule is the service's own.
    /// </summary>
    public static IQueryable<ProjectBriefingDeck> DeletedBriefingDecks(ApplicationDbContext db, DateTimeOffset nowUtc) =>
        db.Set<ProjectBriefingDeck>().AsNoTracking().Restorable(nowUtc);

    public static Task<int> CountProjectsAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
        DeletedProjects(db).CountAsync(cancellationToken);

    public static Task<int> CountProjectDocumentsAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
        DeletedProjectDocuments(db).CountAsync(cancellationToken);

    public static Task<int> CountRepositoryDocumentsAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
        DeletedRepositoryDocuments(db).CountAsync(cancellationToken);

    public static Task<int> CountEventsAsync(ApplicationDbContext db, CancellationToken cancellationToken = default) =>
        DeletedEvents(db).CountAsync(cancellationToken);

    public static Task<int> CountBriefingDecksAsync(ApplicationDbContext db, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        DeletedBriefingDecks(db, nowUtc).CountAsync(cancellationToken);
}
