using System;

namespace ProjectManagement.Services.IndustryPartners.Exceptions
{
    public sealed class DuplicateAssociationException : Exception
    {
        // Section: Construction
        public DuplicateAssociationException(string message) : base(message)
        {
        }
    }
}
