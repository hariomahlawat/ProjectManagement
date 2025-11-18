const palette = [
  '#1a73e8',
  '#fbbc04',
  '#34a853',
  '#ea4335',
  '#9c27b0',
  '#fb8c00',
  '#00acc1',
  '#8d6e63',
  '#5c6bc0',
  '#43a047'
];

const lifecycleStatuses = ['Ongoing', 'Completed', 'Cancelled'];

const lifecycleColorMap = {
  Ongoing: '#2563eb',
  Completed: '#fbbf24',
  Cancelled: '#16a34a'
};

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

  return new window.Chart(canvas.getContext('2d'), {
    type: 'doughnut',
    data: {
      labels,
      datasets: [
        {
          data: values,
          backgroundColor: Array.isArray(colors) && colors.length
            ? colors
            : values.map((_, idx) => palette[idx % palette.length]),
          borderWidth: 0
        }
      ]
    },
    options: mergeChartOptions(
      {
        responsive: true,
        plugins: {
          legend: { position: 'bottom' }
        }
      },
      options
    )
  });
}

function createBarChart(
  canvas,
  { labels, values, label = 'Projects', backgroundColor = '#1a73e8', options }
) {
  if (!canvas || !window.Chart) {
    return null;
  }

  const rawLabels = Array.isArray(labels) ? labels : [];
  const wrappedLabels = rawLabels.map((l) => wrapLabel(l));

  return new window.Chart(canvas.getContext('2d'), {
    type: 'bar',
    data: {
      labels: wrappedLabels,
      datasets: [
        {
          label,
          data: values,
          backgroundColor,
          borderRadius: 4
        }
      ]
    },
    options: mergeChartOptions(
      {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false }
        },
        scales: {
          x: {
            ticks: {
              maxRotation: 0,
              autoSkip: false
            }
          },
          y: { beginAtZero: true }
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

  if (perYearEl && data.perYear?.length) {
    createBarChart(perYearEl, {
      labels: data.perYear.map((point) => point.year?.toString() ?? ''),
      values: data.perYear.map((point) => point.count),
      label: 'Projects completed',
      backgroundColor: '#34a853'
    });
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
    const series = parseSeries(stageCanvas);
    if (series.length) {
      createBarChart(stageCanvas, {
        labels: series.map((point) => point.name),
        values: series.map((point) => point.count),
        label: 'Projects'
      });
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
        backgroundColor: '#34a853',
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
function initCoeAnalytics() {
  const stageCanvas = document.getElementById('coe-by-stage-chart');
  const subcategoryCanvas = document.getElementById('coe-subcategories-by-lifecycle-chart');

  renderSeriesChart(stageCanvas, (series) => {
    createBarChart(stageCanvas, {
      labels: series.map((point) => point.stageName ?? point.label ?? ''),
      values: series.map((point) => ensureNumber(point.projectCount ?? point.value)),
      label: 'Projects',
      backgroundColor: '#5c6bc0',
      options: {
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
        color: lifecycleColorMap[status] ?? palette[0],
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

    new window.Chart(subcategoryCanvas.getContext('2d'), {
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
              minRotation: rotation
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
              stepSize: 1
            }
          }
        },
        plugins: {
          legend: {
            position: 'bottom',
            labels: {
              usePointStyle: true,
              generateLabels() {
                return legendEntries.map((entry) => ({
                  text: `${entry.status} · ${entry.total}`,
                  fillStyle: entry.isActive ? entry.color : '#cbd5f5',
                  strokeStyle: entry.isActive ? entry.color : '#cbd5f5',
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
    buildDataset(stageTimeBucketKeys.below, 'Latest cost below 1 crore', '#1a73e8'),
    buildDataset(stageTimeBucketKeys.above, 'Latest cost at or above 1 crore', '#34a853')
  ];

  new window.Chart(canvas.getContext('2d'), {
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
            autoSkip: false
          }
          // END SECTION
        },
        y: {
          beginAtZero: true,
          title: { display: true, text: 'Median days in stage' }
        }
      },
      plugins: {
        legend: {
          position: 'bottom'
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
// END SECTION

// SECTION: Analytics bootstrap
document.addEventListener('DOMContentLoaded', () => {
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
});
// END SECTION
