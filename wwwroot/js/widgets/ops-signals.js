// SECTION: OpsSignals sparkline bootstrapper
(function(){
  if (typeof Chart === 'undefined') return;

  function spark(canvas, values){
    return new Chart(canvas.getContext('2d'), {
      type: 'line',
      data: { labels: values.map((_,i)=>i+1),
              datasets:[{ data: values, tension:.35, pointRadius:0, borderWidth:2, fill:false }] },
      options: {
        responsive:true, maintainAspectRatio:false, animation:false,
        scales:{ x:{display:false}, y:{display:false} },
        plugins:{ legend:{display:false}, tooltip:{enabled:false} }
      }
    });
  }

  function init(){
    const io = new IntersectionObserver(entries=>{
      entries.forEach(e=>{
        if (!e.isIntersecting) return;
        io.unobserve(e.target);
        const values = JSON.parse(e.target.getAttribute('data-values') || '[]');
        if (!values.length) return;
        spark(e.target.querySelector('canvas'), values);
      });
    }, { rootMargin: '120px' });

    document.querySelectorAll('[data-ops-spark]').forEach(el=>io.observe(el));
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init, { once:true });
  } else { init(); }
})();
// END SECTION
