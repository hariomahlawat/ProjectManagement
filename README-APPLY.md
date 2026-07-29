# PRISM Admin Role Access Refinement

This incremental update is intended for the current PRISM source after the Industry Directory owner-edit permission update.

## What it changes

- Replaces verbose role cards with compact role-name selectors.
- Adds a live **Access granted by selected roles** panel grouped by PRISM module.
- Shows the combined effective capability set when multiple roles are selected.
- States project-assignment and organisation-ownership restrictions explicitly.
- Keeps the privileged-role warning without placing generic labels or descriptions inside role cards.
- Displays the role `TA` exactly as **TA**; it is no longer expanded to “Technical Assistant”.
- Centralises existing Project Create, Checklist, Document Repository and Action Tracker role collections so the access reference and registered policies use the same role lists.
- Exposes existing Project Office Reports role collections for the same purpose.

## Apply

Copy all folders and files from this package into the `ProjectManagement` project root, preserving the folder structure and replacing existing files when prompted.

No database migration is required.

## Verify

```bash
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
```

The project build also requires the existing Notebook frontend dependencies described by the project file; run `npm ci` first where those dependencies are not already installed.

## Behaviour

- Role cards contain the role name and selection state only; generic category and descriptive text have been removed.
- The access panel updates immediately when a role is selected or removed.
- Access is grouped under Common access, Projects, Command and coordination, Project Office reports, Industry Directory, Documents, Calendar and activities, and Administration.
- The panel is an operational reference. Project assignment, record ownership and approval state may further restrict a listed action.
