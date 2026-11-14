# Photo Tools: second review (14 Nov 2026) and fix plan

Two reviewers read Photo Tools (`/Tools/Photo`) cold at `9865767` (after the passport plan,
#3213 to #3218), without the earlier reviews or plans:

- **UX:** the page, every screen's script, the CSS and the docs, judged for office staff making
  passport photos, form signatures and smaller photos on a LAN with no Internet.
- **Code:** `Features/PhotoTools`, the face path in the Photos library's ONNX engine, every
  Photo Tools script and the tests. The 103 Node tests passed.

**Verdict:** UX 7/10, code 7.5/10. One High (the privacy wording on the page), nothing High in the
code. The findings below were checked against the code before this plan was written.

## Where it stands against online tools

| Kind | Examples | PRISM |
| --- | --- | --- |
| Free in-browser makers | passport-photo.utils.com, mergeimages.net, imgpace | Ahead: face placed, a background model, checks from the real face, KB limits, signatures and true-size print sheets |
| Paid passport services | PhotoGov, Visafoto, Passport Photo Online | About level for the Indian 35 × 45 mm photo; behind on checks (eyes, glasses, shadows, expression), camera capture and guarantees; ahead on privacy, cost and being offline |
| Background removers | remove.bg, Photoroom, Adobe Express | Same class of model (BRIA RMBG-2.0) and edge work; slower (about 30 s on the server's processor, one at a time, against seconds on their GPUs); full resolution with no outside upload |

This plan fixes what the review found. Closing the gap with the paid services (deeper checks,
camera capture) is a separate decision for the owner (see the end).

## High

| # | Finding | Where |
| --- | --- | --- |
| H1 | The page says "Only Remove background sends a copy to the PRISM server" (top bar) and "Everything happens in this browser, except Remove background" (home). Passport photo now sends every photo it opens to find the face, and sends it to the background model by itself when the colour method gives up. The Passport drop zone has no privacy line; the only notice is the model's note, shown after the upload. `docs/photo-tools.md` already says what really happens | `Index.cshtml` top bar and home; Passport drop zone; Better edges |

## Medium

| # | Finding | Where |
| --- | --- | --- |
| M1 | Placing the face fails without a word. The request has no signal or time limit and is never cancelled when the photo or mode changes, so earlier requests keep the server's two face places; the next is answered busy, and any failure is taken as "nothing found": no message, no Try again, Place the face again stays hidden. While a request hangs, the model is never asked by itself (`faceAsking` stays true). There is no "Placing the face…" state | `findFace()` in `photo-tools-passport.js`; `locateFace()` in `photo-tools-background.js`; `PassportFaceService` |
| M2 | On the server the face request has no time limit of its own and waits on the same lock as the Photos library's People indexing, while holding one of the two places (unverified how long in practice) | `FaceAsync` in `PhotoToolsApi.cs`; `LocateLargestFaceAsync` in `OnnxFaceAnalysisEngine.cs` |
| M3 | Download and Print sheet stay on while the model works: the photo with the old background can be saved or printed during the 30 to 60 s wait | `save()`, `renderSheet()` in `photo-tools-passport.js` |
| M4 | The page keeps the server's status for good once Remove background is available: a lower `MaximumSide`, or face models installed later, are not seen until a reload, and a 413 then clears the photo in Remove background | `loadStatus()`, `requestMask()`, `findSubject()` in `photo-tools-background.js` |
| M5 | Edit photo: Adjust and Straighten show at once but reach the file only after Apply; Download and "Ready" stay on, and changing tool drops the change without a word | `photo-tools-editor.js` |
| M6 | Phones: Hold to see the original, the checks and Download are several screens below the photo | `photo-tools.css` (one column below 900 px), Passport side panel |

## Low

| # | Finding |
| --- | --- |
| L1 | A sideways photo (eye line near ±90°) is turned 15° and zoomed instead of being left for Rotate (`placeFace`) |
| L2 | The face search stops at the first size that finds a face, so a small face found at 640 px can win over the real one (unverified) (`LocateLargestFaceAsync`) |
| L3 | `MaskAsync` and `FaceAsync` repeat about 60 lines (antiforgery, type, size, place, deadline, reading the body); the place class is written twice |
| L4 | The face's upload size, longest side and time limit come from `BackgroundRemovalOptions`; its two places are a constant |
| L5 | `renderOutput()` also sets the background state, and runs every frame while painting on the model's cut-out |
| L6 | The copy sent for the mask and the enlarged mask canvas (up to 16 MB each) are not released |
| L7 | Wording: the stage hint still says "Drag the photo so the face fills the oval" after the face was placed; the home card promises "a white background" while the default is Keep; "How much to change" is vague; "No problems found" can show when the head was not checked |
| L8 | Photo has no pixel size for online forms (Signature has 140 × 60 px) |
| L9 | Nothing warns before leaving with unsaved edits or touch-ups |
| L10 | Touch up works differently on Passport photo and Remove background (separate mode with Undo, Reset and Done, against always painting with Undo in the bar) |
| L11 | Tests: no test for the multi-size face search, the screen races (`findFace`, `findSubject`, `saveRun`), or a 413 after the status was kept |

## Build plan

Each PR is checked live (with the real model and face detector where it touches them), on a phone
and in the Dark theme, and adds tests where the code can be tested.

| PR | Scope |
| --- | --- |
| 1 | This plan |
| 2 | H1: the privacy wording: top bar, home, Passport drop zone ("To place the face, and for a new background when needed, a copy is sent to the PRISM server; nothing is stored"), Better edges says it sends a copy; contract test that the page names both screens |
| 3 | M1, L1: the face request is cancelled with the photo or mode, has a time limit and is tried again once when busy; "Placing the face…" in the stage bar; "The face could not be placed. Try again" when it fails; a sideways photo is left for Rotate |
| 4 | M2, L2, L4: on the server, a time limit for the face and a short wait for the lock that answers busy; the largest face from every size; the face's own small settings section (defaults as today, so nothing changes in production) |
| 5 | M3, L5: Download and Print sheet wait for the model (the result line says why); `renderOutput()` split into drawing and the background state |
| 6 | M4, L6, L11 (413): the status read again with each new photo, and after a 413 or 503 once before giving up; the copy and the mask canvases released |
| 7 | M5: Edit photo applies a pending Adjust or Straighten on Download and on changing tool (or says "Press Apply first"; owner choice, see below) |
| 8 | M6: on phones, Hold to see the original in the bar under the photo, and a small bar with the checks' summary and Download that stays on screen |
| 9 | L7, L8, L9: wording; an "Online form" pixel size for Photo; a warning before leaving with unsaved work |
| 10 | L3, L10, L11: one shared reader for the two endpoints and one place class; Touch up the same on both screens; tests for the screen races |

PRs 2 to 6 are independent of each other; 7 to 10 can follow in any order.

## Owner decisions (14 Nov 2026)

The owner agreed with the recommendations and asked for all ten PRs:

1. **Edit photo, pending changes (PR 7):** applied by themselves on Download and on changing
   tool.
2. **Online form size (PR 9):** a 630 × 810 px size labelled for online uploads, without naming
   any one portal (the figure is common for Indian portals but not confirmed from an official
   page); "Pixels" stays for anything else.
3. **Hand-offs between tools** and **4. deeper checks and camera capture:** separate plans, after
   this one.
