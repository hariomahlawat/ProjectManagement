// =============================================================================
// PRISM chart defaults: one chart style, defined once.
//
// Every Chart.js canvas in PRISM goes through applyPmChartDefaults() so nothing
// renders with the library's own look: Mulish axis text in the muted ink, faint
// horizontal grid only, no chart border, rounded bars with a capped thickness,
// values printed on the bars, HTML legend chips in the card style, tooltips that
// look like PRISM cards and a short eased animation.
//
// Colours are never literals here. They are read from the site tokens
// (--pm-chart-* in site.css), so Dark gets its lifted hues automatically and a
// theme switch only needs the charts to be re-rendered (see watchTheme()).
// =============================================================================

// SECTION: Token access
function cssVar(name) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name);
  return value ? value.trim() : '';
}

export function chartTokens() {
  return {
    font: cssVar('--pm-chart-font') || cssVar('--pm-font-body') || 'sans-serif',
    text: cssVar('--pm-chart-text') || cssVar('--pm-text-muted'),
    textStrong: cssVar('--pm-chart-text-strong') || cssVar('--pm-text-strong'),
    grid: cssVar('--pm-chart-grid-soft') || cssVar('--pm-chart-grid'),
    label: cssVar('--pm-chart-label') || cssVar('--pm-text-strong'),
    labelOnFill: cssVar('--pm-chart-label-on-fill') || cssVar('--pm-text-inverted'),
    tooltipBg: cssVar('--pm-chart-tooltip-bg') || cssVar('--pm-card'),
    tooltipText: cssVar('--pm-chart-tooltip-text') || cssVar('--pm-text'),
    tooltipMuted: cssVar('--pm-chart-tooltip-muted') || cssVar('--pm-text-muted'),
    tooltipBorder: cssVar('--pm-chart-tooltip-border') || cssVar('--pm-border'),
    card: cssVar('--pm-card'),
    primary: cssVar('--pm-chart-primary'),
    primarySoft: cssVar('--pm-chart-primary-soft'),
    primaryTrack: cssVar('--pm-chart-primary-track'),
    category: {
      dcd: cssVar('--pm-chart-cat-dcd'),
      coe: cssVar('--pm-chart-cat-coe'),
      other: cssVar('--pm-chart-cat-other'),
      neutral: cssVar('--pm-chart-cat-neutral')
    },
    lifecycle: {
      Ongoing: cssVar('--pm-chart-ongoing'),
      Completed: cssVar('--pm-chart-completed'),
      Cancelled: cssVar('--pm-chart-cancelled')
    },
    hotspot: cssVar('--pm-chart-hotspot')
  };
}

// Category colours are fixed by meaning, not by position: DCD blue, CoE green,
// Other R&D amber; anything else (unassigned, an unexpected root) is neutral grey.
export function categoryColor(name, tokens = chartTokens()) {
  const key = String(name ?? '').trim().toLowerCase();
  if (!key) return tokens.category.neutral;
  if (key.startsWith('dcd')) return tokens.category.dcd;
  if (key.includes('coe') || key.includes('centre of excellence') || key.includes('center of excellence')) {
    return tokens.category.coe;
  }
  if (key.includes('other') || key.includes('r&d')) return tokens.category.other;
  return tokens.category.neutral;
}

// Colours for a list of category names: the three roots keep their fixed hues; any other
// (exact sub-)category gets the next of the six accent tokens, so slices and their legend
// swatches stay distinguishable in the PNG export, where there are no tooltips.
export function categorySeriesColors(names, tokens = chartTokens()) {
  const fallback = ['--pm-chart-accent-4', '--pm-chart-accent-6', '--pm-chart-accent-5', '--pm-chart-accent-2', '--pm-chart-accent-1', '--pm-chart-accent-3']
    .map((name) => cssVar(name))
    .filter(Boolean);
  let next = 0;
  return names.map((name) => {
    const colour = categoryColor(name, tokens);
    if (colour !== tokens.category.neutral || !fallback.length) return colour;
    const picked = fallback[next % fallback.length];
    next += 1;
    return picked;
  });
}

export function lifecycleColor(status, tokens = chartTokens()) {
  return tokens.lifecycle[status] ?? tokens.category.neutral;
}
// END SECTION

