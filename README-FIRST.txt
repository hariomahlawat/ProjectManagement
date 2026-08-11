PRISM PHASE 7 BUILD HOTFIX
===========================

This hotfix addresses all three diagnostics reported after installing Phase 7.

1) CS0023 at BrochurePrintCompactComposer.cs line ~139
2) CS0023 at BrochurePrintCompactComposer.cs line ~371

Cause:
QuestPDF's IContainer.Text(Action<TextDescriptor>) overload returns void.
Phase 7 incorrectly chained .FontSize(), .LineHeight() and .FontColor() after the rich-text
callback in two places.

Fix:
The common rich-text style is now applied INSIDE each callback with
text.DefaultTextStyle(...), which matches the QuestPDF API already used elsewhere in PRISM.

No print geometry, page dimensions, text, colours, or brochure logic are changed.

3) CS8622 warning at Pages/ActionTasks/_TaskDetails.cshtml line ~206

Cause:
TaskUpdateTimelineViewModel expects Func<string?, string>, while
Pages/ActionTasks/Index.cshtml.cs exposed ResolveActorName(string).

Fix:
ResolveActorName now accepts string? and handles a missing actor as "System".
This warning is unrelated to Publications, but the hotfix removes it safely.

INSTALL
-------
1. Copy the replacement file:
   Utilities\Reporting\BrochurePrintCompactComposer.cs

2. Copy tools\Apply-PrismPhase7BuildHotfix.ps1 to your project (if not already copied).

3. From the ProjectManagement root run:

   Set-ExecutionPolicy -Scope Process Bypass
   .\tools\Apply-PrismPhase7BuildHotfix.ps1

4. Build and test:

   dotnet build .\ProjectManagement.csproj
   dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

IMPORTANT
---------
The package deliberately does NOT replace the whole Pages\ActionTasks\Index.cshtml.cs file.
That file is unrelated and may contain newer local changes. The script patches only the exact
ResolveActorName method.
