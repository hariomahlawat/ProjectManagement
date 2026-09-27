# Storage and File-Delivery Hardening

**Status: Partially implemented.** Signed `/files` tokens, relative storage keys and
per-upload filename entropy are in place. Malware scanning is only a hook: no
`IVirusScanner` implementation exists or is registered, so every scan call is a no-op.

## Signed download URLs and the `/files` gateway

- `IProtectedFileUrlBuilder` (`Services/Storage/IProtectedFileUrlBuilder.cs`) builds `/files?t={token}` links. `CreateDownloadUrl` produces attachment links and `CreateInlineUrl` produces inline links.
- Tokens come from `IFileAccessTokenService` (`Services/Security/FileAccessTokenService.cs`). The service uses Data Protection, with purpose `FileAccessTokenService`, to protect a JSON payload holding the storage key, file name, content type, expiry and, when `FileDownload:BindTokensToUser` is true, the user id.
- `Controllers/FilesController.cs` (`[Authorize]`, route `files`, also `files/{token}`) handles download requests:
  1. It validates the token.
  2. It returns 403 when the token is bound to a different user.
  3. It resolves the key with `IUploadPathResolver.ToAbsolute`.
  4. It streams the file with range support.
- Current users of the builder are activity attachments (`ActivityAttachmentManager`), action-task collaboration, progress review, and the Activities and Photos pages.
- Other modules serve files through their own authorised page handlers or controllers rather than through `/files`. These include project documents, photos and videos, IPR, FFC, ARPP, DocRepo and office-report photos.
- `FileDownload` options (`Configuration/FileDownloadOptions.cs`, the `FileDownload` section of `appsettings.json`):

  | Option | Default |
  | --- | --- |
  | `TokenLifetimeMinutes` | 30 |
  | `BindTokensToUser` | true |

## Static files

`Program.cs` calls `UseStaticFiles` for the web root (`wwwroot`) only; no static-file provider points at the upload root. However, when neither `PM_UPLOAD_ROOT` nor `ProjectPhotos:StorageRoot` is set, `ProjectPhotoOptionsSetup` defaults the upload root to `{WebRoot}/uploads`, and anything stored there is served anonymously as a static file. `appsettings.Development.json` and `appsettings.Production.json` both set `StorageRoot`, but the base `appsettings.json` does not. See [storage-migration-plan.md](storage-migration-plan.md).

## Storage keys

- `IUploadPathResolver` / `UploadPathResolver` (`Services/Storage/IUploadPathResolver.cs`) converts between relative keys and absolute paths under `IUploadRootProvider.RootPath`.
  - `ToAbsolute` accepts rooted (legacy absolute) keys unchanged. For relative keys it rejects results outside the root, using a case-insensitive prefix check.
  - `ToRelative` returns the absolute path unchanged when it lies outside the root.
- `ProjectCommentService` (`SafeToRelative`) and `FfcAttachmentStorage` store relative keys.
- `IprAttachmentStorage` resolves its root from `IprAttachments:StorageRoot` if set, otherwise from `IprAttachments:StorageFolderName` (default `ipr-attachments`) under the upload root. It verifies that paths stay inside that root.
- Module folder options:

  | Option | Default |
  | --- | --- |
  | `FfcAttachments:StorageFolderName` | `ffc` |
  | `ArppAttachments:StorageFolderName` | `arpp` |
  | `ProjectOfficeReports:VisitPhotos:StoragePrefix` | `project-office-reports/visits` |
  | `ProjectOfficeReports:SocialMediaPhotos:StoragePrefix` | `org/social/{eventId}` |

  Project files live under `ProjectDocuments:ProjectsSubpath/{projectId}` (default `projects`).
- DocRepo is separate. `LocalDocStorageService` stores files under `DocRepo:RootPath` (default `App_Data/DocRepo`, relative to the content root) at `yyyy/MM/{guid}.pdf`, and saves that relative path in `Documents.StoragePath`.

## Filename entropy

- `DocumentService.BuildDocumentStorageKey` (`Services/Documents/DocumentService.cs`) builds keys of the form `{ProjectsSubpath}/{projectId}/{StorageSubPath}/stages/{stageId|general}/{documentId}-{guid:N}{ext}`.
- Temporary uploads use `BuildTempStorageKey`.
- DocRepo files use a random GUID file name.

## Malware scanning

- `Application/Security/FileSecurityValidator.cs` is registered as `IFileSecurityValidator`. It takes an optional `IVirusScanner`, and `IsSafeAsync` returns `true` without scanning when no scanner is present.
- `Services/IVirusScanner.cs` defines the interface, but **no implementation exists in the repository and none is registered**.
- `FileSystemActivityAttachmentStorage` writes each upload to a temporary file, calls `IsSafeAsync`, and then moves the file into place.
- `VisitPhotoService` and `SocialMediaEventPhotoService` accept an optional `IVirusScanner?` and scan only when one is supplied.
- To enable scanning, register an `IVirusScanner` implementation in `Program.cs`.

## Checklist for new upload features

1. Write within the upload root, or a configured module root under it.
2. Store relative keys, using `IUploadPathResolver.ToRelative`.
3. Serve files through an authorised endpoint, or through `IProtectedFileUrlBuilder` and `/files`.
4. Call `IFileSecurityValidator.IsSafeAsync`, so scanning takes effect once a scanner is registered.
5. Validate size, MIME type and signature in the feature's own options.
