# Industry partners and Joint Development Partners (JDP)

The Industry Partners directory (`/IndustryPartners`) records organisations, their contacts and
optional company files. An organisation becomes a project's Joint Development Partner (JDP)
through a project link (`IndustryPartnerProjects`); a project may have several JDPs and an
organisation may support several projects.

Code: `Services/IndustryPartners/`, `Pages/IndustryPartners/`, the JDP handlers in
`Pages/Projects/Overview.MultiJdp.cs` and `Overview.cshtml.cs`, and
`Controllers/IndustryPartnersProjectLookupController.cs`.

## Who may do what

| Action | Rule |
| --- | --- |
| View the directory, add a contact | Any signed-in user |
| Create an organisation | `IndustryPartners.Create` roles (see `Policies.IndustryPartners`) |
| Edit an organisation, upload or remove its files | Its creator, or Admin / HoD / Comdt |
| Edit or remove a contact | Its author, or Admin / HoD / Comdt |
| Delete an organisation | Admin / HoD, and only once it has no project links |
| **Add or remove a project's JDP link** | **Admin / HoD / Comdt for any project, or a Project Officer for projects they lead** |

The JDP link rule lives in `ProjectJdpAccess` and is enforced by `IndustryPartnerService` for every
link change, whether it starts on the project Overview page or in the directory (including
"create organisation and link it to this project"). In the directory both the organisation rule
and the project rule must pass. The directory's project picker lists only projects the user may
link. Links to soft-deleted projects can still be removed by the same people, so an organisation
can be cleaned up before it is deleted.

## JDP status and counts

`JdpStatusRules` is the single source for a linked project's status (Ongoing, Completed,
Cancelled, Archived, Deleted project), its display order, and the "Also linked to N other
projects" summary; every directory, Overview and lookup view uses it.

An organisation is **Current JDP** when it has a link to an ongoing project, **Past JDP** when
its live links are all completed, cancelled or archived, and **Contact only** otherwise. Links to
soft-deleted projects are not counted anywhere (directory counts, filters, status, duplicate
suggestions). They still appear in the organisation's JDP history, labelled "Deleted project", so
they can be removed, and they still block permanent deletion until removed. The Current and Past
filters overlap on purpose: an organisation with both kinds of link appears in both.

Duplicate suggestions compare every organisation's name with company suffixes, "M/s" and
punctuation removed.

## Company files

Allowed types are PDF, Word (`.doc`, `.docx`), PowerPoint (`.ppt`, `.pptx`), JPEG and PNG, up to
`ProjectDocuments:MaxSizeMb`. The declared type must match the extension, and the file contents
must match the type (PDF header, OLE header for `.doc`/`.ppt`, an Office Open XML package with the
right main part for `.docx`/`.pptx`, JPEG/PNG signatures). File names are limited to 260
characters and only the file name (never a client path) is kept. Stored files are removed if
their database record cannot be saved; a file that cannot be removed after its record is deleted
is logged rather than reported as a failure.

## Concurrent changes

Adding or removing a contact or a project link stamps the organisation's "last updated" fields.
If another user changed the same organisation meanwhile, the stamp is re-applied and the save
retried, so these actions do not fail because of unrelated concurrent edits. Editing the
organisation's own details or a contact still uses the record version and reports a conflict.
