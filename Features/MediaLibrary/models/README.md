# Approved face models

Model weights are deliberately excluded from source control.

Install the pinned OpenCV Zoo YuNet and SFace ONNX files with one of the supplied scripts:

- `install-approved-models.ps1` on Windows
- `install-approved-models.sh` on Linux

Both scripts verify SHA-256 before accepting a file. The approved profile and provenance are recorded in `../MODEL-MANIFEST.json`, `../DEPENDENCY-LICENSES.json` and `../THIRD-PARTY-NOTICES.md`.

Do not enable the People worker until the Admin Media Intelligence readiness page reports that model files, hashes, tensor contracts, schema and cache are ready.
