(function (window) {
    'use strict';

    // SECTION: Configuration
    var STORAGE_KEY = 'ffc.filters.delivery';
    var DEFAULT_STATE = {
        showCompleted: true,
        showPlanned: true
    };

    // SECTION: Normalization helpers
    function normalizeState(state) {
        var resolved = state || {};

        return {
            showCompleted: resolved.showCompleted !== false,
            showPlanned: resolved.showPlanned !== false
        };
    }

    // SECTION: Storage helpers
    function loadState() {
        try {
            var raw = window.localStorage.getItem(STORAGE_KEY);
            if (!raw) {
                return normalizeState(DEFAULT_STATE);
            }

            var parsed = JSON.parse(raw);
            return normalizeState(parsed);
        } catch (error) {
            return normalizeState(DEFAULT_STATE);
        }
    }

    function saveState(state) {
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(normalizeState(state)));
        } catch (error) {
            // ignore storage failures
        }
    }

    // SECTION: Public API
    window.FfcFilterState = {
        load: loadState,
        save: saveState
    };
})(window);
