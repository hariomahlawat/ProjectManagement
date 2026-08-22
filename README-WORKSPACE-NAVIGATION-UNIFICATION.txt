PRISM ERP — Workspace Navigation Unification
============================================

Purpose
-------
This package removes the high-friction "two separate workspace menus" behaviour for users
who hold both Project Officer and Command (Comdt/HoD) access.

The workspace now has one stable, permission-aware navigation rail. Role/lens selection
changes the landing context only; it no longer changes which authorised navigation items
exist in the rail.

Implemented behaviour
---------------------
1. Dual-role users always see both navigation groups:
   - My work
     * Overview
     * Action queue
     * My conference review
     * Assigned projects
     * Assigned tasks
     * My ideas
     * Follow-ups
   - Command oversight
     * Officer workload
     * Project portfolio
     * Conference review
     * Briefing decks
   - System adoption
     * ERP adoption
     * ERP usage pattern
   - My resources
     * My documents (when available)
     * My ERP activity

2. Single-role users see only the groups they are authorised to use. No disabled/dead menu
   items are introduced.

3. The old "Workspace mode" navigation rows have been removed. A compact Personal | Command
   lens control now lives in the rail header for dual-role users. It is a landing-context
   selector, not a permission gate.

4. The same rail is used by:
   - /Workspace personal pages
   - /Workspace command pages
   - /Workspace/Conference
   - /Workspace/BriefingDecks

5. Personal and command badges remain available across contexts. The opposite navigation
   shell is loaded so a dual-role user does not need to switch modes merely to discover
   assigned work or command portfolio counts.

6. Rail expansion/collapse uses one preference key:
      prism.workspace.navigationExpanded
   Therefore the rail no longer expands in one role and collapses in the other after a
   context switch.

7. Mobile/tablet behaviour is aligned with the command workspace: the collapsed rail remains
   compact and expands as an overlay; click-away and Escape close it.

8. Visual refinement:
   - "My Workspace" institutional identity instead of role-specific identity replacement
   - concise access descriptor (Project Officer · Command)
   - clearer section hierarchy with restrained icons
   - compact Personal | Command segmented lens
   - consistent 252 px expanded rail width and 64 px desktop collapsed width
   - slightly tighter navigation rhythm so dual-role navigation remains usable without
     excessive vertical scrolling

Important authorization note
----------------------------
This change does NOT broaden permissions. It only exposes every workspace destination the
current user is already authorised to use. Command access remains Comdt/HoD based. Project
Officer work remains Project Officer based.

Installation
------------
Copy the contents of this folder over the ProjectManagement project root, preserving paths.
No database migration is required.

Recommended validation after paste
----------------------------------
1. Build/test:
   dotnet build .\ProjectManagement.csproj
   dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

2. JavaScript:
   node --check .\wwwroot\js\pages\workspace-index.js
   node --check .\wwwroot\js\pages\command-workspace.js
   node --test .\wwwroot\js\pages\workspace-navigation-unification.test.js

3. Dual-role smoke test:
   a. Open /Workspace in Command context.
   b. Confirm My work + Command oversight + System adoption + My resources are visible.
   c. Click Assigned projects directly; do not use the lens first.
   d. Confirm Project Officer content opens and the same rail remains visible.
   e. Click Officer workload directly and confirm Command content returns with the same rail.
   f. Open Conference review and Briefing decks; confirm the same unified rail persists.
   g. Collapse the rail, change context, and confirm it remains collapsed.

4. Single-role smoke test:
   - Project Officer only: no Command oversight/System adoption groups.
   - Comdt/HoD only: no Project Officer work group; command destinations remain available.

5. Responsive smoke test:
   - Below 992 px the rail should open as an overlay and close on outside click/Escape.

No schema or migration changes are included in this phase.
