# Legacy training entry wireframes

The following mock-ups outline the proposed legacy entry experience within the training management form. They focus on providing a lightweight toggle for migrations while keeping the roster workflow intact for new records.

## 1. Entry header state

```
┌─────────────────────────────────────────────┐
│ Training type: [dropdown             v]     │
│ Schedule mode: (• Exact dates) (  Month )    │
│ Legacy record: [ ]                           │
│  "Use this when migrating totals without a  │
│     roster"                                  │
└─────────────────────────────────────────────┘
```

* The **Legacy record** option appears beneath the primary selectors as a quiet checkbox with helper text.
* Leaving the box unchecked keeps the roster workflow fully visible.

## 2. Legacy record enabled

```
┌──────────────────────────────────────────────┐
│ Legacy record: [x]                           │
│ ──────────────────────────────────────────── │
│ Legacy attendees (inline inputs)             │
│  Officers: [ 12 ]  JCOs: [ 3 ]  ORs: [ 58 ]  │
│                                              │
│ ℹ️  Legacy records capture totals only.       │
│     The roster panel is hidden in this mode. │
└──────────────────────────────────────────────┘
```

* Enabling the option reveals a contextual hint and subtly hides roster management.
* Manual counts remain editable so historical totals can be entered quickly.

## 3. Roster workflow (default)

```
┌──────────────────────────────────────────────┐
│ Roster summary                               │
│  Officers: 4   JCOs: 1   ORs: 15  Total: 20  │
│  Source: Roster                              │
│                                              │
│ [ Add row ]  [ Paste ]  [ Save roster ]      │
└──────────────────────────────────────────────┘
```

* When **Legacy record** is unchecked the existing roster interactions remain unchanged.
* The hint text collapses to keep the page compact.

These sketches support discussions with stakeholders and guide the implementation of the “Legacy record” affordance without overhauling the roster tooling.
