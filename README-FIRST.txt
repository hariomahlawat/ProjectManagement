PRISM Notebook — Conference Digest New-Tab Links
Date: 09 Aug 2026

PURPOSE
Operational drill-down links inside the read-only PRISM Conference Directions modal now open in a new browser tab so the Notebook remains available as the user's working/reference surface.

PRODUCTION FILE TO REPLACE
Pages/Notebook/_NotebookConferenceDigestModal.cshtml

BEHAVIOUR
- Project / Idea / Task title links open in a new tab.
- PO Conference review links open in a new tab.
- Links use target="_blank" with rel="noopener noreferrer".
- A visually-hidden accessibility cue announces that the link opens in a new tab.
- View all, Close, colour, labels, pin and other Notebook-local actions remain in the current tab/page.

NO CHANGES TO
- database / EF migrations
- services / queries
- authorisation
- Conference direction logic
- Notebook card placement or personalisation
- CSS / JavaScript runtime behaviour

VALIDATION
Run the focused contract test from the ProjectManagement root:
  node --test wwwroot/js/notebook/notebook-conference-digest-new-tab-contract.test.js

Then run your normal:
  dotnet build
  dotnet test
