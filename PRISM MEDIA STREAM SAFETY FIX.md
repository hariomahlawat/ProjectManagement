# PRISM Media Stream Safety Fix

## Root cause

`MediaMetadataReader` passed one `MemoryStream` to `SKCodec` and then reused the same stream for EXIF parsing. Disposing the native codec can close its managed stream wrapper, causing `ObjectDisposedException: Cannot access a closed Stream` for otherwise valid photographs.

## Corrections

- The source is copied once into an immutable byte buffer.
- SkiaSharp receives an independent `SKData` copy.
- MetadataExtractor receives a separate read-only `MemoryStream`.
- Derivative generation uses the same ownership-safe pattern.
- Empty, oversized, unreadable and unsupported images become permanent processing failures.
- Missing source files remain `MediaContentUnavailableException` and are dead-lettered immediately by the reliability worker.
- EXIF parsing failures no longer reject a valid image.
- Non-seekable source streams are supported.
- Temporary derivative files are cleaned safely.

## Recovery after deployment

Retry jobs whose failure code is `ObjectDisposedException`. Do not bulk-retry `MediaContentUnavailableException` jobs unless the underlying physical media has been restored.
