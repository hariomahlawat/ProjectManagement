// =============================================================================
// Projects → Analytics: chart rendering for the four modes (Ongoing, Completed,
// CoE, Insights). Data arrives in data-* attributes written by the Razor
// partials; the PageModel owns every business number. This file only draws.
//
// Style: everything goes through charts/pm-chart-defaults.js (one chart style,
// token colours, HTML legend chips, printed values). No colour literals here.
// =============================================================================
import {
  applyPmChartDefaults,
  barScales,
  categoryColor,
  chartTokens,
  enableClickThrough,
  lifecycleColor,
  renderLegend,
  sizeChartHost,
  watchTheme
} from './charts/pm-chart-defaults.js';

// SECTION: Small helpers
function ensureNumber(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function readJson(element, key, fallback = []) {
  const raw = element?.dataset?.[key];
  if (!raw) return fallback;
  try {
    return JSON.parse(raw);
  } catch (error) {
    console.error(`Analytics: could not parse data-${key}.`, error);
    return fallback;
  }
}

function getCssVar(name, fallback) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name);
  return value ? value.trim() : fallback;
}

function withQuery(baseUrl, params) {
  if (!baseUrl) return null;
  const url = new URL(baseUrl, window.location.origin);
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') url.searchParams.set(key, String(value));
  });
  return url.pathname + url.search;
}

const root = () => document.getElementById('analytics-root');
const chartRegistry = new Map(); // canvas id → () => Chart, so a theme switch can redraw

function mount(canvas, build) {
  if (!canvas || !window.Chart) return null;
  const existing = window.Chart.getChart(canvas);
  if (existing) existing.destroy();
  const chart = build();
  if (chart) chartRegistry.set(canvas, build);
  return chart;
}

function renderEmptyState(canvas, message) {
  const host = canvas?.parentElement;
  if (!host) return;
  canvas.hidden = true;
  if (host.querySelector('.analytics-empty')) return;
  const empty = document.createElement('p');
  empty.className = 'analytics-empty';
  empty.setAttribute('role', 'status');
  empty.textContent = message || canvas.dataset.emptyMessage || 'No data yet.';
  host.appendChild(empty);
  host.style.height = 'auto';
}
// END SECTION

// SECTION: Stage axis
// Every stage chart draws the same workflow order (from #analytics-root data-stages) and
// the same short labels, so the eye can compare charts across modes.
const stageShortLabels = {
  FS: 'FS', IPA: 'IPA', SOW: 'SoW', AON: 'AoN', BID: 'Bid', TEC: 'TEC', BM: 'BM', COB: 'COB',
  PNC: 'PNC', EAS: 'EAS', SO: 'SO', DEVP: 'Devp', ATP: 'ATP', PAYMENT: 'Pay', TOT: 'ToT'
};

function workflowStages() {
  return readJson(root(), 'stages', []).map((stage) => ({
    code: String(stage.code ?? ''),
    name: String(stage.name ?? stage.code ?? '')
  }));
}

// Known stages first in workflow order, then anything ad hoc that appears in the data.
function stageAxis(codesInData, namesByCode = new Map()) {
  const axis = workflowStages().map((stage) => ({ ...stage, short: stageShortLabels[stage.code] ?? stage.code }));
  const known = new Set(axis.map((stage) => stage.code.toUpperCase()));
  codesInData.forEach((code) => {
    const key = String(code ?? '').toUpperCase();
    if (!key || known.has(key)) return;
    known.add(key);
    const name = namesByCode.get(code) ?? code;
    axis.push({ code, name, short: name.length > 8 ? code : name });
  });
  return axis;
}

function stageTooltipTitle(axis) {
  return (items) => axis[items?.[0]?.dataIndex ?? -1]?.name ?? '';
}
// END SECTION

// SECTION: Category series (DCD / CoE / Other R&D)
const categoryOrder = ['DCD Projects', 'CoE', 'Other R&D Projects'];

function orderCategories(names) {
  const present = Array.from(new Set(names.filter(Boolean)));
  const ranked = categoryOrder.filter((name) => present.includes(name));
  const rest = present.filter((name) => !ranked.includes(name)).sort((a, b) => a.localeCompare(b));
  return [...ranked, ...rest];
}

function paintCategoryDots(scope) {
  const tokens = chartTokens();
  (scope ?? document).querySelectorAll('[data-category-dot]').forEach((dot) => {
    dot.style.setProperty('--pm-dot', categoryColor(dot.dataset.categoryDot, tokens));
  });
}

function parentCategoryId(name) {
  const options = readJson(root(), 'parentCategories', []);
  return options.find((option) => option.name === name)?.id ?? null;
}

// The CoE root category, so CoE charts open lists scoped the same way they are counted.
function coeParentCategoryId() {
  const options = readJson(root(), 'parentCategories', []);
  return options.find((option) => /\bcoe\b|cent(?:re|er)s? of excellence/i.test(String(option.name ?? '')))?.id ?? null;
}
// END SECTION

