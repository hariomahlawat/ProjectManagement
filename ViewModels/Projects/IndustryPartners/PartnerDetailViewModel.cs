using System;
using System.Collections.Generic;

namespace ProjectManagement.ViewModels.Projects.IndustryPartners
{
    public class PartnerDetailViewModel
    {
        // Section: Identity
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? LegalName { get; set; }
        public string PartnerType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        public bool CanManage { get; set; }

        // Section: Registration
        public string? RegistrationNumber { get; set; }

        // Section: Location
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public string LocationSummary { get; set; } = string.Empty;

        // Section: Contact
        public string? Website { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // Section: Associations
        public int ProjectCount { get; set; }
        public IReadOnlyList<ProjectAssociationViewModel> ProjectAssociations { get; set; } = Array.Empty<ProjectAssociationViewModel>();

        // Section: Contacts
        public IReadOnlyList<PartnerContactViewModel> Contacts { get; set; } = Array.Empty<PartnerContactViewModel>();

        // Section: Documents
        public IReadOnlyList<PartnerDocumentViewModel> Documents { get; set; } = Array.Empty<PartnerDocumentViewModel>();

        // Section: Notes
        public IReadOnlyList<PartnerNoteViewModel> Notes { get; set; } = Array.Empty<PartnerNoteViewModel>();
    }

    public class ProjectAssociationViewModel
    {
        // Section: Project detail
        public int AssociationId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectLink { get; set; } = string.Empty;
        public string AssociationStatus { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? Notes { get; set; }
    }

    public class PartnerContactViewModel
    {
        // Section: Contact detail
        public string Name { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class PartnerDocumentViewModel
    {
        // Section: Document metadata
        public string FileName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
        public DateTime UploadedOn { get; set; }
    }

    public class PartnerNoteViewModel
    {
        // Section: Note content
        public string Author { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}
