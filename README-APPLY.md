# PRISM Photos — Person Discovery & PRISM User Linkage

## Apply

Copy the contents of this package into the PRISM project root and overwrite matching files:

```text
E:\Dot Net Web Development\ProjectManagement\
```

This package is a **delta**, not a replacement project. Apply it over the current codebase that already contains the preceding Photos / People phases.

## Database migration

This phase adds one immutable Media Library migration:

```text
20260819190000_LinkMediaPeopleToPrismUsers
```

The migration creates the explicit, audited one-to-one PRISM-user ↔ Media-Person link table. Deploy the complete migration assembly and manifest together.

PRISM's startup migration boundary is governed by:

```text
Database:ApplyMigrationsOnStartup
```

- If enabled in the deployed environment, the normal startup gate applies the new migration before Photos becomes available.
- If disabled, apply the migration through your normal controlled deployment procedure before opening Photos.

Do not manually create the table while leaving the migration history/manifest behind.

## Build / test after paste

```powershell
dotnet clean

Remove-Item .\bin, .\obj `
    -Recurse -Force `
    -ErrorAction SilentlyContinue

dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\photos-person-profile.js
node --test .\wwwroot\js\pages\photos-person-profile-contract.test.js
```

Then restart PRISM.

## Functional verification

1. Open **Photos → People → Manage identity** as an identity manager.
2. Search for an existing active human PRISM user and explicitly link the correct Media Person.
3. Confirm the same PRISM user cannot be linked to another active Media Person and vice versa.
4. Sign in as the linked user. Verify:
   - the user menu exposes **My Photos**;
   - the Media Person portrait is used as the avatar fallback when a representative portrait is available;
   - **Profile** shows the linked Photos identity.
5. Open **My Photos** and verify it reuses the normal single-person Photo Profile.
6. Open **Find more photos**. Verify direct Strong / Moderate suggestions plus relevant identity-group evidence are shown, with weaker direct suggestions collapsed.
7. Verify **nothing is preselected**. Explicit group **Select all** must be clicked before group-wide confirmation.
8. As the linked user, verify the actions read **Yes, that's me** / **Not me** and that **Manage identity** remains unavailable unless the account also has the existing manager role.
9. Confirm self-confirming an appearance creates a normal human-confirmed appearance with `SelfConfirmed` audit provenance but does **not** make it a trusted matching reference.
10. Unlink the PRISM user with a reason and verify My Photos/avatar/profile enrichment disappears while confirmed Photos identity data remains intact.
11. Verify merging a linked duplicate into an unlinked surviving person transfers the user link; merging two identities linked to different PRISM users is blocked.