// SECTION: Ongoing mode
function initOngoing(panel) {
  initOngoingStageFilterPopover(panel);
  paintCategoryDots(panel);

  const canvas = panel.querySelector('#ongoing-by-stage-chart');
  const points = readJson(canvas, 'series', []);
  if (!canvas) return;
  if (!points.length) {
    renderEmptyState(canvas);
    return;
  }

  const stageNames = new Map(points.map((p) => [p.stageCode, p.stageName]));
  const axis = stageAxis(points.map((p) => p.stageCode), stageNames);
  const categories = orderCategories(points.map((p) => p.categoryName ?? 'Unassigned'));
  const ongoingUrl = root()?.dataset.ongoingIndexUrl;

  sizeChartHost(canvas.parentElement, { min: 300, max: 340 });
  mount(canvas, () => {
    const tokens = chartTokens();
    const datasets = categories.map((category) => ({
      label: category,
      data: axis.map((stage) => points
        .filter((p) => p.stageCode === stage.code && (p.categoryName ?? 'Unassigned') === category)
        .reduce((sum, p) => sum + ensureNumber(p.count), 0)),
      backgroundColor: categoryColor(category, tokens),
      stack: 'category'
    }));
    const chart = new window.Chart(canvas, {
      type: 'bar',
      data: { labels: axis.map((stage) => stage.short), datasets },
      options: {
        interaction: { mode: 'index', intersect: true },
        scales: barScales({ stacked: true }),
        plugins: {
          tooltip: {
            callbacks: {
              title: stageTooltipTitle(axis),
              label: (item) => `${item.dataset.label}: ${item.formattedValue}`,
              footer: (items) => `Total: ${items.reduce((sum, item) => sum + ensureNumber(item.parsed.y), 0)}`
            }
          }
        }
      }
    });
    renderLegend(panel.querySelector('[data-legend="ongoing-by-stage-chart"]'), chart, datasets.map((dataset, index) => ({
      label: dataset.label,
      color: dataset.backgroundColor,
      value: dataset.data.reduce((sum, value) => sum + value, 0),
      datasetIndex: index
    })));
    enableClickThrough(chart, (index, datasetIndex) => withQuery(ongoingUrl, {
      PresentStageCode: axis[index]?.code,
      ProjectCategoryId: parentCategoryId(datasets[datasetIndex]?.label)
    }));
    return chart;
  });
}

function initOngoingStageFilterPopover(panel) {
  const form = panel.querySelector('[data-stage-filter-form]');
  if (!form) return;
  const trigger = form.querySelector('[data-stage-filter-trigger]');
  const popover = form.querySelector('[data-stage-filter-popover]');
  const summary = form.querySelector('[data-stage-filter-summary]');
  const options = Array.from(form.querySelectorAll('[data-stage-filter-option]'));
  if (!trigger || !popover || !summary || !options.length) return;

  const labelOf = (option) => form.querySelector(`label[for="${option.id}"]`)?.textContent?.trim() ?? '';
  const setOpen = (open) => {
    trigger.setAttribute('aria-expanded', open ? 'true' : 'false');
    popover.hidden = !open;
  };
  const selected = () => options.filter((option) => option.checked);
  const updateSummary = () => {
    const count = selected().length;
    summary.textContent = count === 0 || count === options.length
      ? 'All'
      : count === 1 ? labelOf(selected()[0]) : `${count} selected`;
  };

  trigger.addEventListener('click', () => setOpen(popover.hidden));
  form.querySelector('[data-stage-filter-select-all]')?.addEventListener('click', () => {
    options.forEach((option) => { option.checked = true; });
    updateSummary();
  });
  form.querySelector('[data-stage-filter-clear]')?.addEventListener('click', () => {
    options.forEach((option) => { option.checked = false; });
    updateSummary();
  });
  options.forEach((option) => option.addEventListener('change', updateSummary));
  form.addEventListener('submit', () => {
    if (!selected().length) options.forEach((option) => { option.checked = true; });
    setOpen(false);
  });
  document.addEventListener('click', (event) => {
    if (event.target instanceof Element && !form.contains(event.target)) setOpen(false);
  });
  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && !popover.hidden) {
      setOpen(false);
      trigger.focus();
    }
  });
  setOpen(false);
  updateSummary();
}
// END SECTION

