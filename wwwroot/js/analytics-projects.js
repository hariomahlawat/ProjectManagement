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

  const monthInputDefaults = (() => {
    const now = new Date();
    now.setDate(1);
    const end = new Date(now);
    const start = new Date(now);
    start.setMonth(start.getMonth() - 5);
    return { start, end };
  })();

  function formatMonthValue(date) {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    return `${year}-${month}`;
  }

  function setInitialMonthInputs(card) {
    const fromInput = card.querySelector('input[data-filter="from-month"]');
    const toInput = card.querySelector('input[data-filter="to-month"]');
    if (fromInput && !fromInput.value) {
      fromInput.value = formatMonthValue(monthInputDefaults.start);
    }
    if (toInput && !toInput.value) {
      toInput.value = formatMonthValue(monthInputDefaults.end);
    }
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

    const fromInput = card.querySelector('input[data-filter="from-month"]');
    if (fromInput) {
      filters.fromMonth = fromInput.value;
    }
    const toInput = card.querySelector('input[data-filter="to-month"]');
    if (toInput) {
      filters.toMonth = toInput.value;
    }
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
  }

  async function loadCategoryShare(card) {
    const canvas = card.querySelector('canvas[data-chart="category-share"]');
    if (!canvas) return;
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/category-share', {
        lifecycle: filters.lifecycle
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.slices.map((slice) => slice.categoryName);
      const values = data.slices.map((slice) => slice.count);
      const meta = data.slices;
      const chart = getChart(card, canvas);
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
            CategoryId: slice.categoryId || undefined
          };
          navigateToProjects(params);
        };
        setChart(card, canvas, newChart);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
    } finally {
      endCardRequest(card, controller);
    }
  }

  async function loadStageDistribution(card) {
    const canvas = card.querySelector('canvas[data-chart="stage-distribution"]');
    if (!canvas) return;
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/stage-distribution', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.items.map((item) => item.stageName);
      const values = data.items.map((item) => item.count);
      const meta = data.items;
      const chart = getChart(card, canvas);
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
    } finally {
      endCardRequest(card, controller);
    }
  }

  async function loadLifecycleStatus(card) {
    const canvas = card.querySelector('canvas[data-chart="lifecycle-status"]');
    if (!canvas) return;
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/lifecycle-breakdown', {
        categoryId: filters.categoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.items.map((item) => item.status);
      const values = data.items.map((item) => item.count);
      const colors = ['#1a73e8', '#34a853', '#ea4335'];
      const chart = getChart(card, canvas);
      const dataset = {
        label: 'Projects',
        data: values,
        backgroundColor: labels.map((_, idx) => colors[idx % colors.length]),
        borderRadius: 4
      };
      if (chart) {
        chart.data.labels = labels;
        chart.data.datasets = [dataset];
        chart.update();
      } else {
        const newChart = new window.Chart(canvas.getContext('2d'), {
          type: 'bar',
          data: {
            labels,
            datasets: [dataset]
          },
          options: {
            responsive: true,
            plugins: {
              legend: { display: false }
            },
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
          const status = newChart.data.labels[point.index];
          const params = {
            Lifecycle: status,
            CategoryId: activeFilters.categoryId || undefined
          };
          navigateToProjects(params);
        };
        setChart(card, canvas, newChart);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
    } finally {
      endCardRequest(card, controller);
    }
  }

  function renderStageCompletionKpis(card, kpis) {
    const container = card.querySelector('[data-kpis]');
    if (!container) return;
    const total = container.querySelector('[data-kpi="total"]');
    const adv = container.querySelector('[data-kpi="advancements"]');
    const top = container.querySelector('[data-kpi="top-stage"]');
    if (total) total.textContent = kpis.totalCompletionsThisMonth ?? '0';
    if (adv) adv.textContent = kpis.projectsAdvancedTwoOrMoreStages ?? '0';
    if (top) {
      if (kpis.topStageName) {
        top.textContent = `${kpis.topStageName} (${kpis.topStageCount})`;
      } else {
        top.textContent = '—';
      }
    }
  }

  async function loadStageCompletions(card) {
    const canvas = card.querySelector('canvas[data-chart="stage-completions"]');
    if (!canvas) return;
    setInitialMonthInputs(card);
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/monthly-stage-completions', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId,
        fromMonth: filters.fromMonth,
        toMonth: filters.toMonth
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      renderStageCompletionKpis(card, data.kpis);
      const labels = data.months.map((month) => month.label);
      const datasets = data.series.map((serie, idx) => ({
        label: serie.stageName,
        data: serie.counts,
        backgroundColor: palette[idx % palette.length],
        stageCode: serie.stageCode,
        stack: 'stages'
      }));
      const chart = getChart(card, canvas);
      if (chart) {
        chart.data.labels = labels;
        chart.data.datasets = datasets;
        chart.$months = data.months;
        chart.update();
      } else {
        const newChart = new window.Chart(canvas.getContext('2d'), {
          type: 'bar',
          data: {
            labels,
            datasets
          },
          options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
              tooltip: { mode: 'index', intersect: false },
              legend: { position: 'bottom' }
            },
            scales: {
              x: { stacked: true },
              y: { stacked: true, beginAtZero: true }
            }
          }
        });
        newChart.$months = data.months;
        canvas.onclick = (evt) => {
          const activeFilters = getFilters(card);
          const points = newChart.getElementsAtEventForMode(evt, 'nearest', { intersect: true }, true);
          if (points.length === 0) return;
          const point = points[0];
          const dataset = newChart.data.datasets[point.datasetIndex];
          const months = newChart.$months || [];
          const monthBucket = months[point.index];
          if (!monthBucket) {
            return;
          }
          const params = {
            Lifecycle: activeFilters.lifecycle === 'All' ? undefined : activeFilters.lifecycle,
            CategoryId: activeFilters.categoryId || undefined,
            StageCode: dataset.stageCode,
            StageCompletedMonth: monthBucket.key
          };
          navigateToProjects(params);
        };
        setChart(card, canvas, newChart);
      }
    } catch (err) {
      if (!isAbortError(err)) {
        console.error(err);
      }
    } finally {
      endCardRequest(card, controller);
    }
  }

  async function loadSlipBuckets(card) {
    const canvas = card.querySelector('canvas[data-chart="slip-buckets"]');
    if (!canvas) return;
    const controller = beginCardRequest(card);
    const filters = getFilters(card);
    try {
      const url = buildUrl('/api/analytics/projects/slip-buckets', {
        lifecycle: filters.lifecycle,
        categoryId: filters.categoryId
      });
      const data = await fetchJson(url, { signal: controller.signal });
      if (!isActiveRequest(card, controller)) {
        return;
      }
      const labels = data.buckets.map((bucket) => bucket.label);
      const values = data.buckets.map((bucket) => bucket.count);
      const chart = getChart(card, canvas);
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
        item.innerHTML = `
          <button type="button" class="analytics-top-link">
            <span class="analytics-top-name">${project.name}</span>
            <span class="analytics-top-meta">${project.category}</span>
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
        case 'lifecycle-status':
          handleFilterInteractions(card, loadLifecycleStatus);
          loadLifecycleStatus(card);
          break;
        case 'stage-completions':
          handleFilterInteractions(card, loadStageCompletions);
          loadStageCompletions(card);
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
