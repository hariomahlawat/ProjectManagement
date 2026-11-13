# Remove background model

Model weights are deliberately excluded from source control.

Remove background in Photo Tools uses BRIA RMBG (owner decision, 13 Nov 2026; non-commercial
licence, credit shown on the screen). BRIA publishes the weights on Hugging Face behind a
sign-in that accepts its terms, so an administrator downloads the ONNX file once, copies it to
the PRISM server, records its SHA-256 and sets `PhotoTools:BackgroundRemoval`.

The steps, settings, model values and troubleshooting are in
[docs/photo-tools-background-removal.md](../../../docs/photo-tools-background-removal.md).
