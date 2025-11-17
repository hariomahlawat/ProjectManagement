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

// SECTION: Chart helpers
function createDoughnutChart(canvas, { labels, values }) {
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
          backgroundColor: values.map((_, idx) => palette[idx % palette.length]),
          borderWidth: 0
        }
      ]
    },
    options: {
      responsive: true,
      plugins: {
        legend: { position: 'bottom' }
      }
    }
  });
}

function createBarChart(
  canvas,
  { labels, values, label = 'Projects', backgroundColor = '#1a73e8' }
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
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false }
      },
      scales: {
        x: { ticks: { maxRotation: 0 } },
        y: { beginAtZero: true }
      }
    }
  });
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
  const stageCanvas = document.getElementById('coe-projects-by-stage-chart');
  const lifecycleCanvas = document.getElementById('coe-lifecycle-status-chart');

  const coeStageData = {
    labels: ['Discovery', 'Planning', 'Execution', 'Adoption'],
    values: [3, 4, 6, 2]
  };

  const coeLifecycleData = {
    labels: ['Ongoing', 'Completed', 'Cancelled'],
    values: [10, 5, 1]
  };

  if (stageCanvas) {
    createBarChart(stageCanvas, {
      ...coeStageData,
      label: 'Projects',
      backgroundColor: '#5c6bc0'
    });
  }

  if (lifecycleCanvas) {
    createDoughnutChart(lifecycleCanvas, coeLifecycleData);
  }
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