// SECTION: Global defaults
export const PM_CHART = Object.freeze({
  barRadius: 6,
  maxBarThickness: 44,
  animationMs: 300
});

export function applyPmChartDefaults(Chart) {
  if (!Chart || Chart.__pmDefaultsApplied) return;
  const t = chartTokens();
  const d = Chart.defaults;

  d.font.family = t.font;
  d.font.size = 12;
  d.font.weight = '600';
  d.color = t.text;
  d.responsive = true;
  d.maintainAspectRatio = false;
  const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  d.animation.duration = reducedMotion ? 0 : PM_CHART.animationMs;
  d.animation.easing = 'easeOutQuart';
  d.layout.padding = { top: 22, right: 8, bottom: 0, left: 0 };

  d.plugins.legend.display = false; // legends are HTML chips (renderLegend)
  d.plugins.title.display = false;

  d.plugins.tooltip.backgroundColor = t.tooltipBg;
  d.plugins.tooltip.titleColor = t.tooltipText;
  d.plugins.tooltip.bodyColor = t.tooltipMuted;
  d.plugins.tooltip.footerColor = t.tooltipText;
  d.plugins.tooltip.borderColor = t.tooltipBorder;
  d.plugins.tooltip.borderWidth = 1;
  d.plugins.tooltip.cornerRadius = 10;
  d.plugins.tooltip.padding = { top: 10, right: 12, bottom: 10, left: 12 };
  d.plugins.tooltip.titleFont = { family: t.font, size: 13, weight: '700' };
  d.plugins.tooltip.bodyFont = { family: t.font, size: 12, weight: '600' };
  d.plugins.tooltip.footerFont = { family: t.font, size: 12, weight: '700' };
  d.plugins.tooltip.boxPadding = 4;
  d.plugins.tooltip.usePointStyle = true;
  d.plugins.tooltip.caretSize = 6;

  d.elements.bar.borderRadius = PM_CHART.barRadius;
  d.elements.bar.borderSkipped = false;
  d.elements.bar.borderWidth = 0;
  d.datasets.bar.maxBarThickness = PM_CHART.maxBarThickness;
  d.datasets.bar.categoryPercentage = 0.72;
  d.datasets.bar.barPercentage = 0.9;

  d.scale.grid.color = t.grid;
  d.scale.grid.drawTicks = false;
  d.scale.border.display = false;
  d.scale.ticks.color = t.text;
  d.scale.ticks.padding = 8;

  Chart.__pmDefaultsApplied = true;
  Chart.register(valueLabelsPlugin);
}

// Re-read the tokens after a live theme switch (user menu → Theme) and let the
// caller re-render. Chart.defaults holds resolved colours, so they are refreshed here.
export function watchTheme(Chart, onChange) {
  if (!window.MutationObserver) return;
  const observer = new MutationObserver((mutations) => {
    if (!mutations.some((m) => m.attributeName === 'data-bs-theme')) return;
    Chart.__pmDefaultsApplied = false;
    applyPmChartDefaults(Chart);
    onChange();
  });
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
}
// END SECTION

// SECTION: Scales
// Vertical bars: category axis without grid, value axis with faint horizontal lines.
export function barScales({ horizontal = false, stacked = false, integer = true, max } = {}) {
  const t = chartTokens();
  const categoryAxis = {
    stacked,
    grid: { display: false },
    // Labels stay flat where they fit (desktop) and turn upright on a phone, never skipped.
    ticks: { autoSkip: false, maxRotation: 90, minRotation: 0, color: t.text, font: { size: 12, weight: '600' } }
  };
  const valueAxis = {
    stacked,
    beginAtZero: true,
    grace: '8%',
    max,
    grid: { color: t.grid, lineWidth: 1 },
    ticks: {
      precision: integer ? 0 : undefined,
      maxTicksLimit: 6,
      color: t.text,
      font: { size: 12, weight: '600' },
      callback(value) { return Number.isInteger(value) || !integer ? value : null; }
    }
  };
  if (horizontal) {
    valueAxis.grid.display = false; // the value is printed beside the bar
    valueAxis.ticks.display = false;
    return { x: valueAxis, y: categoryAxis };
  }
  return { x: categoryAxis, y: valueAxis };
}
// END SECTION

