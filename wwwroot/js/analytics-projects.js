const paletteFallback = [
  '#2563eb',
  '#f97316',
  '#22c55e',
  '#a855f7',
  '#9c27b0',
  '#fb8c00',
  '#00acc1',
  '#8d6e63',
  '#5c6bc0',
  '#43a047'
];

const lifecycleStatuses = ['Ongoing', 'Completed', 'Cancelled'];
const chartRegistry = new Map();

// SECTION: Ongoing analytics storage keys
const ongoingStorageKeys = {
  stageCategories: 'analytics.ongoing.stage.categories',
  durationCategories: 'analytics.ongoing.duration.categories',
  stageShowLabels: 'analytics.ongoing.stage.showLabels',
  durationShowLabels: 'analytics.ongoing.duration.showLabels'
};
// END SECTION

function getPalette() {
  if (window.PMTheme && typeof window.PMTheme.getChartPalette === 'function') {
    const palette = window.PMTheme.getChartPalette();
    if (palette && Array.isArray(palette.accents)) {
      return palette;
    }
  }

  return {
    axisColor: '#4b5563',
    gridColor: '#e5e7eb',
    accents: paletteFallback
  };
}

function getAccentColor(index) {
  const palette = getPalette();
  const accents = palette.accents && palette.accents.length ? palette.accents : paletteFallback;
  return accents[index % accents.length];
}

function getLifecycleColor(status) {
  const lifecycleColors = {
    Ongoing: getAccentColor(0),
    Completed: getAccentColor(1),
    Cancelled: getAccentColor(2)
  };
  return lifecycleColors[status] || getAccentColor(0);
}

const stageTimeBucketKeys = {
  below: 'Below1Cr',
  above: 'AboveOrEqual1Cr'
};

const MAX_STAGE_AXIS_LABEL_LENGTH = 16;

// SECTION: Label helpers
// Wrap long axis labels onto multiple lines so they do not overlap
function wrapLabel(label, maxLineLength = 16) {
  if (!label) {
    return '';
  }

  const text = String(label).trim();
  if (!text) {
    return '';
  }

  const words = tokenizeLabel(text, maxLineLength);
  const lines = [];
  let current = '';

  const pushCurrentLine = () => {
    if (current) {
      lines.push(current);
      current = '';
    }
  };

  words.forEach((word) => {
    const segments = splitLabelSegment(word, maxLineLength);

    segments.forEach((segment, index) => {
      const candidate = current ? `${current} ${segment}` : segment;
      if (candidate.length > maxLineLength && current) {
        pushCurrentLine();
        current = segment;
      } else {
        current = candidate;
      }

      const hasMoreSegments = index < segments.length - 1;
      if (hasMoreSegments) {
        pushCurrentLine();
      }
    });
  });

  pushCurrentLine();

  if (!lines.length) {
    return text;
  }

  // Chart.js supports either a string or an array of strings.
  return lines.length > 1 ? lines : lines[0];
}

function tokenizeLabel(text, maxLineLength) {
  return text
    .replace(/([-/])/g, '$1 ')
    .split(/\s+/)
    .filter(Boolean)
    .map((token) => token.trim())
    .flatMap((token) => splitLabelSegment(token, maxLineLength));
}

function splitLabelSegment(segment, maxLineLength) {
  if (!segment) {
    return [];
  }

  if (segment.length <= maxLineLength) {
    return [segment];
  }

  const parts = [];
  let start = 0;
  while (start < segment.length) {
    parts.push(segment.slice(start, start + maxLineLength));
    start += maxLineLength;
  }

  return parts;
}
// END SECTION

// SECTION: Chart helpers
function createDoughnutChart(canvas, { labels, values, colors, options }) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const palette = getPalette();
  const accentColors = Array.isArray(colors) && colors.length
    ? colors
    : values.map((_, idx) => getAccentColor(idx));

  return renderWithTheme(canvas, {
    type: 'doughnut',
    data: {
      labels,
      datasets: [
        {
          data: values,
          backgroundColor: accentColors,
          borderWidth: 0
        }
      ]
    },
    options: mergeChartOptions(
      {
        responsive: true,
        plugins: {
          legend: { position: 'bottom', labels: { color: palette.axisColor } }
        }
      },
      options
    )
  });
}

function createBarChart(
  canvas,
  { labels, values, label = 'Projects', backgroundColor = null, options }
) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const rawLabels = Array.isArray(labels) ? labels : [];
  const wrappedLabels = rawLabels.map((l) => wrapLabel(l));
  const palette = getPalette();

  return renderWithTheme(canvas, {
    type: 'bar',
    data: {
      labels: wrappedLabels,
      datasets: [
        {
          label,
          data: values,
          backgroundColor: backgroundColor || getAccentColor(0),
          borderRadius: 4
        }
      ]
    },
    options: mergeChartOptions(
      {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false, labels: { color: palette.axisColor } }
        },
        scales: {
          x: {
            ticks: {
              maxRotation: 0,
              autoSkip: false,
              color: palette.axisColor
            },
            grid: { color: palette.gridColor }
          },
          y: {
            beginAtZero: true,
            ticks: { color: palette.axisColor },
            grid: { color: palette.gridColor }
          }
        }
      },
      options
    )
  });
}
// END SECTION

