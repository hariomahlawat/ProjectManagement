// wwwroot/js/theme/chart-palette.js

// SECTION: Palette helpers
export function getAppChartPalette() {
    const style = getComputedStyle(document.documentElement);

    function css(name, fallback) {
        const val = style.getPropertyValue(name).trim();
        return val || fallback;
    }

    return {
        primary: css('--pm-chart-primary', '#e0edff'),
        secondary: css('--pm-chart-secondary', '#2563eb'),
        neutral: css('--pm-chart-neutral', '#d1d5db'),
        success: css('--pm-success', '#22c55e'),
        warning: css('--pm-warning', '#fbbf24'),
        danger: css('--pm-danger', '#ef4444')
    };
}

// Maintain backward compatibility with older imports.
export function getChartPalette() {
    return getAppChartPalette();
}
// END SECTION
