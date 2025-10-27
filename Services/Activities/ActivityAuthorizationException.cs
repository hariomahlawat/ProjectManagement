using System;

namespace ProjectManagement.Services.Activities;

public sealed class ActivityAuthorizationException : Exception
{
    public ActivityAuthorizationException(string message)
        : base(message)
    {
    }
}
