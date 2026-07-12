# PRISM Media Availability Reconciliation

This release introduces an explicit source-availability lifecycle, historical dead-letter reconciliation, automatic startup backfill, safe restoration checks, and consistent Photos filtering.

## Deployment

1. Copy the package into the ProjectManagement root.
2. Run `dotnet ef database update --context MediaLibraryDbContext`.
3. Build and restart PRISM.
4. Open `/Admin/MediaSources` and select **Reconcile availability** if historical items remain.

No original media file is modified or deleted.