// SECTION: Value labels plugin
const valueLabelsPlugin = {
  id: 'valueLabels',
  afterDatasetsDraw(chart, _args, pluginOptions) {
    if (!pluginOptions?.display) {
      return;
    }

    const mode = pluginOptions.mode ?? 'stack-total';
    const formatter = typeof pluginOptions.formatter === 'function'
      ? pluginOptions.formatter
      : (value) => value;

    if (mode === 'dataset') {
      drawDatasetLabels(chart, formatter);
      return;
    }

    drawStackTotals(chart, formatter);
  }
};

if (window.Chart && typeof window.Chart.register === 'function') {
  window.Chart.register(valueLabelsPlugin);
}

function drawStackTotals(chart, formatter) {
  const metas = chart.getSortedVisibleDatasetMetas();
  if (!metas.length) {
    return;
  }

  const totals = new Array(chart.data.labels.length).fill(0);

  metas.forEach((meta) => {
    const dataset = chart.data.datasets[meta.index];
    meta.data.forEach((_bar, index) => {
      totals[index] += toNumber(dataset.data[index]);
    });
  });

  const anchorMeta = metas[metas.length - 1];
  const { ctx } = chart;
  ctx.save();
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  ctx.font = '12px sans-serif';
  ctx.fillStyle = getPalette().axisColor;

  anchorMeta.data.forEach((bar, index) => {
    const total = totals[index];
    if (!total) {
      return;
    }

    ctx.fillText(String(formatter(total)), bar.x, bar.y - 4);
  });

  ctx.restore();
}

function drawDatasetLabels(chart, formatter) {
  const metas = chart.getSortedVisibleDatasetMetas();
  if (!metas.length) {
    return;
  }

  const { ctx } = chart;
  ctx.save();
  ctx.textAlign = 'center';
  ctx.textBaseline = 'bottom';
  ctx.font = '12px sans-serif';
  ctx.fillStyle = getPalette().axisColor;

  metas.forEach((meta) => {
    const dataset = chart.data.datasets[meta.index];
    meta.data.forEach((bar, index) => {
      const value = toNumber(dataset.data[index]);
      if (!value) {
        return;
      }

      ctx.fillText(String(formatter(value)), bar.x, bar.y - 4);
    });
  });

  ctx.restore();
}
// END SECTION

// SECTION: Stacked and grouped bar helpers
function createStackedBarChart(canvas, { labels, datasets, options }) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const rawLabels = Array.isArray(labels) ? labels : [];
  const wrappedLabels = rawLabels.map((label) => wrapLabel(label, MAX_STAGE_AXIS_LABEL_LENGTH));

  return renderWithTheme(canvas, (ctx, palette) => {
    const resolvedDatasets = datasets.map((dataset, index) => ({
      ...dataset,
      backgroundColor: palette.accents[index % palette.accents.length],
      borderRadius: 4
    }));

    return new window.Chart(ctx, {
      type: 'bar',
      data: {
        labels: wrappedLabels,
        datasets: resolvedDatasets
      },
      options: mergeChartOptions(
        {
          responsive: true,
          maintainAspectRatio: false,
          scales: {
            x: {
              stacked: true,
              ticks: {
                autoSkip: false,
                maxRotation: 0,
                minRotation: 0,
                color: palette.axisColor
              },
              grid: { color: palette.gridColor }
            },
            y: {
              stacked: true,
              beginAtZero: true,
              ticks: { color: palette.axisColor },
              grid: { color: palette.gridColor }
            }
          },
          plugins: {
            legend: {
              display: true,
              position: 'bottom',
              labels: { color: palette.axisColor }
            },
            valueLabels: {
              display: false,
              mode: 'stack-total'
            }
          }
        },
        options
      )
    });
  });
}

function createGroupedBarChart(canvas, { labels, datasets, options }) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const rawLabels = Array.isArray(labels) ? labels : [];
  const wrappedLabels = rawLabels.map((label) => wrapLabel(label, MAX_STAGE_AXIS_LABEL_LENGTH));

  return renderWithTheme(canvas, (ctx, palette) => {
    const resolvedDatasets = datasets.map((dataset, index) => ({
      ...dataset,
      backgroundColor: palette.accents[index % palette.accents.length],
      borderRadius: 4
    }));

    return new window.Chart(ctx, {
      type: 'bar',
      data: {
        labels: wrappedLabels,
        datasets: resolvedDatasets
      },
      options: mergeChartOptions(
        {
          responsive: true,
          maintainAspectRatio: false,
          scales: {
            x: {
              stacked: false,
              ticks: {
                autoSkip: false,
                maxRotation: 0,
                minRotation: 0,
                color: palette.axisColor
              },
              grid: { color: palette.gridColor }
            },
            y: {
              stacked: false,
              beginAtZero: true,
              ticks: { color: palette.axisColor },
              grid: { color: palette.gridColor }
            }
          },
          plugins: {
            legend: {
              display: true,
              position: 'bottom',
              labels: { color: palette.axisColor }
            },
            valueLabels: {
              display: false,
              mode: 'dataset'
            }
          }
        },
        options
      )
    });
  });
}

