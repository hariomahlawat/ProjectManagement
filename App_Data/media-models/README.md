# Face model installation

Face intelligence is disabled by default. Place approved ONNX detector and embedding models in this folder, configure exact filenames, tensor names, versions, licences and SHA-256 checksums under `MediaLibrary:People`, then enable the feature.

The detector contract expected by this release is:
- input: NCHW float tensor;
- boxes output: `[N,4]` or `[1,N,4]` as x1,y1,x2,y2;
- scores output: `[N]`, `[1,N]` or `[1,N,1]`;
- optional landmarks output: `[N,10]` or `[1,N,10]`.

The embedding model must return one float vector under the configured output name. No model weights are bundled.
