using System;

namespace ProjectManagement.Helpers;

public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
