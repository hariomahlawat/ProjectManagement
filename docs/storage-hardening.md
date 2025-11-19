# Storage and file-delivery hardening

This document records the cross-cutting changes that were made to close the gaps identified in the storage review. It should help future contributors understand how the different pieces fit together and where to hook new features.

## Signed download URLs & `/files` gateway

* All download links are now generated via `IProtectedFileUrlBuilder`. It wraps a short-lived token produced by `IFileAccessTokenService` (Data Protection + `FileDownload` options) and ties it to the current user when `BindTokensToUser` is true.
* `/files/{token}` is handled by `FilesController`. The controller validates the token, re-hydrates the storage key via `IUploadPathResolver`, enforces authorization, and streams the file with range support. Direct static-file exposure of the upload root was removed from `Program.cs`.
* Any feature that needs a download link should request `IProtectedFileUrlBuilder` (for example `ActivityAttachmentManager`, `ProgressReviewService`). The builder ensures consistent URLs and lets us change the transport in one place if we need to move to pre-signed CDN links later.

## Storage key normalization

* `ProjectCommentService`, `FfcAttachmentStorage`, and `IprAttachmentStorage` now store relative storage keys rather than absolute file-system paths. `IUploadPathResolver` centralises the conversion between relative keys and absolute paths, so moving the upload root or mounting a new volume only requires one configuration change.
* New configuration knobs (`IprAttachments:StorageFolderName`, `IprAttachments:StorageRoot`) let operators relocate module-specific folders without touching code. The resolver gracefully falls back to absolute paths for legacy data.

## Malware scanning coverage

* `FileSystemActivityAttachmentStorage` writes uploads to a temp file, runs the shared `IFileSecurityValidator`, and only moves the file into the permanent location once it passes scanning.
* `VisitPhotoService` and `SocialMediaEventPhotoService` received optional `IVirusScanner` hooks. When a scanner is registered, raw streams are rewound and scanned before any image processing occurs.

## Document filename entropy

* `DocumentService` now generates storage keys with a `{documentId}-{guid}.pdf` pattern. This introduces entropy per upload so deterministic paths can no longer be guessed even if a project/stage ID is known.

## Download configuration

* The `FileDownload` section in `appsettings.json` exposes the token lifetime and whether tokens should be bound to the generating user. This keeps tuning out of the compiled binaries and documents the blast-radius when operators adjust security posture.

## Operational notes

* When migrating existing environments, run through `docs/configuration-reference.md` to set the new options. Existing absolute paths continue to work because the resolver falls back to the stored value if it is already rooted.
* Any new upload-capable feature should: (1) write within the upload root, (2) convert absolute paths to storage keys with `IUploadPathResolver`, (3) call `IProtectedFileUrlBuilder` when returning URLs to clients, and (4) run `IFileSecurityValidator`/`IVirusScanner` before persisting the payload.
