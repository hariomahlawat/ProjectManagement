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
    return {
      axisColor: getCssVar('--pm-chart-axis'),
      gridColor: getCssVar('--pm-chart-grid'),
      accents: [
        getCssVar('--pm-chart-accent-1'),
        getCssVar('--pm-chart-accent-2'),
        getCssVar('--pm-chart-accent-3'),
        getCssVar('--pm-chart-accent-4')
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
