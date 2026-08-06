import { prismConfirm } from './project-briefing-confirm.js';

const root = document.querySelector('[data-pbd-root]');

if (root) {
  const additionalSlides = root.querySelector('[data-pbd-additional-slides]');
  const manageAdditionalSlides = root.querySelector('[data-pbd-manage-additional-slides]');
  const additionalSlideList = root.querySelector('[data-pbd-additional-slide-list]');
  const additionalSlideOrderForm = root.querySelector('[data-pbd-additional-slide-order-form]');
  const additionalSlideOrderInput = root.querySelector('[data-pbd-additional-slide-order]');

  manageAdditionalSlides?.addEventListener('click', () => {
    const settingsClose = root.querySelector('[data-pbd-settings-close]');
    if (settingsClose instanceof HTMLButtonElement) settingsClose.click();
    window.setTimeout(() => {
      additionalSlides?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      const addButton = additionalSlides?.querySelector('[data-bs-target="#pbd-additional-slide-library-modal"]');
      if (addButton instanceof HTMLElement) addButton.focus({ preventScroll: true });
    }, 240);
  });

  root.querySelectorAll('[data-pbd-remove-additional-slide-form]').forEach((form) => {
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const name = form.dataset.slideName || 'this slide';
      const confirmed = await prismConfirm({
        title: `Remove ${name}?`,
        message: 'The slide will be removed from this deck. Its deck-specific configuration will be retained and restored if the slide is added again.',
        confirmText: 'Remove from deck',
        cancelText: 'Keep slide',
        tone: 'danger',
        returnFocus: form.querySelector('button[type="submit"]')
      });
      if (confirmed) HTMLFormElement.prototype.submit.call(form);
    });
  });

  root.querySelectorAll('[data-pbd-add-slide-disabled-tip]').forEach((element) => {
    window.bootstrap?.Tooltip?.getOrCreateInstance(element, {
      container: 'body',
      placement: 'bottom',
      trigger: 'hover focus'
    });
  });

  if (additionalSlideList && additionalSlideOrderForm instanceof HTMLFormElement
      && additionalSlideOrderInput instanceof HTMLInputElement
      && typeof window.Sortable === 'function') {
    new window.Sortable(additionalSlideList, {
      animation: 150,
      handle: '[data-pbd-additional-slide-handle]',
      draggable: '[data-pbd-additional-slide-card][data-can-reorder="true"]',
      ghostClass: 'is-sorting',
      chosenClass: 'is-chosen',
      onMove: (event) => Boolean(
        event.related?.matches?.('[data-pbd-additional-slide-card][data-can-reorder="true"]')
      ),
      onEnd: () => {
        const order = [...additionalSlideList.querySelectorAll('[data-pbd-additional-slide-card]')]
          .map((card) => card.dataset.slideType || '')
          .filter(Boolean);
        additionalSlideOrderInput.value = order.join(',');
        additionalSlideOrderForm.submit();
      }
    });
  }

  // Focused editor for the Role & Charter additional slide.
  const drawer = root.querySelector('[data-pbd-role-charter-drawer]');
  const backdrop = root.querySelector('[data-pbd-role-charter-backdrop]');
  const form = root.querySelector('[data-pbd-role-charter-form]');
  const saveButton = root.querySelector('[data-pbd-role-charter-save]');
  const status = root.querySelector('[data-pbd-role-charter-status]');
  const enable = root.querySelector('[data-pbd-role-charter-enable]');
  const settings = root.querySelector('[data-pbd-role-charter-settings]');
  const sharedChoice = root.querySelector('[data-pbd-role-charter-shared]');
  const customChoice = root.querySelector('[data-pbd-role-charter-custom]');
  const sharedSummary = root.querySelector('[data-pbd-role-charter-shared-summary]');
  const customContent = root.querySelector('[data-pbd-role-charter-custom-content]');
  const validation = root.querySelector('[data-pbd-role-charter-validation]');
  const roleList = root.querySelector('[data-pbd-role-list]');
  const charterList = root.querySelector('[data-pbd-charter-list]');
  const roleLines = root.querySelector('[data-pbd-role-lines]');
  const charterLines = root.querySelector('[data-pbd-charter-lines]');
  const layout = form?.querySelector('select[name="RoleCharterLayout"]');
  let returnFocus = null;
  let initialState = '';
  let initialRoleMarkup = roleList?.innerHTML || '';
  let initialCharterMarkup = charterList?.innerHTML || '';
  let dirty = false;
  let valid = true;

  const serializeForm = () => form instanceof HTMLFormElement
    ? [...new FormData(form).entries()]
      .filter(([name]) => name !== 'RowVersion')
      .map(([name, value]) => `${name}=${String(value).trim()}`)
      .sort()
      .join('&')
    : '';

  const createActionButton = (attribute, label, icon, extraClass = '') => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `btn btn-sm btn-light ${extraClass}`.trim();
    button.setAttribute(attribute, '');
    button.setAttribute('aria-label', label);
    button.innerHTML = `<i class="bi ${icon}" aria-hidden="true"></i>`;
    return button;
  };

  const createEntry = (kind, lead = '', text = '') => {
    const isRole = kind === 'role';
    const row = document.createElement('div');
    row.className = 'pbd-role-charter-entry';
    row.setAttribute(isRole ? 'data-pbd-role-item' : 'data-pbd-charter-item', '');

    const handle = document.createElement('span');
    handle.className = 'pbd-institutional-list-editor__handle';
    handle.setAttribute('aria-hidden', 'true');
    handle.innerHTML = '<i class="bi bi-grip-vertical"></i>';

    const leadInput = document.createElement('input');
    leadInput.className = 'form-control pbd-role-charter-entry__lead';
    leadInput.maxLength = 60;
    leadInput.placeholder = 'Lead phrase';
    leadInput.value = lead;
    leadInput.setAttribute(isRole ? 'data-pbd-role-lead' : 'data-pbd-charter-lead', '');
    leadInput.setAttribute('aria-label', isRole ? 'Role lead phrase' : 'Charter lead phrase');

    const textInput = document.createElement('input');
    textInput.className = 'form-control';
    textInput.maxLength = 240;
    textInput.placeholder = isRole ? 'Authorised role statement' : 'Charter detail';
    textInput.value = text;
    textInput.setAttribute(isRole ? 'data-pbd-role-text' : 'data-pbd-charter-text', '');
    textInput.setAttribute('aria-label', isRole ? 'Role statement' : 'Charter detail');

    const actions = document.createElement('span');
    actions.className = 'pbd-institutional-list-editor__actions';
    actions.append(
      createActionButton('data-pbd-list-up', `Move ${isRole ? 'role statement' : 'charter item'} up`, 'bi-arrow-up'),
      createActionButton('data-pbd-list-down', `Move ${isRole ? 'role statement' : 'charter item'} down`, 'bi-arrow-down'),
      createActionButton('data-pbd-list-remove', `Remove ${isRole ? 'role statement' : 'charter item'}`, 'bi-trash', 'text-danger')
    );

    row.append(handle, leadInput, textInput, actions);
    return row;
  };

  const entries = (list, selector) => list ? [...list.querySelectorAll(selector)] : [];

  const updateButtons = (list, selector) => {
    entries(list, selector).forEach((row, index, rows) => {
      const up = row.querySelector('[data-pbd-list-up]');
      const down = row.querySelector('[data-pbd-list-down]');
      if (up instanceof HTMLButtonElement) up.disabled = index === 0;
      if (down instanceof HTMLButtonElement) down.disabled = index === rows.length - 1;
    });
  };

  const syncList = (list, selector, leadSelector, textSelector, output) => {
    if (!list || !(output instanceof HTMLTextAreaElement)) return;
    output.value = entries(list, selector)
      .map((row) => {
        const lead = row.querySelector(leadSelector)?.value?.trim() || '';
        const text = row.querySelector(textSelector)?.value?.trim() || '';
        return lead || text ? `${lead}\t${text}` : '';
      })
      .filter(Boolean)
      .join('\n');
    updateButtons(list, selector);
  };

  const syncRole = () => syncList(
    roleList,
    '[data-pbd-role-item]',
    '[data-pbd-role-lead]',
    '[data-pbd-role-text]',
    roleLines
  );

  const syncCharter = () => syncList(
    charterList,
    '[data-pbd-charter-item]',
    '[data-pbd-charter-lead]',
    '[data-pbd-charter-text]',
    charterLines
  );

  const contentCount = (list, selector, leadSelector, textSelector) => entries(list, selector)
    .filter((row) => Boolean(
      row.querySelector(leadSelector)?.value?.trim()
      || row.querySelector(textSelector)?.value?.trim()
    )).length;

  const updateVisibility = () => {
    const shared = Boolean(sharedChoice?.checked);
    // Inclusion is controlled from the Additional Slides workspace. The editor
    // remains available while the slide is disabled so it can be prepared first.
    if (settings instanceof HTMLElement) settings.hidden = false;
    if (sharedSummary instanceof HTMLElement) sharedSummary.hidden = !shared;
    if (customContent instanceof HTMLElement) customContent.hidden = shared;
  };

  const validate = ({ focus = false } = {}) => {
    syncRole();
    syncCharter();
    const included = true;
    const shared = Boolean(sharedChoice?.checked);
    const charterOnly = layout?.value === 'CharterOnly' || layout?.value === '3';
    const roleCount = shared ? 2 : contentCount(roleList, '[data-pbd-role-item]', '[data-pbd-role-lead]', '[data-pbd-role-text]');
    const charterCount = shared ? 10 : contentCount(charterList, '[data-pbd-charter-item]', '[data-pbd-charter-lead]', '[data-pbd-charter-text]');
    valid = !included || (charterCount > 0 && (charterOnly || roleCount > 0));
    validation?.toggleAttribute('hidden', valid);
    if (!valid && focus) {
      if (!charterOnly && roleCount === 0) roleList?.querySelector('input')?.focus();
      else charterList?.querySelector('input')?.focus();
    }
    if (saveButton) saveButton.disabled = !dirty || !valid;
    return valid;
  };

  const setDirty = (next) => {
    dirty = next;
    if (status) {
      status.textContent = next ? 'Unsaved Role & Charter changes' : 'No unsaved Role & Charter changes';
      status.classList.toggle('is-dirty', next);
      status.classList.remove('is-saving');
    }
    if (saveButton) saveButton.disabled = !next || !valid;
  };

  const refreshDirty = () => {
    syncRole();
    syncCharter();
    validate();
    setDirty(serializeForm() !== initialState);
  };

  const focusable = () => drawer
    ? [...drawer.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')]
      .filter((element) => !element.hidden && element.getClientRects().length > 0)
    : [];

  const openDrawer = (trigger = null) => {
    if (!drawer || !backdrop || !(form instanceof HTMLFormElement)) return;
    returnFocus = trigger || document.activeElement;
    drawer.classList.add('is-open');
    drawer.setAttribute('aria-hidden', 'false');
    backdrop.hidden = false;
    document.body.classList.add('pbd-profile-drawer-open');
    root.querySelectorAll('[data-pbd-role-charter-open]').forEach((button) => button.setAttribute('aria-expanded', 'true'));
    updateVisibility();
    syncRole();
    syncCharter();
    validate();
    initialState = serializeForm();
    initialRoleMarkup = roleList?.innerHTML || '';
    initialCharterMarkup = charterList?.innerHTML || '';
    setDirty(false);
    window.requestAnimationFrame(() => focusable()[0]?.focus());
  };

  const restoreInitial = () => {
    if (!(form instanceof HTMLFormElement)) return;
    form.reset();
    if (roleList) roleList.innerHTML = initialRoleMarkup;
    if (charterList) charterList.innerHTML = initialCharterMarkup;
    updateVisibility();
    syncRole();
    syncCharter();
    validate();
    setDirty(false);
  };

  const confirmRoleCharterDiscard = async (message = 'Your changes to the Role & Charter slide have not been saved.') => {
    if (!dirty) return true;
    return prismConfirm({
      title: 'Discard unsaved changes?',
      message,
      confirmText: 'Discard changes',
      cancelText: 'Keep editing',
      tone: 'danger',
      returnFocus: document.activeElement
    });
  };

  const closeDrawer = async ({ force = false } = {}) => {
    if (!drawer || !backdrop) return false;
    if (dirty && !force && !(await confirmRoleCharterDiscard())) return false;
    if (dirty) restoreInitial();
    drawer.classList.remove('is-open');
    drawer.setAttribute('aria-hidden', 'true');
    backdrop.hidden = true;
    document.body.classList.remove('pbd-profile-drawer-open');
    root.querySelectorAll('[data-pbd-role-charter-open]').forEach((button) => button.setAttribute('aria-expanded', 'false'));
    if (returnFocus instanceof HTMLElement) returnFocus.focus();
    return true;
  };

  root.querySelectorAll('[data-pbd-role-charter-open]').forEach((button) => {
    button.addEventListener('click', () => openDrawer(button));
  });
  root.querySelectorAll('[data-pbd-role-charter-close]').forEach((button) => {
    button.addEventListener('click', async () => { await closeDrawer(); });
  });
  backdrop?.addEventListener('click', async () => { await closeDrawer(); });

  drawer?.addEventListener('keydown', async (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      await closeDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const candidates = focusable();
    if (candidates.length === 0) return;
    const first = candidates[0];
    const last = candidates.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  const handleListClick = (event, list, selector) => {
    const button = event.target.closest('button');
    const row = button?.closest(selector);
    if (!button || !row || !list) return false;
    if (button.matches('[data-pbd-list-remove]')) row.remove();
    else if (button.matches('[data-pbd-list-up]') && row.previousElementSibling) list.insertBefore(row, row.previousElementSibling);
    else if (button.matches('[data-pbd-list-down]') && row.nextElementSibling) list.insertBefore(row.nextElementSibling, row);
    else return false;
    return true;
  };

  roleList?.addEventListener('click', (event) => {
    if (handleListClick(event, roleList, '[data-pbd-role-item]')) refreshDirty();
  });
  charterList?.addEventListener('click', (event) => {
    if (handleListClick(event, charterList, '[data-pbd-charter-item]')) refreshDirty();
  });
  roleList?.addEventListener('input', refreshDirty);
  charterList?.addEventListener('input', refreshDirty);
  root.querySelector('[data-pbd-role-add]')?.addEventListener('click', () => {
    roleList?.append(createEntry('role'));
    refreshDirty();
    roleList?.lastElementChild?.querySelector('input')?.focus();
  });
  root.querySelector('[data-pbd-charter-add]')?.addEventListener('click', () => {
    charterList?.append(createEntry('charter'));
    refreshDirty();
    charterList?.lastElementChild?.querySelector('input')?.focus();
  });
  enable?.addEventListener('change', () => { updateVisibility(); refreshDirty(); });
  sharedChoice?.addEventListener('change', () => { updateVisibility(); refreshDirty(); });
  customChoice?.addEventListener('change', () => { updateVisibility(); refreshDirty(); });
  layout?.addEventListener('change', refreshDirty);
  form?.addEventListener('input', (event) => {
    if (event.target.closest('[data-pbd-role-list], [data-pbd-charter-list]')) return;
    refreshDirty();
  });
  form?.addEventListener('change', refreshDirty);
  form?.addEventListener('submit', (event) => {
    if (!validate({ focus: true })) {
      event.preventDefault();
      return;
    }
    if (!dirty) {
      event.preventDefault();
      return;
    }
    if (saveButton) saveButton.disabled = true;
    if (status) {
      status.textContent = 'Saving Role & Charter…';
      status.classList.remove('is-dirty');
      status.classList.add('is-saving');
    }
    dirty = false;
  });

  document.addEventListener('click', async (event) => {
    if (!dirty || event.defaultPrevented || event.button !== 0
        || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    const link = event.target.closest('a[href]');
    if (!(link instanceof HTMLAnchorElement) || drawer?.contains(link)) return;
    const href = link.getAttribute('href') || '';
    if (!href || href.startsWith('#') || href.startsWith('javascript:')
        || link.target === '_blank' || link.hasAttribute('download')) return;

    event.preventDefault();
    if (!(await confirmRoleCharterDiscard('Your unsaved Role & Charter changes will be lost if you leave this page.'))) return;
    setDirty(false);
    window.location.assign(link.href);
  }, true);

  document.addEventListener('submit', async (event) => {
    if (!dirty || event.defaultPrevented) return;
    const submittedForm = event.target;
    if (!(submittedForm instanceof HTMLFormElement) || submittedForm === form) return;

    event.preventDefault();
    if (!(await confirmRoleCharterDiscard('Your unsaved Role & Charter changes will be lost if you continue.'))) return;
    setDirty(false);
    HTMLFormElement.prototype.submit.call(submittedForm);
  }, true);

  window.addEventListener('beforeunload', (event) => {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = '';
  });

  if (drawer?.dataset.pbdRoleCharterReopen === 'true') {
    window.setTimeout(() => openDrawer(), 0);
  }

  // Focused editor for the ERP-backed FFC Global Footprint additional slide.
  const ffcDrawer = root.querySelector('[data-pbd-ffc-footprint-drawer]');
  const ffcBackdrop = root.querySelector('[data-pbd-ffc-footprint-backdrop]');
  const ffcForm = root.querySelector('[data-pbd-ffc-footprint-form]');
  const ffcSave = root.querySelector('[data-pbd-ffc-footprint-save]');
  const ffcStatus = root.querySelector('[data-pbd-ffc-footprint-status]');
  let ffcReturnFocus = null;
  let ffcInitialState = '';
  let ffcDirty = false;

  const serializeFfc = () => ffcForm instanceof HTMLFormElement
    ? [...new FormData(ffcForm).entries()]
      .filter(([name]) => name !== 'RowVersion')
      .map(([name, value]) => `${name}=${String(value).trim()}`)
      .sort()
      .join('&')
    : '';

  const setFfcDirty = (next) => {
    ffcDirty = next;
    if (ffcStatus) {
      ffcStatus.textContent = next ? 'Unsaved FFC footprint changes' : 'No unsaved FFC footprint changes';
      ffcStatus.classList.toggle('is-dirty', next);
      ffcStatus.classList.remove('is-saving');
    }
    if (ffcSave instanceof HTMLButtonElement) ffcSave.disabled = !next;
  };

  const ffcFocusable = () => ffcDrawer
    ? [...ffcDrawer.querySelectorAll('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex="-1"])')]
      .filter((element) => !element.hidden && element.getClientRects().length > 0)
    : [];

  const openFfcDrawer = (trigger = null) => {
    if (!ffcDrawer || !ffcBackdrop || !(ffcForm instanceof HTMLFormElement)) return;
    ffcReturnFocus = trigger || document.activeElement;
    ffcDrawer.classList.add('is-open');
    ffcDrawer.setAttribute('aria-hidden', 'false');
    ffcBackdrop.hidden = false;
    document.body.classList.add('pbd-profile-drawer-open');
    root.querySelectorAll('[data-pbd-ffc-footprint-open]').forEach((button) => button.setAttribute('aria-expanded', 'true'));
    ffcInitialState = serializeFfc();
    setFfcDirty(false);
    window.requestAnimationFrame(() => ffcFocusable()[0]?.focus());
  };

  const confirmFfcDiscard = async () => !ffcDirty || prismConfirm({
    title: 'Discard unsaved changes?',
    message: 'Your changes to the FFC Global Footprint slide have not been saved.',
    confirmText: 'Discard changes',
    cancelText: 'Keep editing',
    tone: 'danger',
    returnFocus: document.activeElement
  });

  const closeFfcDrawer = async ({ force = false } = {}) => {
    if (!ffcDrawer || !ffcBackdrop) return false;
    if (!force && !(await confirmFfcDiscard())) return false;
    if (ffcDirty && ffcForm instanceof HTMLFormElement) ffcForm.reset();
    ffcDrawer.classList.remove('is-open');
    ffcDrawer.setAttribute('aria-hidden', 'true');
    ffcBackdrop.hidden = true;
    document.body.classList.remove('pbd-profile-drawer-open');
    root.querySelectorAll('[data-pbd-ffc-footprint-open]').forEach((button) => button.setAttribute('aria-expanded', 'false'));
    setFfcDirty(false);
    if (ffcReturnFocus instanceof HTMLElement) ffcReturnFocus.focus();
    return true;
  };

  root.querySelectorAll('[data-pbd-ffc-footprint-open]').forEach((button) => {
    button.addEventListener('click', () => openFfcDrawer(button));
  });
  root.querySelectorAll('[data-pbd-ffc-footprint-close]').forEach((button) => {
    button.addEventListener('click', async () => { await closeFfcDrawer(); });
  });
  ffcBackdrop?.addEventListener('click', async () => { await closeFfcDrawer(); });
  ffcForm?.addEventListener('input', () => setFfcDirty(serializeFfc() !== ffcInitialState));
  ffcForm?.addEventListener('change', () => setFfcDirty(serializeFfc() !== ffcInitialState));
  ffcForm?.addEventListener('submit', (event) => {
    if (!ffcDirty) {
      event.preventDefault();
      return;
    }
    if (ffcSave instanceof HTMLButtonElement) ffcSave.disabled = true;
    if (ffcStatus) {
      ffcStatus.textContent = 'Saving FFC Global Footprint…';
      ffcStatus.classList.remove('is-dirty');
      ffcStatus.classList.add('is-saving');
    }
    ffcDirty = false;
  });
  ffcDrawer?.addEventListener('keydown', async (event) => {
    if (event.key === 'Escape') {
      event.preventDefault();
      await closeFfcDrawer();
      return;
    }
    if (event.key !== 'Tab') return;
    const candidates = ffcFocusable();
    if (!candidates.length) return;
    const first = candidates[0];
    const last = candidates.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });
  if (ffcDrawer?.dataset.pbdFfcFootprintReopen === 'true') {
    window.setTimeout(() => openFfcDrawer(), 0);
  }

}
