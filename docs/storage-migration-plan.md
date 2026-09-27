# Storage Location and Migration

**Status: Partially implemented.**
- In place: a shared upload root outside the web root in the Development and Production configs, a project document service, and authenticated file delivery.
- Not built: the proposed `IFileStore` abstraction and per-project quotas.
- Still open: the framework default for the upload root still points under `wwwroot`.

## How the upload root is resolved (current code)

1. `ProjectPhotoOptionsSetup` (`Services/Projects/ProjectPhotoOptionsSetup.cs`) runs after configuration binding. If `ProjectPhotos:StorageRoot` is empty, it sets it to `{WebRootPath}/uploads`, falling back to `{ContentRootPath}/uploads` and then `AppContext.BaseDirectory/uploads`.
2. `UploadRootProvider` (`Services/Storage/UploadRootProvider.cs`) resolves the root in this order:
   1. the `PM_UPLOAD_ROOT` environment variable;
   2. `ProjectPhotos:StorageRoot`;
   3. `/var/pm/uploads`, only if the storage root is still empty. Step 1 means this fallback is effectively unreachable.

   The value is passed through environment-variable and `~` expansion and turned into a full path, and the directory is created.
3. If the directory cannot be created, the provider logs a warning and falls back to `%LOCALAPPDATA%/ProjectManagement/uploads` on Windows, or `{ContentRootPath}/uploads` elsewhere.
4. Sub-folders come from `ProjectDocuments` options (`Configuration/ProjectDocumentOptions.cs`):

   | Option | Default |
   | --- | --- |
   | `ProjectsSubpath` | `projects` |
   | `PhotosSubpath` | `""` (empty) |
   | `StorageSubPath` | `docs` |
   | `CommentsSubpath` | `comments` |
   | `VideosSubpath` | `videos` |
   | `TempSubPath` | `temp` |

   These produce paths such as `{root}/projects/{id}/docs/...`. Social-media photos use `GetSocialMediaRoot(prefix, eventId)`.

Configured roots in the repository:

| File | `ProjectPhotos:StorageRoot` | `DocRepo:RootPath` |
| --- | --- | --- |
| `appsettings.json` | not set, so the default is `wwwroot/uploads` | `App_Data/DocRepo` |
| `appsettings.Development.json` | `D:/ProjectManagementData/uploads` | `D:/ProjectManagementData/DocRepo` |
| `appsettings.Production.json` | `F:/ProjectManagementData/uploads` | `F:/ProjectManagementData/DocRepo` |

The document repository has its own root (`DocRepo:RootPath`, resolved against the content root by `Services/DocRepo/LocalDocStorageService.cs`). It does not use `IUploadRootProvider`.

## What was implemented from the original plan

- **Document service**: `Services/Documents/DocumentService.cs` stores project documents under `projects/{projectId}/docs/stages/{stageId|general}/` with per-upload GUID names. It uses temporary keys for pending requests and supports publish, overwrite, soft delete, restore, hard delete and streaming.
- **Authenticated delivery**: files are served through authorised handlers and the token-based `/files` gateway. The upload root is not exposed through `UseStaticFiles` unless it has fallen back into `wwwroot`. See [storage-hardening.md](storage-hardening.md).
- **Project purge** moves `{root}/{ProjectsSubpath}/{projectId}` into quarantine and then deletes it (`FileSystemQuarantine`).

## Not implemented

- There is no `IFileStore`/`LocalFileStore` abstraction. Each module writes to the file system directly, using `IUploadRootProvider` and `IUploadPathResolver`.
- There are no per-project quotas.
- `ProjectPhotoOptionsSetup` has not been changed to default outside the web root.
- The repository still contains sample files under `wwwroot/uploads/projects/`.

## Operational guidance

- Set `PM_UPLOAD_ROOT`, or `ProjectPhotos:StorageRoot`, in every environment to a writable, non-web-root path. If you do not, uploads land in `wwwroot/uploads` and are served anonymously as static files.
- Back up the upload root and `DocRepo:RootPath` together with the database.
- When relocating storage, copy the tree and update the configuration. Relative storage keys resolve against the new root. Legacy absolute keys still resolve as-is (`UploadPathResolver.ToAbsolute`), so they must remain valid paths or be rewritten.
