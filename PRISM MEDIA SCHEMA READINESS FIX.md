# PRISM Media Schema Readiness Fix

This patch corrects migration discovery and prevents false-positive schema readiness.

## Corrections

- Adds `DbContext` and `Migration` metadata to `AddMediaAvailabilityState`.
- Verifies pending migrations, migration history, required physical columns, and a representative EF query.
- Prevents a green success message unless post-migration validation succeeds.
- Replaces raw PostgreSQL errors in the administration UI with safe diagnostic messages.
- Gates media processing and historical availability reconciliation until the catalogue schema is genuinely current.
- Uses the existing PostgreSQL advisory lock to keep migration execution safe across multiple application instances.

## Required migration

`20260628103000_AddMediaAvailabilityState`

The migration adds:

- AvailabilityStatus
- UnavailableReason
- UnavailableSinceUtc
- LastAvailabilityCheckUtc

No existing catalogue data is deleted or reset.
