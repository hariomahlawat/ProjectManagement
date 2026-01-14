// Section: Directory filter helpers
export function initIndustryPartnerDirectoryFilters() {
  const form = document.querySelector('#industryPartnerDirectoryForm');
  if (!form) {
    return;
  }

  // Section: Partner hidden input sync
  const syncSelectedPartnerHidden = () => {
    const url = new URL(window.location.href);
    const partner = url.searchParams.get('partner') || '';
    const input = form.querySelector('input[name="partner"]');
    if (input) {
      input.value = partner;
    }
  };

  // Section: Clear filters handling
  const requestDirectoryRefresh = () => {
    if (window.htmx) {
      window.htmx.trigger(form, 'submit');
      return;
    }

    if (form.requestSubmit) {
      form.requestSubmit();
      return;
    }

    form.submit();
  };

  const clearFormFilters = () => {
    const searchInput = form.querySelector('input[name="q"]');
    if (searchInput) {
      searchInput.value = '';
    }

    const typeSelect = form.querySelector('select[name="type"]');
    if (typeSelect) {
      typeSelect.value = '';
    }

    const statusSelect = form.querySelector('select[name="status"]');
    if (statusSelect) {
      statusSelect.value = '';
    }

    const sortSelect = form.querySelector('select[name="sort"]');
    if (sortSelect) {
      sortSelect.value = 'name';
    }
  };

  const handleClearFiltersClick = (event) => {
    const target = event.target.closest('[data-role="industry-partner-clear-filters"]');
    if (!target) {
      return;
    }

    event.preventDefault();
    clearFormFilters();
    syncSelectedPartnerHidden();
    requestDirectoryRefresh();
  };

  // Section: Clear filters handling
  document.body.addEventListener('click', handleClearFiltersClick);

  // Section: HTMX navigation sync
  document.body.addEventListener('htmx:afterOnLoad', syncSelectedPartnerHidden);
  window.addEventListener('popstate', syncSelectedPartnerHidden);

  syncSelectedPartnerHidden();
}