function buildCategoryDatasets(points, valueKey, { stacked }) {
  const stages = [];
  const stageIndex = new Map();
  const categories = [];
  const categoryIndex = new Map();

  points.forEach((point) => {
    if (!stageIndex.has(point.stageCode)) {
      stageIndex.set(point.stageCode, stages.length);
      stages.push({ code: point.stageCode, name: point.stageName });
    }

    const categoryKey = String(point.parentCategoryId);
    if (!categoryIndex.has(categoryKey)) {
      categoryIndex.set(categoryKey, categories.length);
      categories.push({ id: point.parentCategoryId, name: point.categoryName });
    }
  });

  const datasets = categories.map((category) => ({
    label: category.name,
    data: new Array(stages.length).fill(0),
    stack: stacked ? 'byCategory' : undefined
  }));

  points.forEach((point) => {
    const stageIdx = stageIndex.get(point.stageCode);
    const categoryIdx = categoryIndex.get(String(point.parentCategoryId));
    if (stageIdx === undefined || categoryIdx === undefined) {
      return;
    }

    datasets[categoryIdx].data[stageIdx] = toNumber(point[valueKey]);
  });

  return {
    labels: stages.map((stage) => stage.name),
    categories,
    datasets
  };
}
// END SECTION

// SECTION: Chart option helpers
function mergeChartOptions(base, override) {
  if (!override) {
    return base;
  }

  return deepMerge(base, override);
}

function deepMerge(target, source) {
  const output = { ...target };

  Object.keys(source).forEach((key) => {
    const sourceValue = source[key];
    const targetValue = target[key];

    if (sourceValue && typeof sourceValue === 'object' && !Array.isArray(sourceValue)) {
      const baseValue =
        targetValue && typeof targetValue === 'object' && !Array.isArray(targetValue)
          ? targetValue
          : {};
      output[key] = deepMerge(baseValue, sourceValue);
    } else {
      output[key] = sourceValue;
    }
  });

  return output;
}

function renderWithTheme(canvas, config) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const context = canvas.getContext('2d');
  if (!context) {
    return null;
  }

  const existing = typeof window.Chart.getChart === 'function'
    ? window.Chart.getChart(canvas)
    : null;

  if (existing) {
    existing.destroy();
  }

  const palette = getPalette();
  const resolvedConfig = typeof config === 'function' ? config(context, palette) : config;
  return new window.Chart(context, resolvedConfig);
}

