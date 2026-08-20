PRISM ERP — ITO Role Governance + Publications
Ready-to-paste implementation

HOW TO APPLY
1. Close the running application / stop IIS App Pool if required by your deployment process.
2. Copy the contents of this package into the ProjectManagement project root.
3. Preserve the folder structure and overwrite the listed files.
4. Rebuild and restart the application.
5. The new EF Core migration will provision the canonical ITO role automatically when normal startup migrations run.

NO MANUAL DATABASE EDIT IS REQUIRED when your normal PRISM migration-on-startup path is enabled.

WHAT THIS IMPLEMENTS
- Defines the institutional role catalogue as 11 canonical assignable roles:
  Admin, Comdt, HoD, Project Officer, Project Office, MCO, TA, ITO,
  Main_Office_Clerk, MC_Cell_Clerk and IT_Cell_Clerk.
- Keeps ProjectOffice and Main Office as compatibility aliases only; they are not offered for new assignments.
- Adds an idempotent migration that ensures every canonical role, including ITO, exists in AspNetRoles.
- Makes ITO a shared-publication manager alongside Commandant and HoD for:
  * Brochure saved/shared configurations
  * Compendium saved/shared configurations
  * Compendium Cover Editor
  * Compendium Structure Editor
  * rename / duplicate / retire lifecycle handled by the preset services
- Does NOT give ITO unrelated command, project-master-data, Admin, approval, briefing-deck or conference privileges.
- Keeps unknown extension roles already assigned to a user when an administrator edits unrelated user details.
- Converts known legacy aliases to their canonical role on the next Admin save.
- Fixes Main Office Clerk Training Tracker visibility while retaining legacy Main Office compatibility.
- Makes Training notifications use the authoritative Training approver-role contract.
- Removes stale Industry Directory view-role metadata; actual view policy remains authenticated-user access.
- Replaces key raw role strings with RoleNames constants.
- Adds role-governance and Publications authorization regression tests.

IMPORTANT COMPENDIUM NOTE
The Compendium Index.cshtml.cs in this package is based on the Phase 39 generation-reliability version.
It preserves the PDF-generation fixes already applied. The Phase 39.1 Focus Review CSS is not replaced by this package.

POST-PASTE VALIDATION
Run from the project root:

  dotnet build .\ProjectManagement.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

Recommended functional smoke tests:
1. Start PRISM and open Admin > Users > Edit user. Confirm ITO is shown as an assignable role.
2. Assign only ITO to a test user and save.
3. Sign in as that user.
4. Confirm Publications > Brochure can create/save/update/rename/duplicate shared brochure configurations.
5. Confirm Publications > Compendium can create/save/update shared Compendiums and save Cover/Structure changes.
6. Confirm ITO cannot edit underlying project master data merely because of the ITO role.
7. Confirm ITO does not receive Commandant/HoD-only briefing-deck, conference or administrative privileges.
8. Confirm a Main Office Clerk can view the Training Tracker as intended.

VALIDATION PERFORMED IN THIS ENVIRONMENT
- All 23 package source/test files are present.
- No merge-conflict markers detected.
- Static role-governance contract checks passed.
- .NET SDK is not installed in this execution environment, so dotnet build/test could not be run here.
