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

### Undo from the server

When the server can really reverse what it just did, offer it in the toast:

```csharp
TempData.ToastWithAction("success", "Idea archived.", new ToastAction(
    "Undo",
    Url.Page("/ProjectIdeas/Details", "Restore", new { id })!,
    new Dictionary<string, string> { ["rowVersion"] = Convert.ToBase64String(Idea.RowVersion) }));
```

- The button is a real form (`Infrastructure/Ui/TempDataToastExtensions.cs`, rendered by
  `Views/Shared/_Toasts.cshtml`): it posts the fields with the antiforgery token to the handler
  that the page's own Restore button uses, so permission and the record version are checked
  again. Only local URLs are rendered.
- A toast with an action stays 10 seconds instead of 5.5.
- Offer it only for actions with a working reverse handler that the same user may run.

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
| `sticky` | `true`: no timer; it waits until dismissed or its action is used (Tasks "Task marked done." with Undo). |

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
| 2 | Done: the 14 scripts with their own toasts hand their messages to `PrismToast` and keep their old code only as a fallback (visits, social media, proliferation and its data-quality panel, JDP, remarks, stages, process flow, Document Repository and its upload queue, Briefing desk). Calendar (moved or edited events), Tasks (marked done) and the Notebook keep their **Undo** as the toast's action. The Document Repository used to wait for a global `showToast` that PRISM never defined; its messages now show. |
| 3 | Done: server toasts can carry one action (`ToastWithAction`). Project Ideas offers **Undo** after archiving (restore) and after deleting (restore from Deleted ideas). Briefing Studio offers **Undo** after deleting a deck (from the Studio or the library), posting to the Restore handler that Recently deleted uses. Document Repository trash restore is admin-only, so it gets no Undo for other users. |
