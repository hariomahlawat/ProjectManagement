using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Data.Projects;
using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Hosted;

// SECTION: Project document OCR background worker
public sealed class ProjectDocumentOcrWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProjectDocumentOcrWorker> _logger;

    public ProjectDocumentOcrWorker(IServiceProvider serviceProvider, ILogger<ProjectDocumentOcrWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // SECTION: Poll loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var runner = scope.ServiceProvider.GetRequiredService<IProjectDocumentOcrRunner>();

                var documents = await db.ProjectDocuments
                    .Include(d => d.DocumentText)
                    .Where(d => d.Status == ProjectDocumentStatus.Published && !d.IsArchived)
                    .Where(d => d.OcrStatus == ProjectDocumentOcrStatus.Pending)
                    .OrderBy(d => d.UploadedAtUtc)
                    .Take(5)
                    .ToListAsync(stoppingToken);

                if (documents.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                foreach (var document in documents)
                {
                    document.OcrLastTriedUtc = DateTimeOffset.UtcNow;

                    try
                    {
                        var result = await runner.RunAsync(document, stoppingToken);

                        if (result.Success)
                        {
                            var text = document.DocumentText ?? new ProjectDocumentText
                            {
                                ProjectDocumentId = document.Id,
                                UpdatedAtUtc = DateTimeOffset.UtcNow
                            };

                            if (document.DocumentText is null)
                            {
                                db.ProjectDocumentTexts.Add(text);
                                document.DocumentText = text;
                            }

                            text.OcrText = CapExtractedText(result.Text);
                            text.UpdatedAtUtc = DateTimeOffset.UtcNow;
                            document.OcrStatus = ProjectDocumentOcrStatus.Succeeded;
                            document.OcrFailureReason = null;
                            _logger.LogInformation("OCR succeeded for project document {DocumentId}", document.Id);
                        }
                        else
                        {
                            document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                            document.OcrFailureReason = TrimForFailure(result.Error);

                            if (document.DocumentText is not null)
                            {
                                document.DocumentText.OcrText = null;
                                document.DocumentText.UpdatedAtUtc = DateTimeOffset.UtcNow;
                            }

                            _logger.LogWarning(
                                "OCR failed for project document {DocumentId}: {Reason}",
                                document.Id,
                                document.OcrFailureReason);
                        }
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        document.OcrStatus = ProjectDocumentOcrStatus.Failed;
                        document.OcrFailureReason = TrimForFailure(ex.Message);

                        if (document.DocumentText is not null)
                        {
                            document.DocumentText.OcrText = null;
                            document.DocumentText.UpdatedAtUtc = DateTimeOffset.UtcNow;
                        }

                        _logger.LogError(
                            ex,
                            "Unexpected error running OCR for project document {DocumentId}",
                            document.Id);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing project document OCR queue");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    // SECTION: Helpers
    private static string? TrimForFailure(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length > 1000 ? value[..1000] : value;
    }

    private static string? CapExtractedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text.Length > 200_000 ? text[..200_000] : text;
    }
}
