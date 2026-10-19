using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;

namespace ProjectManagement.Tests.Fakes;

/// <summary>A structured log entry, and whether a transaction was open when it was written.</summary>
internal sealed record LogEntry(
    LogLevel Level,
    EventId EventId,
    IReadOnlyDictionary<string, object?> Fields,
    Exception? Exception,
    bool TransactionOpen)
{
    public object? Field(string name) => Fields.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Captures the D-1d chronology observability log entries a writer emits, with
/// their structured fields and whether <paramref name="db"/> still had an open
/// transaction at the time.
/// </summary>
internal sealed class CapturingLogger<T>(ApplicationDbContext db) : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Warnings =>
        _entries.Where(entry => entry.EventId.Name == "Stages.ActualChronologyWarning").ToArray();

    public IReadOnlyList<LogEntry> Unconfirmed =>
        _entries.Where(entry => entry.EventId.Name == "Stages.ActualChronologyWarningUnconfirmed").ToArray();

    public IReadOnlyList<LogEntry> Errors =>
        _entries.Where(entry => entry.EventId.Name == "Stages.ActualChronologyWarningFailed").ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, object?>();
        _entries.Add(new LogEntry(logLevel, eventId, fields, exception, db.Database.CurrentTransaction is not null));
    }
}
