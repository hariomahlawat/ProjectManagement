# Chat attachments: design

Status: **decided and built, switched off by default** (owner decisions 2026-10-23). This note
records the design and its reasons; `docs/chat.md` describes how attachments work as built.
They appear only once `Chat:Attachments:Enabled` is set to `true`.

Earlier owner decisions (2026-10-22): people may attach files to chat messages, with an
allowance of 1 GB per person. Messages are kept indefinitely; administrators cannot read chats.

## Decisions (owner, 2026-10-23)

| # | Decision |
| --- | --- |
| D1 | Files live under the upload root, in `chat/…`, with random names on disk |
| D2 | **1 GB per person to start** (a setting); at most 100 MB per file; uploads refused while the upload disk has under 20 GB free. About 50 users, so about 50 GB in the worst case |
| D3 | Files are kept as long as their message; the sender can remove a file to free space |
| D4 | Allowed: PDF, `.docx`/`.xlsx`/`.pptx`, `.jpg`/`.png`/`.webp`, `.txt`/`.csv`. Not allowed: executables and scripts, archives, macro-enabled and legacy Office formats, video |
| D5 | **No malware scanning.** Protection comes from the type allow-list, content checks, image decoding and download-as-file |
| D6 | Unsending a message removes its file; who can download follows who can read the message |
| D7 | Files of deactivated or removed accounts stay with their messages |
| D8 | Administrators see storage totals only; no access to files |
| D9 | Image thumbnails, file cards, up to 5 files per message |

## What PRISM already has (reused, not rebuilt)

| Need | Existing piece |
| --- | --- |
| Where files live | `IUploadRootProvider` / `UploadRootProvider` (`PM_UPLOAD_ROOT`, under `PM_DATA_ROOT`), relative storage keys via `IUploadPathResolver` (`docs/storage-hardening.md`) |
| Backup | `ops/backup-files.*` mirrors the data root; `docs/production-readiness-backup.md` asks that every upload lives under it |
| Streaming validation | `DocumentService` pattern: stream to a temporary file, count bytes against the limit, check the file signature (`DocumentTypeValidation`: PDF, OOXML) |
| Image thumbnails | `MediaDerivativeService` (SkiaSharp: EXIF orientation applied, WebP output, pixel cap) |
| Two-step delete | `FileSystemQuarantine` (stage, restore, finalise) |
| Request size | `UploadRequestLimitResolver` (largest configured limit + 8 MiB) feeding Kestrel/IIS/form limits; `web.config` allows 256 MiB |
| Rate limiting | per-user policies like `DocumentUploadRateLimiting` |

Downloads do **not** use the shared `/files` tokens: a token is its own authorisation for 30
minutes, whatever happens to the conversation meanwhile. Chat serves its files itself and checks
who may read the message on **every** request (D6).

## D1. Storage

Under the upload root, in `chat/{yyyy}/{MM}/{random id}` (a new `GetChatRoot` beside the
project and social-media roots). The stored name is a random id, never the uploaded file name;
the original name is kept in the database for display and downloads. Chat files are therefore
inside the existing backup with no change to the backup scripts.

## D2. Allowance, file size, disk

- The allowance is the **total size of the files a person has sent that are still kept**.
  Unsending or removing a file gives the space back. Receiving files costs nothing.
- **1 GB per person to start**: `Chat:Attachments:AllowanceMb` (default 1024), so it can be
  raised without a release.
- **One file: at most 100 MB** (`Chat:Attachments:MaxFileMb`), well inside the 256 MiB IIS
  limit; a new key in `UploadRequestLimitResolver`.
- **Free-disk floor:** uploads are refused while the upload disk has less than 20 GB free
  (`Chat:Attachments:MinimumFreeDiskGb`), so chat can never fill the disk that project
  documents and backups also need.
- **Sizing:** about 50 users × 1 GB = about **50 GB** worst case, plus a few percent for
  thumbnails. The data disk needs that much room on top of the 20 GB floor.

## D3. Retention

Files are kept as long as their message. The sender can **remove a file** from a message they
sent at any time: the message stays and shows "File removed", and the space is freed. A full
allowance asks the person to remove older files before sending new ones. There is no automatic
expiry.

