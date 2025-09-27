using System;

namespace ProjectManagement.Services.Plans;

public sealed class PlanDraftLockedException : InvalidOperationException
{
    public PlanDraftLockedException(string message)
        : base(message)
    {
    }
}
