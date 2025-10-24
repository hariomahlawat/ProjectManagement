(() => {
  const offcanvasElement = document.getElementById('iprRecordOffcanvas');
  if (!offcanvasElement) return;
  const mode = (offcanvasElement.getAttribute('data-ipr-mode') || '').toLowerCase();
  const hasForm = (offcanvasElement.getAttribute('data-ipr-has-form') || '').toLowerCase() === 'true';

  const supportsUrlApi = typeof URL === 'function' && URL.prototype && 'searchParams' in URL.prototype;

  const parseSearchParams = () => {
    const search = window.location.search ? window.location.search.substring(1) : '';
    if (!search) {
      return {};
    }

    return search.split('&').reduce((accumulator, part) => {
      if (!part) {
        return accumulator;
      }

      const [rawKey, rawValue = ''] = part.split('=');
      const key = decodeURIComponent(rawKey.replace(/\+/g, ' '));
      const value = decodeURIComponent(rawValue.replace(/\+/g, ' '));
      accumulator[key] = value;
      return accumulator;
    }, {});
  };

  const getQueryParam = key => {
    if (supportsUrlApi) {
      const currentUrl = new URL(window.location.href);
      return currentUrl.searchParams.get(key);
    }

    const params = parseSearchParams();
    return Object.prototype.hasOwnProperty.call(params, key) ? params[key] : null;
  };

  const buildUrlWithoutModeAndId = () => {
    if (supportsUrlApi) {
      const url = new URL(window.location.href);
      url.searchParams.delete('mode');
      url.searchParams.delete('id');
      return url.toString();
    }

    const location = window.location;
    const origin = location.origin || `${location.protocol}//${location.host}`;
    const base = `${origin}${location.pathname}`;
    const params = parseSearchParams();
    delete params.mode;
    delete params.id;

    const query = Object.keys(params)
      .map(paramKey => `${encodeURIComponent(paramKey)}=${encodeURIComponent(params[paramKey])}`)
      .join('&');

    const hash = location.hash || '';
    return query ? `${base}?${query}${hash}` : `${base}${hash}`;
  };

  const lacksOffcanvasSupport = typeof bootstrap === 'undefined' || !bootstrap.Offcanvas;
  const currentId = (getQueryParam('id') || '').toString();

  if (lacksOffcanvasSupport) {
    if (hasForm && (mode === 'create' || mode === 'edit')) {
      offcanvasElement.classList.add('show');
      offcanvasElement.removeAttribute('aria-hidden');
      updateTriggerStates(mode, currentId);
    } else {
      updateTriggerStates('', '');
    }
    return;
  }

  const offcanvasInstance = bootstrap.Offcanvas.getOrCreateInstance(offcanvasElement);

  if (hasForm && (mode === 'create' || mode === 'edit')) {
    offcanvasInstance.show();
    updateTriggerStates(mode, currentId);
  } else {
    updateTriggerStates('', '');
  }

  offcanvasElement.addEventListener('hidden.bs.offcanvas', () => {
    const nextUrl = buildUrlWithoutModeAndId();

    if (typeof history !== 'undefined' && typeof history.replaceState === 'function') {
      history.replaceState({}, document.title, nextUrl);
    } else {
      window.location.assign(nextUrl);
    }
    updateTriggerStates('', '');
  });

  function updateTriggerStates(nextMode, nextId) {
    document.querySelectorAll('[data-ipr-offcanvas-trigger]').forEach(btn => {
      const triggerMode = (btn.getAttribute('data-ipr-offcanvas-trigger') || '').toLowerCase();
      const triggerId = btn.getAttribute('data-ipr-record-id') || '';
      const expanded =
        (triggerMode === 'create' && nextMode === 'create') ||
        (triggerMode === 'edit' && nextMode === 'edit' && triggerId === nextId && nextId !== '');
      btn.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    });
  }

  ['iprCreateForm', 'iprEditForm'].forEach(formId => {
    const form = document.getElementById(formId);
    if (!form) return;

    form.addEventListener(
      'invalid',
      () => {
        setTimeout(() => {
          const first = form.querySelector(':invalid');
          if (first && typeof first.scrollIntoView === 'function') {
            first.scrollIntoView({ block: 'center' });
          }
        }, 0);
      },
      true
    );
  });
})();

(() => {
  const filterModalElement = document.getElementById('iprFilterModal');
  const filterTrigger = document.querySelector('[data-ipr-filter-trigger]');
  if (!filterModalElement || !filterTrigger || typeof bootstrap === 'undefined' || !bootstrap.Modal) {
    return;
  }

  filterModalElement.addEventListener('show.bs.modal', () => {
    filterTrigger.setAttribute('aria-expanded', 'true');
  });

  filterModalElement.addEventListener('shown.bs.modal', () => {
    const initialFocus = filterModalElement.querySelector('[data-ipr-filter-initial-focus]');
    if (initialFocus) {
      initialFocus.focus();
    }
  });

  filterModalElement.addEventListener('hidden.bs.modal', () => {
    filterTrigger.setAttribute('aria-expanded', 'false');
    filterTrigger.focus();
  });
})();

(() => {
  const confirmForms = document.querySelectorAll('form[data-ipr-confirm]');
  if (confirmForms.length === 0) {
    return;
  }

  confirmForms.forEach(form => {
    form.addEventListener('submit', event => {
      const message = form.getAttribute('data-ipr-confirm') || 'Are you sure?';

      if (!window.confirm(message)) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    });
  });
})();
