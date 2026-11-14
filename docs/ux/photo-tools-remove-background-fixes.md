# Photo Tools, Remove background: review (14 Nov 2026) and fix plan

Remove background (#3182) came after the Photo Tools review of 13 Nov
([photo-tools-review-fixes.md](photo-tools-review-fixes.md)), so it was reviewed on its own. Three
reviewers read it cold:

- **Server:** `Features/PhotoTools` (options, readiness, service, ONNX segmenter, endpoints),
  the settings, the install guide and the tests.
- **Browser:** `photo-tools-background.js` and the cut-out functions in `photo-tools-core.js`.
- **Screen:** the `#remove` screen, its messages and states, the CSS and the docs.

It was also run live: with the model off (as it ships), with the tiny test model
(`red-channel-mask.onnx`), and with the real model the owner downloaded on 13 Nov
(RMBG-2.0, full precision, 1,024,331,469 bytes; settings in #3204).

**Verdict:** server code 7.5/10, browser code 7.5/10, UX 7/10, docs 8/10. Nothing High and no
privacy problem: both endpoints need sign-in, the mask upload checks the antiforgery token, oversized
images are refused from their header before decoding, nothing is written to disk, answers are
`no-store`, and only size and time are logged. The tool is off by default and never stops PRISM
from starting.

**Measured with the real model** on a development PC (Intel Core i7-10750H, 6 cores, 12 threads;
`IntraOpThreads` 6): the cut-outs were very good, but each photo took **about 44 seconds** (the
first and later ones alike), and after two photos the PRISM process held **6.1 GB of memory with
18.9 GB committed**. ONNX Runtime keeps its working memory at its peak after a photo. The owner
decided on 14 Nov to keep the tool **off in production** until this is fixed (#3204).

## Fix first

| # | Finding | Where |
| --- | --- | --- |
| 1 | Memory stays at its peak after a photo: ONNX Runtime's CPU memory arena is on and never shrinks, and the loaded model is never freed (measured: 18.9 GB committed after two photos) | `OnnxBackgroundSegmenter.cs` session options |
| 2 | About 44 s per photo; with the 60 s time limit, a photo waiting behind one other is stopped. The threads are clamped to the processor count, not to a share of it, and ONNX Runtime spin-waits between steps, so one photo can take every core from the rest of PRISM | `OnnxBackgroundSegmenter.cs:179`, `BackgroundRemovalOptions` |
| 3 | The whole photo is read into memory (and copied twice) before the "busy" check, so many parallel uploads from one signed-in user can hold gigabytes before most are refused | `PhotoToolsApi.cs:103`, `BackgroundRemovalService.cs:101` |
| 4 | With the model off, the home card still promises a cut-out and leads to a dead end: a yellow note, a greyed button, and a drop hint and privacy line for a tool that cannot run (seen live) | `Index.cshtml` home card, `photo-tools-background.js:762-770` |

## Medium

| # | Finding |
| --- | --- |
| 5 | A user changing photo while one is processed is logged as an error with a stack trace (`LogError("Background removal failed.")`), filling the production log with false failures |
| 6 | Changing photo during a save names the file after the new photo, writes "Saved" over its status and removes its spinner while its mask is still pending; a photo opened during the save's wait can also fail the save |
| 7 | Saving at 4,096 px keeps about eight 64 MB copies alive at once, plus the full-size original for the whole session (a 48 MP photo is about 190 MB); the JPG path makes one more copy it does not need |
| 8 | A "busy" answer, a timeout or a network error throws the photo away, although the message says "try again in a few seconds" |
| 9 | Nothing tells the user that a photo can take a long time ("Finding the subject…" never changes), and there is no Cancel |
| 10 | The install check says "ready" when the file and checksum are right, even if the model's settings are wrong or the native ONNX Runtime library cannot load; every photo then fails. A failed load is retried, 1 GB at a time, on every photo |
| 11 | The licence credit lacked the model version and licence (fixed in #3204, owner's wording) |

## Low

- **Server:**
  - Hashing the model can throw an unhandled I/O error while an administrator copies a new file, and the first check after a change hashes 1 GB under a lock that blocks every request.
  - `MaximumSide` and `MaximumUploadBytes` have no bounds; a negative upload limit makes every request fail.
  - Any format the decoder knows (BMP, GIF, ICO) is accepted whatever the declared type.
- **Browser:**
  - Leaving the screen does not cancel the mask request, so it keeps the server's only slot for up to a minute, and there is no browser-side time limit.
  - An expired sign-in is reported as "server could not be reached".
  - A stroke can stay "painting" when the mouse is let go outside the photo and pointer capture failed.
  - Painting slows as strokes add up (every point of every stroke is replayed each frame).
  - The save waits for an animation frame, so it stalls in a background tab.
  - Faint hair loses colour in see-through PNG/WEBP, because the canvas stores colours premultiplied.
- **Screen:**
  - No "checking" state while the status is fetched; dropping a photo while the tool is off is silently ignored.
  - One message ("not set up … yet") for every off state, including a deliberate switch-off.
  - "Other" is announced as "Another background colour", with no pressed state.
  - The privacy detail in the top bar is a mouse-only tooltip.
  - "Reset all touch-ups" against Passport photo's "Reset"; the brush hint never says "Drag on the photo to paint".
  - JPG is greyed out with no reason until a colour is chosen.
  - "Subject" is jargon; say "person or object".
- **Docs:**
  - The native prerequisite (the Visual C++ runtime that ONNX Runtime needs on Windows) is not mentioned.
  - The install check should end with one test photo and the log line.
  - Rollback does not say the model stays in memory until restart.
  - "16 cores" is stated as a fact; example paths are mixed.
- **Code and tests:**
  - The brush painter and the latest-photo logic are copies of Passport photo's and of `latestPhotoOpener`.
  - No endpoint-level tests (antiforgery, 413/415, `no-store`); no test of a real cancellation or a load failure; nothing in `photo-tools-background.js` is tested.

## Owner decisions (14 Nov 2026)

1. **Credit:** "Background removal powered by BRIA RMBG-2.0 (CC BY-NC 4.0)" (done in #3204).
2. **When the tool is off:** the home card is dimmed and says "Not set up on this server".
3. **Model files:** with the face models in `media-models`, named
   `background_removal_bria_rmbg_2.0.onnx`; development on, production off until PR 2 and PR 3 are
   in and measured (done in #3204).

## For the owner

1. **The real server.** How many processor cores and how much memory does the PRISM server
   have, and what else runs on it? The thread and memory limits in PR 2 are set from that, and the
   time per photo is measured there before the tool is turned on.
2. **A lighter model file.** If 44 s per photo stays too slow after PR 2, the model page may offer
   smaller versions of the same model (half precision or quantised). Trying one needs a download
   on a computer with Internet access, as before. Ask after PR 2 is measured.

## Build plan

Each PR adds tests for what it fixes and is checked live with the real model on a development PC
(memory and time written in the PR).

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | Built, #3206. Memory and processor: no ONNX memory arena (or shrink it after each photo), free the model when the tool is switched off or not used for a while, threads at most half the cores and no spin-waiting, `MaximumConcurrent × IntraOpThreads` kept under the core count, measured time and memory written in the guide | 1, 2 |
| 3 | Built, #3207. Requests: the "busy" check before the photo is read, a buffer sized from the declared length (no double copy), only JPG/PNG/WEBP decoded, bounds on `MaximumSide` and `MaximumUploadBytes` (clamped, never a start-up failure), endpoint tests | 3, Low (format, bounds) |
| 4 | Built, #3208. Status and errors: a user's cancel is not an error; I/O errors while hashing become a status; hashing once, in the background, with a "checking" state; a failed load is remembered and shown as "model-invalid"; the native library checked when the status is asked; tests with the tiny model | 5, 10, Low (hashing) |
| 5 | Built, #3209. The screen when off or busy: dimmed home card "Not set up on this server" (decision 2), no drop hint or privacy line while off, "checking" state, the photo kept with Try again after busy, timeout or network errors, a "still working" message after a few seconds and a Cancel button, 401 told as "sign in again" | 4, 8, 9, Low (checking, messages) |
| 6 | The save and memory in the browser: the save takes its photo, name and masks before it starts and never touches a newer photo's screen; at most a 4,096 px copy kept; temporary canvases released; no extra JPG copy; no wait for an animation frame; cancel the mask on leaving the screen and a browser-side time limit; pointer fixes; only strokes that touch the changed area replayed; shared brush painter and `latestPhotoOpener` | 6, 7, Low (Browser, Code) |
| 7 | Wording, accessibility and docs: "person or object", one off message without "yet", "Other background colour" with a pressed state, a focusable privacy note, "Reset", "Drag on the photo to paint", a JPG hint; the guide's prerequisites, install check, rollback and measured figures | Low (Screen, Docs) |
| 8 | Turn it on in production, after PR 2 and PR 3 are measured on the real server (owner question 1) | — |
