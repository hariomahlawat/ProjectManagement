# Phase 3 Razor hotfix

Replace this file in the project:

`Pages/Workspace/_ConferenceSection.cshtml`

## Cause

The local variable was named `section`. In Razor markup, expressions such as `@section.Kind` were parsed as the reserved Razor `@section` directive, producing RZ1011 and RZ2005.

## Fix

The variable has been renamed to `sectionModel`, and all references in the partial have been updated. No functional or database changes are involved.