function ensureNumber(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function getDefaultLegendClick() {
  const fallback = window.Chart?.defaults?.plugins?.legend?.onClick;
  return typeof fallback === 'function' ? fallback : function noop() {};
}
// END SECTION

// SECTION: Dataset helpers
function parseSeries(canvas) {
  if (!canvas) {
    return [];
  }

  const payload = canvas.dataset?.series;
  if (!payload) {
    return [];
  }

  try {
    return JSON.parse(payload);
  } catch (error) {
    console.error('Failed to parse analytics series payload.', error);
    return [];
  }
}

function parseSeriesByCategory(canvas) {
  if (!canvas) {
    return [];
  }

  const payload = canvas.dataset?.seriesByCategory;
  if (!payload) {
    return [];
  }

  try {
    return JSON.parse(payload);
  } catch (error) {
    console.error('Failed to parse analytics category breakdown payload.', error);
    return [];
  }
}

function renderSeriesChart(canvas, renderCallback) {
  if (!canvas) {
    return;
  }

  const series = parseSeries(canvas);
  if (!series.length) {
    renderEmptyState(canvas);
    return;
  }

  renderCallback(series);
}

function renderEmptyState(canvas) {
  if (!canvas) {
    return;
  }

  const container = canvas.parentElement;
  if (!container) {
    return;
  }

  const message = canvas.dataset?.emptyMessage ?? 'No data available yet.';
  canvas.style.display = 'none';
  canvas.setAttribute('aria-hidden', 'true');

  if (container.querySelector('.analytics-empty-state')) {
    return;
  }

  const placeholder = document.createElement('div');
  placeholder.className = 'analytics-empty-state';
  placeholder.setAttribute('role', 'status');
  placeholder.textContent = message;
  container.appendChild(placeholder);
}
// END SECTION

// SECTION: Stage axis helpers
const stageAxisLabelOverrides = {
  EAS: 'EAS',
  SO: 'SO'
};

function getStageAxisLabel(point) {
  if (!point) {
    return '';
  }

  const name = point.name ?? '';
  const code = point.stageCode ?? '';

  const normalizedCode = code.toUpperCase();
  const overrideLabel = stageAxisLabelOverrides[normalizedCode];
  if (overrideLabel) {
    return overrideLabel;
  }

  if (!name) {
    return code;
  }

  if (name.length <= MAX_STAGE_AXIS_LABEL_LENGTH || !code) {
    return name;
  }

  return code;
}

function createStageTooltipTitle(series) {
  return (tooltipItems) => {
    if (!tooltipItems?.length) {
      return '';
    }

    const index = tooltipItems[0]?.dataIndex ?? 0;
    return series[index]?.name ?? tooltipItems[0]?.label ?? '';
  };
}
// END SECTION

// SECTION: Completed analytics helpers
function getCompletedAnalyticsData() {
  const panel = document.querySelector('.analytics-panel--completed');
  if (!panel) {
    return null;
  }

  const json = panel.dataset.completedAnalytics;
  if (!json) {
    return null;
  }

  try {
    return JSON.parse(json);
  } catch (error) {
    console.error('Failed to parse completed analytics payload.', error);
    return null;
  }
}

// SECTION: Completed analytics chart builders
function createCompletedPerYearStackedChart(canvas, points) {
  if (!canvas || !window.Chart || !Array.isArray(points) || !points.length) {
    return null;
  }

  const context = canvas.getContext('2d');
  if (!context) {
    return null;
  }

  const years = Array.from(new Set(points.map((point) => point.year))).sort(
    (first, second) => first - second
  );

  const categories = Array.from(
    new Set(points.map((point) => point.categoryName))
  ).sort((first, second) => first.localeCompare(second));

  const palette = getPalette();
  const accentColors = palette.accents && palette.accents.length ? palette.accents : paletteFallback;

  const datasets = categories.map((category, index) => ({
    label: category,
    data: years.map((year) => {
      const match = points.find(
        (point) => point.year === year && point.categoryName === category
      );
      return ensureNumber(match?.count);
    }),
    backgroundColor: accentColors[index % accentColors.length],
    borderWidth: 1
  }));

  return renderWithTheme(canvas, {
    type: 'bar',
    data: {
      labels: years,
      datasets
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: {
        mode: 'index',
        intersect: false
      },
      plugins: {
        legend: {
          position: 'top',
          labels: { color: palette.axisColor }
        },
        tooltip: {
          callbacks: {
            footer(context) {
              const total = context.reduce(
                (sum, entry) => sum + ensureNumber(entry.parsed?.y),
                0
              );
              return `Total: ${total}`;
            }
          }
        }
      },
      scales: {
        x: {
          stacked: true,
          title: {
            display: true,
            text: 'Year',
            color: palette.axisColor
          }
        },
        y: {
          stacked: true,
          beginAtZero: true,
          title: {
            display: true,
            text: 'Number of projects',
            color: palette.axisColor
          },
          ticks: {
            precision: 0,
            color: palette.axisColor
          },
          grid: { color: palette.gridColor }
        }
      }
    }
  });
}
// END SECTION

function initCompletedAnalytics() {
  const data = getCompletedAnalyticsData();
  if (!data) {
    return;
  }

  const byCategoryEl = document.getElementById('completedByCategoryChart');
  const byTechnicalEl = document.getElementById('completedByTechnicalChart');
  const perYearEl = document.getElementById('completedPerYearChart');

  if (byCategoryEl && data.byCategory?.length) {
    createDoughnutChart(byCategoryEl, {
      labels: data.byCategory.map((point) => point.name),
      values: data.byCategory.map((point) => point.count)
    });
  }

  if (byTechnicalEl && data.byTechnical?.length) {
    createBarChart(byTechnicalEl, {
      labels: data.byTechnical.map((point) => point.name),
      values: data.byTechnical.map((point) => point.count)
    });
  }

  if (perYearEl) {
    if (data.perYearByParentCategory?.length) {
      createCompletedPerYearStackedChart(perYearEl, data.perYearByParentCategory);
    } else if (data.perYear?.length) {
      createBarChart(perYearEl, {
        labels: data.perYear.map((point) => point.year?.toString() ?? ''),
        values: data.perYear.map((point) => point.count),
        label: 'Projects completed',
        backgroundColor: getAccentColor(2)
      });
    }
  }
}
// END SECTION

// SECTION: Ongoing chart controls helpers
function bindCategoryControls({
  canvas,
  chart,
  categories,
  categoriesKey,
  labelsKey,
  labelMode,
  filename
}) {
  if (!canvas || !chart) {
    return;
  }

  chartRegistry.set(canvas.id, chart);
  chart.$categoryIds = categories.map((category) => category.id);

  const controls = document.querySelector(`[data-chart-controls-for="${canvas.id}"]`);
  if (!controls) {
    return;
  }

  const select = controls.querySelector('[data-chart-category-multiselect]');
  const selectAllButton = controls.querySelector('[data-chart-select-all]');
  const clearButton = controls.querySelector('[data-chart-clear]');
  const showLabelsToggle = controls.querySelector('[data-chart-show-labels]');
  const downloadButton = controls.querySelector('[data-chart-download]');

  populateCategorySelect(select, categories);

  const defaultCategoryIds = categories.map((category) => String(category.id));
  const persistedCategoryIds = readStoredList(categoriesKey);
  const initialCategoryIds = persistedCategoryIds.length ? persistedCategoryIds : defaultCategoryIds;
  setSelectValues(select, initialCategoryIds);
  applyCategorySelection(chart, initialCategoryIds);

  const persistedShowLabels = readStoredBoolean(labelsKey, false);
  setToggleChecked(showLabelsToggle, persistedShowLabels);
  applyLabelToggle(chart, persistedShowLabels, labelMode);

  select?.addEventListener('change', () => {
    const selected = getSelectedValues(select);
    storeList(categoriesKey, selected);
    applyCategorySelection(chart, selected);
  });

  selectAllButton?.addEventListener('click', () => {
    setSelectValues(select, defaultCategoryIds);
    storeList(categoriesKey, defaultCategoryIds);
    applyCategorySelection(chart, defaultCategoryIds);
  });

  clearButton?.addEventListener('click', () => {
    setSelectValues(select, []);
    storeList(categoriesKey, []);
    applyCategorySelection(chart, []);
  });

  showLabelsToggle?.addEventListener('change', () => {
    const checked = Boolean(showLabelsToggle.checked);
    storeBoolean(labelsKey, checked);
    applyLabelToggle(chart, checked, labelMode);
  });

  downloadButton?.addEventListener('click', () => {
    downloadCanvasPng(canvas, filename);
  });
}

function populateCategorySelect(select, categories) {
  if (!select) {
    return;
  }

  select.innerHTML = '';
  categories.forEach((category) => {
    const option = document.createElement('option');
    option.value = String(category.id);
    option.textContent = category.name;
    select.appendChild(option);
  });
}

function applyCategorySelection(chart, selectedIds) {
  const selected = new Set(selectedIds);

  chart.data.datasets.forEach((dataset, index) => {
    const datasetId = String(chart.$categoryIds?.[index] ?? '');
    dataset.hidden = !selected.has(datasetId);
  });

  chart.update();
}

function applyLabelToggle(chart, checked, mode) {
  if (!chart.options.plugins) {
    chart.options.plugins = {};
  }

  chart.options.plugins.valueLabels = {
    ...(chart.options.plugins.valueLabels ?? {}),
    display: checked,
    mode
  };

  chart.update();
}

function downloadCanvasPng(canvas, filename) {
  if (!canvas?.toBlob) {
    return;
  }

  canvas.toBlob((blob) => {
    if (!blob) {
      return;
    }

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  });
}

function getSelectedValues(select) {
  if (!select) {
    return [];
  }

  return Array.from(select.selectedOptions).map((option) => option.value);
}

function setSelectValues(select, values) {
  if (!select) {
    return;
  }

  const allowed = new Set(values);
  Array.from(select.options).forEach((option) => {
    option.selected = allowed.has(option.value);
  });
}

function setToggleChecked(toggle, checked) {
  if (!toggle) {
    return;
  }

  toggle.checked = checked;
}

function readStoredList(key) {
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.map((value) => String(value)) : [];
  } catch (error) {
    console.warn('Unable to read stored analytics selection.', error);
    return [];
  }
}

