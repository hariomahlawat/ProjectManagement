# Industry Partners (Organisations and JDP Links)

## Purpose

A directory of industry organisations. Each organisation can have contacts, file attachments and links to PRISM projects as **Joint Development Partners (JDP)**. A project can have several JDP organisations. The link table is keyed on (`IndustryPartnerId`, `ProjectId`).

## Routes

| Route | Handler(s) | Guard |
|---|---|---|
| `/IndustryPartners` | `Pages/IndustryPartners/Index.cshtml.cs` `OnGetAsync` (directory filters: `all`, `contact`, `associated`, `current`, `past`; tabs: `overview`, `contacts`, `projects`, `files`) | `IndustryPartners.View` |
| same page | `DuplicateSuggestions`, `CreatePartner`, `UpdatePartner`, `AddContact`, `UpdateContact`, `DeleteContact`, `UploadAttachment`, `DeleteAttachment`, `DownloadAttachment`, `LinkProject`, `UnlinkProject`, `DeletePartner` | see below |
| `GET /api/industry-partners/projects` | `Controllers/IndustryPartnersProjectLookupController` | `IndustryPartners.View` |
| `/Projects/Overview/{id}` multi-JDP handlers | `Pages/Projects/Overview.MultiJdp.cs` (`AddProjectJdp`, `RemoveProjectJdp`) | `Overview.CanManageProjectJdpAsync` |

## Authorization

Policies are registered in `Program.cs`. Role lists are in `Configuration/Policies.cs` `Policies.IndustryPartners`.

| Policy | Rule |
|---|---|
| `View`, `AddContact` | any authenticated user |
| `Create` | Admin, HoD, Comdt, Project Officer, Project Office, ProjectOffice (legacy), MCO, TA, ITO |
| `EditAny` | Admin, HoD, Comdt |
| `Delete` | Admin, HoD |
| `ManageAnyContact` | Admin, HoD, Comdt |

What each action requires, as checked in the page:

- **Update organisation, upload/delete attachment, link/unlink project**: `CanEditOrganisationAsync` passes for `EditAny`, or for the organisation's creator (`IndustryPartnerService.IsOwnerAsync`). An owner keeps edit rights even without the `Create` role.
- **Update/delete contact**: enforced in the service, which throws `ForbiddenException`. Allowed for a `ContactOverrideRoles` holder or for the contact's author.
- **Project Overview JDP add/remove** (a separate rule from the partner page): allowed for Admin, HoD and Comdt on any non-deleted project, and otherwise for the Project Officer of that project.

## Main types

- `Services/IndustryPartners/IndustryPartnerService` (`IIndustryPartnerService`) handles:
  - search and duplicate suggestions
  - CRUD
  - contacts
  - link/unlink
  - project JDP profile (`GetProjectJdpProfileAsync`, `GetProjectMultiJdpProfileAsync`, `AddProjectJdpAsync`, `RemoveProjectJdpAsync`)
  - `DeletePartnerAsync`
- `IndustryPartnerAttachmentManager` uploads files after a scan by `IFileScanner`. It records a SHA-256 hash, and handles download and delete.
- `FileSystemIndustryPartnerAttachmentStorage` stores files at `<UploadRoot>/industry-partners/{partnerId}/{guid}{ext}`. Paths are checked by `IFileSecurityValidator`.
- `IndustryPartnerAttachmentValidator` enforces the MIME/extension allow-list below.
- `IndustryPartnerProjectEligibility.IsEligibleForJdpLink` allows any project that is not deleted and not archived. Its code comment still says "one linked organisation", which is out of date: multiple links are allowed.

## Data entities (`Models/IndustryPartners`)

- `IndustryPartner`: Name/NormalizedName, Location/NormalizedLocation, Remarks, Created/Updated audit fields, RowVersion.
- `IndustryPartnerContact`, `IndustryPartnerAttachment` (Guid id, StorageKey, Sha256), `IndustryPartnerProject` (composite key, `LinkedByUserId`, RowVersion).

## Business rules

- Uniqueness is checked on normalized (name, location) by `EnsureUniqueAsync`. The create form offers duplicate suggestions.
- Creating an organisation can optionally add a first contact and a first project link in the same save.
- An organisation cannot be permanently deleted while it has JDP project links. Deletion removes the stored attachment files.
- Attachment allow-list: pdf, doc/docx, ppt/pptx, jpg/jpeg, png.
  - The declared MIME type must match the extension. File content is not sniffed.
  - Maximum size is `ProjectDocuments:MaxSizeMb` (`ProjectDocumentOptions.MaxSizeMb`).

## Configuration

- `ProjectDocuments:MaxSizeMb` sets the attachment size limit.
- The upload root comes from `IUploadRootProvider`.

## Exports

None.
