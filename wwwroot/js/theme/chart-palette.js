// wwwroot/js/theme/chart-palette.js

// SECTION: Palette helpers
export function getChartPalette() {
    const style = getComputedStyle(document.documentElement);

    const primary = style.getPropertyValue('--pm-primary').trim() || '#2d6cdf';
    const primarySoft = style.getPropertyValue('--pm-primary-soft').trim() || '#e0edff';
    const accentGreen = style.getPropertyValue('--pm-success').trim() || '#22c55e';
    const accentAmber = style.getPropertyValue('--pm-warning').trim() || '#fbbf24';
    const accentTeal = style.getPropertyValue('--pm-accent-teal').trim() || '#14b8a6';
    const textContrast = style.getPropertyValue('--pm-text-contrast').trim() || '#0f172a';

    return {
        primary,
        primarySoft,
        accentGreen,
        accentAmber,
        accentTeal,
        textContrast
    };
}
// END SECTION