// SECTION: Value labels
// Every count on these pages is small, so the number is printed on the bar and the
// grid can stay faint. Stacked bars get their total above the stack and a segment
// value inside a segment when there is room; single bars get the value above (or to
// the right, for horizontal bars). Doughnuts get the total in the hole.
export const valueLabelsPlugin = {
  id: 'pmValueLabels',
  afterDatasetsDraw(chart, _args, options) {
    if (options?.display === false) return;
    const t = chartTokens();
    const { ctx } = chart;
    const format = typeof options?.format === 'function' ? options.format : (v) => String(v);
    const type = chart.config.type;

    ctx.save();
    ctx.font = `700 12px ${t.font}`;
    ctx.textBaseline = 'middle';

    if (type === 'doughnut') {
      drawDoughnutCentre(chart, ctx, t, options);
      ctx.restore();
      return;
    }

    const horizontal = chart.options.indexAxis === 'y';
    const stacked = Boolean(chart.options.scales?.x?.stacked || chart.options.scales?.y?.stacked);
    const metas = chart.getSortedVisibleDatasetMetas();
    if (!metas.length) { ctx.restore(); return; }

    const labelCount = chart.data.labels?.length ?? 0;

    // Grouped bars (several datasets side by side): each bar carries its own value.
    if (!stacked && metas.length > 1) {
      ctx.fillStyle = t.label;
      ctx.font = `700 11px ${t.font}`;
      metas.forEach((meta) => {
        meta.data.forEach((bar, index) => {
          const value = Number(meta.controller.getParsed(index)?.[horizontal ? 'x' : 'y']);
          if (!Number.isFinite(value) || value <= 0) return;
          const props = bar.getProps(['x', 'y'], true);
          if (horizontal) {
            ctx.textAlign = 'left';
            ctx.fillText(format(value), props.x + 6, props.y);
          } else {
            ctx.textAlign = 'center';
            ctx.fillText(format(value), props.x, props.y - 9);
          }
        });
      });
      ctx.restore();
      return;
    }

    for (let index = 0; index < labelCount; index += 1) {
      let total = 0;
      let outerEdge = null;
      metas.forEach((meta) => {
        const value = Number(meta.controller.getParsed(index)?.[horizontal ? 'x' : 'y']);
        if (!Number.isFinite(value) || value <= 0) return;
        total += value;
        const bar = meta.data[index];
        if (!bar) return;
        const props = bar.getProps(['x', 'y', 'base', 'width', 'height'], true);
        if (horizontal) {
          outerEdge = outerEdge === null ? props.x : Math.max(outerEdge, props.x);
        } else {
          outerEdge = outerEdge === null ? props.y : Math.min(outerEdge, props.y);
        }
        if (stacked && metas.length > 1 && options?.segments !== false) {
          const size = horizontal ? Math.abs(props.x - props.base) : Math.abs(props.base - props.y);
          const text = format(value);
          const fits = horizontal ? size >= ctx.measureText(text).width + 10 : size >= 18;
          if (fits) {
            ctx.fillStyle = t.labelOnFill;
            ctx.textAlign = 'center';
            const cx = horizontal ? (props.x + props.base) / 2 : props.x;
            const cy = horizontal ? props.y : (props.y + props.base) / 2;
            ctx.fillText(text, cx, cy);
          }
        }
      });
      if (total <= 0 || outerEdge === null) continue;
      const anchor = metas[0].data[index]?.getProps(['x', 'y'], true);
      if (!anchor) continue;
      ctx.fillStyle = t.label;
      if (horizontal) {
        ctx.textAlign = 'left';
        ctx.fillText(format(total), outerEdge + 8, anchor.y);
      } else {
        ctx.textAlign = 'center';
        ctx.fillText(format(total), anchor.x, outerEdge - 10);
      }
    }
    ctx.restore();
  }
};

