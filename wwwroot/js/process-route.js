// Procurement route model: pure graph and layout logic for the Process page.
// Everything here is derived from the flow API (stage templates and dependencies);
// no stage codes, names or purposes are hard-coded. Loaded as a classic script so the
// page module can read window.PrismProcessRoute and node tests can require() it.
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismProcessRoute = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  // Lane used for a conditional detour that leaves and rejoins the main route.
  const DETOUR_LANE = 1.75;

  function normalizeFlow(dto, fallbackVersion = '') {
    const nodes = (Array.isArray(dto?.nodes) ? dto.nodes : [])
      .map((node, index) => ({
        code: String(node.code || '').trim().toUpperCase(),
        name: String(node.name || node.code || '').trim(),
        sequence: Number(node.sequence) || ((index + 1) * 10),
        optional: Boolean(node.optional),
        parallelGroup: node.parallelGroup || null
      }))
      .filter(node => node.code)
      .sort((a, b) => a.sequence - b.sequence || a.code.localeCompare(b.code));

    nodes.forEach((node, index) => {
      node.displayIndex = index + 1;
      node.searchText = `${node.code} ${node.name}`.toLowerCase();
    });

    const edges = (Array.isArray(dto?.edges) ? dto.edges : [])
      .map(edge => ({
        source: String(edge.source || '').trim().toUpperCase(),
        target: String(edge.target || '').trim().toUpperCase()
      }))
      .filter(edge => edge.source && edge.target);

    return { version: dto?.version || fallbackVersion, nodes, edges };
  }

  function buildRoute(flow) {
    const nodes = flow.nodes.map(node => ({ ...node }));
    const byCode = new Map(nodes.map(node => [node.code, node]));
    const edges = flow.edges.filter(edge => byCode.has(edge.source) && byCode.has(edge.target));

    const incoming = new Map(nodes.map(node => [node.code, []]));
    const outgoing = new Map(nodes.map(node => [node.code, []]));
    edges.forEach(edge => {
      outgoing.get(edge.source).push(edge.target);
      incoming.get(edge.target).push(edge.source);
    });

    const mandatory = nodes.filter(node => !node.optional);
    const structural = edges.filter(edge => !byCode.get(edge.source).optional && !byCode.get(edge.target).optional);
    const structuralIn = new Map(mandatory.map(node => [node.code, []]));
    const structuralOut = new Map(mandatory.map(node => [node.code, []]));
    structural.forEach(edge => {
      structuralOut.get(edge.source).push(edge.target);
      structuralIn.get(edge.target).push(edge.source);
    });

    // Depth = longest path from a start stage along mandatory dependencies.
    const depth = new Map();
    const unresolved = new Set(mandatory.map(node => node.code));
    let progressed = true;
    while (unresolved.size && progressed) {
      progressed = false;
      [...unresolved].forEach(code => {
        const prerequisites = structuralIn.get(code);
        if (!prerequisites.every(dep => depth.has(dep))) return;
        depth.set(code, prerequisites.length ? Math.max(...prerequisites.map(dep => depth.get(dep))) + 1 : 0);
        unresolved.delete(code);
        progressed = true;
      });
    }
    // A dependency cycle should never exist; fall back to sequence order rather than failing.
    unresolved.forEach(code => depth.set(code, byCode.get(code).displayIndex - 1));

    const detours = nodes
      .filter(node => node.optional)
      .map(node => {
        const source = incoming.get(node.code)
          .map(code => byCode.get(code))
          .filter(candidate => !candidate.optional)
          .sort((a, b) => b.displayIndex - a.displayIndex)[0] || null;
        const explicitSuccessor = outgoing.get(node.code)
          .map(code => byCode.get(code))
          .filter(candidate => !candidate.optional)
          .sort((a, b) => a.displayIndex - b.displayIndex)[0] || null;
        const inferredSuccessor = mandatory.find(candidate => candidate.displayIndex > node.displayIndex) || null;
        return { code: node.code, source: source?.code || null, successor: (explicitSuccessor || inferredSuccessor)?.code || null };
      })
      .filter(detour => detour.source);

    // Each mid-route detour gets its own column, so the stages after it shift right by one.
    const midDetours = detours.filter(detour => detour.successor);
    const terminalDetours = detours.filter(detour => !detour.successor);
    mandatory.forEach(node => {
      const d = depth.get(node.code);
      const shift = midDetours.filter(detour => depth.get(detour.source) < d).length;
      node.column = d + shift;
    });

    const byDepth = new Map();
    mandatory.forEach(node => {
      const d = depth.get(node.code);
      if (!byDepth.has(d)) byDepth.set(d, []);
      byDepth.get(d).push(node);
    });
    byDepth.forEach(group => {
      group.sort((a, b) => a.sequence - b.sequence || a.code.localeCompare(b.code));
      group.forEach((node, index) => {
        node.lane = group.length === 1 ? 0 : -1 + (2 * index) / (group.length - 1);
        node.parallelWith = group.filter(other => other !== node).map(other => other.code);
      });
    });

    const placed = new Map();
    midDetours.forEach(detour => {
      const node = byCode.get(detour.code);
      const offset = placed.get(detour.source) || 0;
      placed.set(detour.source, offset + 1);
      node.column = byCode.get(detour.source).column + 1 + offset;
      node.lane = DETOUR_LANE;
    });
    const lastColumn = Math.max(0, ...mandatory.map(node => node.column));
    terminalDetours.forEach((detour, index) => {
      const node = byCode.get(detour.code);
      node.column = Math.max(lastColumn, byCode.get(detour.source).column) + 1 + index;
      node.lane = 0;
    });
    nodes.forEach(node => {
      if (node.column === undefined) node.column = node.displayIndex - 1;
      if (node.lane === undefined) node.lane = 0;
      if (!node.parallelWith) node.parallelWith = [];
    });

    const links = structural.map(edge => ({ ...edge, kind: 'structural', conditional: false }));
    detours.forEach(detour => {
      if (detour.successor && !links.some(link => link.source === detour.source && link.target === detour.successor)) {
        links.push({ source: detour.source, target: detour.successor, kind: 'bypass', conditional: false });
      }
      links.push({ source: detour.source, target: detour.code, kind: 'conditional-entry', conditional: true });
      // A conditional stage in the middle of the route rejoins it. A terminal conditional
      // stage is a single optional continuation from the last mandatory stage, so it has no return.
      if (detour.successor) {
        links.push({ source: detour.code, target: detour.successor, kind: 'conditional-return', conditional: true });
      }
    });

    const predecessors = code => links.filter(link => link.target === code).map(link => byCode.get(link.source));
    const successors = code => links.filter(link => link.source === code)
      .map(link => byCode.get(link.target))
      .filter((node, index, list) => list.indexOf(node) === index);

    return {
      nodes,
      byCode,
      links,
      detours,
      columns: Math.max(1, ...nodes.map(node => node.column + 1)),
      predecessors,
      successors,
      detourFor: code => detours.find(detour => detour.code === code) || null
    };
  }

  // Station state relative to the selected stage. Parallel stages share a column, so
  // selecting one never marks its partner as complete.
  function stationState(route, code, activeCode) {
    const node = route.byCode.get(code);
    const active = route.byCode.get(activeCode);
    if (!node || !active) return 'ahead';
    if (node.code === active.code) return 'active';
    return node.column < active.column ? 'done' : 'ahead';
  }

  function linkState(route, link, activeCode) {
    const active = route.byCode.get(activeCode);
    if (!active) return 'idle';
    if (link.conditional) {
      return link.source === activeCode || link.target === activeCode ? 'lit' : 'idle';
    }
    const target = route.byCode.get(link.target);
    if (target.code === activeCode) return 'done';
    return target.column < active.column ? 'done' : 'idle';
  }

  function listNames(nodes) {
    const names = nodes.map(node => node.name);
    if (names.length <= 1) return names.join('');
    return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
  }

  // Plain-language explanation of how a stage connects to its neighbours.
  function describe(route, code) {
    const node = route.byCode.get(code);
    if (!node) return '';
    const parts = [];
    const preds = route.predecessors(code).filter(pred => !pred.optional);
    if (!preds.length && !node.optional) parts.push('Every case starts here.');

    const detour = route.detourFor(code);
    if (detour) {
      const source = route.byCode.get(detour.source);
      const successor = detour.successor ? route.byCode.get(detour.successor) : null;
      parts.push(successor
        ? `This stage applies only when it is required. When it is not, the case moves straight from ${source.name} to ${successor.name}.`
        : `This stage applies only when it is required. For most cases the route ends at ${source.name}.`);
      if (successor) parts.push(`When it is complete, the case rejoins the main route at ${successor.name}.`);
      return parts.join(' ');
    }

    const next = route.links
      .filter(link => link.source === code && !link.conditional)
      .map(link => route.byCode.get(link.target));
    const optionalNext = route.links
      .filter(link => link.source === code && link.kind === 'conditional-entry')
      .map(link => route.byCode.get(link.target));

    if (node.parallelWith.length) {
      const partners = node.parallelWith.map(partner => route.byCode.get(partner));
      const join = next[0];
      parts.push(`${node.name} runs at the same time as ${listNames(partners)}.`);
      if (join) parts.push(`${partners.length === 1 ? 'Both' : 'All of them'} must be complete before ${join.name} can begin.`);
    } else if (next.length > 1) {
      parts.push(`When this stage is complete, the route splits: ${listNames(next)} run in parallel.`);
    } else if (next.length === 1) {
      parts.push(`When this stage is complete, the case moves to ${next[0].name}.`);
    }

    if (!next.length && !node.parallelWith.length) {
      parts.push(optionalNext.length ? 'This is the last mandatory stage of the route.' : 'This is the final stage of the route.');
    }
    optionalNext.forEach(optional => {
      parts.push(`${optional.name} may follow, but only when it is required.`);
    });
    return parts.join(' ');
  }

  return { normalizeFlow, buildRoute, stationState, linkState, describe, DETOUR_LANE };
});
