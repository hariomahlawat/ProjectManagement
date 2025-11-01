if (typeof Chart !== 'undefined' && typeof Chart.register === 'function') {
  Chart.register({
    id: 'iprCenterText',
    afterDraw(chart) {
      const datasets = chart.config && chart.config.data && chart.config.data.datasets;
      const dataset = datasets && datasets[0];
      const meta = chart.getDatasetMeta(0);
      if (!dataset || !Array.isArray(dataset.data) || !meta || !meta.data || !meta.data.length) {
        return;
      }

      const total = dataset.data.reduce((sum, value) => sum + (Number(value) || 0), 0);
      const ctx = chart.ctx;
      const center = meta.data[0];
      if (!center) {
        return;
      }

      ctx.save();
      ctx.font = '600 16px system-ui,-apple-system,Segoe UI,Roboto';
      ctx.fillStyle = getComputedStyle(document.body).getPropertyValue('--bs-body-color') || '#111';
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(total.toString(), center.x, center.y);
      ctx.restore();
    }
  });
}

(async function () {
  const canvas = document.getElementById('iprStatusChart');
  if (!canvas || typeof Chart === 'undefined') {
    return;
  }

  try {
    const response = await fetch('/ProjectOfficeReports/Ipr?handler=Summary', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' }
    });

    if (!response.ok) {
      return;
    }

    const summary = await response.json();
    const labels = ['Filing', 'Filed', 'Granted', 'Rejected', 'Withdrawn'];
    const dataset = [
      summary.filing ?? 0,
      summary.filed ?? 0,
      summary.granted ?? 0,
      summary.rejected ?? 0,
      summary.withdrawn ?? 0
    ];

    new Chart(canvas, {
      type: 'doughnut',
      data: {
        labels,
        datasets: [
          {
            data: dataset,
            backgroundColor: [
              'rgba(13, 110, 253, 0.8)',
              'rgba(32, 201, 151, 0.8)',
              'rgba(102, 16, 242, 0.8)',
              'rgba(220, 53, 69, 0.8)',
              'rgba(108, 117, 125, 0.8)'
            ],
            borderWidth: 0
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        aspectRatio: 2.0,
        cutout: '68%',
        layout: {
          padding: 8
        },
        plugins: {
          legend: {
            position: 'right',
            labels: {
              boxWidth: 10
            }
          },
          tooltip: {
            intersect: false,
            mode: 'nearest'
          }
        }
      }
    });
  } catch (error) {
    console.error('Failed to render IPR chart', error);
  }
})();
