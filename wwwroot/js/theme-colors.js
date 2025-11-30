// =============================
// THEME COLOR HELPERS
// =============================
(function (global) {
  'use strict';

  // SECTION: CSS variable accessors
  function getCssVar(name) {
    var styles = getComputedStyle(document.documentElement);
    return styles.getPropertyValue(name).trim();
  }
  // END SECTION

  // SECTION: Chart palette
  function getChartPalette() {
    var primary = getCssVar('--pm-chart-primary') || '#e0edff';
    var secondary = getCssVar('--pm-chart-secondary') || '#2563eb';
    var neutral = getCssVar('--pm-chart-neutral') || '#d1d5db';

    return {
      primary: primary,
      secondary: secondary,
      neutral: neutral,
      success: getCssVar('--pm-success') || '#22c55e',
      warning: getCssVar('--pm-warning') || '#fbbf24',
      danger: getCssVar('--pm-danger') || '#ef4444',
      axisColor: getCssVar('--pm-chart-axis') || '#4b5563',
      gridColor: getCssVar('--pm-chart-grid') || '#e5e7eb',
      accents: [
        primary,
        secondary,
        getCssVar('--pm-chart-accent-2') || '#f97316',
        getCssVar('--pm-chart-accent-3') || '#22c55e',
        getCssVar('--pm-chart-accent-4') || '#a855f7'
      ]
    };
  }
  // END SECTION

  // SECTION: Map palette
  function getMapPalette() {
    return {
      fillInstalled: getCssVar('--pm-chart-accent-1'),
      fillDelivered: getCssVar('--pm-chart-accent-2'),
      fillPlanned: getCssVar('--pm-chart-accent-3'),
      border: getCssVar('--pm-border'),
      highlightBorder: getCssVar('--pm-accent-indigo-bright'),
      legendBackground: getCssVar('--pm-card'),
      tooltipBackground: getCssVar('--pm-card'),
      tooltipText: getCssVar('--pm-text')
    };
  }
  // END SECTION

  // SECTION: Skeleton palette
  function getSkeletonColor() {
    return getCssVar('--pm-skeleton');
  }
  // END SECTION

  // SECTION: Public API
  global.PMTheme = {
    getChartPalette: getChartPalette,
    getMapPalette: getMapPalette,
    getSkeletonColor: getSkeletonColor
  };
  // END SECTION
})(window);
