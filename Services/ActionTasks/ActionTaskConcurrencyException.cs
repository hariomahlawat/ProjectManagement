using System;

namespace ProjectManagement.Services.ActionTasks;

// SECTION: Domain-level concurrency exception surfaced to UI workflows.
public sealed class ActionTaskConcurrencyException : InvalidOperationException
{
    public ActionTaskConcurrencyException(string message) : base(message)
    {
    }
}
