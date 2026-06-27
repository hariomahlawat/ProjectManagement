# PRISM Media Library — Optional External Folders

This release hardens the Photos catalogue so external folders are optional adapters.
The core Photos page continues to load PRISM project, visit, event and video media even
when no external folder exists, the media catalogue migration is pending, or a remote
server/NAS is temporarily offline.

## Supported external paths

The same `FileSystem` provider supports:

- a folder on the PRISM server, for example `D:\PhotoArchive`;
- a NAS SMB share, for example `\\NAS01\OfficialPhotos`;
- a shared folder on another Windows server, for example
  `\\MEDIA-SERVER\Archive\Photos`.

A drive on another server cannot be addressed as `D:\...` from the PRISM server. It
must be exposed as a share and configured by UNC path. Do not use mapped drive letters.

## Default behaviour

External sources are disabled by default:

```json
"MediaLibrary": {
  "Enabled": true,
  "Catalogue": {
    "Enabled": true,
    "SynchronizePrismMedia": true
  },
  "ExternalSources": {
    "Enabled": false,
    "ScannerWorkerEnabled": false,
    "Sources": []
  },
  "People": {
    "Enabled": false,
    "WorkerEnabled": false
  }
}
```

With these defaults:

- Photos uses normal PRISM-owned media;
- no external path is opened;
- no external scanner runs;
- no face/vector processing runs;
- no pgvector extension is required.

## Enabling external folders

1. Apply the media catalogue migrations.
2. Merge the example configuration from `ops/media-library/media-library-settings.example.json` into your environment and set both switches:

```json
"ExternalSources": {
  "Enabled": true,
  "ScannerWorkerEnabled": true,
  "Sources": []
}
```

3. Restart PRISM.
4. Open `/Admin/MediaSources` as Admin or HoD.
5. Add a fully-qualified local or UNC folder and select **Test connection**.
6. Save the source. A scan is queued only when the source is enabled.

Configuration-managed sources are also supported, but database-managed sources created
through the Admin UI are preferred for normal operations.

## Service account and permissions

For remote shares, run the IIS application pool or future standalone media worker under
a dedicated domain/service identity. Grant only:

- Read;
- List folder;
- Read attributes;
- Read extended attributes.

PRISM never renames, moves or deletes originals. Credentials are not stored in PRISM.

## Failure behaviour

- One unavailable source does not stop scans of other sources.
- A failed or partial scan does not mark previously indexed files as missing.
- Cached thumbnails remain available during a temporary remote outage.
- Original view/download returns a controlled unavailable response when the source is
  offline.
- Core PRISM media remains available even when the optional media catalogue itself is
  unavailable.
- Two application instances cannot scan one source concurrently because source scans
  use an expiring database lease.

## Processing and cache

Original files remain at the source. Rebuildable WebP thumbnails and previews are placed
under `MediaLibrary:CacheRoot`. The cache directory requires read/write permission for
the application identity.

## People and pgvector

People/face recognition is intentionally disabled in this phase. The core catalogue
migration contains no vector column and does not create the PostgreSQL `vector`
extension. A future opt-in People migration will be delivered only after an approved
open model, model-weight licence review, threshold calibration and governance approval.

## Open-source dependency policy

The external-folder feature directly uses:

- SkiaSharp 2.88.8 — MIT;
- MetadataExtractor 2.9.0 — Apache-2.0.

No face model or proprietary inference service is included. See
`Features/MediaLibrary/THIRD-PARTY-NOTICES.md` and `MODEL-MANIFEST.json`.

This notice covers dependencies introduced by this media phase. It is not a substitute
for a separate application-wide dependency audit of the pre-existing PRISM solution.