function storeList(key, values) {
  try {
    window.localStorage.setItem(key, JSON.stringify(values));
  } catch (error) {
    console.warn('Unable to store analytics selection.', error);
  }
}

function readStoredBoolean(key, fallback) {
  try {
    const raw = window.localStorage.getItem(key);
    if (raw === null) {
      return fallback;
    }

    return raw === 'true';
  } catch (error) {
    console.warn('Unable to read stored analytics toggle.', error);
    return fallback;
  }
}

function storeBoolean(key, value) {
  try {
    window.localStorage.setItem(key, value ? 'true' : 'false');
  } catch (error) {
    console.warn('Unable to store analytics toggle.', error);
  }
}
// END SECTION

// SECTION: Ongoing analytics initialiser
function initOngoingAnalytics() {
  const categoryCanvas = document.getElementById('ongoing-by-category-chart');
  const stageCanvas = document.getElementById('ongoing-by-stage-chart');
  const durationCanvas = document.getElementById('ongoing-stage-duration-chart');

  if (categoryCanvas) {
    const series = parseSeries(categoryCanvas);
    if (series.length) {
      createDoughnutChart(categoryCanvas, {
        labels: series.map((point) => point.name),
        values: series.map((point) => point.count)
      });
    }
  }

  if (stageCanvas) {
    const seriesByCategory = parseSeriesByCategory(stageCanvas);
    if (seriesByCategory.length) {
      const built = buildCategoryDatasets(seriesByCategory, 'count', { stacked: true });
      const chart = createStackedBarChart(stageCanvas, {
        labels: built.labels.map((label) => getStageAxisLabel({ name: label })),
        datasets: built.datasets,
        options: {
          interaction: {
            mode: 'index',
            intersect: false
          },
          plugins: {
            tooltip: {
              callbacks: {
                label(context) {
                  const value = toNumber(context.parsed?.y);
                  return `${context.dataset.label}: ${value}`;
                }
              }
            },
            valueLabels: {
              formatter: (value) => Math.round(toNumber(value))
            }
          },
          scales: {
            y: {
              ticks: {
                precision: 0
              }
            }
          }
        }
      });

      bindCategoryControls({
        canvas: stageCanvas,
        chart,
        categories: built.categories,
        categoriesKey: ongoingStorageKeys.stageCategories,
        labelsKey: ongoingStorageKeys.stageShowLabels,
        labelMode: 'stack-total',
        filename: 'ongoing-projects-by-stage.png'
      });
    } else {
      const series = parseSeries(stageCanvas);
      if (series.length) {
        createBarChart(stageCanvas, {
          labels: series.map((point) => point.name),
          values: series.map((point) => point.count),
          label: 'Projects'
        });
      }
    }
  }

  if (durationCanvas) {
    const seriesByCategory = parseSeriesByCategory(durationCanvas);
    if (seriesByCategory.length) {
      const built = buildCategoryDatasets(seriesByCategory, 'days', { stacked: false });
      const chart = createGroupedBarChart(durationCanvas, {
        labels: built.labels.map((label) => getStageAxisLabel({ name: label })),
        datasets: built.datasets,
        options: {
          interaction: {
            mode: 'index',
            intersect: false
          },
          plugins: {
            tooltip: {
              callbacks: {
                label(context) {
                  const value = toNumber(context.parsed?.y);
                  return `${context.dataset.label}: ${value.toFixed(1)} days`;
                }
              }
            },
            valueLabels: {
              formatter: (value) => toNumber(value).toFixed(1)
            }
          }
        }
      });

      bindCategoryControls({
        canvas: durationCanvas,
        chart,
        categories: built.categories,
        categoriesKey: ongoingStorageKeys.durationCategories,
        labelsKey: ongoingStorageKeys.durationShowLabels,
        labelMode: 'dataset',
        filename: 'ongoing-stage-duration-by-category.png'
      });
    } else {
      const series = parseSeries(durationCanvas);
      if (series.length) {
        const labels = series.map((point) => getStageAxisLabel(point));
        createBarChart(durationCanvas, {
          labels,
          values: series.map((point) => point.days),
          label: 'Average days in stage',
          backgroundColor: getAccentColor(2),
          options: {
            plugins: {
              tooltip: {
                callbacks: {
                  title: createStageTooltipTitle(series)
                }
              }
            },
            scales: {
              x: {
                ticks: {
                  autoSkip: false,
                  maxRotation: 0,
                  minRotation: 0
                }
              }
            }
          }
        });
      }
    }
  }
}
// END SECTION

