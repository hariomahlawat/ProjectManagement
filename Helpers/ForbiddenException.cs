using System;

namespace ProjectManagement.Helpers;

public class ForbiddenException : DomainException
{
    public ForbiddenException(string message)
        : base(message)
    {
    }
}
