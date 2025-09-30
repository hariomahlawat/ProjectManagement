using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Services;

namespace ProjectManagement.Tests.Fakes;

public sealed class RecordingAudit : IAuditService
{
    private readonly List<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public Task LogAsync(
        string action,
        string? message = null,
        string level = "Info",
        string? userId = null,
        string? userName = null,
        IDictionary<string, string?>? data = null,
        HttpContext? http = null)
    {
        var payload = data is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);

        _entries.Add(new AuditEntry(action, message, level, userId, userName, payload));
        return Task.CompletedTask;
    }

    public sealed record AuditEntry(
        string Action,
        string? Message,
        string Level,
        string? UserId,
        string? UserName,
        IReadOnlyDictionary<string, string?> Data);
}