## D4. Allowed files

An allow-list, checked by content as well as by name:

| Allowed | Check |
| --- | --- |
| PDF | `%PDF-` signature |
| Word, Excel, PowerPoint (`.docx`, `.xlsx`, `.pptx`) | OOXML (zip) signature and expected parts; macro-enabled forms (`.docm`, `.xlsm`, `.pptm`) refused |
| Images (`.jpg`, `.png`, `.webp`) | decoded with SkiaSharp; must be a real image within a pixel cap |
| Plain text and CSV (`.txt`, `.csv`) | valid UTF-8, no NUL bytes |

Not allowed: executables and scripts of any kind, archives (`.zip`, `.7z`, `.rar`: their
contents cannot be checked), legacy Office formats (`.doc`, `.xls`, `.ppt`: no reliable
signature check, and the usual carrier for macros), and video. Any of these can be added later
as a separate decision.

## D5. No malware scanning (deliberate)

PRISM has no scanner today and chat attachments will not add one. Protection comes from:

- the allow-list and content checks above (a renamed executable fails the signature check;
  macro-enabled and legacy Office formats are refused);
- images being decoded, and shown only through a re-encoded thumbnail;
- every non-image file being served as a download (`Content-Disposition: attachment`,
  `nosniff`), never rendered by PRISM; PDFs may open in the browser's own viewer.

The existing `IVirusScanner` hook remains available: scanning can be added later (for example
with Microsoft Defender's command-line scanner on the Windows server) without a schema change.

## D6. Unsend, remove, leave

- **Unsending** a message (within its 5 minutes) also removes its file: the file goes to
  quarantine at once and is deleted by the purge job; the space returns to the sender.
- **Removing a file** later (D3) works the same way, but the message itself stays.
- **Who can download** follows who can read the message: both people in a direct chat; in a
  group, members who joined before it was sent (the same rule as group history, #2734).
  Someone who leaves or is removed loses access, as they lose the history.

## D7. Deactivated or removed accounts

Files stay with their messages (chat history survives removed accounts today), still readable
by the people in the conversation. They no longer count against anyone's allowance.

## D8. Administrators

Administrators cannot read chats, and that covers files: no administrator download, list or
search of chat files. They get **numbers only**: total chat storage, free disk, and the
largest allowances in use by size (no file names, no conversations), to manage disk space.

## D9. How files appear

- **Images:** a thumbnail in the message (WebP, made on upload; location and camera data are
  not carried into it), opening the full image in the browser.
- **Everything else:** a file card with an icon, the name and the size, which downloads the
  file.
- One message may carry **up to 5 files**, with optional text.
- Upload progress in the window, with Cancel; a failed upload can be retried like a failed
  message.

## As built

- **Schema** (additive migration `AddChatAttachments`): `ChatAttachments` (id, message id,
  uploader, relative storage key, uploaded name, content type, size, SHA-256, image size,
  thumbnail key, created, removed, purged). The allowance is the sum of a person's kept files,
  checked under a per-person PostgreSQL advisory lock so two uploads at once cannot both slip
  under it.
- **Upload first, then send:** `POST /api/chat/attachments` streams one file to
  `chat/tmp`, counting bytes against the limit, checks it (`ChatAttachmentInspector`), moves it
  to `chat/{yyyy}/{MM}/{random}.{ext}` and returns its id; a message lists the ids
  (`attachmentIds`) and the files are linked in the same save as the message.
- **Download:** `GET /api/chat/attachments/{id}` (and `?thumbnail=true`) checks who may read the
  message on every request; thumbnails may be kept by the browser (`private, max-age=86400`),
  files are not (`private, no-store`); images are shown, everything else is a download.
- **Clean-up:** `ChatAttachmentCleanupWorker` (every 15 minutes, while attachments are on)
  removes uploads not sent within an hour and deletes the files of removed attachments; each
  sweep only does what is still to do.
- **Limits:** per-user `chat-upload` rate limit, the per-file limit (also counted in
  `UploadRequestLimitResolver` while attachments are on), the free-disk floor, the allowance.
- **Switch:** `Chat:Attachments:Enabled`, default **off**.
