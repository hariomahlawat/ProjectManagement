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

// SECTION: Completed analytics initialiser
function initCompletedAnalytics() {
  const canvases = [
    document.getElementById('completedByCategoryChart'),
    document.getElementById('completedByTechnicalChart'),
    document.getElementById('completedPerYearChart')
  ].filter((canvas) => Boolean(canvas));

  if (canvases.length === 0) {
    return;
  }

  // SECTION: Placeholder references for future chart initialisation
  // Real data binding will be added in a follow-up iteration.
  canvases.forEach((canvas) => {
    canvas.dataset.analyticsChart = 'pending';
  });
  // END SECTION
}
// END SECTION

// SECTION: Ongoing analytics initialiser
function initOngoingAnalytics() {
  const categoryCanvas = document.getElementById('ongoing-projects-by-category-chart');
  const stageCanvas = document.getElementById('ongoing-projects-by-stage-chart');
  const durationCanvas = document.getElementById('ongoing-stage-duration-chart');
  if (!categoryCanvas || !stageCanvas || !durationCanvas || !window.Chart) {
    return;
  }

  const ongoingCategoryData = {
    labels: ['Innovation', 'Sustainment', 'Operations', 'Optimization'],
    values: [12, 9, 6, 4]
  };

  const ongoingStageData = {
    labels: ['Ideation', 'Planning', 'Execution', 'Stabilizing'],
    values: [5, 7, 12, 8]
  };

  const ongoingDurationData = {
    labels: ['Ideation', 'Planning', 'Execution', 'Stabilizing'],
    values: [14, 28, 64, 21]
  };

  new window.Chart(categoryCanvas.getContext('2d'), {
    type: 'doughnut',
    data: {
      labels: ongoingCategoryData.labels,
      datasets: [
        {
          data: ongoingCategoryData.values,
          backgroundColor: ongoingCategoryData.values.map((_, idx) => palette[idx % palette.length]),
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

  new window.Chart(stageCanvas.getContext('2d'), {
    type: 'bar',
    data: {
      labels: ongoingStageData.labels,
      datasets: [
        {
          label: 'Projects',
          data: ongoingStageData.values,
          backgroundColor: '#1a73e8',
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

  new window.Chart(durationCanvas.getContext('2d'), {
    type: 'bar',
    data: {
      labels: ongoingDurationData.labels,
      datasets: [
        {
          label: 'Average days in stage',
          data: ongoingDurationData.values,
          backgroundColor: '#34a853',
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

// SECTION: CoE analytics initialiser
function initCoeAnalytics() {
  const stageCanvas = document.getElementById('coe-projects-by-stage-chart');
  const lifecycleCanvas = document.getElementById('coe-lifecycle-status-chart');
  if (!stageCanvas || !lifecycleCanvas || !window.Chart) {
    return;
  }

  const coeStageData = {
    labels: ['Discovery', 'Planning', 'Execution', 'Adoption'],
    values: [3, 4, 6, 2]
  };

  const coeLifecycleData = {
    labels: ['Ongoing', 'Completed', 'Cancelled'],
    values: [10, 5, 1]
  };

  new window.Chart(stageCanvas.getContext('2d'), {
    type: 'bar',
    data: {
      labels: coeStageData.labels,
      datasets: [
        {
          label: 'Projects',
          data: coeStageData.values,
          backgroundColor: '#5c6bc0',
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

  new window.Chart(lifecycleCanvas.getContext('2d'), {
    type: 'doughnut',
    data: {
      labels: coeLifecycleData.labels,
      datasets: [
        {
          data: coeLifecycleData.values,
          backgroundColor: coeLifecycleData.values.map((_, idx) => palette[idx % palette.length]),
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
// END SECTION

// SECTION: Analytics bootstrap
document.addEventListener('DOMContentLoaded', () => {
  const page = document.querySelector('.analytics-page');
  if (!page) {
    return;
  }

  const tab = (page.dataset.analyticsTab || '').toLowerCase();

  switch (tab) {
    case 'ongoing':
      initOngoingAnalytics();
      break;
    case 'coe':
      initCoeAnalytics();
      break;
    default:
      initCompletedAnalytics();
      break;
  }
});
// END SECTION
