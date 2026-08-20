# PRISM Photos — Avatar State Stabilisation

This package is based on the supplied `ProjectManagement-master (10)(20260820-054526).zip` source and is ready to paste over that codebase.

## What this phase fixes

- Removes the fragile client-supplied Boolean avatar toggle.
- Adds explicit **Use Photos portrait** and **Use initials instead** server commands.
- Verifies the persisted avatar preference after every change before reporting success.
- Uses one resolved presentation state (`ShouldUsePortraitAsAvatar`) in both Account Settings and the PRISM header.
- Keeps initials as the deterministic fallback whenever the portrait cannot be presented.
- Keeps the user able to clear a previously enabled portrait preference even if the representative portrait later becomes unavailable.
- Compacts Account Settings: Photos identity, avatar choice, roles and password actions now use substantially less vertical space.
- Converts **This isn't my identity** into a deliberate correction workflow with clear consequences before submission.
- Adds regression tests for ON → portrait, OFF → initials, failed-state verification, missing portrait, representative portrait change, concern handling and source-contract integrity.

## Replace / add these files

1. `Areas/Identity/Pages/Account/Manage/Index.cshtml`
2. `Areas/Identity/Pages/Account/Manage/Index.cshtml.cs`
3. `Features/MediaLibrary/Services/MediaPersonUserLinkService.cs`
4. `Pages/Shared/_LoginPartial.cshtml`
5. `wwwroot/css/site.css`
6. `ProjectManagement.Tests/AccountManagePageTests.cs`
7. `ProjectManagement.Tests/MediaLibrary/MediaPersonUserLinkServiceTests.cs`
8. `ProjectManagement.Tests/AccountPhotoAvatarContractTests.cs` — new
9. `tools/Test-PrismPhotosAvatarStabilisation.ps1` — new validation helper

## Database

**No database migration is required.** This phase uses the existing `UsePortraitAsAvatar` persistence and the existing linkage-governance fields.

## Validation

From the project root in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPhotosAvatarStabilisation.ps1
```

The script checks the source contracts, cleans the project, builds it and runs the focused tests.

If you prefer the normal full verification:

```powershell
dotnet clean .\ProjectManagement.csproj
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item .\ProjectManagement.Tests\bin, .\ProjectManagement.Tests\obj -Recurse -Force -ErrorAction SilentlyContinue

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj
```

## Functional acceptance check

1. Link a confirmed Photos person to the PRISM account. The header must remain on initials initially.
2. Open **Profile → Account settings**. The current state must read **Initials in use**.
3. Click **Use Photos portrait**.
4. After redirect, the success message must say the portrait is now being used; the current-avatar preview and header must both show the portrait.
5. Click **Use initials instead**. The preview and header must both return to initials.
6. Refresh the page after each transition. The state must persist.
7. Change the representative Photos portrait while portrait use is enabled. The header must automatically use the new representative portrait because the avatar references the person endpoint, not a duplicated image.
8. Report **This isn't my identity**. Portrait use and My Photos/self-review must be disabled while the concern is open.
9. Resolve the concern as an identity manager. The link may remain, but portrait use must remain OFF until the user explicitly enables it again.

## Important implementation invariant

The PRISM header must never infer avatar state independently. It renders the Photos portrait only when the linked identity reports `ShouldUsePortraitAsAvatar == true`; otherwise it uses initials.