// SECTION: Completed mode
function initCompleted(panel) {
  const data = readJson(panel, 'completedAnalytics', null);
  if (!data) return;
  paintCategoryDots(panel);
  const projectsUrl = root()?.dataset.projectsIndexUrl;
  const technicalOptions = readJson(root(), 'technicalCategories', []);
  const categoryOptions = readJson(root(), 'categories', []);

  // Projects completed per year: the big chart, stacked by parent category.
  const perYearCanvas = panel.querySelector('#completedPerYearChart');
  const perYearPoints = Array.isArray(data.perYearByParentCategory) && data.perYearByParentCategory.length
    ? data.perYearByParentCategory
    : (data.perYear ?? []).map((p) => ({ year: p.year, categoryName: 'All projects', count: p.count }));
  if (perYearCanvas) {
    if (!perYearPoints.length) {
      renderEmptyState(perYearCanvas);
    } else {
      const years = Array.from(new Set(perYearPoints.map((p) => p.year))).sort((a, b) => a - b);
      const categories = orderCategories(perYearPoints.map((p) => p.categoryName ?? 'Unassigned'));
      sizeChartHost(perYearCanvas.parentElement, { min: 340, max: 380 });
      mount(perYearCanvas, () => {
        const tokens = chartTokens();
        const datasets = categories.map((category) => ({
          label: category,
          data: years.map((year) => perYearPoints
            .filter((p) => p.year === year && (p.categoryName ?? 'Unassigned') === category)
            .reduce((sum, p) => sum + ensureNumber(p.count), 0)),
          backgroundColor: categories.length === 1 ? tokens.primary : categoryColor(category, tokens),
          stack: 'year'
        }));
        const chart = new window.Chart(perYearCanvas, {
          type: 'bar',
          data: { labels: years, datasets },
          options: {
            interaction: { mode: 'index', intersect: true },
            scales: barScales({ stacked: true }),
            plugins: {
              tooltip: {
                callbacks: {
                  title: (items) => `Completed in ${items?.[0]?.label ?? ''}`,
                  label: (item) => `${item.dataset.label}: ${item.formattedValue}`,
                  footer: (items) => `Total: ${items.reduce((sum, item) => sum + ensureNumber(item.parsed.y), 0)}`
                }
              }
            }
          }
        });
        renderLegend(panel.querySelector('[data-legend="completedPerYearChart"]'), chart, datasets.map((dataset, index) => ({
          label: dataset.label,
          color: dataset.backgroundColor,
          value: dataset.data.reduce((sum, value) => sum + value, 0),
          datasetIndex: index
        })));
        enableClickThrough(chart, (index) => withQuery(projectsUrl, { Lifecycle: 'Completed', CompletedYear: years[index] }));
        return chart;
      });
    }
  }

  // By category: a doughnut in the fixed category colours with the total in the hole.
  const byCategoryCanvas = panel.querySelector('#completedByCategoryChart');
  const byCategory = Array.isArray(data.byCategory) ? data.byCategory : [];
  if (byCategoryCanvas) {
    if (!byCategory.length) {
      renderEmptyState(byCategoryCanvas);
    } else {
      sizeChartHost(byCategoryCanvas.parentElement, { min: 260, max: 280 });
      mount(byCategoryCanvas, () => {
        const tokens = chartTokens();
        const colors = byCategory.map((p) => categoryColor(p.name, tokens));
        const chart = new window.Chart(byCategoryCanvas, {
          type: 'doughnut',
          data: {
            labels: byCategory.map((p) => p.name),
            datasets: [{ data: byCategory.map((p) => ensureNumber(p.count)), backgroundColor: colors, borderWidth: 3, borderColor: tokens.card, hoverOffset: 6 }]
          },
          options: {
            cutout: '68%',
            layout: { padding: 8 },
            plugins: {
              pmValueLabels: { centreLabel: 'completed' },
              tooltip: {
                callbacks: {
                  label(item) {
                    const total = byCategory.reduce((sum, p) => sum + ensureNumber(p.count), 0);
                    const share = total ? Math.round((item.parsed / total) * 100) : 0;
                    return `${item.label}: ${item.formattedValue} (${share}%)`;
                  }
                }
              }
            }
          }
        });
        renderLegend(panel.querySelector('[data-legend="completedByCategoryChart"]'), null, byCategory.map((p, index) => ({
          label: p.name,
          color: colors[index],
          value: ensureNumber(p.count)
        })));
        enableClickThrough(chart, (index) => {
          const option = categoryOptions.find((c) => c.name === byCategory[index]?.name);
          return option ? withQuery(projectsUrl, { Lifecycle: 'Completed', CategoryId: option.id, IncludeCategoryDescendants: true }) : null;
        });
        return chart;
      });
    }
  }

  // By technical category: horizontal bars, most first, one hue.
  const byTechnicalCanvas = panel.querySelector('#completedByTechnicalChart');
  const byTechnical = (Array.isArray(data.byTechnical) ? data.byTechnical : [])
    .slice()
    .sort((a, b) => ensureNumber(b.count) - ensureNumber(a.count));
  if (byTechnicalCanvas) {
    if (!byTechnical.length) {
      renderEmptyState(byTechnicalCanvas);
    } else {
      sizeChartHost(byTechnicalCanvas.parentElement, { horizontal: true, rows: byTechnical.length, min: 200, max: 420, rowHeight: 34 });
      mount(byTechnicalCanvas, () => {
        const tokens = chartTokens();
        const chart = new window.Chart(byTechnicalCanvas, {
          type: 'bar',
          data: {
            labels: byTechnical.map((p) => p.name),
            datasets: [{ label: 'Completed projects', data: byTechnical.map((p) => ensureNumber(p.count)), backgroundColor: tokens.primary, maxBarThickness: 22 }]
          },
          options: {
            indexAxis: 'y',
            layout: { padding: { right: 36, top: 4 } },
            scales: barScales({ horizontal: true }),
            plugins: { tooltip: { callbacks: { label: (item) => `${item.formattedValue} completed` } } }
          }
        });
        enableClickThrough(chart, (index) => {
          const option = technicalOptions.find((t) => t.name === byTechnical[index]?.name);
          return option ? withQuery(projectsUrl, { Lifecycle: 'Completed', TechnicalCategoryId: option.id }) : null;
        });
        return chart;
      });
    }
  }
}
// END SECTION

