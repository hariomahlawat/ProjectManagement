using System;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityConcurrencyException : Exception
{
    public ActivityConcurrencyException(string message)
        : base(message)
    {
    }

    public ActivityConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