// SECTION: CoE analytics initialiser
// Stage label overrides specific to the CoE stage chart
const coeStageLabelOverrides = {
  'SOW Vetting': 'SoW',
  Development: 'Devp',
  Benchmarking: 'BM'
};

function initCoeAnalytics() {
  const stageCanvas = document.getElementById('coe-by-stage-chart');
  const subcategoryCanvas = document.getElementById('coe-subcategories-by-lifecycle-chart');

  renderSeriesChart(stageCanvas, (series) => {
    if (!window.Chart) {
      return;
    }

    const stageAxisPoints = series.map((point) => ({
      name: point.stageName || '',
      stageCode: point.stageKey || ''
    }));

    const axisLabels = stageAxisPoints.map((point) => {
      const label = getStageAxisLabel(point);
      return coeStageLabelOverrides[label] ?? label;
    });

    createBarChart(stageCanvas, {
      labels: axisLabels,
      values: series.map((point) => ensureNumber(point.projectCount ?? point.value)),
      label: 'Projects',
      backgroundColor: getAccentColor(3),
      options: {
        plugins: {
          tooltip: {
            callbacks: {
              title: createStageTooltipTitle(stageAxisPoints)
            }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            ticks: {
              precision: 0,
              stepSize: 1
            }
          }
        }
      }
    });
  });

  renderSeriesChart(subcategoryCanvas, (series) => {
    if (!window.Chart) {
      return;
    }

    const palette = getPalette();
    const labels = series.map((point) => point.name ?? '');
    const lifecycleKeyMap = {
      Ongoing: 'ongoing',
      Completed: 'completed',
      Cancelled: 'cancelled'
    };

    const legendEntries = lifecycleStatuses.map((status) => {
      const key = lifecycleKeyMap[status];
      const total = series.reduce((sum, row) => sum + ensureNumber(row[key]), 0);
      return {
        status,
        key,
        total,
        color: getLifecycleColor(status),
        isActive: total > 0
      };
    });

    const datasets = legendEntries
      .filter((entry) => entry.isActive)
      .map((entry) => ({
        label: entry.status,
        data: series.map((row) => ensureNumber(row[entry.key])),
        backgroundColor: entry.color,
        stack: 'lifecycle',
        borderRadius: 4,
        maxBarThickness: 48
      }));

    const datasetIndexLookup = new Map();
    datasets.forEach((dataset, index) => datasetIndexLookup.set(dataset.label, index));
    legendEntries.forEach((entry) => {
      entry.datasetIndex = datasetIndexLookup.get(entry.status) ?? 0;
    });

    const rotation = labels.length > 6 ? 30 : 0;

    renderWithTheme(subcategoryCanvas, {
      type: 'bar',
      data: {
        labels,
        datasets
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: {
            stacked: true,
            ticks: {
              maxRotation: rotation,
              minRotation: rotation,
              color: palette.axisColor
            },
            grid: {
              display: false
            }
          },
          y: {
            stacked: true,
            beginAtZero: true,
            ticks: {
              precision: 0,
              stepSize: 1,
              color: palette.axisColor
            },
            grid: {
              color: palette.gridColor
            }
          }
        },
        plugins: {
          legend: {
            position: 'bottom',
            labels: {
              usePointStyle: true,
              color: palette.axisColor,
              generateLabels() {
                return legendEntries.map((entry) => ({
                  text: `${entry.status} · ${entry.total}`,
                  fillStyle: entry.isActive ? entry.color : palette.gridColor,
                  strokeStyle: entry.isActive ? entry.color : palette.gridColor,
                  hidden: false,
                  datasetIndex: entry.datasetIndex,
                  lineWidth: entry.isActive ? 1 : 0
                }));
              }
            },
            onClick(event, legendItem, legend) {
              const entry = legendEntries[legendItem.index];
              if (!entry?.isActive) {
                return;
              }

              const defaultClick = getDefaultLegendClick();
              defaultClick.call(this, event, legendItem, legend);
            }
          },
          tooltip: {
            callbacks: {
              title(items) {
                const item = items?.[0];
                if (!item) {
                  return '';
                }

                const row = series[item.dataIndex] ?? {};
                return row.name ?? '';
              },
              label(context) {
                const status = context.dataset?.label ?? '';
                const row = series[context.dataIndex] ?? {};
                const key = lifecycleKeyMap[status];
                const value = ensureNumber(context.raw ?? row[key]);
                const total = ensureNumber(row.total);
                if (!total) {
                  return `${status}: ${value} projects`;
                }
                const percentage = Math.round((value / total) * 100);
                return `${status}: ${value} of ${total} projects (${percentage}%)`;
              },
              footer(items) {
                const item = items?.[0];
                if (!item) {
                  return '';
                }

                const row = series[item.dataIndex] ?? {};
                const total = ensureNumber(row.total);
                return total ? `Total: ${total} projects` : '';
              }
            }
          }
        }
      }
    });
  });
}
// END SECTION

