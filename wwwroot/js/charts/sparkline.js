export function initSparklines() {
  document.querySelectorAll('.sparkline').forEach(el => {
    const points = (el.dataset.points || '').split(',').map(s => parseFloat(s)).filter(n => !isNaN(n));
    if (points.length === 0) return;
    const width = el.clientWidth || 60;
    const height = el.clientHeight || 20;
    const max = Math.max(...points);
    const min = Math.min(...points);
    const len = points.length - 1;
    const stepX = width / len;
    const scaleY = max === min ? 0 : height / (max - min);
    const path = points.map((p, i) => {
      const x = i * stepX;
      const y = height - (p - min) * scaleY;
      return `${i === 0 ? 'M' : 'L'}${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(' ');
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
    svg.setAttribute('width', width);
    svg.setAttribute('height', height);
    const pathEl = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    pathEl.setAttribute('d', path);
    pathEl.setAttribute('fill', 'none');
    pathEl.setAttribute('stroke', 'currentColor');
    pathEl.setAttribute('stroke-width', '1');
    svg.appendChild(pathEl);
    el.innerHTML = '';
    el.appendChild(svg);
  });
}
