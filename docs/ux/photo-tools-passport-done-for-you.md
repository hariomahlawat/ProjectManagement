# Photo Tools: passport photo, done for you (plan, 14 Nov 2026)

The owner chose three additions on 14 Nov 2026, after the Remove background fixes
([photo-tools-remove-background-fixes.md](photo-tools-remove-background-fixes.md)):

1. **Real background removal in Passport photo.** Today Passport photo changes the background by
   its colour at the edges, which works only on a plain wall. With the Remove background model on
   the PRISM server, any background (an office, a corridor) becomes clean white, light blue or
   another colour, hair included.
2. **The face placed for you.** PRISM already has a face detector on the server (YuNet, used by
   the Photos library). Passport photo asks it where the face is and places and sizes the photo so
   the face sits on the guide; the checks before saving then use the real face instead of an
   estimate from the background.
3. **Choices remembered.** Paper, file type, size limit, background and the like are kept per
   browser, so a clerk doing ten photos in a row sets them once.

Both server features stay optional: when the model or the detector is not set up, Passport photo
works as it does today (colour background, placing by hand), and says so in one line.

## How it works

### 1. Background: colour first, the model when it is needed

Owner decision (14 Nov 2026), after weighing the two: a plain, evenly lit wall needs no 15 to 30 s
wait and no place in the server's one-at-a-time queue, so the colour method stays first, and the
model is used where colour cannot cope.

- Picking a background colour shows the colour method's result at once, as today.
- The model is asked **automatically** when the colour method cannot do it cleanly: the
  background is not plain enough, no plain background was found, or too much was taken as
  background. (Built in PR 5 on these three answers of the colour method; the checks before saving
  do not ask the model by themselves, Better edges is there for that.)
- The model keeps whatever it sees as the subject: a second person, or a machine the person stands
  in front of, stays in the photo. The Erase brush takes it out.
- A **Better edges** button sends the photo to the model when the user is not happy with the
  colour result (a shadowed wall, a white shirt on a white wall, loose hair).
- When the model is used, Passport photo sends the same copy Remove background would (at most
  2048 px) and gets the same mask, **once per photo**; changing the colour afterwards is instant.
- The cut-out uses the same steps as Remove background (old background's tint taken out of hair
  and edges, then the new colour behind), so both screens give the same edge.
- The Touch up brushes (Refine edge, Erase, Restore) work on the model's mask, as in Remove
  background.
- While the mask is on its way the screen shows the photo with "Finding the person…", the same
  "Still working" message and Cancel after 8 s, and keeps the photo with Try again if the server is
  busy (one photo at a time on the production server, owner 14 Nov 2026).
- When the model is not set up, the colour method of today is used, unchanged, and Better edges
  is not shown.
- Privacy is as for Remove background: a copy goes to the PRISM server, is used in memory and is
  not stored. The privacy line under the background choices says so only when the model is used.

### 2. The face placed

- New endpoint `POST /api/photo-tools/face` (signed in, antiforgery token, the same size and type
  limits and the same "nothing stored, only size and time logged" rules as the mask). It runs
  **only the detector**, never the face recogniser, creates no record and does not touch People.
  It answers the largest face's box and its five points (eyes, nose, mouth corners), in the
  photo's pixels, or "no face".
- It uses the Photos library's detector readiness: off when the face models are not installed.
  It is cheap (a 320 px detector, well under a second) and has its own small queue.
- When a photo is opened, Passport photo asks for the face automatically (owner decision) and,
  when one is found, sets the zoom, position and straighten so that:
  - the eyes are level (straighten from the eye line, within the ±15° the screen allows);
  - the chin is on the guide's chin line and the top of the head on its top line (the top of the
    head is estimated from the face box: the detector's box ends at the brow);
  - the face is in the middle across.
  The user can still drag, zoom and straighten; "Place the face again" puts it back.
- The checks before saving use the face box for head size, head position and light on the face,
  instead of the outline of the background. "No face found" is a check of its own.
- When the detector is not set up, nothing changes from today.

### 3. Choices remembered

Kept in the browser (local storage, per computer and user profile; never sent anywhere), read when
the screen opens, and ignored if they no longer make sense:

| Screen | Remembered |
| --- | --- |
| Passport photo | Photo size (or custom size and unit), size limit in KB, background colour, Photo or Signature |
| Print sheet | Paper, PDF or JPG, cut lines |
| Reduce size | Already remembered (target, unit, width and height, file type, black and white) |
| Edit photo | File type and quality |
| Remove background | Background (see-through, colour, blur) and file type |

Photos themselves are never remembered.

## Owner decisions (14 Nov 2026)

1. **Placing the face:** automatically as soon as a photo is opened; the user can still move it,
   and "Place the face again" puts it back.
2. **Background method:** colour first, the model automatically when colour cannot cope, and a
   Better edges button (see 1 above). Not "always the model": that would make every plain-wall
   photo wait 15 to 30 s and queue on the server.
3. **Head size:** about 75% of the photo's height (Indian passport photos ask for 70 to 80%). The
   guide's lines move from 10% / 78% (about 68%) to about 8% / 83%, and the head checks follow.

## Build plan

Each PR is checked live with the real model and detector, on a phone and in the Dark theme, and adds
tests.

| PR | Scope |
| --- | --- |
| 1 | This plan |
| 2 | Choices remembered on all five screens (a small shared helper in `photo-tools-canvas.js`) |
| 3 | Server: the face endpoint on the detector only, with its own readiness, limits, queue and tests |
| 4 | Passport photo: the face placed and straightened on open, "Place the face again", the guide at about 75% head height, and checks from the real face |
| 5 | Passport photo: the model when colour cannot cope and on Better edges, with the Remove background edge steps, brushes on the mask, waiting and Try again |
| 6 | Phones, Dark theme, keyboard and the docs (`docs/photo-tools.md`, the install guides) |
