# Phase 42 acceptance tests

Use a Triptych cover with three usable photographs initially shown as `A / B / C`.

| Test | Action | Required result |
|---|---|---|
| Direct slot edit | Change Supporting image 1 to photograph D | Proof becomes `A / D / C`; Hero and Supporting image 2 remain byte-for-byte the same assignment |
| Save/reload | Save, leave the editor, reopen it | `A / D / C` remains assigned |
| Preview parity | Generate Preview PDF | The PDF cover uses `A / D / C` |
| Download parity | Complete review and download | The issued PDF uses the same `A / D / C` cover |
| Automatic target only | Set Supporting image 1 to Automatic | Only Supporting image 1 is resolved again |
| No image target only | Set optional Supporting image 1 to No image | Hero and Supporting image 2 remain unchanged |
| Explicit precedence | Manually choose a photo currently used by an automatic slot | The manual slot keeps it; only the conflicting automatic slot is repaired |
| Explicit refresh | Click Refresh automatic | All visible Automatic slots on the current Front/Back surface may be re-ranked; manual slots remain unchanged |
| Template round trip | Triptych → Quartet → Triptych | Hidden slot assignments survive; Quartet repairs only duplicates/unavailable automatic slots |
| Quartet duplicate | Attempt to choose a photo already used by another Quartet slot | The photo is visibly unavailable and cannot be selected |
| Front/back independence | Edit any front slot | Back-cover assignments remain unchanged, and vice versa |
| Temporary request failure | Interrupt a preview-photo request | Stored slot IDs remain intact; no unrelated automatic reshuffle occurs |

For the deployment smoke test, also run `ProjectManagement.exe --compendium-offline-self-test` from the exact IIS payload folder before attaching it to the site.
