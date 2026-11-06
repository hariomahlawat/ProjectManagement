# Confirmation dialogs

Every "are you sure?" in PRISM uses one dialog: `window.PrismConfirm.show(options)`. It is a
glass sheet over a dimmed, softly blurred page, in the same Liquid Glass style as the
[public pages](public-pages.md). Phones get a bottom sheet with stacked buttons.

The browser's own `confirm()` box ("localhost says…", OK/Cancel) is not used for new work.
The conversion plan for the remaining places is at the end of this page.

## Using it

`_Layout` renders the dialog (`Pages/Shared/_PrismConfirmDialog.cshtml`) and loads its script
and styles on every page, so a page only calls it:

```js
const confirmed = await window.PrismConfirm.show({
    tone: "danger",
    title: "Delete this document?",
    message: "It is removed from the project and moves to Trash.",
    subject: { icon: "bi-file-earmark-pdf", name: "Technical Evaluation Report.pdf", meta: "2.4 MB · uploaded 12 Oct 2026" },
    note: "An administrator can restore it from Trash for 30 days.",
    confirmText: "Delete document",
    cancelText: "Keep document"
});
if (!confirmed) return;
```

| Option | Use |
| --- | --- |
| `title` | The question, naming the action and the thing: "Delete this document?" |
| `message` | What happens: what is lost, who is affected. One or two sentences. |
| `detail` | Optional smaller line under the message. |
| `subject` | `{ name, meta, icon }`: the record the action applies to, so nobody acts on the wrong one. |
| `note` | A short line under the subject, usually how to undo. |
| `irreversible` | `true` shows "This cannot be undone." (or `note`) in the tone colour. |
| `tone` | `primary` (ask), `warning` (amber: reversible but disruptive), `danger` (red: removes or destroys). |
| `icon` | A Bootstrap Icons name to replace the tone's icon (`bi-megaphone` for publish). |
| `confirmText`, `cancelText` | Button labels. |
| `reason` | `{ label, placeholder, hint, minLength }`: require a written reason (default 10 characters). The promise then resolves to the trimmed reason text instead of `true`. |
| `confirmPhrase` | Text the user must type exactly (a username, a project code) before the action unlocks. |

It resolves `false` on Cancel, the close button, Esc or a click outside the sheet. Opening a
second confirmation cancels the first. Inside an open Bootstrap modal or offcanvas the dialog
is moved into it for the time it is open, so focus trapping keeps working.

### Without writing script: `data-confirm`

A form, a submit button or a link can ask in the same dialog with attributes alone
(`wwwroot/js/core/declarative-actions.js`):

```html
<form method="post" asp-page-handler="Delete"
      data-confirm="Delete this visit to SDD? The record and all its photos will be removed. This cannot be undone.">
```

- `data-confirm` is "Question? Consequence.": the text up to the first `?` becomes the title
  and the rest the message. One question with nothing after it is a title alone.
- The tone follows the wording: delete, remove, purge, clear, discard or "cannot be undone" is
  `danger`; withdraw, disconnect, archive, deactivate, unlink, merge, retry or rebuild is
  `warning`; anything else is `primary`.