// SECTION: Analytics form helpers
function enableAutoSubmitFilters() {
  const elements = document.querySelectorAll('[data-auto-submit="change"]');
  elements.forEach((element) => {
    element.addEventListener('change', () => {
      const form = element.closest('form');
      if (form?.requestSubmit) {
        form.requestSubmit();
        return;
      }

      if (form) {
        form.submit();
      }
    });
  });
}
// END SECTION

// SECTION: Project management insights initialiser
function initStageTimeInsights() {
  enableAutoSubmitFilters();
  initStageCycleTimeByCostChart();
  initStageHotspotsChart();
}

function initStageCycleTimeByCostChart() {
  const canvas = document.getElementById('stage-time-by-cost-chart');
  if (!canvas || !window.Chart) {
    return;
  }

  const series = parseSeries(canvas);
  if (!series.length) {
    renderEmptyState(canvas);
    return;
  }

  const orderedSeries = [...series].sort((a, b) => {
    const orderCompare = (a.stageOrder ?? 0) - (b.stageOrder ?? 0);
    if (orderCompare !== 0) {
      return orderCompare;
    }

    const nameA = a.stageName || '';
    const nameB = b.stageName || '';
    return nameA.localeCompare(nameB);
  });

  const stages = Array.from(
    new Map(
      orderedSeries.map((row) => {
        const key = row.stageKey || '';
        return [key, {
          key,
          name: row.stageName || key,
          order: row.stageOrder ?? 0
        }];
      })
    ).values()
  );

  // SECTION: Stage axis label formatting
  const stageAxisPoints = stages.map((stage) => ({
    name: stage.name,
    stageCode: stage.key
  }));
    const axisLabels = stageAxisPoints.map((point) => getStageAxisLabel(point));
    const wrappedLabels = axisLabels.map((label) => wrapLabel(label, MAX_STAGE_AXIS_LABEL_LENGTH));
    // END SECTION

    const palette = getPalette();

  function buildDataset(bucketKey, label, color) {
    return {
      label,
      bucketKey,
      data: stages.map((stage) => {
        const match = series.find((row) => row.stageKey === stage.key && row.bucket === bucketKey);
        return match ? Number(match.medianDays) || 0 : 0;
      }),
      backgroundColor: color,
      borderRadius: 4,
      maxBarThickness: 40
    };
  }

  const datasets = [
    buildDataset(stageTimeBucketKeys.below, 'Latest cost below 1 crore', getAccentColor(0)),
    buildDataset(stageTimeBucketKeys.above, 'Latest cost at or above 1 crore', getAccentColor(2))
  ];

    renderWithTheme(canvas, {
      type: 'bar',
      data: {
        labels: wrappedLabels,
        datasets
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: {
            stacked: false,
            // SECTION: Ensure all stage labels display on x-axis
            ticks: {
              maxRotation: 0,
              autoSkip: false,
              color: palette.axisColor
            }
            // END SECTION
          },
          y: {
            beginAtZero: true,
            title: { display: true, text: 'Median days in stage', color: palette.axisColor },
            ticks: { color: palette.axisColor },
            grid: { color: palette.gridColor }
          }
        },
        plugins: {
          legend: {
            position: 'bottom',
            labels: { color: palette.axisColor }
          },
        tooltip: {
          callbacks: {
            title(items) {
              const item = items?.[0];
              if (!item) {
                return '';
              }

              const stage = stages[item.dataIndex];
              return stage?.name || '';
            },
            label(context) {
              const stage = stages[context.dataIndex];
              const bucketKey = context.dataset?.bucketKey;
              if (!stage || !bucketKey) {
                return `${context.dataset?.label || 'Cost bucket'}: 0 days`;
              }

              const row = series.find(
                (entry) => entry.stageKey === stage.key && entry.bucket === bucketKey
              );
              if (!row) {
                return `${context.dataset?.label || 'Cost bucket'}: 0 days`;
              }

              const median = Number(row.medianDays ?? 0).toFixed(1);
              const average = Number(row.averageDays ?? 0).toFixed(1);
              const count = row.projectCount ?? 0;
              return `${context.dataset?.label}: median ${median} days, average ${average} days, ${count} projects`;
            }
          }
        }
      }
    }
  });
}

