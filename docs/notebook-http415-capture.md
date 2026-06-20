# Notebook HTTP 415 Capture Notes

## SECTION: Capture status

Chrome DevTools capture could not be performed in this non-interactive build container because no running browser session or authenticated Notebook page is available. The pre-fix source path that generated the suspected request was recorded before modification instead:

- Entry script: `wwwroot/js/pages/notebook-index.js`
- API module import path: `wwwroot/js/notebook/notebook-api.js`
- Mutation method: `PATCH /api/notebook/items/{id}`
- Pre-fix wrapper behavior: plain-object headers with a case-sensitive `headers['Content-Type']` check and implicit JSON inference from `options.body`
- Expected failed status when the browser sends no supported media type: `415 Unsupported Media Type`

## SECTION: Manual capture checklist for deployment verification

Before production deployment, reproduce an existing-note edit and record the Network panel values for Request URL, Request Method, Request Headers, `Content-Type`, `RequestVerificationToken`, Request Payload, Response Status, Response Headers, Response Body, initiator script, loaded bundle URL, and browser cache status. The fixed request must show `Content-Type: application/json; charset=utf-8` and load only the versioned Notebook bundle.