- The button is the title without "this / the / your" ("Delete this task?" gives "Delete
  task"), or just its verb when that would be longer than 32 characters.
- Override any of it on the same element: `data-confirm-title`, `data-confirm-detail`,
  `data-confirm-action` (button label) and `data-confirm-tone`.
- The submit (or link) is held while the dialog is open and replayed once on confirm, with
  the button that was pressed, so form listeners such as disable-on-submit run only then.
- If the dialog script failed to load, the browser's own box still guards the action.
- An element with `data-page-handled` is left to its page script.

The server still decides. A confirmation is a courtesy to the user, never an authorization
check: every action it guards must be validated and authorized server-side.

## Wording rules

- **Title:** a question that names the action and the object. "Withdraw the plan from
  approval?", not "Confirm action" or "Are you sure?".
- **Message:** the consequence in plain words. Say who else is affected and whether it can
  be undone. No "please" and no exclamation marks.
- **Buttons:** verbs naming the action, "Delete document" and "Keep document" or "Cancel".
  Never OK / Yes / Confirm.
- **Tone:** match the consequence, not the anxiety. Red only when something is removed or
  destroyed.
- **Subject:** show it whenever the action applies to one record (file name, project name,
  user), with a line of meta that tells similar records apart.
- **Heavy actions:** permanent purges ask for a `reason` (it goes to the audit trail
  server-side); deleting an account or another hard-to-rebuild record can ask for a
  `confirmPhrase`.

## Behaviour

- **Focus:** the reason or phrase box when there is one; otherwise **Cancel** for `danger`
  (Enter never deletes by accident) and the action for `primary` and `warning`.
- **Keys:** Esc cancels; Enter in the phrase box and Ctrl/Cmd+Enter in the reason box confirm
  once the input is valid.
- **Motion:** the page dims and blurs, the sheet springs in (slightly lower and smaller,
  sharpening from a blur) and the icon pops a beat later. On phones the sheet slides up.
  Reduce Motion removes the motion; Reduce Transparency makes the sheet solid.
- **Theme:** site tokens only (`--pm-*`), so Light and Dark follow the site palette.

## Conversion plan

Surveyed 6 Nov 2026. PRs 1 (#2926), 2 (#2929), 3 (#2931), 4 (#2933) and 5 are done: no PRISM
script calls the browser `confirm()` box any more; the 8 `data-confirm` elements
marked `data-page-handled` follow their page scripts in the PRs below. The rest move one area
at a time so each change stays testable.

The browser `confirm()` returns synchronously and the dialog does not, so each call site has
to wait for the answer rather than simply swap the function:

- **Plain handlers** become `async` and `await` the answer, then re-check that the action is
  still possible (the page may have changed while the dialog was open).
- **Events that need an answer now** (`hide.bs.offcanvas`, `show.bs.tab`, a form `submit`, a
  link click) are always held (`preventDefault`), the question asked, and the action run again
  on yes: `Offcanvas.hide()`, `Tab.show()`, `form.requestSubmit(submitter)` or
  `location.assign(href)`. A one-shot flag lets the replay through.
- **Timers** that the old blocking box froze must be held too. The planner pauses its
  Durations autosave while it asks whether to post one tab only (`plan:hold-autosave`,
  `plan:resume-autosave`), so a save cannot make the other tab's edit token stale.
- Each converted script has a one-line `askConfirm` that falls back to the browser box only
  if the dialog script is missing.

| PR | Scope |
| --- | --- |
| 1 | Glass `PrismConfirm` on every page, this guide, tests |
| 2 | `data-confirm` (about 32 places, `core/declarative-actions.js`) through `PrismConfirm`, with `data-confirm-title` / `-tone` / `-action` for better wording |
| 3 | Browser `confirm()` in Projects: plan edit, remarks, Overview (ToT, multi-JDP), content, brochure, compendium, completed summary, photo reorder |
| 4 | Browser `confirm()` in Office records: IPR, visits, social media, ARPP, proliferation, industry partners. Visits and Social media drop their own Bootstrap confirm modals; their forms, IPR's and Industry partners' now use `data-confirm` |
| 5 | Browser `confirm()` in Teamwork and media: calendar, comments, to-do (the layout's `appConfirmModal` is removed), action-task remarks, activities, photos, video library, PDF tools, admin media; the last fallbacks in Project Ideas and the Briefing library |
| 6 | Bootstrap confirm modals (about 19): convert the simple ones; give the ones with forms the same glass look; move the Notebook and Briefing Studio dialogs onto `PrismConfirm` |

`npm test` fails if any PRISM script calls the browser `confirm()` directly
(`projects/app-no-native-confirm.test.js`); only the one-line `askConfirm` fallbacks and the
`data-confirm` fallback in `core/declarative-actions.js` are allowed. The Notebook keeps its own
check (`notebook-no-native-confirm.test.js`) until PR 6 moves its dialog onto `PrismConfirm`.