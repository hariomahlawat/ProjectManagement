# Notebook HTTP 415 capture notes

## Background (resolved)

Notebook mutation endpoints in `Controllers/Api/NotebookController` are declared
`[Consumes("application/json")]`. An earlier client wrapper used plain-object headers with a
case-sensitive `headers['Content-Type']` check, so some requests (for example
`PATCH /api/notebook/items/{id}`) could be sent without a JSON content type and were rejected with
`415 Unsupported Media Type`. No browser capture was taken at the time; the diagnosis was made from
source.

## Current client behaviour

`wwwroot/js/notebook/notebook-api.js` now:
- builds headers with the `Headers` class (case-insensitive);
- sets `Content-Type: application/json; charset=utf-8` whenever a non-`FormData` body is sent;
- sends the antiforgery token in the `X-CSRF-TOKEN` header for unsafe methods (the header name
  configured in `Program.cs` `AddAntiforgery`), reading it from
  `#notebook-antiforgery-token input[name="__RequestVerificationToken"]`.

Note that some bodiless-looking actions (for example `POST items/{id}/archive`,
`DELETE items/{id}/permanent`) still require a JSON body carrying the item `version`.

## Manual verification checklist

Reproduce an existing-note edit in the browser Network panel and confirm:
- Request URL/method match the endpoint (for example `PATCH /api/notebook/items/{id}`).
- `Content-Type: application/json; charset=utf-8` and an `X-CSRF-TOKEN` header are present.
- The payload includes the current `version`.
- The response is 200 (or 409 with the current item on a version conflict), not 415/400.
- Only the versioned bundle `/dist/notebook-index.bundle.js?v=...` is loaded (a stale cached bundle
  is the usual cause of old client behaviour; see `docs/notebook.md` for the bundle pipeline).
