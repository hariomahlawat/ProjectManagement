# External Media Sources — Operator Guide

Use `/Admin/MediaSources` to connect, test, pause, hide or disconnect optional folders.
The same file-system provider supports a folder on the PRISM server, a NAS share, or a
folder shared by another Windows server.

## Source states

- **Enabled + visible**: scanned and shown in Photos.
- **Enabled + hidden**: scanned but not shown to users.
- **Paused**: retained in the catalogue; no scheduled scans.
- **Disconnected**: hidden, scanning stopped, catalogue/audit history retained.

Disconnecting never changes the source folder or deletes an original file.

## No external folder

No external source is required. When the feature is disabled or the source list is empty,
PRISM continues to show its own project, visit, event and video media. The optional media
catalogue reader contains database/network failures and returns no external items rather
than failing the Photos page.

## Connection test

The test resolves the configured root, checks read/list access and examines only a bounded
sample. It does not start a full archive scan.

## Supported paths

```text
D:\Archives\Photos
\\NAS01\Photos
\\MEDIA-SERVER\Shared\OfficialPhotos
```

Use a UNC path for a folder located on another computer. A remote server's local path,
such as `D:\Photos`, is not directly accessible from PRISM unless that folder is shared.

## Credentials

PRISM does not store SMB credentials. Run the worker/application pool under a dedicated
service identity and grant that identity read/list access to the source folder.

## Large archives

Scans are incremental and batched. A PostgreSQL lease prevents two PRISM instances from
scanning the same source concurrently. The Photos browser retrieves a bounded external
window for each page rather than imposing the prototype's former 1,000-item ceiling.

## Storage outages

If a source folder, NAS share or the PRISM upload volume is briefly unreachable, the
processing worker treats the failure as temporary: the job is retried with back-off and
the media stays visible in Photos. A file is recorded as missing only when its storage
root is reachable and the file itself is absent. Jobs that exhaust their retries during a
long outage are dead-lettered with a recoverable failure code, so **Retry recoverable
jobs** picks them up once storage is back.

When an external-folder scan finds and reads a file that was earlier recorded as missing, access-denied or
temporarily unavailable, it is restored automatically. Media hidden by an outage before
this behaviour shipped can be restored with **Recheck unavailable media** in
`/Admin/MediaSources`.

## Very large images

`MediaLibrary:Processing:MaxImagePixels` (default 100,000,000, i.e. 100 megapixels) caps
the decoded size of any image that processing will open. It is checked from the image
header before decoding, because the file-size limit alone does not bound memory: a small,
highly compressible file can declare enormous dimensions. Thumbnails and previews of JPEG
photographs are decoded at reduced scale, so ordinary camera images are well within the
limit. Images above it are recorded as a permanent processing failure and are not retried.

A job whose every attempt ends with the worker's lease expiring, for example because an
image brought the process down, is moved to dead-letter with the failure code
`RepeatedWorkerLoss` instead of being retried indefinitely. It is treated as a permanent
failure: neither **Retry recoverable jobs** nor catalogue synchronisation runs it again
(unless the file itself changes). Check the source file, then use **Force retry** on the
job.

## Photos ZIP downloads

Each selection download is built as a temporary archive on the media cache volume, which
derivatives and face crops also use. Builds are limited per application instance:

- `MediaLibrary:BulkDownload:MaxConcurrentArchives` (default 2): archives built at once.
  Each user may build one archive at a time; another request is refused with "Another ZIP
  is being prepared" (HTTP 429).
- `MediaLibrary:BulkDownload:MinimumFreeSpaceBytes` (default 1 GB): free space that must
  remain after the archive is written. A request that would leave less is refused
  (HTTP 507) instead of filling the volume.

`MaxItems` and `MaxSourceBytes` continue to cap the size of one archive.
