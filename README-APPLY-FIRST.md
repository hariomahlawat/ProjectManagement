# PRISM Conference Completion Carryover

This package contains exact replacement files for the Conference Completion Carryover phase.

## Apply

1. Back up the current application source.
2. Copy the folders and files from this package into the project root, preserving paths.
3. Replace existing files when prompted.
4. Rebuild the solution and run the test suite.
5. Restart the application/IIS site after deployment.

Alternatively, apply `IMPLEMENTATION.patch` from the project root with Git.

## Configuration

The carryover period is configured in both appsettings files:

```json
"Conference": {
  "CompletedProjectRetentionDays": 90
}
```

The accepted range is 1–730 days. Startup validation rejects an invalid value.

## Behaviour

- Active workload counts remain unchanged.
- Recently completed projects remain in Conference for the configured carryover period.
- Active and recently completed projects are shown as separate groups.
- Officers with only recently completed projects remain selectable in Conference.
- Direction history and further directions remain available during carryover.
- Completion display respects exact date, month/year, year-only and unknown precision.
- A direction issued after completion records the semantic snapshot `Completed`.
- No database migration is required.
