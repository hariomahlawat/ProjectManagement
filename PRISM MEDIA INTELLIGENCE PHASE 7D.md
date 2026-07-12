# PRISM Media Intelligence Phase 7D

## Scope

This package operationalises media intelligence without enabling unsafe automatic identity assignment.

### Implemented

- Detailed readiness state and dependency checklist.
- Independent status for feature configuration, worker, model configuration, files, licences, checksums, ONNX Runtime, database schema and derivative cache.
- Readiness refresh with timestamp.
- Queue controls locked unless every dependency passes and the worker is enabled.
- Explicit labelled batch size with bounded range.
- Worker and face-job telemetry.
- Correct separation of available images, face-eligible photographs, excluded non-photographs and low-confidence classifications.
- Classification review workspace with search, filters, pagination, decision signals and face-eligibility visibility.
- Audited manual classification override and reset-to-automatic workflow.
- Manual classifications remain authoritative.
- No database migration required.

## Deployment

Copy all files into the ProjectManagement root, preserving paths, then run:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet test
```

Keep `MediaLibrary:People:Enabled` and `WorkerEnabled` false until approved models, checksums, licences and tensor contracts are installed.
