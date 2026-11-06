# Toasts

A toast is the "done" half of an action: a short message after something happened ("Remark
saved.", "Bid Opening Minutes.pdf moved to Trash."). PRISM has one toast, in the same Liquid Glass
style as the [confirmation dialog](confirm-dialogs.md): a frosted pill at the bottom centre of the
screen with a tinted icon, one sentence and at most one action.

## From the server

Set one of these in a page handler; `_Layout` renders it on the next page through
`Views/Shared/_Toasts.cshtml`:

| TempData key | Tone |
| --- | --- |
| `ToastSuccess` | success (green tick) |
| `ToastMessage`, `ToastInfo` | info (blue) |
| `ToastWarning` | warning (amber) |
| `ToastError` | danger (red), stays until dismissed |
| `ToastJson` | several at once (`Infrastructure/Ui/TempDataToastExtensions.cs`) |

The markup is the finished toast, so it shows even if the script has not loaded.

## From a script

```js
window.PrismToast.show({
    tone: "success",
    subject: "Bid Opening Minutes.pdf",
    text: "moved to Trash.",
    action: { label: "Undo", run: () => restore(id) }
});
```

| Option | Use |
| --- | --- |
| `tone` | `success`, `info`, `warning` or `danger` (`error` is read as `danger`). |
| `text` | One sentence, ending with a full stop. |
| `subject` | Optional bold lead-in, usually the record's name. |
| `detail` | Optional smaller second line: what happens next, or why it failed. |
| `action` | Optional `{ label, run }` or `{ label, href }`: one button such as **Undo**, **View**, **Retry**. |
| `life` | Milliseconds before it fades: 4500 by default, 7000 with an action. |

It returns `{ element, dismiss }`. Text is always set as text, never as markup.

## Behaviour

- **Lifetime:** success, info and warning fade on their own; a thin line in the tone shows the
  time left. Errors stay until dismissed (close button, or Esc when the toast has focus).
- **Pausing:** hovering the stack or moving focus into it pauses every timer.
- **Stacking:** up to three show, newest in front; older ones tuck behind, fainter and smaller.
  An error stays in front until it is dismissed. Hovering fans the stack out so each can be read.
- **Announcements:** errors use `role="alert"`; the rest `role="status"`.
- **Phones:** the stack spans the bottom of the screen, above the home indicator.
- **Reduce Motion** removes the motion and the timer line; **Reduce Transparency** makes the pill
  solid. Colours are site tokens only (`--pm-*`).

## Wording

- Say what happened, in the past tense, about the thing: "Remark saved.", "3 files uploaded.",
  not "Success!" or "Operation completed successfully".
- Errors say what failed and what is safe: "Couldn't save the remark." + "The server could not
  be reached. Your text is kept." Offer **Retry** when retrying is possible.
- Offer **Undo** only when the server can really undo (restore from Trash, re-open); never a
  promise the system cannot keep.
- No exclamation marks; no titles such as "Success" or "Action required".

## Conversion plan

| PR | Scope |
| --- | --- |
| 1 | `PrismToast` on every page; server messages (`_Toasts.cshtml`) in the glass toast; this guide |
| 2 | The scripts that build their own toasts move onto `PrismToast` (visits, social media, proliferation, JDP, remarks, stages, process flow, calendar, Document Repository, Briefing desk, tasks) |
| 3 | **Undo** where the server can restore (for example Document Repository trash) |
