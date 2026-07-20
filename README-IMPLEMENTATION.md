# Proliferation submission service — CS0120 correction

Replace:

`Areas/ProjectOfficeReports/Application/ProliferationSubmissionService.cs`

with the file included in this bundle.

## Correction

The validation helpers remain static and pure. The service now passes the injected clock value into them explicitly:

- `ValidateYearlyRequiredFields(dto, _clock.UtcNow)`
- `ValidatePreferenceRequiredFields(dto, _clock.UtcNow)`

The helpers accept `DateTimeOffset now` and no longer attempt to access the instance field `_clock` from static context.

No migration or package change is required.

## Separate static-file lock

The `proliferation-manage.js` IOException is not a source-code compilation defect. Another process had the file open while the Static Web Assets task attempted to fingerprint it.

1. Stop debugging and stop the running PRISM application.
2. Allow any file-copy/extraction operation to finish.
3. Close any external editor or formatter actively writing `proliferation-manage.js`.
4. Delete the project `bin` and `obj` folders.
5. Rebuild.

If the lock remains, use Resource Monitor (`resmon.exe`) → CPU → Associated Handles and search for `proliferation-manage.js`, then close the process holding the handle.
