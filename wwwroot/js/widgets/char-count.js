(function () {
    function parseMaxLength(target) {
        if (typeof target.maxLength === 'number' && target.maxLength > 0) {
            return target.maxLength;
        }

        var attributeValue = target.getAttribute('maxlength') || target.getAttribute('data-val-length-max');
        if (!attributeValue) {
            return null;
        }

        var parsed = parseInt(attributeValue, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    }

    function initCounter(counter) {
        var targetId = counter.getAttribute('data-char-count-for');
        if (!targetId) {
            return;
        }

        var target = document.getElementById(targetId);
        if (!target) {
            return;
        }

        var maxLength = parseMaxLength(target);
        if (!maxLength) {
            return;
        }

        var update = function () {
            var length = target.value ? target.value.length : 0;
            counter.textContent = length + '/' + maxLength;
        };

        update();
        target.addEventListener('input', update);
        target.addEventListener('change', update);
    }

    function init() {
        var counters = document.querySelectorAll('[data-char-count-for]');
        counters.forEach(initCounter);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
