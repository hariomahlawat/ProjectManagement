# PRISM Project Remarks — Commandant Default Conference Type

This package contains exact replacement files for the project-remarks default-type refinement.

## Apply

1. Back up the current application source.
2. Copy the folders and files from this package into the project root, preserving paths.
3. Replace existing files when prompted.
4. Rebuild the solution and run the test suite.
5. Restart the application/IIS site after deployment.

Alternatively, apply `IMPLEMENTATION.patch` from the project root with Git.

## Behaviour

- When the effective posting role is **Comdt**, the project remark composer opens with **Conference** selected.
- HoD, Admin, Project Officer, MCO and other authorised roles continue to default to **Internal**.
- A multi-role user whose effective role resolves to Commandant also defaults to Conference.
- Users may still manually select Internal, External or Conference according to their permissions.
- **Clear** and successful posting reset the composer to the role-resolved default rather than always returning to Internal.
- The remarks history filter continues to open on **All**; only the composer default changes.
- The server resolves the policy and the client validates it against the available permissions, preventing an unauthorised Conference default.
- No database migration or configuration change is required.
