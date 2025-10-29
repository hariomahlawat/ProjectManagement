using System;

namespace ProjectManagement.Application.Ffc;

public sealed class FfcAttachmentAuthorizationException : Exception
{
    public FfcAttachmentAuthorizationException(string message)
        : base(message)
    {
    }
}
