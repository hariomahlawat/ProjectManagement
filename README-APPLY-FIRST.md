# Project Briefing Deck — Settings Density Refinement

## Apply

Copy the five project-relative replacement files into the project root, preserving their folders. Review `REPLACEMENT-MANIFEST.txt` before copying.

Alternatively, apply `IMPLEMENTATION.patch` from the project root:

```bash
git apply IMPLEMENTATION.patch
```

## What changes

- Standard Briefing `Deck format` and `Project content` choices use three equal columns in the settings drawer.
- Editorial Light and Graphite Dark are compared side by side.
- Header-branding choices use one compact three-column row.
- Project Update Sheets labels the section `Header branding`; Standard Briefing labels it `Appearance`.
- Standard Briefing opens only `Content and layout` by default; secondary sections remain collapsed unless restored from the current session.
- `Handling / classification marking` is shortened to `Classification marking (optional)`.
- Mobile/narrow drawers continue to stack all choices into one column.
- Existing template-specific settings, dirty-state protection, drawer state memory, project ordering, readiness logic, and PowerPoint generation are preserved.

## Local verification

```bash
node --check wwwroot/js/pages/project-briefing-decks.js
node --test wwwroot/js/projects/project-briefing-decks.test.js
dotnet build -c Release
dotnet test -c Release --no-build
```

The package was produced without database changes.
