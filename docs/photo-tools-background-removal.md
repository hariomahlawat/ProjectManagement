# Photo Tools: Remove background

Remove background is the one Photo Tools screen that uses the PRISM server. A segmentation model
(BRIA RMBG, chosen by the owner) runs on the server's processor and finds the subject; everything
else is done in the browser. It is **off** until an administrator installs the model and turns it
on, so a deployment without the model behaves exactly as before: the screen says it is not set
up, and every other tool works.

The user-facing description is in [photo-tools.md](photo-tools.md).

## How it works

1. The browser opens the photo, the right way up, and makes a copy of at most 2048 px on the
   longest side. It sends that copy as a JPG to `POST /api/photo-tools/background/mask`.
2. The server checks the request (signed in, antiforgery token, PNG/JPG/WEBP, size), waits for
   its turn, stretches the photo to the model's input size (1024 × 1024 for BRIA RMBG), runs the
   model with ONNX Runtime and returns **only the mask** as a greyscale PNG the size of the copy.
3. The browser does the rest (`photo-tools-core.js`, `photo-tools-background.js`):
   - reads the old background's colour at every spot from where the mask is sure
     (`cutoutBackground`);
   - checks the model's unsure band (around hair) against the colours, and lowers the mask where
     a pixel looks like the background (`refineCutoutEdge`); the model's outline is never raised;
   - takes the old background's tint out of the soft edge, so hair keeps its own colour on any
     new background (`cutoutColours`); a pixel whose worked-out colour would fall outside black
     to white is no mix and keeps its colour;
   - applies the Refine Edge, Erase and Restore brushes to the mask (`applyCutoutStrokes`), the
     same brushes and maths as Passport photo's touch-up;
   - saves the cut-out see-through (PNG, WEBP) or on a plain colour or a blur of its own
     background (JPG, PNG, WEBP), at up to 4096 px; the mask is enlarged to that size and the
     edge cleaned again at full size.

### Privacy

- The photo is held in the server's memory for the one request and dropped. It is never
  written to disk, stored, cached (`Cache-Control: no-store`) or logged. The log records only
  the photo's size and how long it took.
- Location and camera details never reach the server: the copy is redrawn on a canvas first.
- The page says that a copy is sent to the PRISM server and is not stored.

## Installing the model

The weights are not in source control, as with the face models. BRIA publishes them on Hugging
Face behind a sign-in that accepts BRIA's terms, so they are downloaded once by an administrator
on a computer with Internet access and copied to the server.

1. On the model page (`briaai/RMBG-2.0`, or `briaai/RMBG-1.4`), sign in, accept the terms, read
   the licence, and download the ONNX file (`onnx/model.onnx`, the full-precision file, which is
   the right one for a processor without a graphics card).
2. Copy it to a folder on the PRISM server outside the web root, for example
   `F:/ProjectManagementData/photo-models/`.
3. Work out its SHA-256:

   ```powershell
   (Get-FileHash -Algorithm SHA256 F:\ProjectManagementData\photo-models\model.onnx).Hash.ToLower()
   ```

   ```bash
   sha256sum /srv/prism/photo-models/model.onnx
   ```

4. Set the settings below (in `appsettings.Production.json` or the environment), at least
   `Enabled`, `ModelRoot`, `Model:FileName` and `Model:Sha256`, and check the model values in the
   table against the model page.
5. Open Photo Tools, Remove background. No warning means it is ready. An administrator can also
   open `/api/photo-tools/background/status` while signed in: `"state": "ready"`.

Settings are re-read while PRISM runs, and the model is loaded on the first photo, so a restart
is not needed; restart if in doubt.

### Licence

