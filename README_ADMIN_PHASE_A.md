# PRISM Admin Module — Phase A Safety Hardening

## Package basis

Apply this package to the current PRISM source after Conference Phase 3.7. Copy the contents of the archive into the project root and preserve all relative paths.

## Scope implemented

### User and identity safety

- User creation is now all-or-nothing across the Identity account, role assignment, security stamp and audit entry.
- User profile and role changes are performed through one atomic service operation.
- Last-active-Admin protection is evaluated inside serializable transactions for role removal, disable and deletion paths.
- Identity operation results are checked before success is recorded.
- Undoing a deletion request restores the exact pre-deletion disabled, lockout and failed-access state.
- New and reset password fields are no longer prefilled with a predictable password.
- Password guidance and secure generation now follow the configured ASP.NET Core Identity policy, including required unique characters.
- Bootstrap administrator provisioning no longer contains a built-in password and compensates if role/security provisioning fails.

### Authentication analytics and audit review

- `AuthEvents/LoginSucceeded` is the canonical source for successful-login analytics.
- Dashboard and analytics date boundaries are calculated using IST days rather than UTC dates labelled as IST.
- Login-scatter drill-down now opens Logs with the correct login name and local calendar date.
- Analytics lookback is restricted to 1–365 days server-side.
- Audit-log date filters use inclusive IST start and exclusive IST end boundaries.

### Administrative data integrity

- Technical Categories cannot be moved below any descendant, preventing hierarchy cycles.
- Project and document permanent deletion now use a filesystem quarantine:
  1. begin the database transaction;
  2. move assets into quarantine;
  3. perform database deletion and audit;
  4. restore assets if the database operation fails;
  5. finalise physical deletion only after commit.
- Cleanup failures are logged with quarantine references for controlled follow-up.
- Unexpected Document Recycle failures are logged with trace references instead of exposing infrastructure details.

### Export safety

- Admin CSV exports now consistently quote fields, include a UTF-8 BOM and neutralise spreadsheet-formula prefixes.

## Database migration

This package adds:

`20261201180000_AdminPhaseASafetyHardening`

The migration adds nullable column:

`AspNetUsers.DeletionPreviousStateJson varchar(2000)`

The application’s existing startup migrator should apply the migration automatically at first startup after deployment. Manual application, where required:

```powershell
dotnet ef database update --context ApplicationDbContext
```

Verify that `20261201180000_AdminPhaseASafetyHardening` appears in `__EFMigrationsHistory`.

## Bootstrap administrator secret

Existing deployments that already contain the configured administrator are unaffected.

For a genuinely fresh database with no administrator, set a one-time secret before first startup:

```powershell
$env:PRISM_BOOTSTRAP_ADMIN_PASSWORD = "<strong-one-time-password>"
```

Optional username configuration:

```json
{
  "Security": {
    "BootstrapAdminUserName": "admin"
  }
}
```

After the administrator has been created successfully:

1. Sign in and change the password when prompted.
2. Remove `PRISM_BOOTSTRAP_ADMIN_PASSWORD` from the server environment or deployment secret store.
3. Do not place the bootstrap password in source control or normal appsettings files.

Development test users are created only when explicit `DevelopmentSeedUsers:*:Password` values are configured.

## Deployment sequence

1. Back up the production database and the configured upload root.
2. Stop the IIS application pool/site.
3. Copy these files into the project source, preserving relative paths.
4. Restore, build and test:

```powershell
dotnet restore
dotnet build ProjectManagement.sln -c Release
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj -c Release --no-build
npm ci
npm test
```

5. Publish and deploy through the normal production procedure.
6. Confirm that the application identity has create/move/delete permissions under the upload root. The service creates `.purge-quarantine` there only during permanent-deletion operations.
7. Start the site and verify successful migration application in startup logs.

## Recommended verification

- Create a user with multiple roles and confirm first-login password change is required.
- Attempt to submit an unknown/tampered role and confirm no partial user is retained.
- Edit a user’s name, rank and roles together.
- Request deletion of an active user, undo it, and confirm the user returns to Active.
- Request deletion of a previously disabled user, undo it, and confirm the user remains Disabled.
- Confirm the last active Admin cannot be demoted, disabled or deleted.
- Verify Login Analytics for records around midnight IST and click a scatter point into Logs.
- Attempt to move a Technical Category beneath its descendant.
- Permanently delete a test document/project with assets and confirm the database and filesystem are both cleaned.
- Open exported Users, Logs and Login Analytics CSV files and verify values beginning with `=`, `+`, `-` or `@` are treated as text.

## Rollback caution

Do not remove the new database column while any user has `PendingDeletion = true`, because the saved pre-deletion state is required for a correct undo. Restore the prior application and database backup together if a full rollback is necessary.

## Validation performed in the packaging environment

- Git whitespace/error check passed.
- Migration manifest ordering, uniqueness and tail identifier checks passed.
- JavaScript syntax checks passed for all modified scripts.
- Secure password generator checks passed for length, character classes and configured unique-character count.
- Modified Razor files passed scoped inline-script/event/style guard checks.
- Unified patch clean-application check passed against the Phase 3.7 baseline.
- ZIP integrity and SHA-256 manifest verification passed.

The .NET SDK and frontend dependency directory were not available in the packaging environment, so the Release build, xUnit suite and complete npm suite must be run in the development/CI environment using the commands above.
