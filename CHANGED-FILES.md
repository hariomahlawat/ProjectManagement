# Changed files

| File | Purpose |
|---|---|
| `Pages/Workspace/BriefingDecks/Index.cshtml` | Manage-individually workflow, selected-project search/filter toolbar, bulk actions, live-update hooks |
| `Pages/Workspace/BriefingDecks/Index.cshtml.cs` | Membership batch endpoint and membership-aware search results |
| `Services/ProjectBriefings/ProjectBriefingContracts.cs` | Membership search and update contracts |
| `Services/ProjectBriefings/ProjectBriefingDeckService.cs` | Atomic add/remove membership command with audit and concurrency protection |
| `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.cs` | Adaptive photo/status geometry, overflow protection, Present Status terminology, bullet normalisation |
| `wwwroot/css/pages/project-briefing-decks.css` | Management toolbar, membership badges, selected filters and responsive behaviour |
| `wwwroot/js/pages/project-briefing-decks.js` | AJAX membership updates, filtering, batch management, live metrics and context preservation |
| `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingContractTests.cs` | UX and adaptive-layout contract checks |
| `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs` | Updated Present Status slide assertions |
