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

  return new window.Chart(canvas.getContext('2d'), {
    type: 'bar',
    data: {
      labels,
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
          x: { ticks: { maxRotation: 0 } },
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
      createBarChart(durationCanvas, {
        labels: series.map((point) => point.name),
        values: series.map((point) => point.days),
        label: 'Average days in stage',
        backgroundColor: '#34a853'
      });
    }
  }
}
// END SECTION

// SECTION: CoE analytics initialiser
function initCoeAnalytics() {
  const stageCanvas = document.getElementById('coe-by-stage-chart');
  const lifecycleCanvas = document.getElementById('coe-by-lifecycle-chart');
  const subcategoryCanvas = document.getElementById('coe-subcategories-chart');

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

  renderSeriesChart(lifecycleCanvas, (series) => {
    const buckets = lifecycleStatuses.map((status) => {
      const match = series.find((point) => {
        const key = point.lifecycleStatus ?? point.status ?? point.label;
        return key === status;
      });
      const count = ensureNumber(match?.projectCount ?? match?.value ?? match?.count);
      return {
        status,
        count,
        color: lifecycleColorMap[status] ?? palette[0]
      };
    });

    const activeBuckets = buckets.filter((bucket) => bucket.count > 0);
    const dataset = activeBuckets.length ? activeBuckets : buckets.slice(0, 1);
    const datasetIndexLookup = new Map();
    dataset.forEach((bucket, index) => datasetIndexLookup.set(bucket.status, index));

    const legendEntries = buckets.map((bucket) => ({
      status: bucket.status,
      count: bucket.count,
      color: bucket.count > 0 ? bucket.color : '#cbd5f5',
      isActive: bucket.count > 0,
      datasetIndex: datasetIndexLookup.get(bucket.status) ?? 0
    }));

    createDoughnutChart(lifecycleCanvas, {
      labels: dataset.map((bucket) => bucket.status),
      values: dataset.map((bucket) => bucket.count),
      colors: dataset.map((bucket) => bucket.color),
      options: {
        plugins: {
          legend: {
            position: 'bottom',
            labels: {
              usePointStyle: true,
              generateLabels() {
                return legendEntries.map((entry) => ({
                  text: `${entry.status} · ${entry.count}`,
                  fillStyle: entry.color,
                  strokeStyle: entry.color,
                  hidden: false,
                  lineWidth: entry.isActive ? 1 : 0,
                  datasetIndex: entry.datasetIndex
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
          }
        }
      }
    });
  });

  renderSeriesChart(subcategoryCanvas, (series) => {
    if (!window.Chart) {
      return;
    }

    const labels = series.map((point) => point.shortLabel ?? point.name ?? '');
    const lifecycleKeyMap = {
      Ongoing: 'ongoingCount',
      Completed: 'completedCount',
      Cancelled: 'cancelledCount'
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

    const rotation = labels.length > 6 ? -30 : 0;

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
                const total = ensureNumber(row.totalCount);
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
                const total = ensureNumber(row.totalCount);
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
  }
});
// END SECTION
