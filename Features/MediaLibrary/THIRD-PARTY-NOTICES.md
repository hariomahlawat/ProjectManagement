# PRISM Media Library Third-Party Notices

This notice covers direct libraries and optional model weights used by the Media Library
and People intelligence implementation. Organisation-wide software-composition review,
model-risk approval and privacy approval remain mandatory before production activation.

## SkiaSharp 2.88.8

Purpose: image decoding, orientation-aware rendering, resizing, face alignment and WebP
derivative output.

License: MIT.

Project: https://github.com/mono/SkiaSharp

## MetadataExtractor 2.9.0

Purpose: EXIF and media metadata extraction.

License: Apache License 2.0.

Project: https://github.com/drewnoakes/metadata-extractor-dotnet

## Microsoft.ML.OnnxRuntime 1.20.1

Purpose: local CPU inference for explicitly approved ONNX detector and embedding models.
No external inference API is used.

License: MIT.

Project: https://github.com/microsoft/onnxruntime

## OpenCV Zoo YuNet 2026may model

Purpose: face detection and five-point landmark localisation.

Model license: MIT.

Pinned file: `face_detection_yunet_2026may.onnx`

Pinned SHA-256: `ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0`

Source: https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet

The model is not bundled with PRISM. The installation script downloads it from the
authoritative OpenCV repository and verifies the exact checksum before installation.

## OpenCV Zoo SFace 2021dec model

Purpose: aligned face embedding for similarity-based candidate suggestions.

Model license: Apache License 2.0.

Pinned file: `face_recognition_sface_2021dec.onnx`

Pinned SHA-256: `0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79`

Source: https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface

The model is not bundled with PRISM. Identity suggestions always require an authorised
human decision; the application does not automatically confirm a person.

## Explicit exclusion: InsightFace pretrained model packs

PRISM does not use or redistribute InsightFace pretrained model packs because their
official repository states that those pretrained models are available for non-commercial
research purposes. This exclusion does not apply to independently licensed code, but the
bundled pretrained weights are not approved for this deployment profile.