function initStageHotspotsChart() {
  const canvas = document.getElementById('stage-hotspots-chart');
  if (!canvas || !window.Chart) {
    return;
  }

  renderSeriesChart(canvas, (series) => {
    const orderedSeries = [...series].sort((a, b) => {
      const medianDelta = (b.medianDays ?? 0) - (a.medianDays ?? 0);
      if (medianDelta !== 0) {
        return medianDelta;
      }

      const orderCompare = (a.stageOrder ?? 0) - (b.stageOrder ?? 0);
      if (orderCompare !== 0) {
        return orderCompare;
      }

      const nameA = a.stageName || '';
      const nameB = b.stageName || '';
      return nameA.localeCompare(nameB);
    });

    const stageAxisPoints = orderedSeries.map((point) => ({
      name: point.stageName || point.stageKey || '',
      stageCode: point.stageKey || ''
    }));
      const labels = stageAxisPoints
        .map((point) => getStageAxisLabel(point))
        .map((label) => wrapLabel(label, MAX_STAGE_AXIS_LABEL_LENGTH));

      const medianValues = orderedSeries.map((point) => ensureNumber(point.medianDays));
      const averageValues = orderedSeries.map((point) => ensureNumber(point.averageDays));
      const projectCounts = orderedSeries.map((point) => Math.max(0, Math.round(point.projectCount ?? 0)));

      const palette = getPalette();
      const emphasisColors = medianValues.map((_, index) => (index < 3 ? getAccentColor(0) : getAccentColor(1)));
      const emphasisBorders = medianValues.map((_, index) => (index < 3 ? getAccentColor(0) : getAccentColor(1)));

      renderWithTheme(canvas, {
        type: 'bar',
        data: {
          labels,
          datasets: [
            {
            label: 'Median days to finish stage',
            data: medianValues,
            backgroundColor: emphasisColors,
            borderColor: emphasisBorders,
            borderWidth: 1,
            borderRadius: 6,
            barThickness: 24
          }
        ]
      },
      options: {
          indexAxis: 'y',
          responsive: true,
          maintainAspectRatio: false,
          scales: {
            x: {
              beginAtZero: true,
              title: { display: true, text: 'Median days in stage', color: palette.axisColor },
              ticks: {
                precision: 0,
                color: palette.axisColor
              },
              grid: { color: palette.gridColor }
            },
            y: {
              grid: { display: false },
              ticks: {
                autoSkip: false,
                color: palette.axisColor
              }
            }
          },
          plugins: {
            legend: { display: false },
          tooltip: {
            callbacks: {
              title: createStageTooltipTitle(stageAxisPoints),
              label(context) {
                const index = context.dataIndex ?? 0;
                const median = medianValues[index];
                const average = averageValues[index];
                const count = projectCounts[index];

                const lines = [];
                if (Number.isFinite(median)) {
                  lines.push(`Median: ${median.toFixed(1)} days`);
                }

                if (Number.isFinite(average)) {
                  lines.push(`Average: ${average.toFixed(1)} days`);
                }

                if (Number.isFinite(count)) {
                  const sampleLabel = count < 3 ? ' (low sample size)' : '';
                  lines.push(`Projects counted: ${count}${sampleLabel}`);
                }

                return lines;
              }
            }
          }
        }
      }
    });
  });
}
// END SECTION

// SECTION: Analytics bootstrap
function hydrateAnalytics() {
  const page = document.querySelector('.analytics-page');
  if (!page) {
    return;
  }

  if (document.querySelector('.analytics-panel--completed')) {
    initCompletedAnalytics();
  } else if (document.querySelector('.analytics-panel--ongoing')) {
    initOngoingAnalytics();
  } else if (document.querySelector('.analytics-panel--coe')) {
    initCoeAnalytics();
  } else if (document.querySelector('.analytics-panel--insights')) {
    initStageTimeInsights();
  }
}

function initAnalyticsBootstrap() {
  hydrateAnalytics();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initAnalyticsBootstrap, { once: true });
} else {
  initAnalyticsBootstrap();
}
// END SECTION
