PRISM Brochure Phase 20.1 — Compile / Nullability Hotfix
========================================================

Apply on top of Phase 20.

REPLACE
1. Services/Publications/BrochurePublicationService.cs
2. Pages/ActionTasks/Index.cshtml.cs

FIX 1 — CS1061 in BrochurePublicationService.cs
BrochurePagePlan exposes its collection as Items:
    public sealed record BrochurePagePlan(BrochurePageLayoutKind Layout,
                                          IReadOnlyList<BrochureProjectFragment> Items);

Phase 20 BuildDigitalPhotoPlacements incorrectly referenced page.Fragments.
It now enumerates page.Items. The elements remain BrochureProjectFragment, so no
behavioural or layout change is introduced.

FIX 2 — nullability method-group warning in Pages/ActionTasks/_TaskDetails.cshtml
TaskUpdateTimelineViewModel.ResolveActorName is Func<string?, string>, but the
ActionTasks IndexModel method accepted only string. IndexModel.ResolveActorName now
accepts string? and handles a null/blank actor as System before dictionary lookup.
This aligns the delegate contract and removes the warning without weakening null safety.

NO DATABASE CHANGE
NO NUGET CHANGE
NO NPM CHANGE
NO PDF GEOMETRY / LAYOUT CHANGE
