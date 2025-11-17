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
  const root = document.getElementById('analytics-root');
  if (!root || !window.Chart) {
    return;
  }

  const defaultLifecycle = root.dataset.defaultLifecycle || 'Active';
  const projectsIndexUrl = root.dataset.projectsIndexUrl || '/Projects/Index';
  const charts = new Map();
  const requestControllers = new WeakMap();

  function setDownloadReady(card, ready) {
    card.querySelectorAll('button[data-download-chart]').forEach((button) => {
      button.disabled = !ready;
      if (ready) {
        button.removeAttribute('aria-disabled');
      } else {
        button.setAttribute('aria-disabled', 'true');
      }
    });
  }

  function triggerDownload(card, canvas) {
    if (!canvas) return;
    const chart = charts.get(canvas);
    let url = '';
    if (chart && typeof chart.toBase64Image === 'function') {
      url = chart.toBase64Image('image/png', 1);
    }
    if (!url && canvas.toDataURL) {
      url = canvas.toDataURL('image/png', 1);
    }
    if (!url) return;

    const slug = card.dataset.analyticsCard || 'chart';
    const date = new Date().toISOString().slice(0, 10);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${slug}-${date}.png`;
    document.body.appendChild(link);
    link.click();
    link.remove();
  }

  function buildUrl(base, params) {
    const url = new URL(base, window.location.origin);
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, value);
      }
    });
    return url.toString();
  }

  function navigateToProjects(params) {
    if (params && params.CategoryId !== undefined && params.CategoryId !== null && params.CategoryId !== '') {
      params.IncludeCategoryDescendants = 'true';
    }
    window.location.href = buildUrl(projectsIndexUrl, params);
  }

  function activateLifecycleButton(button, activeValue) {
    const group = button.closest('[data-filter-strip]');
    if (!group) return;
    group.querySelectorAll('button[data-filter="lifecycle"]').forEach((btn) => {
      const match = btn.dataset.value === activeValue;
      btn.classList.toggle('active', match);
      btn.setAttribute('aria-pressed', match ? 'true' : 'false');
    });
  }

  function getFilters(card) {
    const filters = {};
    const lifecycleBtn = card.querySelector('button[data-filter="lifecycle"].active');
    filters.lifecycle = lifecycleBtn ? lifecycleBtn.dataset.value : defaultLifecycle;

    const categorySelect = card.querySelector('select[data-filter="category"]');
    filters.categoryId = categorySelect ? categorySelect.value : '';

    const technicalCategorySelect = card.querySelector('select[data-filter="technical-category"]');
    filters.technicalCategoryId = technicalCategorySelect ? technicalCategorySelect.value : '';

    return filters;
  }

  async function fetchJson(url, { signal } = {}) {
    const response = await fetch(url, {
      headers: { 'Accept': 'application/json' },
      signal
    });
    if (!response.ok) {
      throw new Error(`Request failed with ${response.status}`);
    }
    return response.json();
  }

  function showLoading(card) {
    card.classList.add('is-loading');
  }

  function hideLoading(card) {
    card.classList.remove('is-loading');
  }

  function beginCardRequest(card) {
    const previous = requestControllers.get(card);
    if (previous) {
      previous.abort();
    }
    const controller = new AbortController();
    requestControllers.set(card, controller);
    showLoading(card);
    setDownloadReady(card, false);
    return controller;
  }

  function endCardRequest(card, controller) {
    const active = requestControllers.get(card);
    if (active === controller) {
      hideLoading(card);
      requestControllers.delete(card);
    }
  }

  function isActiveRequest(card, controller) {
    return requestControllers.get(card) === controller;
  }

  function isAbortError(error) {
    return error && typeof error === 'object' && error.name === 'AbortError';
  }

  function getChart(card, canvas) {
    if (!canvas) return null;
    if (charts.has(canvas)) {
      return charts.get(canvas);
    }
    return null;
  }

  function setChart(card, canvas, chart) {
    charts.set(canvas, chart);
    card.dataset.hasChart = 'true';
    setDownloadReady(card, true);
  }

  async function loadCategoryShare(card) {
    const canvas = card.querySelector('canvas[data-chart="category-share"]');
    if (!canvas) return;
    const existingChart = getChart(card, canvas);
    const hadChart = Boolean(existingChart);
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/category-share', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId,
        technicalCategoryId: filters.technicalCategoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.slices.map((slice) => slice.categoryName);
      const values = data.slices.map((slice) => slice.count);
      const meta = data.slices;
      let chart = existingChart;
      const dataset = {
        label: 'Projects',
        data: values,
        backgroundColor: values.map((_, idx) => palette[idx % palette.length]),
        borderWidth: 0,
        meta
      };
      if (chart) {
        chart.data.labels = labels;
        chart.data.datasets = [dataset];
        chart.update();
        setDownloadReady(card, true);
      } else {
        const newChart = new window.Chart(canvas.getContext('2d'), {
          type: 'doughnut',
          data: {
            labels,
            datasets: [dataset]
          },
          options: {
            responsive: true,
            plugins: {
              legend: { position: 'bottom' }
            }
          }
        });
        canvas.onclick = (evt) => {
          const activeFilters = getFilters(card);
          const points = newChart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
          if (points.length === 0) {
            return;
          }
          const point = points[0];
          const slice = newChart.data.datasets[point.datasetIndex].meta[point.index];
          const params = {
            Lifecycle: activeFilters.lifecycle === 'All' ? undefined : activeFilters.lifecycle,
            CategoryId: slice.categoryId || undefined,
            TechnicalCategoryId: activeFilters.technicalCategoryId || undefined
          };
          navigateToProjects(params);
        };
        setChart(card, canvas, newChart);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
      if (hadChart) {
        setDownloadReady(card, true);
      }
    } finally {
      endCardRequest(card, controller);
    }
  }

  async function loadStageDistribution(card) {
    const canvas = card.querySelector('canvas[data-chart="stage-distribution"]');
    if (!canvas) return;
    const existingChart = getChart(card, canvas);
    const hadChart = Boolean(existingChart);
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/stage-distribution', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId,
        technicalCategoryId: filters.technicalCategoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.items.map((item) => item.stageName);
      const values = data.items.map((item) => item.count);
      const meta = data.items;
      let chart = existingChart;
      const dataset = {
        label: 'Projects',
        backgroundColor: '#1a73e8',
        borderRadius: 4,
        data: values,
        meta
      };
      if (chart) {
        chart.data.labels = labels;
        chart.data.datasets = [dataset];
        chart.update();
        setDownloadReady(card, true);
      } else {
        const newChart = new window.Chart(canvas.getContext('2d'), {
          type: 'bar',
          data: {
            labels,
            datasets: [dataset]
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
        canvas.onclick = (evt) => {
          const activeFilters = getFilters(card);
          const points = newChart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
          if (points.length === 0) {
            return;
          }
          const point = points[0];
          const stage = newChart.data.datasets[point.datasetIndex].meta[point.index];
          const params = {
            Lifecycle: activeFilters.lifecycle === 'All' ? undefined : activeFilters.lifecycle,
            CategoryId: activeFilters.categoryId || undefined,
            TechnicalCategoryId: activeFilters.technicalCategoryId || undefined,
            StageCode: stage.stageCode
          };
          navigateToProjects(params);
        };
        setChart(card, canvas, newChart);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
      if (hadChart) {
        setDownloadReady(card, true);
      }
    } finally {
      endCardRequest(card, controller);
    }
  }

  function handleFilterInteractions(card, loader) {
    card.querySelectorAll('button[data-filter="lifecycle"]').forEach((button) => {
      button.addEventListener('click', () => {
        activateLifecycleButton(button, button.dataset.value || defaultLifecycle);
        loader(card);
      });
    });

    card.querySelectorAll('select[data-filter], input[data-filter]').forEach((input) => {
      input.addEventListener('change', () => loader(card));
    });
  }

  root.addEventListener('click', (event) => {
    const button = event.target.closest('button[data-download-chart]');
    if (!button || button.disabled) {
      return;
    }
    const card = button.closest('[data-analytics-card]');
    if (!card) {
      return;
    }
    const target = button.dataset.downloadChart;
    const selector = target ? `canvas[data-chart="${target}"]` : 'canvas';
    const canvas = card.querySelector(selector);
    if (!canvas) {
      return;
    }
    event.preventDefault();
    triggerDownload(card, canvas);
  });

  function init() {
    const cards = root.querySelectorAll('[data-analytics-card]');
    cards.forEach((card) => {
      const type = card.dataset.analyticsCard;
      switch (type) {
        case 'category-share':
          handleFilterInteractions(card, loadCategoryShare);
          loadCategoryShare(card);
          break;
        case 'stage-distribution':
          handleFilterInteractions(card, loadStageDistribution);
          loadStageDistribution(card);
          break;
        default:
          break;
      }
    });
  }

  init();
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
