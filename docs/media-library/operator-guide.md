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
