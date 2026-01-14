using System;

namespace ProjectManagement.Services.IndustryPartners.Exceptions
{
    public sealed class IndustryPartnerInactiveException : Exception
    {
        // Section: Construction
        public IndustryPartnerInactiveException(string message) : base(message)
        {
        }
    }
}
