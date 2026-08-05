# PRISM Role & Charter HeaderAccentSoft build fix

## Problem fixed

`ProjectBriefingSlideComposer.RoleCharter.cs` referenced `canvas.Theme.HeaderAccentSoft`, but `ProjectBriefingThemeDefinition` has no such member. This caused CS1061 during build.

## Correction

The Role panel now resolves its background from existing, supported theme tokens:

- Graphite Dark: `SurfaceRaised`
- Editorial Light: `CriticalSoft` (the existing restrained soft-maroon surface)

This keeps the slide visually consistent without expanding the theme constructor or breaking existing theme definitions.

## Replace these files

1. `Services/ProjectBriefings/Presentation/ProjectBriefingSlideComposer.RoleCharter.cs`
2. `ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs`

## After replacement

1. Stop the running application.
2. Clean Solution.
3. Rebuild Solution.
4. Run the `ProjectBriefings` tests.
5. Start the application and regenerate a Role & Charter deck.

No database migration is required.
