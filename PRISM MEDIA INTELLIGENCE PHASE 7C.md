# PRISM Media Intelligence Phase 7C

This package adds an opt-in, offline CPU face-intelligence foundation with model checksum/licence validation, ONNX inference, face and embedding persistence, controlled job queueing, conservative candidate matching, human confirmation, identity audit records, and People/review administration pages.

No model weights are bundled. The feature remains disabled until approved model files, tensor contracts, licences and checksums are configured. Automatic identity confirmation is not implemented.

## Deployment
1. Copy files into the project root.
2. Run `dotnet restore`.
3. Run `dotnet ef database update --context MediaLibraryDbContext`.
4. Run `dotnet build` and `dotnet test`.
5. Install approved ONNX models under `App_Data/media-models`.
6. Configure `MediaLibrary:People` and enable only after the readiness page reports success.

## Safety
- Ordinary Photos remains independent of face intelligence.
- No vectors are exposed to users.
- Every assignment/rejection is audited.
- No person is automatically named.
- Existing source authorization remains authoritative.
