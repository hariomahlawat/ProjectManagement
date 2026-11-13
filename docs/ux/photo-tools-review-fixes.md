# Photo Tools: review (13 Nov 2026) and fix plan

Three reviewers read Photo Tools (`/Tools/Photo`) cold at `0602d8b` (before #3182):

- **Core:** the maths and pixel work in `photo-tools-core.js` and its tests, checked with scripts in Node.
- **Screens:** `photo-tools-app.js`, `photo-tools-passport.js`, `photo-tools-print.js`,
  `photo-tools-editor.js` and `photo-tools-canvas.js`.
- **Page:** `Pages/Tools/Photo/Index.cshtml`, the page model, `photo-tools.css`, the contract test
  and the docs, compared with PDF Tools.

**Not covered:** Remove background (#3182, merged after the review: `photo-tools-background.js`,
`Features/PhotoTools`). It shares the Refine Edge brushes with Passport photo, so PRs 2, 5 and 7
check it as well.

**Verdict:** UX 7/10, code 7.5/10, nothing High. There are no privacy problems: nothing goes over
the network apart from Remove background's own calls, pdf-lib is shipped, the page is `[Authorize]`
with `OnGet` only, and the CSS uses tokens only. All 72 Node tests passed. The findings are about
pointers and re-entry, accessibility, a few edge cases in the maths, and wording.

## Fix first

| # | Finding | Where |
| --- | --- | --- |
| 1 | Print sheet: Download turns back on while the file is still being made (any `render()` sets `disabled = !copy \|\| placed === 0`). Changing the paper or Cut lines meanwhile gives a PDF with the old layout, the new cut lines and the new paper's name, and a second press makes a second file | `download()`, `sheetPdf()`, `render()` in `photo-tools-print.js` |
| 2 | Edit photo and Print sheet accept any pointer. Two fingers in Mark up draw one zig-zag stroke, a second finger makes the crop box or the print picture jump, and a right-click places a Number or Text mark. Passport's brush already checks `isPrimary` and the pointer id | `photo-tools-editor.js` (mark up, crop, keyboard point), `photo-tools-print.js` (drag) |
| 3 | Changing screens moves neither the focus nor the tab title, so keyboard and screen-reader users are not told the screen changed. PDF Tools does this in `route()` | `showView()` in `photo-tools-app.js` |

## Medium

| # | Finding |
| --- | --- |
| 4 | Passport: after a new photo or a quarter turn, the old brush panel, "Touch up edges" and "See original" stay up until the background is worked out again (seconds on a big photo); `usePhoto` never calls `renderBackground()` |
| 5 | Passport: Rotate makes a full-size canvas with no `try`; on a phone over the canvas limit the button silently does nothing |
| 6 | Core: `measureLighting`, `measurePaper` and `cleanDocument` count a pixel at alpha 1 as fully as a solid one, so Auto enhance and Fix colour cast on a PNG/WEBP with soft edges are steered by pixels nobody sees (checked in Node: one invisible yellow pixel gives gains `[0.86, 0.86, 1.25]`) |
| 7 | The home cards are `<a role="listitem">`, so screen readers announce list items, not links (PDF Tools has the same markup) |
| 8 | Signature is a mode inside Passport photo; no home card mentions signatures, so someone who needs a signature file will not find it |
| 9 | `docs/photo-tools.md` says no third-party library is used (Print sheet uses pdf-lib) and that the print/background plan "is being built" |

## Low

- **Core:**
  - `formatBytes` rounds to the nearest KB, so 51,300 bytes reads "50 KB" and a message can say "cannot be made smaller than 50 KB, which is over 50 KB"; 1,048,575 bytes reads "1024 KB".
  - `parseTargetBytes` reads `1,500` as fifteen hundred even for MB (1.5 GB), and rejects Indian grouping (`1,00,000`).
  - `outputNames` can repeat a name when an input already looks like a generated one (latent: the only caller adds `-small`).
  - `encodeToTarget` can halve the short side below the 16 px floor.
  - Edit photo allows 10000 × 10000 px, far over a phone's canvas area; cap the area, not each side.
  - A fixed-shape crop can be off by one pixel (`tidyCrop` rounds each side on its own).
- **Screens:**
  - The brush rebuilds a full-size mask on every pointer move.
  - Redo replays every step from the original instead of applying one.
  - Reduce size cannot cancel a photo that is being worked on when the settings change.
  - Double-clicking Download in Reduce size, Passport and Edit saves the file twice.
- **Page:**
  - The crop handles are 32 px on touch screens; the docs promise 44 px.
  - "Refine Edge" probably overflows its button at 375 px (to be checked on screen).
  - The phone text "Hold for original" does not match its `aria-label` "Hold to see the original".
  - The cards slide on hover with no `prefers-reduced-motion` rule (shared `pdf-tools.css`).
- **Wording:**
  - Blurring is "blur" on the home card and "Hide" on the button.
  - "Refine Edge" is in title case.
  - "px", "300 dpi", "WEBP" and "Dimensions" have no plain hint.
  - The print copies counter shows 0 before a photo is chosen.
- **Docs:** `docs/ux/photo-tools.md` still lists partials, "three large choices", a PNG "offered as JPG" and a large-photo confirmation; none of these exist.
- **Contract test:**
  - The script list is fixed in the test.
  - The URL check matches double quotes only.
  - The CSS is not checked for outside addresses or `@import`.
  - `EventSource`, `import(` and `importScripts` are not looked for.
  - A PDF Tools test lives in the Photo Tools file.
- **Duplication:** opening a file, the drop zones, the debounced save, "hold to see the original" and the dark-under-light overlay are written three or four times.

## For the owner

1. **Signature:** give it its own card on the home screen that opens Passport photo in Signature
   mode (recommended), or only mention signatures in the Passport photo card?
2. **What "50 KB" means:** many upload forms check 50,000 bytes, others 51,200. Aim under both
   (recommended: the file is at most 1–2 KB smaller), or keep 1 KB = 1024 bytes?
3. **Reduce size default:** photos shrink to 1,600 px on the longest side even when only a KB
   target is typed. Keep it, or keep the pixels unless the target cannot be reached otherwise?

## Build plan

Each PR is checked on a phone, in the Dark theme and with the keyboard, and adds tests for what it
fixes.

| PR | Scope | Findings |
| --- | --- | --- |
| 1 | This plan | — |
| 2 | Pointers and re-entry: primary pointer and left button only in Edit photo and Print sheet; Print sheet takes its settings before the first `await` and keeps Download off while working; one download at a time in every tool; Passport resets the brush screen on a new photo and catches Rotate failures | 1, 2, 4, 5, Low (double click) |
| 3 | Built. Accessibility (the tab title also follows Signature mode): focus and title on each screen, home cards as a real list of links (PDF Tools too), 44 px crop handles on touch, `aria-label` that matches the text, reduced motion | 3, 7, Low (Page) |
| 4 | Core edge cases: alpha-weighted measuring (one shared helper), KB rounding and parsing (after owner question 2), unique output names, 16 px floor, canvas area cap, exact crop shape | 6, Low (Core) |
| 5 | Wording and Signature: owner question 1, one name for blurring, sentence case, plain hints for pixels and file types, copies counter | 8, Low (Wording) |
| 6 | Docs and contract test: correct both docs, read scripts from the page, check the CSS, more patterns, move the PDF test | 9, Low (Docs, Contract test) |
| 7 | Speed and shared code: coalesce brush moves, one-step Redo, cancel Reduce size between tries, shared open/drop/save/hold helpers in `photo-tools-canvas.js` | Low (Screens, Duplication) |
| 8 | Reduce size default, if the owner changes it (question 3) | — |
