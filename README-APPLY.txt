PRISM Procurement Journey — Final Operational Refinement
Date: 30 Jul 2026

APPLICATION
1. Copy the folders in this package into the ProjectManagement project root.
2. Preserve the relative folder structure and replace the four existing files.
3. Run:
     dotnet build
     dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
4. Hard-refresh /Process once with Ctrl+F5.

NO DATABASE MIGRATION IS REQUIRED.

IMPLEMENTED
- Removes the synthetic “Capability complete” endpoint.
- Keeps Payment as the final mandatory procurement stage.
- Shows Transfer of Technology as one simple optional continuation from Payment, with no return loop.
- Softens the light-theme vignette and active-stage halo for prolonged office use.
- Makes second-level contextual stages more legible while retaining progressive focus.
- Uses a compact current-stage locator in normal mode; full identity returns in full-screen mode.
- Renders checklist items as structured content with headings, paragraphs, numbered lists and bullet lists.
- Automatically restructures legacy single-line entries such as:
    Documents required 1. Drawings 2. User manual 3. Technical manual
- Retains plain-text storage and existing APIs; no data conversion or migration is required.
- Adds editing guidance for blank lines, numbered lists and bullet lists.
- Retains safe HTML escaping before formatting.

CHECKLIST AUTHORING EXAMPLE
Documents required

1. Drawings
2. User manual
3. Technical manual
4. Circuit diagrams
5. Parts list
6. MET documents

Bold emphasis may be entered as **important text**.
