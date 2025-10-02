using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics;

namespace ProjectManagement.Services.Remarks;

public sealed class RemarkMetrics : IRemarkMetrics
{
    private readonly Counter<long> _createCounter;
    private readonly Counter<long> _deleteCounter;
    private readonly Counter<long> _editWindowExpiredCounter;
    private readonly Counter<long> _permissionDeniedCounter;

    public RemarkMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create("ProjectManagement.Remarks");
        _createCounter = meter.CreateCounter<long>("remarks.create.count");
        _deleteCounter = meter.CreateCounter<long>("remarks.delete.count");
        _editWindowExpiredCounter = meter.CreateCounter<long>("remarks.edit.denied.window_expired");
        _permissionDeniedCounter = meter.CreateCounter<long>("remarks.permission.denied");
    }

    public void RecordCreated() => _createCounter.Add(1);

    public void RecordDeleted() => _deleteCounter.Add(1);

    public void RecordEditDeniedWindowExpired(string action)
    {
        var tags = new TagList
        {
            { "action", action }
        };
        _editWindowExpiredCounter.Add(1, tags);
    }

    public void RecordPermissionDenied(string action, string reason)
    {
        var tags = new TagList
        {
            { "action", action },
            { "reason", reason }
        };
        _permissionDeniedCounter.Add(1, tags);
    }
}