BRIA RMBG weights are licensed for **non-commercial use** (RMBG-2.0: CC BY-NC 4.0; RMBG-1.4:
BRIA's own non-commercial licence). The owner decided on 13 Nov 2026 that this organisation's
use, as a non-commercial, non-profit organisation, fits the licence. The licence asks for
credit: the screen shows `Model:Attribution` ("Background removal by BRIA RMBG") under the
download button. Read the licence again when downloading a new version; the terms are BRIA's.

## Settings

Section `PhotoTools:BackgroundRemoval` (defaults in `appsettings.json`, code in
`Features/PhotoTools/BackgroundRemovalOptions.cs`).

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `false` | Turns the tool on. While off, the screen says it is not set up. |
| `ModelRoot` | empty | Folder holding the model file; a relative path is under the content root. |
| `IntraOpThreads` | `6` | Processor threads one photo may use. Kept well under the server's cores (16) so the rest of PRISM stays responsive. |
| `MaximumConcurrent` | `1` | Photos processed at the same time. |
| `MaximumWaiting` | `4` | Photos that may wait; beyond this the answer is "busy, try again in a few seconds". |
| `MaximumUploadBytes` | `20971520` | Largest request (20 MB). |
| `MaximumSide` | `2048` | Longest side of the copy the browser sends. The model works at 1024 px; a larger copy only costs transfer. |
| `TimeoutSeconds` | `60` | How long one photo may take, waiting included; then it is stopped. |
| `Model:FileName` | `model.onnx` | The file in `ModelRoot`. |
| `Model:Sha256` | empty | Lower-case SHA-256 of the file. **Required**: the model is not used without it, or when it does not match. |
| `Model:Key`, `Version`, `License`, `Publisher` | BRIA RMBG 2.0 | Recorded and logged when the model loads. |
| `Model:Attribution` | "Background removal by BRIA RMBG" | Credit shown on the screen. |
| `Model:InputName`, `OutputName` | empty | Tensor names; empty takes the only input and the first output. |
| `Model:InputWidth`, `InputHeight` | `1024` | The model's input size. |
| `Model:Mean`, `Model:Std` | ImageNet | Per channel: `(pixel / 255 − Mean) / Std`. |
| `Model:OutputActivation` | `Auto` | `Auto` applies a sigmoid only when the output holds raw scores (values outside 0 to 1); `None`; `Sigmoid`. |
| `Model:OutputNormalization` | `None` | `MinMax` stretches the output to the full 0 to 1 range. |

### Model values

| Setting | BRIA RMBG-2.0 | BRIA RMBG-1.4 | U²-Net small (stand-in for testing) |
| --- | --- | --- | --- |
| `InputWidth` × `InputHeight` | 1024 × 1024 | 1024 × 1024 | 320 × 320 |
| `Mean` | 0.485, 0.456, 0.406 | 0.5, 0.5, 0.5 | 0.485, 0.456, 0.406 |
| `Std` | 0.229, 0.224, 0.225 | 1.0, 1.0, 1.0 | 0.229, 0.224, 0.225 |
| `OutputActivation` | `Auto` | `Auto` | `Auto` |
| `OutputNormalization` | `None` | `MinMax` | `MinMax` |

The BRIA columns follow BRIA's published example code as far as it was known when this was
written; the model pages could not be reached from the build environment, so **check them
against the model page** when installing. Two safeguards cover the likeliest mistakes: `Auto`
decides the sigmoid from the output itself, and when the model loads its input names and size
are checked against these settings (`BackgroundModelContract`); a mismatch is written to the
server log in plain words and the screen reports that the background could not be removed.

The U²-Net small values were checked by running it (`u2netp.onnx`, Apache-2.0, 4.6 MB). It is
much weaker than BRIA's models and is only for testing the pipeline.

## Load on the server

- A photo is processed on the processor only (no graphics card is needed). With RMBG-2.0 a photo
  is expected to take a few seconds on a 16-core server; measure on the real server after
  installing. The first photo after a start also loads the model.
- Only `MaximumConcurrent` photos run at once, each with at most `IntraOpThreads` threads; others
  wait their turn, and when `MaximumWaiting` are already waiting the answer is "busy".
- A photo that takes longer than `TimeoutSeconds` is stopped inside ONNX Runtime, not just
  abandoned.
- Memory: the model stays loaded once used (RMBG-2.0 is about 1 GB on disk). It is loaded again
  only when its file or path changes.

## Endpoints

| Endpoint | Who | Answers |
| --- | --- | --- |
| `GET /api/photo-tools/background/status` | Signed-in users | `available`, `state`, a message when not ready, the credit, the size limits. |
| `POST /api/photo-tools/background/mask` | Signed-in users, with the `X-CSRF-TOKEN` header | The mask as a greyscale PNG (`X-Mask-Width`, `X-Mask-Height`), or a JSON `message`: 400 (token), 413 (too large), 415 (not a photo), 503 (not set up, busy, or took too long), 500. |

### Status states

| `state` | Meaning | What to do |
| --- | --- | --- |
| `disabled` | `Enabled` is false. | Turn it on when the model is installed. |
| `not-configured` | No `ModelRoot` or file name. | Set them. |
| `model-missing` | The file is not where the settings say. | Copy it, or correct `ModelRoot` / `Model:FileName`. |
| `checksum-not-configured` | `Model:Sha256` is empty. | Set it to the file's SHA-256. |
| `checksum-mismatch` | The file's SHA-256 differs. | The file is not the approved one, or the setting is wrong. |
| `ready` | All good. | |

## Rollback

Set `PhotoTools:BackgroundRemoval:Enabled` to `false`. The screen then says it is not set up;
nothing else changes. No database or stored data is involved.

## Limits

- The mask comes from a 2048 px copy and a 1024 px model; very fine detail (single hairs) is as
  good as the model makes it, and the Refine Edge brush is for the rest.
- Where the model is sure but wrong (wall between hair strands it took as subject), the
  automatic step does not touch it, so as not to eat a white shirt against a white wall; the
  Refine Edge brush does.
- The saved photo is at most 4096 px on its longest side.
- Some offices do not accept a photo with a changed background.
