# FFC Simulators Map – Final UX Spec (Dashboard Widget)

## Overview
Use this document as the single-source spec for the dashboard FFC simulators map widget and the full-map page. The goals are consistent tooltip behaviour across pins and country polygons, and a compact below-map country list built from the same dataset as the markers.

## A. Pins and Hover Behaviour

- **Pin style**
  - Keep existing yellow-ring teardrop pins with white fill and dark count text.
  - Fixed size (approx. 28×34 px) and not scaled by map zoom.
- **Unified hover tooltip**
  - One tooltip design per country; the same tooltip appears when hovering either the country polygon or the pin.
  - Tooltip content includes: country name; Installed X; Delivered Y; Total = X + Y; optionally key simulators and latest install year.
- **Hover interactions**
  - Hover on pin or polygon: show tooltip anchored near the pin (or cursor) with a small arrow; slightly scale pin (~1.05) with subtle glow/shadow; highlight country polygon with soft fill.
  - Mouse leave from pin or polygon: hide tooltip after a 150–200 ms delay to prevent flicker while crossing between pin and country.
- **Implementation hint**
  - Centralize tooltip control:
    - `showCountryTooltip(countryCode: string, anchor: { x: number; y: number })`
    - `hideCountryTooltip()`
  - `mouseenter` on both polygon and pin should call `showCountryTooltip` with the same `countryCode` and appropriate anchor. `mouseleave` from either calls `hideCountryTooltip`.

## B. Below-Map Country List (“At a Glance”)

- **Which countries**
  - Include only countries where Installed + Delivered > 0.
  - Sort by Total (descending), then country name.
- **Layout**
  - Compact table under the map within the same card. Recommended columns: Country | Installed | Delivered | Total.
  - Use XS/SM body text to keep focus on the map. Cap height around 180–200 px with internal scrollbar when needed.
- **Interactions**
  - Row hover: highlight corresponding pin/country on the map (same effect as pin hover).
  - Row click: center the map on that country and mark it selected (tooltip shown).
- **Data source**
  - Build the list from the same in-memory dataset used for markers; no extra API call.
- **Optional**
  - Consider a future “View full list →” link under the table that opens the full-map page with an expanded table.

## C. Dashboard vs Full-Page Map Behaviour

- **Dashboard widget (current card)**
  - Shows pins with unified tooltip behaviour.
  - Includes the compact country list below the map.
  - Only one tooltip visible at a time.
- **Full map page (opened via “View full map”)**
  - Same pin + tooltip behaviour.
  - Larger map with filters (year, category, etc.) and a full-height country table on the side.

## D. Mini Dev Checklist

1. Route pin and country polygon events through shared `showCountryTooltip` / `hideCountryTooltip` functions.
2. Build the below-map country list from the marker dataset and sort as specified.
3. Wire row hover/click to trigger the same highlight/tooltip logic and recenter as needed.
4. Keep the widget height so that “My Projects” remains visible above the fold.

## Notes
- No inline scripts; keep CSP compliance in mind.
- Use section comments in code for clarity and faster replacement.
- Follow modular, best-practice UI/UX approaches; do not break existing functionality when implementing.
