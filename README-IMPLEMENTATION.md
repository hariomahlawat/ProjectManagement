# Conference Review sticky-header jitter fix

## Replace these files

Copy the contents of this package into the project root and replace the matching files:

- `wwwroot/css/officer-conference.css`
- `wwwroot/js/pages/officer-conference.js`
- `wwwroot/js/pages/officer-conference.test.js`

## Implemented

- Keeps the officer conference toolbar at one invariant height and padding in both normal and sticky states.
- Removes sticky-state typography, avatar and visibility changes that altered layout at the release threshold.
- Restricts the sticky visual transition to non-layout properties: border, radius and shadow.
- Adds separate enter and release thresholds (hysteresis) to prevent one-pixel/fractional-pixel oscillation.
- Avoids applying a stale sticky state when the responsive layout changes the toolbar to `position: static`.
- Avoids redundant CSS custom-property writes during scroll and resize processing.
- Adds an explicit window-resize synchronisation path in addition to `ResizeObserver`.
- Adds regression tests for hysteresis and geometry invariance.

## Deployment

Stop the running application before replacing static files, then run:

```powershell
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
npm test
dotnet build .\ProjectManagement.csproj
```

Reload the Conference Review page with `Ctrl+F5`.

No database migration, package change, service registration or Razor change is required.