// SECTION: CoE mode
function initCoe(panel) {
  const ongoingUrl = root()?.dataset.ongoingIndexUrl;

  const stageCanvas = panel.querySelector('#coe-by-stage-chart');
  const stageSeries = readJson(stageCanvas, 'series', []);
  if (stageCanvas) {
    if (!stageSeries.length) {
      renderEmptyState(stageCanvas);
    } else {
      const names = new Map(stageSeries.map((p) => [p.stageKey, p.stageName]));
      const axis = stageAxis(stageSeries.map((p) => p.stageKey), names);
      sizeChartHost(stageCanvas.parentElement, { min: 280, max: 300 });
      mount(stageCanvas, () => {
        const tokens = chartTokens();
        const chart = new window.Chart(stageCanvas, {
          type: 'bar',
          data: {
            labels: axis.map((stage) => stage.short),
            datasets: [{
              label: 'Ongoing CoE projects',
              data: axis.map((stage) => stageSeries
                .filter((p) => String(p.stageKey).toUpperCase() === stage.code.toUpperCase())
                .reduce((sum, p) => sum + ensureNumber(p.projectCount ?? p.value), 0)),
              backgroundColor: tokens.category.coe
            }]
          },
          options: {
            scales: barScales(),
            plugins: { tooltip: { callbacks: { title: stageTooltipTitle(axis), label: (item) => `${item.formattedValue} ongoing` } } }
          }
        });
        enableClickThrough(chart, (index) => withQuery(ongoingUrl, { PresentStageCode: axis[index]?.code, ProjectCategoryId: coeParentCategoryId() }));
        return chart;
      });
    }
  }

  const subcategoryCanvas = panel.querySelector('#coe-subcategories-by-lifecycle-chart');
  const rows = readJson(subcategoryCanvas, 'series', []);
  if (subcategoryCanvas) {
    if (!rows.length) {
      renderEmptyState(subcategoryCanvas);
    } else {
      const statuses = ['Ongoing', 'Completed', 'Cancelled'];
      const keyOf = { Ongoing: 'ongoing', Completed: 'completed', Cancelled: 'cancelled' };
      sizeChartHost(subcategoryCanvas.parentElement, { horizontal: true, rows: rows.length, min: 220, max: 460, rowHeight: 38 });
      mount(subcategoryCanvas, () => {
        const tokens = chartTokens();
        const datasets = statuses.map((status) => ({
          label: status,
          data: rows.map((row) => ensureNumber(row[keyOf[status]])),
          backgroundColor: lifecycleColor(status, tokens),
          stack: 'lifecycle',
          maxBarThickness: 24
        }));
        const chart = new window.Chart(subcategoryCanvas, {
          type: 'bar',
          data: { labels: rows.map((row) => row.name ?? ''), datasets },
          options: {
            indexAxis: 'y',
            layout: { padding: { right: 36, top: 4 } },
            interaction: { mode: 'index', intersect: true },
            scales: barScales({ horizontal: true, stacked: true }),
            plugins: {
              tooltip: {
                callbacks: {
                  label(item) {
                    const total = ensureNumber(rows[item.dataIndex]?.total);
                    const share = total ? Math.round((item.parsed.x / total) * 100) : 0;
                    return `${item.dataset.label}: ${item.formattedValue}${total ? ` (${share}%)` : ''}`;
                  },
                  footer: (items) => `Total: ${ensureNumber(rows[items?.[0]?.dataIndex]?.total)}`
                }
              }
            }
          }
        });
        renderLegend(panel.querySelector('[data-legend="coe-subcategories-by-lifecycle-chart"]'), chart, datasets.map((dataset, index) => ({
          label: dataset.label,
          color: dataset.backgroundColor,
          value: dataset.data.reduce((sum, value) => sum + value, 0),
          datasetIndex: index
        })));
        return chart;
      });
    }
  }
}
// END SECTION

// SECTION: Insights mode (one stage cycle-time chart with toggles, plus the ranked list)
const costBands = [
  { key: 'Below1Cr', label: 'Below ₹1 crore' },
  { key: 'AboveOrEqual1Cr', label: '₹1 crore and above' }
];

function initInsights(panel) {
  enableAutoSubmit(panel);
  const canvas = panel.querySelector('#stage-cycle-time-chart');
  if (!canvas) return;
  const rows = readJson(canvas, 'rows', []);     // per stage × cost band
  const points = readJson(canvas, 'points', []); // per stage, all projects
  if (!points.length && !rows.length) {
    renderEmptyState(canvas);
    return;
  }

  const names = new Map([...points, ...rows].map((p) => [p.stageKey, p.stageName]));
  const codes = Array.from(new Set([...points, ...rows].map((p) => p.stageKey)));
  const axis = stageAxis(codes, names);
  const state = { measure: 'median', split: 'all' };
  const legend = panel.querySelector('[data-legend="stage-cycle-time-chart"]');
  const field = () => (state.measure === 'mean' ? 'averageDays' : 'medianDays');
  const measureLabel = () => (state.measure === 'mean' ? 'Mean' : 'Median');
  const matchStage = (p, stage) => String(p.stageKey).toUpperCase() === stage.code.toUpperCase();

  sizeChartHost(canvas.parentElement, { min: 300, max: 340 });

  const draw = () => mount(canvas, () => {
    const tokens = chartTokens();
    let datasets;
    if (state.split === 'band') {
      const tints = [tokens.primary, tokens.primarySoft];
      datasets = costBands.map((band, index) => ({
        label: band.label,
        bandKey: band.key,
        data: axis.map((stage) => ensureNumber(rows.find((r) => matchStage(r, stage) && r.bucket === band.key)?.[field()])),
        backgroundColor: tints[index]
      }));
    } else {
      datasets = [{
        label: `${measureLabel()} days in stage`,
        data: axis.map((stage) => ensureNumber(points.find((p) => matchStage(p, stage))?.[field()])),
        backgroundColor: tokens.primary
      }];
    }
    const chart = new window.Chart(canvas, {
      type: 'bar',
      data: { labels: axis.map((stage) => stage.short), datasets },
      options: {
        interaction: { mode: 'index', intersect: true },
        scales: barScales({ integer: false }),
        plugins: {
          pmValueLabels: { format: (value) => Math.round(value).toString() },
          tooltip: {
            callbacks: {
              title: stageTooltipTitle(axis),
              label(item) {
                const stage = axis[item.dataIndex];
                const source = item.dataset.bandKey
                  ? rows.find((r) => matchStage(r, stage) && r.bucket === item.dataset.bandKey)
                  : points.find((p) => matchStage(p, stage));
                if (!source) return `${item.dataset.label}: no finished stages yet`;
                return [
                  `${item.dataset.label}: ${Math.round(ensureNumber(source[field()]))} days`,
                  `${state.measure === 'mean' ? 'Median' : 'Mean'}: ${Math.round(ensureNumber(source[state.measure === 'mean' ? 'medianDays' : 'averageDays']))} days`,
                  `${ensureNumber(source.projectCount)} project${ensureNumber(source.projectCount) === 1 ? '' : 's'}${ensureNumber(source.projectCount) < 3 ? ' (small sample)' : ''}`
                ];
              }
            }
          }
        }
      }
    });
    renderLegend(legend, datasets.length > 1 ? chart : null, datasets.map((dataset, index) => ({
      label: dataset.label,
      color: dataset.backgroundColor,
      datasetIndex: index
    })));
    return chart;
  });

  panel.querySelectorAll('[data-cycle-toggle]').forEach((group) => {
    const key = group.dataset.cycleToggle;
    group.querySelectorAll('button[data-value]').forEach((button) => {
      button.addEventListener('click', () => {
        state[key] = button.dataset.value;
        group.querySelectorAll('button[data-value]').forEach((other) => {
          other.setAttribute('aria-pressed', other === button ? 'true' : 'false');
        });
        draw();
      });
    });
  });

  draw();
}

