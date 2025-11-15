// SECTION: OpsSignals sparkline bootstrapper
(function(){
  'use strict';

  if (typeof window === 'undefined' || typeof window.Chart === 'undefined') {
    return;
  }

  var ChartCtor = window.Chart;
  var strokeColor = '#94a3b8';

  function spark(canvas, values){
    if (!canvas || !values.length) {
      return null;
    }

    var min = Math.min.apply(null, values);
    var max = Math.max.apply(null, values);
    var paddedMin = Math.max(0, min - 1);
    var paddedMax = Math.max(paddedMin + 1, max + 1);

    return new ChartCtor(canvas.getContext('2d'), {
      type: 'line',
      data: {
        labels: values.map(function(_, idx){ return idx + 1; }),
        datasets:[{
          data: values,
          tension: 0.35,
          pointRadius: 0,
          borderWidth: 2,
          borderColor: strokeColor,
          fill: false
        }]
      },
      options: {
        responsive:true,
        maintainAspectRatio:false,
        animation:false,
        scales:{ x:{display:false}, y:{display:false, min:paddedMin, max:paddedMax} },
        plugins:{ legend:{display:false}, tooltip:{enabled:false} }
      }
    });
  }

  function init(){
    var observed = document.querySelectorAll('[data-ops-spark]');
    if (!observed.length) {
      return;
    }

    var io = 'IntersectionObserver' in window ? new IntersectionObserver(function(entries){
      entries.forEach(function(entry){
        if (!entry.isIntersecting) {
          return;
        }
        io.unobserve(entry.target);
        hydrate(entry.target);
      });
    }, { rootMargin: '120px' }) : null;

    function hydrate(target){
      var values = JSON.parse(target.getAttribute('data-values') || '[]');
      if (!values.length) {
        return;
      }
      spark(target.querySelector('canvas'), values);
    }

    observed.forEach(function(el){
      if (io) {
        io.observe(el);
      } else {
        hydrate(el);
      }
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once:true });
  } else {
    init();
  }
})();
// END SECTION
