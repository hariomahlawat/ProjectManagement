function parsePoints(el) {
  return (el.dataset.points || '')
    .split(',')
    .map(s => parseFloat(s))
    .filter(n => Number.isFinite(n));
}

function drawSparkline(el) {
  let points = parsePoints(el);
  if (points.length === 0) return;

  // Handle single value by duplicating it to form a flat line
  if (points.length === 1) points = [points[0], points[0]];

  // Dimensions (prefer computed / layout box)
  const rect = el.getBoundingClientRect();
  const width = Math.max(1, Math.round(rect.width || el.clientWidth || 120));
  const height = Math.max(1, Math.round(rect.height || el.clientHeight || 24));

  const max = Math.max(...points);
  const min = Math.min(...points);
  const span = max - min || 1; // avoid 0 (flat series)
  const len = points.length - 1;
  const stepX = width / len;

  const path = points.map((p, i) => {
    const x = i * stepX;
    const y = height - ((p - min) / span) * height;
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
  pathEl.setAttribute('stroke-width', '1.5');

  // Optional: rounded caps for a nicer feel
  pathEl.setAttribute('stroke-linecap', 'round');
  pathEl.setAttribute('stroke-linejoin', 'round');

  svg.appendChild(pathEl);
  el.replaceChildren(svg);
}

export function initSparklines() {
  const els = Array.from(document.querySelectorAll('.sparkline'));

  // draw after layout settles
  requestAnimationFrame(() => els.forEach(drawSparkline));

  // redraw on resize (throttled)
  let raf = 0;
  window.addEventListener('resize', () => {
    if (raf) cancelAnimationFrame(raf);
    raf = requestAnimationFrame(() => els.forEach(drawSparkline));
  });
}
