# Storage Migration Plan

## Current Behaviour
- `ProjectPhotoOptionsSetup` sets a default uploads root beneath the ASP.NET Core web/content root, falling back to the app base directory when neither is configured. (see Services/Projects/ProjectPhotoOptionsSetup.cs lines 17-36)
- `UploadRootProvider` resolves the final directory by checking the `PM_UPLOAD_ROOT` environment variable first, then the configured `ProjectPhotoOptions.StorageRoot`, and finally the hard-coded `/var/pm/uploads` fallback before ensuring the directory exists. (see Services/Storage/UploadRootProvider.cs lines 9-32)
- `ProjectPhotoService` persists each derivative beneath `projects/{projectId}` using the resolved root, so all features that depend on the provider already share the same tree. (see Services/Projects/ProjectPhotoService.cs lines 495-536)

## Recommended Configuration Strategy
1. **Explicit root selection**
   - Prefer setting the `PM_UPLOAD_ROOT` environment variable in each deployment environment. This guarantees a non-webroot path and stays consistent across future features.
   - Backstop with a `ProjectPhotos:StorageRoot` configuration value for hosting models where setting env vars is difficult; the runtime precedence already honours the environment variable.
2. **Default safety net**
   - Consider updating `ProjectPhotoOptionsSetup` so the framework default also points at `/var/pm/uploads` (or a platform-specific equivalent). This avoids accidental reversion to `wwwroot/uploads` when configuration is missed.
3. **Operational concerns**
   - Provision the chosen directory (local disk, network share, or mounted volume) with sufficient capacity and ensure the application identity has read/write permissions.
   - Mount the path outside the static-file pipeline (`app.UseStaticFiles`) and continue serving media through controller/page actions for authorisation and caching control.

## Extension Path for Document Uploads
1. **Abstraction**
   - Introduce an `IFileStore` abstraction that consumes `IUploadRootProvider`. A `LocalFileStore` can mirror the existing logic and keeps future cloud migrations (S3/Azure) straightforward.
2. **Document service**
   - Create a `DocumentService` that mirrors `ProjectPhotoService` by validating inputs, building deterministic paths (e.g., `projects/{projectId}/docs/...`), and returning metadata for download endpoints.
   - Reuse the existing validation patterns (magic-byte checking, size limits, optional AV scanning) already established in `ProjectPhotoService` to maintain consistent security posture.
3. **Serving documents**
   - Extend existing Razor Pages/controllers to stream documents through authenticated endpoints, reusing the caching headers set by photo downloads.
4. **Lifecycle management**
   - Plan for backup/retention strategies at the storage-root level and introduce per-project quotas if required (DB counters + validation before writing).

## Immediate Action Items
- Set `PM_UPLOAD_ROOT=/var/pm/uploads` (Linux) or the equivalent Windows path in every environment.
- Backfill or migrate existing `wwwroot/uploads` contents into the centralised directory, updating any hard-coded links to reference controller/page endpoints.
- Verify new deployments (CI/CD, container images, etc.) propagate the environment variable and include storage-mount provisioning steps.
