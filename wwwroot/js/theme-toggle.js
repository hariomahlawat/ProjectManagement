// SECTION: Theme toggle and persistence
(function () {
    const STORAGE_KEY = 'pm-theme';
    const root = document.documentElement;

    function applyTheme(theme) {
        const safeTheme = theme === 'dark' ? 'dark' : 'light';
        root.setAttribute('data-theme', safeTheme);

        // SECTION: Persist preference
        try {
            localStorage.setItem(STORAGE_KEY, safeTheme);
        } catch (e) {
            // Ignore storage errors (private mode, etc.)
        }

        // SECTION: Update dropdown label
        const label = document.getElementById('pm-theme-toggle-label');
        if (label) {
            label.textContent = safeTheme === 'dark' ? 'Dark' : 'Light';
        }

        // SECTION: Broadcast theme change for listeners (charts, maps, etc.)
        window.dispatchEvent(
            new CustomEvent('pm-theme-changed', { detail: { theme: safeTheme } })
        );
    }

    function getStoredTheme() {
        try {
            return localStorage.getItem(STORAGE_KEY);
        } catch (e) {
            return null;
        }
    }

    function getSystemPreference() {
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }
        return 'light';
    }

    function initTheme() {
        // SECTION: Initialise theme
        const stored = getStoredTheme();
        const initial = stored || getSystemPreference();
        applyTheme(initial);

        // SECTION: Wire dropdown toggle
        const toggle = document.getElementById('pm-theme-toggle');
        if (!toggle) return;

        toggle.addEventListener('click', function () {
            const current = root.getAttribute('data-theme') || 'light';
            const next = current === 'light' ? 'dark' : 'light';
            applyTheme(next);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initTheme);
    } else {
        initTheme();
    }
})();
