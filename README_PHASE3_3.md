# PRISM Conference Phase 3.3 — Operational Progress Alignment

Apply this package **after Phase 3.2 Final Polish**.

## Purpose

This phase aligns the conference page with the actual conduct of the officer workload conference:

- The first column remains the authoritative current position of the Project, Idea or Task.
- The middle column presents only the **Directions from last conference**.
- The progress column presents the responsible functionary's actual response after that direction.

## Implemented changes

### Project progress

- Removed stage movement and generic subsequent-remark counts from the conference progress column.
- Shows the latest remark entered after the direction by the **currently assigned Project Officer**.
- A remark from another officer carrying the Project Officer role is not treated as the assigned officer's response.
- When the assigned Project Officer has not entered a remark, the page states:
  `No remark by the Project Officer after the direction.`
- Shows the latest MCO remark after the direction when one exists.
- No empty MCO placeholder is rendered.

### Idea progress

- Removed status movement and generic comment counts from the conference progress column.
- Shows the latest non-Conference comment after the direction.
- Shows the latest note created or updated after the direction.
- When neither exists, the page states:
  `No comment or note after the direction.`

### Direction presentation

- Replaced the generic Conference label with **Directions from last conference**.
- Uses the official-instruction `bi-file-earmark-check` icon.
- Removes author, appointment and stage/status snapshot from this compact view.
- Retains only a subdued timestamp.
- Uses a restrained maroon instruction accent and a slightly stronger direction text treatment.
- Makes the progress column wider than the direction column.

### Interaction and accessibility

- Inline saves immediately reset the progress area using the correct Project or Idea empty state.
- Long progress remarks and notes have accessible More/Less controls.
- Dynamic content continues to use `textContent`; no user-entered HTML is injected.
- Existing keyboard, anti-forgery, focus restoration and trace-reference handling are retained.

## Files

The archive preserves project-relative paths. Copy the contents into the project root and allow the listed files to be replaced.

## Database

No migration is required.

## Recommended validation

```powershell
dotnet clean
dotnet build -c Release
dotnet test -c Release
node --check .\wwwroot\js\pages\officer-conference.js
node --test .\wwwroot\js\pages\officer-conference.test.js
```

## Functional checks

1. Add a Conference direction against a Project.
2. Confirm that the progress column shows the Project Officer empty-response message.
3. Add a Project remark as the assigned Project Officer and verify that its full latest text appears.
4. Add a newer Project Officer remark and verify that it replaces the previous response.
5. Add an MCO remark and verify that a separate MCO response block appears.
6. Open an Idea with a Conference direction and add a General comment.
7. Add or update an Idea note and verify that the latest comment and latest note both appear.
8. Confirm that direction author, role and stage/status snapshot remain available in the source module but are not shown in the compact conference view.
