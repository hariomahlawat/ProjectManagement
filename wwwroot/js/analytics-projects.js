// SECTION: Palette helpers
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

function getPalette() {
  const defaultPalette = {
    axisColor: '#4b5563',
    gridColor: '#e5e7eb',
    neutral: '#9ca3af',
    accents: paletteFallback
  };

  if (!window.PMTheme || typeof window.PMTheme.getChartPalette !== 'function') {
    return defaultPalette;
  }

  const themePalette = window.PMTheme.getChartPalette();
  if (!themePalette || !Array.isArray(themePalette.accents)) {
    return defaultPalette;
  }

  const themeAccents = themePalette.accents
    .map((value) => (value ?? '').trim())
    .filter(Boolean);

  const combinedAccents = [...themeAccents];
  paletteFallback.forEach((fallback) => {
    if (!combinedAccents.includes(fallback)) {
      combinedAccents.push(fallback);
    }
  });

  return {
    axisColor: (themePalette.axisColor ?? '').trim() || defaultPalette.axisColor,
    gridColor: (themePalette.gridColor ?? '').trim() || defaultPalette.gridColor,
    neutral: (themePalette.neutral ?? '').trim() || defaultPalette.neutral,
    accents: combinedAccents.length ? combinedAccents : defaultPalette.accents
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
// END SECTION

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

  const existing = typeof window.Chart.getChart === 'function'
    ? window.Chart.getChart(canvas)
    : null;

  if (existing) {
    existing.destroy();
  }

  return new window.Chart(canvas.getContext('2d'), config);
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

// SECTION: Category color mapping
function buildCategoryColorMapping({ donutPoints, stackedPoints }) {
  const totals = new Map();
  const hasDonutPoints = Array.isArray(donutPoints) && donutPoints.length;
  const hasStackedPoints = Array.isArray(stackedPoints) && stackedPoints.length;

  if (hasDonutPoints) {
    donutPoints.forEach((point) => {
      const key = point?.name ?? 'Unassigned';
      totals.set(key, ensureNumber(point?.count));
    });
  } else if (hasStackedPoints) {
    stackedPoints.forEach((point) => {
      const key = point?.categoryName ?? 'Unassigned';
      totals.set(key, (totals.get(key) ?? 0) + ensureNumber(point?.count));
    });
  }

  const unassignedLabels = ['Unassigned', 'Uncategorized'];
  const unassignedLabel = Array.from(totals.keys()).find((label) => {
    const normalizedLabel = String(label ?? '').trim().toLowerCase();
    return unassignedLabels.some(
      (candidate) => candidate.toLowerCase() === normalizedLabel
    );
  });
  const unassignedTotal = unassignedLabel ? totals.get(unassignedLabel) ?? 0 : 0;

  if (unassignedLabel) {
    totals.delete(unassignedLabel);
  }

  const rankedCategories = Array.from(totals.entries())
    .sort((first, second) => {
      const totalDifference = second[1] - first[1];
      return totalDifference !== 0 ? totalDifference : first[0].localeCompare(second[0]);
    })
    .map(([name]) => name);

  const palette = getPalette();
  const accentColors = palette.accents && palette.accents.length ? palette.accents : paletteFallback;
  const categoryColorMap = new Map();

  rankedCategories.forEach((name, index) => {
    categoryColorMap.set(name, accentColors[index % accentColors.length]);
  });

  if (unassignedLabel && unassignedTotal >= 0) {
    categoryColorMap.set(unassignedLabel, palette.neutral ?? '#9ca3af');
    rankedCategories.push(unassignedLabel);
  }

  return {
    categoryColorMap,
    rankedCategories
  };
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

// SECTION: Ongoing analytics chart builders
function createOngoingStageStackedChart(
  canvas,
  points,
  categoryColorMap = new Map(),
  categoryOrder = []
) {
  if (!canvas || !window.Chart || !Array.isArray(points) || !points.length) {
    return null;
  }

  const context = canvas.getContext('2d');
  if (!context) {
    return null;
  }

  const stageCodes = [];
  points.forEach((point) => {
    if (point?.stageCode && !stageCodes.includes(point.stageCode)) {
      stageCodes.push(point.stageCode);
    }
  });

  const stageAxisPoints = stageCodes.map((code) => {
    const match = points.find((point) => point.stageCode === code);
    return {
      name: match?.stageName ?? match?.stageCode ?? '',
      stageCode: code
    };
  });

  const axisLabels = stageAxisPoints.map((point) => getStageAxisLabel(point));

  const detectedCategories = Array.from(
    new Set(points.map((point) => point.categoryName ?? 'Unassigned'))
  );
  let orderedCategories = detectedCategories.sort((first, second) =>
    first.localeCompare(second)
  );

  if (Array.isArray(categoryOrder) && categoryOrder.length) {
    const orderedSet = categoryOrder.filter((category) => detectedCategories.includes(category));
    const remainingCategories = detectedCategories.filter(
      (category) => !orderedSet.includes(category)
    );
    orderedCategories = [...orderedSet, ...remainingCategories];
  }

  const palette = getPalette();
  const accentColors = palette.accents && palette.accents.length ? palette.accents : paletteFallback;

  const datasets = orderedCategories.map((category, index) => ({
    label: category,
    data: stageCodes.map((stageCode) => {
      const match = points.find(
        (point) => point.stageCode === stageCode && point.categoryName === category
      );
      return ensureNumber(match?.count);
    }),
    backgroundColor: categoryColorMap.get(category) ?? accentColors[index % accentColors.length],
    borderWidth: 1
  }));

  return renderWithTheme(canvas, {
    type: 'bar',
    data: {
      labels: axisLabels,
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
            title: createStageTooltipTitle(stageAxisPoints),
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
          ticks: {
            autoSkip: false,
            maxRotation: 0,
            minRotation: 0
          }
        },
        y: {
          stacked: true,
          beginAtZero: true,
          ticks: {
            precision: 0
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

// SECTION: Ongoing analytics initialiser
function initOngoingAnalytics() {
  const categoryCanvas = document.getElementById('ongoing-by-category-chart');
  const stageCanvas = document.getElementById('ongoing-by-stage-chart');
  const durationCanvas = document.getElementById('ongoing-stage-duration-chart');

  const categorySeries = categoryCanvas ? parseSeries(categoryCanvas) : [];
  const stageSeries = stageCanvas ? parseSeries(stageCanvas) : [];
  const { categoryColorMap, rankedCategories } = buildCategoryColorMapping({
    donutPoints: categorySeries,
    stackedPoints: stageSeries
  });

  if (categoryCanvas) {
    if (categorySeries.length) {
      const categoryLookup = new Map(
        categorySeries.map((point) => [point.name ?? 'Unassigned', ensureNumber(point.count)])
      );
      const orderedLabels = rankedCategories.length
        ? rankedCategories.filter((label) => categoryLookup.has(label))
        : categorySeries.map((point) => point.name ?? 'Unassigned');
      const orderedValues = orderedLabels.map((label) => categoryLookup.get(label) ?? 0);
      const orderedColors = orderedLabels.map(
        (label, index) => categoryColorMap.get(label) ?? getAccentColor(index)
      );

      createDoughnutChart(categoryCanvas, {
        labels: orderedLabels,
        values: orderedValues,
        colors: orderedColors
      });
    }
  }

  if (stageCanvas) {
    if (stageSeries.length) {
      const looksStacked =
        Object.prototype.hasOwnProperty.call(stageSeries[0], 'categoryName') &&
        Object.prototype.hasOwnProperty.call(stageSeries[0], 'stageCode');

      if (looksStacked) {
        createOngoingStageStackedChart(
          stageCanvas,
          stageSeries,
          categoryColorMap,
          rankedCategories
        );
      } else {
        createBarChart(stageCanvas, {
          labels: stageSeries.map((point) => point.name),
          values: stageSeries.map((point) => point.count),
          label: 'Projects'
        });
      }
    }
  }

  if (durationCanvas) {
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

// SECTION: Analytics chart export helpers
function getCssVar(name, fallback) {
  const css = getComputedStyle(document.documentElement);
  const value = css.getPropertyValue(name);
  return value ? value.trim() : fallback;
}

// SECTION: Export profile constants
const EXPORT_PROFILE = {
  width: 1920,
  height: 1080,
  dpr: 2
};

const EXPORT_TYPOGRAPHY = {
  legend: 28,
  axisX: 26,
  axisY: 24,
  dataLabel: 40,
  totalLabel: 52,
  footerTitle: 32,
  footerText: 24,
  watermark: 18,
  title: 32,
  subtitle: 24
};

const EXPORT_PADDING = {
  top: 44,
  right: 40,
  bottom: 40,
  left: 40
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
      ctx.fillStyle = '#111827';
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
      ctx.fillStyle = '#4b5563';
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
      const labelFontFamily = '"Inter", "Segoe UI", Arial, sans-serif';
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
            ctx.fillStyle = stacked ? '#f9fafb' : '#111827';
            ctx.strokeStyle = stacked ? 'rgba(17, 24, 39, 0.35)' : 'transparent';
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
            ctx.fillStyle = '#111827';
            ctx.textBaseline = 'middle';

            if (isHorizontal && xScale) {
              const offset = Math.round(totalFontSize * 0.35);
              const x = xScale.getPixelForValue(total);
              const y = anchor.y ?? 0;
              ctx.textAlign = 'left';
              ctx.fillText(valueLabel, x + offset, y);
            } else if (yScale) {
              const offset = Math.round(totalFontSize * 0.35);
              const x = anchor.x ?? 0;
              const y = yScale.getPixelForValue(total);
              ctx.textAlign = 'center';
              ctx.fillText(valueLabel, x, y - offset);
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
          ctx.fillStyle = '#111827';
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
  // END SECTION

  // SECTION: Legend typography
  exportConfig.options.plugins.legend = exportConfig.options.plugins.legend || {};
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
  const backgroundColor = (opts.backgroundColor ?? getCssVar('--pm-card', '')) || '#ffffff';

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

  initAnalyticsChartDownloads();
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
