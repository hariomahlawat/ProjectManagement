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
   - applies the Refine edge, Erase and Restore brushes to the mask (`applyCutoutStrokes`), the
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
2. Copy it, renamed `background_removal_bria_rmbg_2.0.onnx`, into the folder that holds PRISM's other models, outside the
   web root: `F:/ProjectManagementData/media-models/` in production (beside the face models),
   `D:/ProjectManagementData/media-models/` on a development computer. The descriptive name keeps
   it from being confused with, or overwritten by, another model's `model.onnx`.
3. Work out its SHA-256:

   ```powershell
   (Get-FileHash -Algorithm SHA256 F:\ProjectManagementData\media-models\background_removal_bria_rmbg_2.0.onnx).Hash.ToLower()
   ```

   ```bash
   sha256sum /srv/prism/media-models/background_removal_bria_rmbg_2.0.onnx
   ```

4. Set the settings below (in `appsettings.Production.json` or the environment), at least
   `Enabled`, `ModelRoot`, `Model:FileName` and `Model:Sha256`, and check the model values in the
   table against the model page. `appsettings.Production.json` already holds `ModelRoot`,
   `FileName` and the SHA-256 of the RMBG-2.0 file downloaded on 13 Nov 2026
   (`5b486f08200f513f460da46dd701db5fbb47d79b4be4b708a19444bcd4e79958`, 1,024,331,469 bytes), with
   `Enabled` left `false`: see "Measured on a development computer" below before turning it on.
5. Open Photo Tools, Remove background. No warning means it is ready. An administrator can also
   open `/api/photo-tools/background/status` while signed in: `"state": "ready"`.

Settings are re-read while PRISM runs, and the model is loaded on the first photo, so a restart
is not needed; the one exception is `MaximumConcurrent`, which is read when PRISM starts.

### Licence

BRIA RMBG weights are licensed for **non-commercial use** (RMBG-2.0: CC BY-NC 4.0; RMBG-1.4:
BRIA's own non-commercial licence). The owner decided on 13 Nov 2026 that this organisation's
use, as a non-commercial, non-profit organisation, fits the licence. The licence asks for
credit: the screen shows `Model:Attribution` ("Background removal powered by BRIA RMBG-2.0 (CC BY-NC 4.0)", wording chosen by the owner on
14 Nov 2026) under the
download button. Read the licence again when downloading a new version; the terms are BRIA's.

## Settings

Section `PhotoTools:BackgroundRemoval` (defaults in `appsettings.json`, code in
`Features/PhotoTools/BackgroundRemovalOptions.cs`).

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `false` | Turns the tool on. While off, the screen says it is not set up. |
| `ModelRoot` | empty | Folder holding the model file; a relative path is under the content root. |
| `IntraOpThreads` | `6` | Processor threads one photo may use. Whatever is set, all photos together never use more than half the server's processors (`ThreadsFor`), so the rest of PRISM stays responsive. |
| `ModelIdleMinutes` | `10` | Minutes the model stays in memory after its last photo; then it is released and loaded again on the next photo (which then takes longer). `0` keeps it loaded. |
| `MaximumConcurrent` | `1` | Photos processed at the same time (1 to 4). Read when PRISM starts: a change needs a restart. |
| `MaximumWaiting` | `4` | Photos that may wait (0 to 32); beyond this the answer is "busy, try again in a few seconds", given before the photo is read. |
| `MaximumUploadBytes` | `20971520` | Largest request (20 MB). Kept between 1 MB and 64 MB: a value outside is used at its nearest end, never a start-up failure. |
| `MaximumSide` | `2048` | Longest side of the copy the browser sends (256 to 4096 px). The model works at 1024 px; a larger copy only costs transfer. |
| `TimeoutSeconds` | `60` | How long one photo may take, waiting included; then it is stopped. |
| `Model:FileName` | `background_removal_bria_rmbg_2.0.onnx` | The file in `ModelRoot`. |
| `Model:Sha256` | empty | Lower-case SHA-256 of the file. **Required**: the model is not used without it, or when it does not match. |
| `Model:Key`, `Version`, `License`, `Publisher` | BRIA RMBG 2.0 | Recorded and logged when the model loads. |
| `Model:Attribution` | "Background removal powered by BRIA RMBG-2.0 (CC BY-NC 4.0)" | Credit shown on the screen. Do not leave it empty: the licence asks for credit. |
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

- A photo is processed on the processor only (no graphics card is needed). The first photo
  after a start also loads the model. Measure on the real server after installing.
- **Measured on a development computer (14 Nov 2026):** the full-precision RMBG-2.0 file above,
  on an Intel Core i7-10750H (6 cores, 12 threads) with `IntraOpThreads` 6, took about
  **44 seconds per photo**, the first and later ones alike, and after two photos the PRISM
  process held **6.1 GB of memory, with 18.9 GB committed**: ONNX Runtime keeps its working
  memory at its peak after a photo. The cut-outs were very good. Until that memory is released
  after each photo (and the time is brought down), the tool stays off in production.
- **After the memory fix (same computer, 14 Nov 2026):** while a photo is processed the PRISM
  process peaks at about **6.5 to 7.5 GB**, and goes back to **about 2.2 GB** when it is done; it
  no longer grows from photo to photo. The first photo after a start (or after the model was
  released) took 45 s, model loading included; the next took **29 s**. A server running
  Remove background therefore needs about 6 GB free for each photo processed at the same time.
- Only `MaximumConcurrent` photos run at once, each with at most `IntraOpThreads` threads; others
  wait their turn, and when `MaximumWaiting` are already waiting the answer is "busy". "Busy" and
  "not set up" are answered before the photo is read, so a full queue never holds photos in
  memory. A request takes its place in the queue before its photo is read and keeps it while the
  photo arrives, so slow uploads count against `MaximumWaiting` too; the photo goes straight into
  one array of its declared size.
- Only JPG, PNG and WEBP are decoded, whatever type the request names.
- A photo that takes longer than `TimeoutSeconds` is stopped inside ONNX Runtime, not just
  abandoned.
- When a user changes photo before the mask arrives, the browser cancels the earlier request and
  the server stops working on it, so replaced photos do not fill the queue.
- The approval (file and checksum) is checked again when a photo's turn comes, and only the very
  file whose checksum was checked is loaded; a model replaced while photos wait is not used until
  it is approved.
- Memory: ONNX Runtime's memory arena is off, so the working memory of a photo is given back
  when the photo is done instead of staying at its peak. The model itself stays loaded while it
  is used (RMBG-2.0 is about 1 GB on disk, about 2.2 GB for the whole PRISM process once loaded),
  is released after `ModelIdleMinutes` without a photo, and is released at once when `Enabled`
  is set to `false`. It is loaded again when its file or path changes.
- Processor: threads do not spin while they wait, and all photos together use at most half the
  processors.

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
  good as the model makes it, and the Refine edge brush is for the rest.
- Where the model is sure but wrong (wall between hair strands it took as subject), the
  automatic step does not touch it, so as not to eat a white shirt against a white wall; the
  Refine edge brush does.
- The saved photo is at most 4096 px on its longest side.
- Some offices do not accept a photo with a changed background.
