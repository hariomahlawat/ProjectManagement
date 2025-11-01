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
        maintainAspectRatio: false,
        plugins: {
          legend: {
            position: 'right',
            labels: {
              boxWidth: 12,
              usePointStyle: true
            }
          }
        },
        cutout: '65%'
      }
    });
  } catch (error) {
    console.error('Failed to render IPR chart', error);
  }
})();
