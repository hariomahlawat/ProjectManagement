// SECTION: Project pulse micro-charts
(() => {
  const ready = () => document.readyState === 'loading'
    ? new Promise(resolve => document.addEventListener('DOMContentLoaded', resolve, { once: true }))
    : Promise.resolve();

  const parsePoints = (element) => {
    const raw = element.getAttribute('data-points') ?? '';
    return raw
      .split(',')
      .map((token) => Number.parseInt(token.trim(), 10))
      .filter((value) => Number.isFinite(value));
  };

  const dataset = (type, points) => {
    if (type === 'bar') {
      return [{
        data: points,
        backgroundColor: 'rgba(45, 108, 223, 0.25)',
        borderRadius: 3,
        maxBarThickness: 10
      }];
    }

    if (type === 'area') {
      return [{
        data: points,
        type: 'line',
        borderColor: 'rgba(45, 108, 223, 1)',
        borderWidth: 1.5,
        fill: true,
        backgroundColor: 'rgba(45, 108, 223, 0.12)',
        tension: 0.35,
        pointRadius: 0
      }];
    }

    return [{
      data: points,
      type: 'line',
      borderColor: 'rgba(15, 23, 42, 0.9)',
      borderWidth: 1.5,
      tension: 0.35,
      pointRadius: 0
    }];
  };

  const sparkOptions = () => ({
    responsive: false,
    maintainAspectRatio: false,
    animation: false,
    elements: { point: { radius: 0 } },
    scales: {
      x: { display: false },
      y: { display: false, beginAtZero: true }
    },
    plugins: {
      legend: { display: false },
      tooltip: { enabled: false }
    }
  });

  const renderSpark = (canvas) => {
    const Chart = window.Chart;
    if (!Chart) {
      return;
    }

    const type = canvas.getAttribute('data-spark') ?? 'line';
    const points = parsePoints(canvas);
    if (points.length === 0) {
      return;
    }

    const labels = Array.from({ length: points.length }, (_, index) => index + 1);
    const config = {
      type: type === 'bar' ? 'bar' : 'line',
      data: {
        labels,
        datasets: dataset(type, points)
      },
      options: sparkOptions(type)
    };

    new Chart(canvas.getContext('2d'), config);
  };

  const init = () => {
    document
      .querySelectorAll('canvas[data-spark]')
      .forEach((canvas) => renderSpark(canvas));
  };

  ready().then(init);
})();
