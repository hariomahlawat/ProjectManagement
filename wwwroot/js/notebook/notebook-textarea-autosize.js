// SECTION: Shared textarea autosizing utility
export function resizeTextareaToContent(textarea, options = {}) {
  if (!textarea?.style) return { height: 0, overflowing: false };

  const minimumHeight = normalisePositiveNumber(options.minimumHeight, 38);
  const maximumHeight = Math.max(
    minimumHeight,
    normalisePositiveNumber(options.maximumHeight, 280)
  );

  // Reset first so scrollHeight reflects the full content rather than the
  // previously constrained element height.
  textarea.style.height = 'auto';

  const measuredHeight = Number.isFinite(Number(textarea.scrollHeight))
    ? Number(textarea.scrollHeight)
    : 0;
  const contentHeight = Math.max(minimumHeight, measuredHeight);
  const height = Math.min(contentHeight, maximumHeight);
  const overflowing = contentHeight > maximumHeight;

  textarea.style.height = `${height}px`;
  textarea.style.overflowY = overflowing ? 'auto' : 'hidden';

  return { height, overflowing };
}

function normalisePositiveNumber(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) && number > 0 ? number : fallback;
}
