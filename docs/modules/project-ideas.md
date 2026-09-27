# Project Ideas (Governed Idea Lifecycle)

## Purpose

An organisation-visible register of proposed project ideas. Each idea has an assigned Project Officer and HoD, a status lifecycle, two kinds of typed discussion (General and Conference remarks), notes and documents. Deletion is a soft delete with a recovery workspace.

## Routes

| Route | Page model | Main handlers |
|---|---|---|
| `/ProjectIdeas` | `Pages/ProjectIdeas/Index.cshtml.cs` | board with filters and sorts (`Models/ProjectIdeas/ProjectIdeaBoardFilters`, `ProjectIdeaSorts`) |
| `/ProjectIdeas/Create` | `Create.cshtml.cs` | `CanCreateIdea`, otherwise `Forbid` |
| `/ProjectIdeas/Edit/{id}` | `Edit.cshtml.cs` | status choices limited to Active / On Hold |
| `/ProjectIdeas/Details/{id}` | `Details.cshtml.cs` | `Comment`, `EditComment`, `DeleteComment`, `Note`, `Archive`, `Restore`, `Delete`, `Upload`, `DeleteDocument`, `Preview`, `Download` |
| `/ProjectIdeas/Deleted` | `Deleted.cshtml.cs` | recovery list, `Restore` |

Every page has `[Authorize]`. For `/ProjectIdeas/Details?handler=Preview`, `Program.cs` relaxes three security headers so that inline PDF previews render:

- COOP is set to `unsafe-none`
- CORP is set to `cross-origin`
- the CSP is reduced to `frame-ancestors 'self'; object-src 'none'`

## Governance rules

The rules live in `Services/ProjectIdeas/ProjectIdeaGovernancePolicy` and `ProjectIdeaPermissionService`. The command service enforces them again on the server side.

| Action | Who |
|---|---|
| View non-deleted ideas | any authenticated user |
| Create | Comdt, HoD, Admin |
| Edit idea record (title, description, status Active/OnHold, assignment) | assigned Project Officer, any HoD, Comdt. **Admin alone is not enough.** Checked against the *persisted* assignment, not posted values (`ProjectIdeaCommandService.UpdateAsync`) |
| Archive / restore / soft-delete / restore-deleted / view Deleted | Comdt, HoD, Admin (`CanManageIdeaLifecycle`) |
| Add General comment | any authenticated user, on a writable idea |
| Add Conference comment | `Policies.ConferenceRemarks.ManageAllowedRoles` (Comdt, HoD). Comdt defaults to the Conference type |
| Edit/delete a General comment | Comdt, HoD or Admin (override); otherwise the author within **3 hours** (`AuthorMutationWindow`) |
| Edit/delete a Conference comment | Comdt, HoD |
| Add note / upload document | lifecycle authority or assigned PO |
| Delete document | lifecycle authority, assigned PO, or the uploader |

"Writable" means not deleted and not `Archived`. Archived ideas are read-only until they are restored.

## Lifecycle

- Statuses (`Models/ProjectIdeas/ProjectIdeaStatuses`): `Active`, `OnHold`, `Archived`.
- `ArchiveAsync` sets `Archived`, `ArchivedAt` and `ArchiveReason`. The page requires a reason of at most 1,000 characters.
- `RestoreAsync` sets the status back to `Active` and clears the reason.
- `SoftDeleteIdeaAsync` requires a reason of at most 1,000 characters. It sets `IsDeleted`, `DeletedAt`/`By` and `DeleteReason`, and is audited. `RestoreDeletedIdeaAsync` reverses it and audits the previous reason.
- Every mutation uses optimistic concurrency (`RowVersion`, `SaveWithFriendlyConcurrencyAsync`).

## Main types

- `ProjectIdeaCommandService` (also exposed as `IProjectIdeaCommandService` for other modules: `CreateAsync`, `AddConferenceCommentAsync`). The Conference workspace (`Services/Workspace/ConferenceIdeaCommandService`, `Services/ConferenceRemarks/ConferenceRemarkCommandService`) creates ideas and conference remarks through this contract.
- `ProjectIdeaReadService`, `ProjectIdeaPermissionService`, `ProjectIdeaDocumentService`.

## Data entities (`Models/ProjectIdeas`)

- `ProjectIdea`: Title (200), Description (2000), Status, assigned PO/HoD, archive and soft-delete fields, RowVersion.
- `ProjectIdeaComment`: `CommentType` (`General` | `Conference`), `CreatedByRole`, `StatusSnapshot`, edit and soft-delete fields.
- `ProjectIdeaNote`: `IsPinned`, soft delete.
- `ProjectIdeaDocument`: stored at `<UploadRoot>/ProjectIdeas/{id}/Documents/{stored}`. Soft-deleted only; the file is kept on disk.

## Documents

`ProjectIdeaDocumentService.UploadAsync`:

1. Check the extension and content-type allow-lists and the size limit (20 MB, `MaxFileSizeBytes`).
2. Copy the upload to `Path.GetTempFileName()` and run `IFileSecurityValidator.IsSafeAsync` on it.
3. Move the temp file into the upload root, then insert the database row.

On any exception the temp file and the target file are deleted, and a generic error message is returned.

## Configuration and exports

No dedicated configuration keys and no exports.