function drawDoughnutCentre(chart, ctx, t, options) {
  const meta = chart.getDatasetMeta(0);
  if (!meta?.data?.length) return;
  const { x, y } = meta.data[0];
  const total = chart.data.datasets[0].data.reduce((sum, v) => sum + (Number(v) || 0), 0);
  ctx.textAlign = 'center';
  ctx.fillStyle = t.textStrong;
  ctx.font = `800 28px ${t.font}`;
  ctx.fillText(String(total), x, y - 8);
  ctx.fillStyle = t.text;
  ctx.font = `600 12px ${t.font}`;
  ctx.fillText(options?.centreLabel ?? 'projects', x, y + 14);
}
// END SECTION

// SECTION: HTML legend chips
// items: [{ label, color, value?, datasetIndex? }]. Clicking a chip toggles its
// dataset, exactly like the built-in legend, but drawn as PRISM chips.
export function renderLegend(container, chart, items) {
  if (!container) return;
  container.innerHTML = '';
  container.classList.add('pm-chart-legend');
  items.forEach((item) => {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'pm-chart-legend__chip';
    chip.setAttribute('aria-pressed', 'true');
    const swatch = document.createElement('i');
    swatch.className = 'pm-chart-legend__swatch';
    swatch.style.setProperty('--pm-legend-color', item.color);
    chip.appendChild(swatch);
    const text = document.createElement('span');
    text.textContent = item.label;
    chip.appendChild(text);
    if (item.value !== undefined && item.value !== null) {
      const value = document.createElement('b');
      value.textContent = String(item.value);
      chip.appendChild(value);
    }
    if (chart && Number.isInteger(item.datasetIndex)) {
      chip.addEventListener('click', () => {
        const visible = chart.isDatasetVisible(item.datasetIndex);
        chart.setDatasetVisibility(item.datasetIndex, !visible);
        chip.setAttribute('aria-pressed', visible ? 'false' : 'true');
        chart.update();
      });
    } else {
      chip.disabled = true;
    }
    container.appendChild(chip);
  });
}
// END SECTION

// SECTION: Bar clicks
// resolve(index, datasetIndex) returns what a click on that bar does: a function to run (open
// a drill-down row on the page), a URL string to open, or null for nothing.
export function enableBarClick(chart, resolve) {
  const hit = (event) => chart.getElementsAtEventForMode(event, 'nearest', { intersect: true }, false)[0] ?? null;
  chart.options.onHover = (event) => {
    const target = hit(event);
    const action = target ? resolve(target.index, target.datasetIndex) : null;
    event.native.target.style.cursor = action ? 'pointer' : 'default';
  };
  chart.options.onClick = (event) => {
    const target = hit(event);
    if (!target) return;
    const action = resolve(target.index, target.datasetIndex);
    if (typeof action === 'function') action();
    else if (typeof action === 'string' && action) window.location.assign(action);
  };
}

// Bars open the filtered projects list. urlFor(index, datasetIndex) returns a URL or null.
export function enableClickThrough(chart, urlFor) {
  // Handlers are read from chart.options at event time, so no update() is needed here;
  // an update during the first animation would freeze the chart mid-frame.
  // The tooltip may use index mode (every dataset at that label), so the clicked segment
  // is resolved separately: the one element actually under the pointer.
  const hit = (event) => chart.getElementsAtEventForMode(event, 'nearest', { intersect: true }, false)[0] ?? null;
  chart.options.onHover = (event) => {
    const target = hit(event);
    const url = target ? urlFor(target.index, target.datasetIndex) : null;
    event.native.target.style.cursor = url ? 'pointer' : 'default';
  };
  chart.options.onClick = (event) => {
    const target = hit(event);
    if (!target) return;
    const url = urlFor(target.index, target.datasetIndex);
    if (url) window.location.assign(url);
  };
}
// END SECTION

// SECTION: Sizing by data
// Chart heights follow the content: a vertical bar chart gets a fixed band; a horizontal
// one grows with its rows so bars keep a readable thickness.
export function sizeChartHost(host, { rows = 0, horizontal = false, min = 220, max = 420, rowHeight = 34 } = {}) {
  if (!host) return;
  const height = horizontal ? Math.min(max, Math.max(min, rows * rowHeight + 24)) : Math.max(min, Math.min(max, 320));
  host.style.height = `${height}px`;
}
// END SECTION
