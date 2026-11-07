using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Services.Documents;

namespace ProjectManagement.Services.Admin.Recovery;

/// <summary>What an administrator asked the document recycle bin to do.</summary>
public enum DocumentRecycleAction
{
    Restore = 0,
    PermanentlyDelete = 1
}

/// <summary>
/// The result of one bulk run over the document recycle bin. <see cref="Skipped"/> counts the
/// chosen documents that were no longer in the bin (restored since the page was opened, or a
/// hand-made id); they are left alone, never deleted.
/// </summary>
public sealed record DocumentRecycleResult(
    DocumentRecycleAction Action,
    int Requested,
    int Succeeded,
    int Skipped,
    IReadOnlyList<int> FailedIds)
{
    public bool NothingEligible => Succeeded == 0 && FailedIds.Count == 0;
}

public interface IDocumentRecycleService
{
    /// <summary>
    /// Restores or permanently deletes the chosen documents that are still in the recycle bin,
    /// one at a time. A document that fails is reported and the rest carry on. Running the same
    /// request twice is safe: the second run finds the documents gone from the bin and skips them.
    /// </summary>
    Task<DocumentRecycleResult> ExecuteAsync(
        DocumentRecycleAction action,
        IReadOnlyCollection<int> documentIds,
        string actorUserId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The document recycle bin's bulk restore and permanent delete. It used to live in the Restore
/// deleted items page (docs/ux/admin-review-3-fixes.md, C8).
/// </summary>
public sealed class DocumentRecycleService : IDocumentRecycleService
{
    private readonly IDocumentRecoveryQueryService _query;
    private readonly IDocumentService _documents;
    private readonly AdminRecoveryOptions _options;
    private readonly ILogger<DocumentRecycleService> _logger;

    public DocumentRecycleService(
        IDocumentRecoveryQueryService query,
        IDocumentService documents,
        IOptions<AdminRecoveryOptions> options,
        ILogger<DocumentRecycleService> logger)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DocumentRecycleResult> ExecuteAsync(
        DocumentRecycleAction action,
        IReadOnlyCollection<int> documentIds,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("An actor is required.", nameof(actorUserId));
        }

        var requested = documentIds.Where(id => id > 0).Distinct().ToArray();
        if (requested.Length > _options.MaximumBulkDocuments)
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentIds),
                $"At most {_options.MaximumBulkDocuments} documents can be processed in one operation.");
        }

        if (requested.Length == 0)
        {
            return new DocumentRecycleResult(action, 0, 0, 0, Array.Empty<int>());
        }

        // Act only on documents that are still in the recycle bin. Anything restored since the
        // admin opened the page (or a hand-made id) is skipped, never permanently deleted.
        var selection = await _query.GetSelectionSummaryAsync(requested, cancellationToken);
        var eligibleIds = selection.EligibleIds;
        var succeeded = 0;
        var failed = new List<int>();
        foreach (var id in eligibleIds)
        {
            try
            {
                if (action == DocumentRecycleAction.Restore)
                    await _documents.RestoreAsync(id, actorUserId, cancellationToken);
                else
                    await _documents.HardDeleteAsync(id, actorUserId, cancellationToken);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed.Add(id);
                _logger.LogError(exception, "Document recovery operation {Action} failed for document {DocumentId}", action, id);
            }
        }

        return new DocumentRecycleResult(
            action,
            requested.Length,
            succeeded,
            requested.Length - eligibleIds.Count,
            failed);
    }
}
