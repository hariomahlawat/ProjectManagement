using System;

namespace ProjectManagement.Application.Ipr;

public sealed class IprValidationException : InvalidOperationException
{
    public IprValidationException(IprValidationCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public IprValidationException(IprValidationCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public IprValidationCode Code { get; }
}
