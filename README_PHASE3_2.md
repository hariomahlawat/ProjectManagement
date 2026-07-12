# PRISM Officer Conference — Phase 3.2 Final Polish

Apply this package **after Phase 3.1 Stabilisation** by copying its contents into the
`ProjectManagement` project root and replacing files when prompted.

## Implemented

- Preserves multiline and structured Conference directions across Projects, Ideas and Action Tasks.
- Introduces one shared, tested direction-text formatter instead of duplicated whitespace-flattening logic.
- Prevents Project remark authors from receiving notifications for their own remarks, including self-mentions.
- Orders Project Idea Discussion entries newest first.
- Removes the Project Idea Discussion horizontal overflow defect.
- Corrects officer workload count singular/plural wording and empty-section wording.
- Shows direction time as well as date.
- Removes duplicate page-level success feedback; row-local feedback remains.
- Adds accessible More/Less controls only when a direction exceeds the compact four-line display.
- Preserves keyboard, anti-forgery, server validation and native single-source remark behaviour.

## Database

No migration or database change is required.

## Validation after replacement

```powershell
dotnet clean
dotnet build -c Release
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj -c Release --no-build
node --check .\wwwroot\js\pages\officer-conference.js
node --test .\wwwroot\js\pages\officer-conference.test.js
```

## Functional checks

1. Add a two-line Project Conference direction and verify both lines remain separate on the Conference and Project pages.
2. Verify the issuing HoD/Comdt does not receive a notification for their own Project direction.
3. Add an Idea direction and verify it appears first in Discussion without a horizontal scrollbar.
4. Add a long direction and verify More/Less appears; a short direction must not show it.
5. Add an Action Task direction and verify its multiline formatting and native Updates-stream visibility.
