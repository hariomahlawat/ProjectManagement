using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Remarks;

namespace ProjectManagement.Services.Remarks;

public sealed class RemarkNotificationService : IRemarkNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<RemarkNotificationService> _logger;

    public RemarkNotificationService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IEmailSender emailSender,
        ILogger<RemarkNotificationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task NotifyRemarkCreatedAsync(
        Remark remark,
        RemarkActorContext actor,
        RemarkProjectInfo project,
        CancellationToken cancellationToken = default)
    {
        if (remark is null)
        {
            throw new ArgumentNullException(nameof(remark));
        }

        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        try
        {
            var projectDetails = await LoadProjectDetailsAsync(project.ProjectId, cancellationToken);
            var recipients = await ResolveRecipientsAsync(projectDetails, remark.Type, cancellationToken);

            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "No remark notification recipients found for project {ProjectId}.",
                    project.ProjectId);
                return;
            }

            var subject = $"[{projectDetails.Name}] New {remark.Type} remark";
            var body = BuildBody(remark, actor, projectDetails.Name);

            foreach (var recipient in recipients)
            {
                try
                {
                    await _emailSender.SendEmailAsync(recipient.Email, subject, body);
                    _logger.LogInformation(
                        "Sent remark notification for remark {RemarkId} to {Recipient}.",
                        remark.Id,
                        recipient.Email);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed sending remark notification for remark {RemarkId} to {Recipient}.",
                        remark.Id,
                        recipient.Email);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed preparing remark notification for remark {RemarkId}.", remark.Id);
        }
    }

    private async Task<ProjectNotificationDetails> LoadProjectDetailsAsync(int projectId, CancellationToken cancellationToken)
    {
        var details = await _db.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new ProjectNotificationDetails(
                p.Id,
                p.Name,
                p.LeadPoUser != null ? p.LeadPoUser.Email : null,
                p.HodUser != null ? p.HodUser.Email : null,
                p.LeadPoUser != null ? p.LeadPoUser.FullName : null,
                p.HodUser != null ? p.HodUser.FullName : null))
            .FirstOrDefaultAsync(cancellationToken);

        if (details is null)
        {
            throw new InvalidOperationException($"Project {projectId} not found when sending remark notification.");
        }

        return details;
    }

    private async Task<IReadOnlyCollection<NotificationRecipient>> ResolveRecipientsAsync(
        ProjectNotificationDetails project,
        RemarkType remarkType,
        CancellationToken cancellationToken)
    {
        var recipients = new Dictionary<string, NotificationRecipient>(StringComparer.OrdinalIgnoreCase);

        AddRecipient(recipients, project.LeadPoEmail, project.LeadPoName);
        AddRecipient(recipients, project.HodEmail, project.HodName);

        await AddRoleRecipientsAsync(recipients, "Comdt", cancellationToken);

        if (remarkType == RemarkType.External)
        {
            await AddRoleRecipientsAsync(recipients, "MCO", cancellationToken);
        }

        return recipients.Values.ToArray();
    }

    private async Task AddRoleRecipientsAsync(
        IDictionary<string, NotificationRecipient> recipients,
        string role,
        CancellationToken cancellationToken)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddRecipient(recipients, user.Email, user.FullName);
        }
    }

    private static void AddRecipient(
        IDictionary<string, NotificationRecipient> recipients,
        string? email,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        if (!recipients.ContainsKey(email))
        {
            recipients[email] = new NotificationRecipient(email, name);
        }
    }

    private static string BuildBody(Remark remark, RemarkActorContext actor, string projectName)
    {
        var stage = string.IsNullOrWhiteSpace(remark.StageNameSnapshot)
            ? remark.StageRef ?? "N/A"
            : remark.StageNameSnapshot;

        var plainBody = ToPlainText(remark.Body);

        return $"""A new {remark.Type} remark was created on project '{projectName}'.

Author: {actor.ActorRole} ({actor.UserId})
Event date: {remark.EventDate:yyyy-MM-dd}
Stage: {stage}
Created at (UTC): {remark.CreatedAtUtc:u}

{plainBody}
""";
    }

    private static string ToPlainText(string html)
        => Regex.Replace(html ?? string.Empty, "<.*?>", string.Empty).Trim();

    private sealed record ProjectNotificationDetails(
        int Id,
        string Name,
        string? LeadPoEmail,
        string? HodEmail,
        string? LeadPoName,
        string? HodName);

    private sealed record NotificationRecipient(string Email, string? Name);
}
