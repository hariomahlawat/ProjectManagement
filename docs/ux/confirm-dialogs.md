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

Surveyed 6 Nov 2026. PR 1 (this page, the glass dialog on every page) is done; the rest move
one area at a time so each change stays testable. The browser `confirm()` returns
synchronously and the dialog does not, so each call site has to wait for the answer rather
than simply swap the function.

| PR | Scope |
| --- | --- |
| 1 | Glass `PrismConfirm` on every page, this guide, tests |
| 2 | `data-confirm` (about 32 places, `core/declarative-actions.js`) through `PrismConfirm`, with `data-confirm-title` / `-tone` / `-action` for better wording |
| 3 | Browser `confirm()` in Projects: plan edit, remarks, Overview (ToT, multi-JDP), content, brochure, compendium, completed summary, photo reorder |
| 4 | Browser `confirm()` in Office records: IPR, visits, social media, ARPP, proliferation, industry partners |
| 5 | Browser `confirm()` in Teamwork and media: calendar, comments, to-do (and the layout's `appConfirmModal`), tasks, activities, photos, video library, PDF tools, admin media |
| 6 | Bootstrap confirm modals (about 19): convert the simple ones; give the ones with forms the same glass look; move the Notebook and Briefing Studio dialogs onto `PrismConfirm` |

After PR 5 an `npm test` check fails on any new browser `confirm()` (the Notebook already has
one: `notebook-no-native-confirm.test.js`).
