(function () {
  const initialized = new WeakSet();

  function buildSearch(select) {
    if (!select || initialized.has(select)) {
      return;
    }

    const source = select.dataset.source;
    if (!source) {
      return;
    }

    const cache = new Map();
    Array.from(select.options).forEach((option) => {
      if (option.value) {
        cache.set(option.value, option.textContent ?? option.value);
      }
    });

    const wrapper = document.createElement('div');
    wrapper.className = 'async-select-wrapper';

    const search = document.createElement('input');
    search.type = 'search';
    search.className = 'form-control form-control-sm mb-2';
    search.placeholder = select.getAttribute('data-search-placeholder') ?? 'Search…';
    search.setAttribute('aria-label', 'Search options');

    const parent = select.parentNode;
    if (!parent) {
      return;
    }

    parent.insertBefore(wrapper, select);
    wrapper.appendChild(search);
    wrapper.appendChild(select);

    initialized.add(select);

    let debounceHandle;
    let lastQuery = '';

    async function loadOptions(query) {
      const trimmed = query.trim();
      if (trimmed === lastQuery) {
        return;
      }
      lastQuery = trimmed;

      const params = new URLSearchParams();
      if (trimmed.length > 0) {
        params.set('q', trimmed);
      }
      params.set('page', '1');
      params.set('pageSize', select.dataset.pageSize ?? '20');

      let response;
      try {
        response = await fetch(`${source}?${params.toString()}`, { credentials: 'same-origin' });
      } catch (err) {
        return;
      }

      if (!response.ok) {
        return;
      }

      let payload;
      try {
        payload = await response.json();
      } catch (err) {
        return;
      }

      if (!payload || !Array.isArray(payload.items)) {
        return;
      }

      const currentValue = select.value;
      const placeholder = Array.from(select.options).find((opt) => opt.value === '');
      const fragment = document.createDocumentFragment();

      if (placeholder) {
        fragment.appendChild(new Option(placeholder.textContent ?? '— (none) —', ''));
      } else {
        fragment.appendChild(new Option('— (none) —', ''));
      }

      const values = new Set();
      for (const item of payload.items) {
        if (item && Object.prototype.hasOwnProperty.call(item, 'id') && Object.prototype.hasOwnProperty.call(item, 'name')) {
          const value = String(item.id);
          const text = String(item.name);
          cache.set(value, text);
          values.add(value);
          fragment.appendChild(new Option(text, value));
        }
      }

      if (currentValue && !values.has(currentValue)) {
        const knownText = cache.get(currentValue) ?? currentValue;
        fragment.appendChild(new Option(knownText, currentValue));
      }

      select.innerHTML = '';
      select.appendChild(fragment);
      select.value = currentValue;
    }

    function handleInput(event) {
      const value = event.target.value;
      if (debounceHandle) {
        clearTimeout(debounceHandle);
      }
      debounceHandle = window.setTimeout(() => {
        loadOptions(value);
      }, 250);
    }

    search.addEventListener('input', handleInput);

    select.addEventListener('focus', () => {
      if (!select.dataset.asyncSelectBootstrapped) {
        loadOptions('');
        select.dataset.asyncSelectBootstrapped = '1';
      }
    }, { once: true });
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('select.js-async-select').forEach(buildSearch);
  });
})();
