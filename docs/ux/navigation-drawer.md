# Navigation drawer (Menu)

The drawer opened by **Menu** in the top bar. It lists every PRISM area the signed-in user may open, in a few fixed sections. Below 768px the top tabs are hidden and the drawer is the only primary navigation, so it must hold every top-bar area.

## Where it lives

| Part | Files |
| --- | --- |
| What is listed, for whom | `Services/Navigation/RoleBasedNavigationProvider.cs` (only the drawer uses it) |
| Shared definitions it reads | `Services/Navigation/ModuleNav/ProjectModuleNavDefinition.cs` (the Projects sub-nav), `Services/Navigation/ModuleNav/AdminNavigationCatalog.cs` (Administration) |
| Active item, URLs | `ViewComponents/NavigationDrawerViewComponent.cs` (active state via `ModuleSubNavActiveMatcher`, the sub-nav's rule) |
| Markup | `Pages/Shared/Components/NavigationDrawer/Default.cshtml`, `_NavigationDrawerLink.cshtml` |
| Behaviour | `wwwroot/js/navigation/drawer.js` (tests: `drawer.test.js`) |
| Styles | `wwwroot/css/site.css` ("Navigation drawer"), `wwwroot/css/navigation-shell.css` ("Navigation drawer hardening", sections, footer, reduced motion) |
| Tests | `RoleBasedNavigationProviderTests`, `NavigationDrawerTests`, `NavigationReachabilityTests` |

## Sections

Every top-level item is a section with a `Key` from `NavigationSectionKeys`; every child is a link. There is no third level.

| Section | Contents | Notes |
| --- | --- | --- |
| Main | Dashboard, My Workspace, Calendar, Notebook, Photos, FFC (the map), Documents (unread badge) | The top tabs, in their order. Always open, not collapsible. |
| Projects | The Projects sub-nav's destinations in its order, then Proliferation compendium, Project ideas, IT projects, then Pending approvals (badge) | Built from `ProjectModuleNavDefinition` minus items marked `IsAction` (Create project stays a sub-nav button). The extras live in the provider, not in the shared definition, so they do not become Projects tabs. |
| Trackers | Task management, Progress review, Visits, Social media, Training (badge), ToT, Proliferation, IPR register, Activities, ARPP administration; Activity types for a HoD who is not Admin | "ARPP administration" is named apart from the Projects section's "ARPP / PPP" library. |
| Tools | PDF Tools (new tab) | Standalone tools outside the PRISM shell. |
| Administration | Home, Users, Audit log, System health, Every admin page | Admins only (`AdminPolicies.Access`). Names, pages and policies come from the catalog by key; Administration's own sidebar and index list the rest. |

A section whose links are all unauthorized is dropped rather than shown empty.

## Rules for adding an item

1. **List areas, not a module's internal pages.** Settings, bins and sub-pages open from the page that owns them (the repository page's menu, the Visits page, Administration's sidebar). Add a link here only for a destination users start from.
2. **No actions.** Commands such as *Create project* belong on the module's sub-nav or page.
3. **Keep the destination's authorization.** Give the link the same `AuthorizationPolicy` or `RequiredRoles` the page enforces. Hiding a link is never the authorization; the page must still check.
4. **Set `ActivePagePrefix`** when the area has more than one page, so its detail and edit pages light the link. Check the prefix cannot match a sibling's pages in the same area.
5. **One icon per link.** `Navigation_IsFixedSectionsOfLinksWithNoDeeperNesting` fails on a repeated icon or destination.
6. **Removing a link?** Add a row to `NavigationReachabilityTests` naming where the page is still linked from, so no role loses its only way in.

## Behaviour

- Only the section holding the current page starts open (Main is always open). Opening a section closes the others (`drawer.js`, the `toggle` event on `details[data-drawer-section]`).
- On open, focus goes to the current page's link when it is visible, otherwise to the first control (the close button). Tab is trapped in the panel; Escape and the overlay close it and focus returns to Menu.
- The drawer is moved to `<body>` so the sticky top bar cannot clip it. The page behind does not scroll while it is open.
- `prefers-reduced-motion` turns off the slide and fade.
- Sections are `<details>`, so they still open and close without JavaScript.

## Compatibility

`drawer.js` is imported by `site.js` without a version stamp, so a browser can briefly combine new markup with a cached older script, or the reverse. Keep the `data-drawer-*` hooks stable (`NavigationDrawerTests` lists them) and add new behaviour so that a missing piece degrades to the plain `<details>` behaviour.

## History

- Nov 2026, PR 1 (#3049): bug fixes (dark-mode close button, dialog name, invalid `aria-hidden`, prefix-aware active state, empty sections, the dead *Project office reports* landing link, reduced motion).
- Nov 2026, PR 2: the section layout above. Before it, the drawer had 61 entries for an admin, every group open, three nesting levels and no Workspace, Notebook or FFC map links.
- Planned, PR 3: a "Find a page…" filter over the drawer's links and remembering the last opened section.
