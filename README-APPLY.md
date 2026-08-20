# PRISM Photos — Avatar / Profile Image Phase Closure

This is the final ready-to-paste closure delta for the Photos identity ↔ PRISM profile-image work. It is designed to be applied **over the current Avatar Stabilisation implementation** already verified in the supplied screenshots.

## What this closes

- Standardises user-facing terminology on **PRISM profile image**. Internal code may still use `Avatar` in type/member names, but the UI and user-visible errors no longer alternate between profile image and avatar.
- Replaces the weak linked-account metadata line with an explicit, compact current-state badge:
  - **Photos portrait in use**
  - **Initials in use**
- The linked-account badge uses `ShouldUsePortraitAsAvatar`, i.e. the resolved presentation state, not merely the stored preference bit.
- Makes **Use initials instead** visibly active rather than disabled-looking, including dark-mode treatment.
- Reduces the secondary current-profile preview from 40 px to 36 px so it reads as a state preview rather than a duplicate identity portrait.
- Tightens the trusted-reference warning to **Choose or prepare a trusted matching reference below.**
- Keeps incorrect-identity reporting authoritative: reporting forces portrait use off; resolving the report never silently restores it.
- Extends regression coverage for manager-view state, representative-portrait changes, concern handling, and unlink fallback.

## Replace / add these files

1. `Areas/Identity/Pages/Account/Manage/Index.cshtml`
2. `Areas/Identity/Pages/Account/Manage/Index.cshtml.cs`
3. `Features/MediaLibrary/Services/MediaPersonUserLinkService.cs`
4. `Pages/Photos/People/Details.cshtml`
5. `wwwroot/css/site.css`
6. `wwwroot/css/pages/photos-reference-readiness.css`
7. `ProjectManagement.Tests/AccountPhotoAvatarContractTests.cs`
8. `ProjectManagement.Tests/MediaLibrary/MediaPersonUserLinkServiceTests.cs`
9. `tools/Test-PrismPhotosAvatarPhaseClosure.ps1` — new validation script

No database migration is required.

## Apply

Copy the package contents into the project root and overwrite matching files.

Then run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\tools\Test-PrismPhotosAvatarPhaseClosure.ps1
```

The script performs source-contract checks, JavaScript syntax/contract tests, a project build, and focused .NET regression tests.

## Final manual acceptance

1. With **Initials in use**, verify the header shows initials.
2. Click **Use Photos portrait**; verify Account Settings and the header immediately show the portrait.
3. Refresh, then sign out/in; verify the selected state persists.
4. On **Photos → People → person details**, verify the linked-account badge reads **Photos portrait in use**.
5. Click **Use initials instead**; verify the header and Account Settings immediately return to initials and Person Details reports **Initials in use**.
6. Re-enable the portrait, then report **This isn't my identity** in test data; verify portrait use is forced off and My Photos/self-review are suspended.
7. Resolve the concern as Admin/HoD; verify the link is restored but portrait use remains off until the user explicitly opts in again.
8. With portrait use enabled, change the person's representative portrait; verify the header resolves the new representative portrait without storing a duplicate profile image.

Once these pass, this phase can be treated as closed.
