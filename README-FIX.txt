PRISM Authentication / Audit Failure Hardening

Observed failure
- Login credentials succeed, then Login.cshtml.cs calls IAuditService.LogAsync.
- AuditService adds an AuditLog and SaveChangesAsync throws a DbUpdateException.
- Because that exception is not isolated, the login request returns HTTP 500.

This focused fix does two things:
1. AuditService detaches the failed AuditLog from the scoped DbContext before rethrowing. This prevents a caller that intentionally recovers from the audit failure from retrying the same broken Added entity on its next SaveChanges.
2. Login persists the authoritative AuthEvent before the secondary AuditLog and treats AuditLogs persistence failure as non-fatal for authentication. The exact provider exception is still written to the application log.

No database migration is included. This is deliberate: the screenshot does not show the innermost PostgreSQL exception, so changing database schema/sequence without that evidence would be unsafe.

Replace:
- Services/AuditService.cs
- Areas/Identity/Pages/Account/Login.cshtml.cs

Then clean/build and retry login. If AuditLogs itself is still unhealthy, login will succeed and the application/debug log will contain the complete provider exception needed for the database-level repair.
