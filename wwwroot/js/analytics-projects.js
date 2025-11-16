const root = document.getElementById('analytics-root');

if (root) {
  const defaultLifecycle = root.dataset.defaultLifecycle || 'Active';
  const projectsIndexUrl = root.dataset.projectsIndexUrl || '/Projects/Index';
  const projectOverviewUrl = root.dataset.projectOverviewUrl || '/Projects/Overview';

  const palette = [
    '#1a73e8', '#fbbc04', '#34a853', '#ea4335', '#9c27b0', '#fb8c00', '#00acc1', '#8d6e63', '#5c6bc0', '#43a047'
  ];

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

  function navigateToOverview(id) {
    window.location.href = buildUrl(projectOverviewUrl, { id });
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

  async function loadSlipBuckets(card) {
    const canvas = card.querySelector('canvas[data-chart="slip-buckets"]');
    if (!canvas) return;
    const existingChart = getChart(card, canvas);
    const hadChart = Boolean(existingChart);
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/slip-buckets', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId,
        technicalCategoryId: filters.technicalCategoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.buckets.map((bucket) => bucket.label);
      const values = data.buckets.map((bucket) => bucket.count);
      let chart = existingChart;
      const dataset = {
        label: 'Projects',
        data: values,
        backgroundColor: '#1a73e8',
        borderRadius: 4,
        meta: data.buckets
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
            plugins: { legend: { display: false } },
            scales: {
              y: { beginAtZero: true }
            }
          }
        });
        canvas.onclick = (evt) => {
          const activeFilters = getFilters(card);
          const points = newChart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
          if (points.length === 0) return;
          const point = points[0];
          const bucket = newChart.data.datasets[point.datasetIndex].meta[point.index];
          const params = {
            Lifecycle: activeFilters.lifecycle === 'All' ? undefined : activeFilters.lifecycle,
            CategoryId: activeFilters.categoryId || undefined,
            TechnicalCategoryId: activeFilters.technicalCategoryId || undefined,
            SlipBucket: bucket.key
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

  async function loadTopOverdue(card) {
    const container = card.querySelector('[data-top-overdue]');
    if (!container) return;
    const controller = beginCardRequest(card);
    container.innerHTML = '<p class="text-secondary mb-0">Loading…</p>';
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/top-overdue', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId,
        technicalCategoryId: filters.technicalCategoryId,
        take: 5
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      if (!data.projects || data.projects.length === 0) {
        container.innerHTML = '<p class="text-secondary mb-0">No overdue projects for this view.</p>';
        return;
      }
      const list = document.createElement('ul');
      list.className = 'analytics-top-overdue-list';
      data.projects.forEach((project) => {
        const item = document.createElement('li');
        const metaParts = [project.category];
        if (project.technicalCategory) {
          metaParts.push(project.technicalCategory);
        }
        const meta = metaParts.join(' · ');
        item.innerHTML = `
          <button type="button" class="analytics-top-link">
            <span class="analytics-top-name">${project.name}</span>
            <span class="analytics-top-meta">${meta}</span>
            <span class="analytics-top-stage">${project.stageName}</span>
            <span class="analytics-top-slip">${project.slipDays}d</span>
          </button>
        `;
        const button = item.querySelector('button');
        button.addEventListener('click', () => navigateToOverview(project.projectId));
        list.appendChild(item);
      });
      container.innerHTML = '';
      container.appendChild(list);
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
      container.innerHTML = '<p class="text-danger mb-0">Unable to load data.</p>';
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
        case 'slip-buckets':
          handleFilterInteractions(card, loadSlipBuckets);
          loadSlipBuckets(card);
          break;
        case 'top-overdue':
          handleFilterInteractions(card, loadTopOverdue);
          loadTopOverdue(card);
          break;
        default:
          break;
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once: true });
  } else {
    init();
  }
}