function enableAutoSubmit(scope) {
  scope.querySelectorAll('[data-auto-submit="change"]').forEach((element) => {
    element.addEventListener('change', () => {
      const form = element.closest('form');
      if (form?.requestSubmit) form.requestSubmit();
      else form?.submit();
    });
  });
}
// END SECTION

// SECTION: Bootstrap
function hydrate() {
  const ongoing = document.querySelector('.analytics-panel--ongoing');
  const completed = document.querySelector('.analytics-panel--completed');
  const coe = document.querySelector('.analytics-panel--coe');
  const insights = document.querySelector('.analytics-panel--insights');
  if (ongoing) initOngoing(ongoing);
  else if (completed) initCompleted(completed);
  else if (coe) initCoe(coe);
  else if (insights) initInsights(insights);
}

function boot() {
  if (!document.getElementById('analytics-root') || !window.Chart) return;
  applyPmChartDefaults(window.Chart);
  hydrate();
  initAnalyticsChartDownloads();
  watchTheme(window.Chart, () => {
    chartRegistry.forEach((build, canvas) => mount(canvas, build));
    paintCategoryDots(document);
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
// END SECTION

// SECTION: Chart export (PNG, 1920x1080). Colours come from the chart tokens, never literals.

// SECTION: Export profile constants
const EXPORT_PROFILE = {
  width: 1920,
  height: 1080,
  dpr: 2
};

const EXPORT_TYPOGRAPHY = {
  legend: 32,
  axisX: 26,
  axisY: 26,
  dataLabel: 26,
  totalLabel: 34,
  footerTitle: 30,
  footerText: 26,
  watermark: 24,
  title: 36,
  subtitle: 28
};

const EXPORT_PADDING = {
  top: 100,
  right: 100,
  bottom: 200,
  left: 100
};
// END SECTION

// SECTION: Export-only helpers
function formatExportTimestamp() {
  return new Date().toLocaleString();
}

function drawWrappedText(ctx, text, x, y, maxWidth, lineHeight, maxLines = 2) {
  if (!text) {
    return { lines: 0, height: 0 };
  }

  const words = String(text).split(/\s+/).filter(Boolean);
  const lines = [];
  let currentLine = '';

  words.forEach((word) => {
    const candidate = currentLine ? `${currentLine} ${word}` : word;
    if (ctx.measureText(candidate).width > maxWidth && currentLine) {
      lines.push(currentLine);
      currentLine = word;
    } else {
      currentLine = candidate;
    }
  });

  if (currentLine) {
    lines.push(currentLine);
  }

  const limitedLines = lines.slice(0, maxLines);
  limitedLines.forEach((line, index) => {
    ctx.fillText(line, x, y + index * lineHeight);
  });

  return { lines: limitedLines.length, height: limitedLines.length * lineHeight };
}

function createExportHeaderFooterPlugin({
  title,
  meta,
  contextLine,
  timestamp,
  branding,
  scale = 1
}) {
  return {
    id: 'exportHeaderFooter',
    afterDraw(chart) {
      const ctx = chart.ctx;
      const padding = chart.options?.layout?.padding ?? {};
      const left = padding.left ?? 0;
      const right = padding.right ?? 0;
      const top = padding.top ?? 0;
      const bottom = padding.bottom ?? 0;
      const maxWidth = chart.width - left - right;

      ctx.save();
      ctx.fillStyle = chartTokens().textStrong;
      ctx.textBaseline = 'top';

      // SECTION: Header branding
      if (branding) {
        ctx.font = `${EXPORT_TYPOGRAPHY.watermark * scale}px "Inter", "Segoe UI", Arial, sans-serif`;
        ctx.textAlign = 'right';
        ctx.fillText(
          branding,
          chart.width - right,
          Math.max(0, (top - 16 * scale) / 2)
        );
      }
      // END SECTION

      // SECTION: Footer content
      const footerTop = chart.height - bottom + 12 * scale;
      let cursorY = footerTop;

      ctx.textAlign = 'left';
      ctx.font = `600 ${EXPORT_TYPOGRAPHY.footerTitle * scale}px "Inter", "Segoe UI", Arial, sans-serif`;
      const titleMetrics = drawWrappedText(
        ctx,
        title,
        left,
        cursorY,
        maxWidth,
        (EXPORT_TYPOGRAPHY.footerTitle + 6) * scale,
        2
      );
      cursorY += titleMetrics.height + 6 * scale;

      ctx.font = `${EXPORT_TYPOGRAPHY.footerText * scale}px "Inter", "Segoe UI", Arial, sans-serif`;
      const metaMetrics = drawWrappedText(
        ctx,
        meta,
        left,
        cursorY,
        maxWidth,
        (EXPORT_TYPOGRAPHY.footerText + 4) * scale,
        2
      );
      cursorY += metaMetrics.height + 6 * scale;

      const detailLine = [contextLine, timestamp].filter(Boolean).join(' • ');
      ctx.fillStyle = chartTokens().text;
      ctx.font = `${EXPORT_TYPOGRAPHY.watermark * scale}px "Inter", "Segoe UI", Arial, sans-serif`;
      drawWrappedText(
        ctx,
        detailLine,
        left,
        cursorY,
        maxWidth,
        (EXPORT_TYPOGRAPHY.watermark + 4) * scale,
        1
      );
      // END SECTION

      ctx.restore();
    }
  };
}

function createExportDataLabelsPlugin({
  maxLabelCount = 15,
  minSegmentPx = 18,
  fontSegment = EXPORT_TYPOGRAPHY.dataLabel,
  fontTotal = EXPORT_TYPOGRAPHY.totalLabel,
  fontWeight = '700'
} = {}) {
  const formatNumber = (value) => {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return '';
    }
    return parsed.toLocaleString();
  };

  const shouldSkipSegmentLabels = (chart) => {
    const labels = chart.data?.labels ?? [];
    return labels.length > maxLabelCount;
  };

  const getBarTotals = (datasets, labelCount) =>
    Array.from({ length: labelCount }, (_, index) =>
      datasets.reduce((sum, dataset) => sum + ensureNumber(dataset.data?.[index]), 0)
    );

  const getNonZeroCount = (datasets, index) =>
    datasets.reduce((count, dataset) => {
      const value = ensureNumber(dataset.data?.[index]);
      return value > 0 ? count + 1 : count;
    }, 0);

  const getFirstElementAtIndex = (chart, index) => {
    for (let datasetIndex = 0; datasetIndex < chart.data.datasets.length; datasetIndex += 1) {
      const meta = chart.getDatasetMeta(datasetIndex);
      const element = meta?.data?.[index];
      if (element) {
        return element;
      }
    }
    return null;
  };

  return {
    id: 'exportDataLabels',
    afterDatasetsDraw(chart) {
      const ctx = chart.ctx;
      const type = chart.config.type;
      const datasets = chart.data?.datasets ?? [];
      const labels = chart.data?.labels ?? [];
      const isHorizontal = chart.options?.indexAxis === 'y';

      ctx.save();

      // SECTION: Export label typography
      const pluginOptions = chart.options?.plugins?.exportValueLabels ?? {};
      const segmentFontSize = pluginOptions.segmentFontSize ?? fontSegment;
      const totalFontSize = pluginOptions.totalFontSize ?? fontTotal;
      const weight = pluginOptions.fontWeight ?? fontWeight;
      const labelFontFamily = chartTokens().font;
      // END SECTION

      if (type === 'bar') {
        const yScale = chart.scales?.y;
        const xScale = chart.scales?.x;
        const stacked =
          Boolean(chart.options?.scales?.x?.stacked) ||
          Boolean(chart.options?.scales?.y?.stacked) ||
          datasets.some((dataset) => dataset.stack);
        const skipSegmentLabels = stacked && shouldSkipSegmentLabels(chart);
        const totals = stacked ? getBarTotals(datasets, labels.length) : [];
        const minSegmentHeight = minSegmentPx;

        // SECTION: Segment labels
        datasets.forEach((dataset, datasetIndex) => {
          const meta = chart.getDatasetMeta(datasetIndex);
          if (!meta?.data?.length) {
            return;
          }

          meta.data.forEach((element, index) => {
            const rawValue = ensureNumber(dataset.data?.[index]);
            if (!rawValue) {
              return;
            }

            if (stacked) {
              if (skipSegmentLabels) {
                return;
              }

              const nonZeroCount = getNonZeroCount(datasets, index);
              if (nonZeroCount <= 1) {
                return;
              }

              const size = isHorizontal
                ? Math.abs(element.base - element.x)
                : Math.abs(element.base - element.y);
              if (size < minSegmentHeight) {
                return;
              }
            }

            const valueLabel = formatNumber(rawValue);
            if (!valueLabel) {
              return;
            }

            ctx.font = `${weight} ${segmentFontSize}px ${labelFontFamily}`;
            ctx.fillStyle = stacked ? chartTokens().labelOnFill : chartTokens().textStrong;
            ctx.strokeStyle = 'transparent';
            ctx.lineWidth = stacked ? 3 : 0;

            let x = 0;
            let y = 0;

            if (stacked) {
              ctx.textBaseline = 'middle';
              ctx.textAlign = 'center';
              x = isHorizontal ? (element.x + element.base) / 2 : element.x;
              y = isHorizontal ? element.y : (element.y + element.base) / 2;
              ctx.strokeText(valueLabel, x, y);
              ctx.fillText(valueLabel, x, y);
            } else {
              const offset = 10;
              if (isHorizontal) {
                ctx.textAlign = 'left';
                ctx.textBaseline = 'middle';
                x = element.x + offset;
                y = element.y;
              } else {
                ctx.textAlign = 'center';
                ctx.textBaseline = 'bottom';
                x = element.x;
                y = element.y - offset;
              }
              ctx.fillText(valueLabel, x, y);
            }
          });
        });
        // END SECTION

        // SECTION: Total labels for stacked bars
        if (stacked && (yScale || xScale)) {
          // SECTION: Total label placement guards
          const chartArea = chart.chartArea || {};
          const chartTop = chartArea.top ?? 0;
          const chartBottom = chartArea.bottom ?? chart.height;
          const chartLeft = chartArea.left ?? 0;
          const chartRight = chartArea.right ?? chart.width;
          const horizontalPadding = totalFontSize * 0.6;
          const verticalPadding = totalFontSize * 0.8;
          // END SECTION

          totals.forEach((total, index) => {
            if (!total) {
              return;
            }

            const valueLabel = formatNumber(total);
            if (!valueLabel) {
              return;
            }

            const anchor = getFirstElementAtIndex(chart, index);
            if (!anchor) {
              return;
            }

            ctx.font = `${weight} ${totalFontSize}px ${labelFontFamily}`;
            ctx.fillStyle = chartTokens().textStrong;
            ctx.textBaseline = 'middle';

            if (isHorizontal && xScale) {
              const offset = Math.round(totalFontSize * 0.35);
              let x = xScale.getPixelForValue(total) + offset;
              let y = anchor.y ?? 0;
              x = Math.min(Math.max(x, chartLeft + horizontalPadding), chartRight - horizontalPadding);
              y = Math.min(Math.max(y, chartTop + horizontalPadding), chartBottom - horizontalPadding);
              ctx.textAlign = 'left';
              ctx.fillText(valueLabel, x, y);
            } else if (yScale) {
              const offset = Math.round(totalFontSize * 0.45);
              let x = anchor.x ?? 0;
              let y = yScale.getPixelForValue(total) - offset;
              x = Math.min(Math.max(x, chartLeft + horizontalPadding), chartRight - horizontalPadding);
              y = Math.min(Math.max(y, chartTop + verticalPadding), chartBottom - verticalPadding);
              ctx.textAlign = 'center';
              ctx.fillText(valueLabel, x, y);
            }
          });
        }
        // END SECTION
      }

      if (type === 'doughnut' || type === 'pie') {
        const dataset = datasets[0];
        const meta = chart.getDatasetMeta(0);
        if (!dataset || !meta?.data?.length) {
          ctx.restore();
          return;
        }

        const values = dataset.data?.map((value) => ensureNumber(value)) ?? [];
        const total = values.reduce((sum, value) => sum + value, 0);
        const showPercentages = values.length <= 10;

        meta.data.forEach((arc, index) => {
          const value = values[index];
          if (!value || !total) {
            return;
          }

          const percent = Math.round((value / total) * 100);
          const labelText = showPercentages
            ? `${formatNumber(value)} (${percent}%)`
            : formatNumber(value);
          if (!labelText) {
            return;
          }

          const angle = (arc.startAngle + arc.endAngle) / 2;
          const radius = (arc.outerRadius + arc.innerRadius) / 2;
          const x = arc.x + Math.cos(angle) * radius;
          const y = arc.y + Math.sin(angle) * radius;

          ctx.font = `${weight} ${segmentFontSize}px ${labelFontFamily}`;
          ctx.textAlign = 'center';
          ctx.textBaseline = 'middle';
          ctx.fillStyle = chartTokens().textStrong;
          ctx.fillText(labelText, x, y);
        });
      }

      ctx.restore();
    }
  };
}
// END SECTION

function sanitizeFilename(name) {
  return (name || 'chart')
    .trim()
    .replace(/[\\/:*?"<>|]+/g, '')
    .replace(/\s+/g, '-')
    .toLowerCase();
}

function cloneForExport(value) {
  if (Array.isArray(value)) {
    return value.map(cloneForExport);
  }

  if (!value || typeof value !== 'object') {
    return value;
  }

  if (value instanceof Date) {
    return new Date(value.getTime());
  }

  const out = {};
  Object.keys(value).forEach((key) => {
    out[key] = cloneForExport(value[key]);
  });
  return out;
}

// SECTION: Export theme overrides
function applyExportChartOverrides(exportConfig, dpr = EXPORT_PROFILE.dpr) {
  exportConfig.options = exportConfig.options || {};
  exportConfig.options.plugins = exportConfig.options.plugins || {};
  exportConfig.options.scales = exportConfig.options.scales || {};
  exportConfig.options.layout = exportConfig.options.layout || {};
  const scale = dpr;

  // SECTION: Export-only rendering rules
  exportConfig.options.responsive = false;
  exportConfig.options.animation = false;
  exportConfig.options.maintainAspectRatio = false;
  exportConfig.options.devicePixelRatio = dpr;
  exportConfig.options.layout.padding = {
    top: EXPORT_PADDING.top * scale,
    right: EXPORT_PADDING.right * scale,
    bottom: EXPORT_PADDING.bottom * scale,
    left: EXPORT_PADDING.left * scale
  };
  exportConfig.options.plugins.datalabels = false;
  exportConfig.options.plugins.pmValueLabels = { display: false }; // the export draws its own, larger labels
  exportConfig.options.plugins.legend = { ...(exportConfig.options.plugins.legend || {}), display: (exportConfig.data?.datasets?.length ?? 0) > 1 };
  delete exportConfig.options.onClick;
  delete exportConfig.options.onHover;
  // END SECTION

  // SECTION: Legend typography
  exportConfig.options.plugins.legend = exportConfig.options.plugins.legend || {};
  exportConfig.options.plugins.legend.position = 'top';
  exportConfig.options.plugins.legend.labels = exportConfig.options.plugins.legend.labels || {};
  exportConfig.options.plugins.legend.labels.font = {
    ...(exportConfig.options.plugins.legend.labels.font || {}),
    size: EXPORT_TYPOGRAPHY.legend * scale,
    weight: '600'
  };
  exportConfig.options.plugins.legend.labels.boxWidth = 20 * scale;
  exportConfig.options.plugins.legend.labels.boxHeight = 20 * scale;
  exportConfig.options.plugins.legend.labels.padding = 18 * scale;
  exportConfig.options.plugins.legend.labels.usePointStyle = false;
  // END SECTION

  // SECTION: Title/subtitle typography
  if (exportConfig.options.plugins.title?.display) {
    exportConfig.options.plugins.title.font = {
      size: EXPORT_TYPOGRAPHY.title * scale,
      weight: '700'
    };
  }
  if (exportConfig.options.plugins.subtitle?.display) {
    exportConfig.options.plugins.subtitle.font = {
      size: EXPORT_TYPOGRAPHY.subtitle * scale,
      weight: '500'
    };
  }
  // END SECTION

  // SECTION: Axis typography
  if (exportConfig.options.scales.x?.ticks) {
    exportConfig.options.scales.x.ticks.font = {
      ...(exportConfig.options.scales.x.ticks.font || {}),
      size: EXPORT_TYPOGRAPHY.axisX * scale,
      weight: '600'
    };
    exportConfig.options.scales.x.ticks.padding =
      (exportConfig.options.scales.x.ticks.padding ?? 12) * scale;
    exportConfig.options.scales.x.ticks.maxRotation = 0;
    exportConfig.options.scales.x.ticks.minRotation = 0;
  }

  if (exportConfig.options.scales.y?.ticks) {
    exportConfig.options.scales.y.ticks.font = {
      ...(exportConfig.options.scales.y.ticks.font || {}),
      size: EXPORT_TYPOGRAPHY.axisY * scale,
      weight: '600'
    };
    exportConfig.options.scales.y.ticks.padding =
      (exportConfig.options.scales.y.ticks.padding ?? 12) * scale;
  }
  // END SECTION

  // SECTION: Export value label overrides
  exportConfig.options.plugins.exportValueLabels = {
    segmentFontSize: EXPORT_TYPOGRAPHY.dataLabel * scale,
    totalFontSize: EXPORT_TYPOGRAPHY.totalLabel * scale,
    fontWeight: '700'
  };
  // END SECTION
}
// END SECTION

function exportChartAsPngFullHd(chart, opts = {}) {
  const width = opts.width ?? EXPORT_PROFILE.width;
  const height = opts.height ?? EXPORT_PROFILE.height;
  const dpr = opts.dpr ?? EXPORT_PROFILE.dpr;
  const scale = dpr;
  const title = opts.title ?? '';
  const meta = opts.meta ?? '';
  const contextLine = opts.contextLine ?? '';
  const branding = opts.branding ?? '';
  const backgroundColor = (opts.backgroundColor ?? getCssVar('--pm-card', '')) || chartTokens().card;

  const hiResWidth = width * dpr;
  const hiResHeight = height * dpr;
  const offscreen = document.createElement('canvas');
  offscreen.width = hiResWidth;
  offscreen.height = hiResHeight;
  offscreen.style.width = `${width}px`;
  offscreen.style.height = `${height}px`;

  const exportConfig = {
    type: chart.config.type,
    data: cloneForExport(chart.config.data),
    options: cloneForExport(chart.config.options),
    plugins: Array.isArray(chart.config.plugins) ? [...chart.config.plugins] : []
  };

  // SECTION: Export theme overrides
  applyExportChartOverrides(exportConfig, dpr);
  // END SECTION

  exportConfig.plugins.push({
    id: 'exportBackgroundFill',
    beforeDraw(c) {
      const ctx = c.ctx;
      ctx.save();
      ctx.globalCompositeOperation = 'destination-over';
      ctx.fillStyle = backgroundColor;
      ctx.fillRect(0, 0, c.width, c.height);
      ctx.restore();
    }
  });

  exportConfig.plugins.push(
    createExportDataLabelsPlugin({
      fontSegment: EXPORT_TYPOGRAPHY.dataLabel,
      fontTotal: EXPORT_TYPOGRAPHY.totalLabel
    }),
    createExportHeaderFooterPlugin({
      title,
      meta,
      contextLine,
      timestamp: formatExportTimestamp(),
      branding,
      scale
    })
  );

  const exportChart = new Chart(offscreen.getContext('2d'), exportConfig);
  const finalCanvas = document.createElement('canvas');
  finalCanvas.width = width;
  finalCanvas.height = height;
  const finalCtx = finalCanvas.getContext('2d');
  finalCtx.drawImage(offscreen, 0, 0, width, height);
  const dataUrl = finalCanvas.toDataURL('image/png', 1.0);

  exportChart.destroy();
  return dataUrl;
}

function triggerDownload(dataUrl, filenameBase) {
  const today = new Date().toISOString().slice(0, 10);
  const link = document.createElement('a');
  link.href = dataUrl;
  link.download = `${filenameBase}-${today}.png`;
  document.body.appendChild(link);
  link.click();
  link.remove();
}

function initAnalyticsChartDownloads() {
  const buttons = document.querySelectorAll('[data-chart-download-target]');
  if (!buttons.length) {
    return;
  }

  buttons.forEach((btn) => {
    const targetId = btn.dataset.chartDownloadTarget;
    const canvas = targetId ? document.getElementById(targetId) : null;

    if (!canvas) {
      btn.disabled = true;
      return;
    }

    const chartInstance = Chart.getChart(canvas);
    btn.disabled = !chartInstance;

    btn.addEventListener('click', () => {
      const chart = Chart.getChart(canvas);
      if (!chart) {
        return;
      }

      const card = btn.closest('.analytics-card');
      const titleEl = card ? card.querySelector('.analytics-card__title') : null;
      const metaEl = card ? card.querySelector('.analytics-card__meta') : null;
      const titleText = titleEl ? titleEl.textContent : 'chart';
      const metaText = metaEl ? metaEl.textContent : '';
      const tabValue = document.querySelector('.analytics-page')?.dataset?.analyticsTab ?? '';
      const contextLine = tabValue ? `Analytics - ${tabValue}` : 'Analytics';
      const filenameBase = sanitizeFilename(titleText);

      const png = exportChartAsPngFullHd(chart, {
        width: 1920,
        height: 1080,
        dpr: 2,
        title: titleText,
        meta: metaText,
        contextLine,
        branding: 'PRISM ERP'
      });
      triggerDownload(png, filenameBase);
    });
  });
}
// END SECTION
