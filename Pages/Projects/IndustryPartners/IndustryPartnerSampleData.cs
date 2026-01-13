using System;
using System.Collections.Generic;
using ProjectManagement.ViewModels.Projects.IndustryPartners;

namespace ProjectManagement.Pages.Projects.IndustryPartners
{
    public static class IndustryPartnerSampleData
    {
        // Section: Sample data (placeholder until persistence layer is connected)
        public static IReadOnlyList<PartnerDetailViewModel> BuildSamplePartners() => new List<PartnerDetailViewModel>
        {
            new PartnerDetailViewModel
            {
                Id = 101,
                DisplayName = "Aeron Industrial Systems",
                LegalName = "Aeron Industrial Systems Limited",
                PartnerType = "DPSU",
                Status = "Active",
                RegistrationNumber = "REG-784512",
                Address = "85 Defence Sector Road",
                City = "Bengaluru",
                State = "Karnataka",
                Country = "India",
                Website = "https://aeron-industrial.example",
                Email = "contact@aeron-industrial.example",
                Phone = "+91 80 5555 1122",
                LocationSummary = "Bengaluru, Karnataka, India",
                ProjectCount = 3,
                ProjectAssociations = new List<ProjectAssociationViewModel>
                {
                    new ProjectAssociationViewModel
                    {
                        ProjectName = "Aquila UAV Programme",
                        ProjectLink = "/projects/overview/12",
                        Role = "Development Partner",
                        AssociationStatus = "Active",
                        Notes = "Primary avionics supplier"
                    },
                    new ProjectAssociationViewModel
                    {
                        ProjectName = "Project Sentinel",
                        ProjectLink = "/projects/overview/27",
                        Role = "OEM",
                        AssociationStatus = "Completed",
                        Notes = "Completed delivery in Q4"
                    }
                },
                Contacts = new List<PartnerContactViewModel>
                {
                    new PartnerContactViewModel
                    {
                        Name = "Rohit Menon",
                        Designation = "Programme Director",
                        Email = "rohit.menon@aeron-industrial.example",
                        Phone = "+91 80 5555 1188"
                    },
                    new PartnerContactViewModel
                    {
                        Name = "Kavita Rao",
                        Designation = "Contracts Lead",
                        Email = "kavita.rao@aeron-industrial.example",
                        Phone = "+91 80 5555 1199"
                    }
                },
                Documents = new List<PartnerDocumentViewModel>
                {
                    new PartnerDocumentViewModel
                    {
                        FileName = "Aeron_Capability_Statement.pdf",
                        DocumentType = "Capability Statement",
                        UploadedBy = "Project Office",
                        UploadedOn = new DateTime(2024, 10, 14)
                    },
                    new PartnerDocumentViewModel
                    {
                        FileName = "Aeron_Compliance_Checklist.pdf",
                        DocumentType = "Compliance",
                        UploadedBy = "Compliance Desk",
                        UploadedOn = new DateTime(2024, 12, 5)
                    }
                },
                Notes = new List<PartnerNoteViewModel>
                {
                    new PartnerNoteViewModel
                    {
                        Author = "Neha Kapoor",
                        Timestamp = new DateTime(2024, 11, 2, 10, 30, 0),
                        Content = "Partner approved for avionics integration scope."
                    },
                    new PartnerNoteViewModel
                    {
                        Author = "Programme Office",
                        Timestamp = new DateTime(2025, 1, 8, 15, 10, 0),
                        Content = "Awaiting updated quality audit certificate."
                    }
                }
            },
            new PartnerDetailViewModel
            {
                Id = 102,
                DisplayName = "Nova Robotics",
                LegalName = "Nova Robotics Private Limited",
                PartnerType = "Startup",
                Status = "Active",
                RegistrationNumber = "REG-663201",
                Address = "12 Innovation Park",
                City = "Hyderabad",
                State = "Telangana",
                Country = "India",
                Website = "https://nova-robotics.example",
                Email = "hello@nova-robotics.example",
                Phone = "+91 40 4444 3322",
                LocationSummary = "Hyderabad, Telangana, India",
                ProjectCount = 1,
                ProjectAssociations = new List<ProjectAssociationViewModel>
                {
                    new ProjectAssociationViewModel
                    {
                        ProjectName = "Project Sentinel",
                        ProjectLink = "/projects/overview/27",
                        Role = "Support",
                        AssociationStatus = "Active",
                        Notes = "Autonomy testing support"
                    }
                },
                Contacts = new List<PartnerContactViewModel>(),
                Documents = new List<PartnerDocumentViewModel>(),
                Notes = new List<PartnerNoteViewModel>()
            },
            new PartnerDetailViewModel
            {
                Id = 103,
                DisplayName = "Helios Tech University",
                LegalName = "Helios Technology University",
                PartnerType = "Academic",
                Status = "Inactive",
                RegistrationNumber = "UNI-445990",
                Address = "44 Research Crescent",
                City = "Pune",
                State = "Maharashtra",
                Country = "India",
                Website = "https://helios-tech.example",
                Email = "engagement@helios-tech.example",
                Phone = "+91 20 3333 1010",
                LocationSummary = "Pune, Maharashtra, India",
                ProjectCount = 4,
                ProjectAssociations = new List<ProjectAssociationViewModel>
                {
                    new ProjectAssociationViewModel
                    {
                        ProjectName = "Skyline Radar Upgrade",
                        ProjectLink = "/projects/overview/44",
                        Role = "ToT Recipient",
                        AssociationStatus = "Completed",
                        Notes = "Academic transfer complete"
                    }
                },
                Contacts = new List<PartnerContactViewModel>
                {
                    new PartnerContactViewModel
                    {
                        Name = "Prof. Sunita Iyer",
                        Designation = "Research Head",
                        Email = "sunita.iyer@helios-tech.example",
                        Phone = "+91 20 3333 1088"
                    }
                },
                Documents = new List<PartnerDocumentViewModel>(),
                Notes = new List<PartnerNoteViewModel>
                {
                    new PartnerNoteViewModel
                    {
                        Author = "Archive Review",
                        Timestamp = new DateTime(2023, 7, 19, 9, 20, 0),
                        Content = "Partnership paused pending funding cycle."
                    }
                }
            }
        };
    }
}
