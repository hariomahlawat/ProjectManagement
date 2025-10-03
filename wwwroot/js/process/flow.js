const ready = () => {
  const root = document.getElementById('proc-flow');
  const dataScript = document.getElementById('proc-flow-data');
  if (!root || !dataScript) {
    return;
  }

  let payload;
  try {
    payload = JSON.parse(dataScript.textContent || '{}');
  } catch (err) {
    console.error('Failed to parse procurement flow payload', err);
    return;
  } finally {
    dataScript.remove();
  }

  const bootstrap = window.bootstrap;
  if (!bootstrap) {
    console.error('Bootstrap is required for the procurement flow module.');
    return;
  }

  const stages = Array.isArray(payload.stages) ? payload.stages : [];
  const edges = Array.isArray(payload.edges) ? payload.edges : [];
  const canEdit = Boolean(payload.canEdit);

  const nodesList = document.getElementById('proc-nodes');
  const svg = root.querySelector('.proc-connectors');
  if (!nodesList || !svg) {
    return;
  }

  const antiToken = document.querySelector('meta[name="request-verification-token"]')?.content ?? '';

  const nodeElById = new Map();
  const CELL_W = 320;
  const CELL_H = 190;
  const OFFSET_X = 70;
  const OFFSET_Y = 60;

  stages.forEach((stage, index) => {
    const li = document.createElement('li');
    li.dataset.id = String(stage.id);
    li.dataset.optional = String(Boolean(stage.isOptional));
    li.dataset.step = String(index + 1).padStart(2, '0');
    li.className = 'proc-node';
    li.tabIndex = 0;
    li.setAttribute('aria-label', stage.isOptional ? `${stage.name} (optional stage)` : stage.name);

    const content = document.createElement('div');
    content.className = 'proc-node-content';

    const title = document.createElement('span');
    title.className = 'proc-node-title';
    title.textContent = stage.name;
    content.appendChild(title);

    if (stage.isOptional) {
      const optionalLabel = document.createElement('span');
      optionalLabel.className = 'proc-node-optional';
      optionalLabel.textContent = 'Optional stage';
      content.appendChild(optionalLabel);
    }

    li.appendChild(content);

    const x = (Number(stage.col) || 0) * CELL_W + OFFSET_X;
    const y = (Number(stage.row) || 0) * CELL_H + OFFSET_Y;
    li.style.left = `${x}px`;
    li.style.top = `${y}px`;

    li.addEventListener('click', () => openChecklist(stage));
    li.addEventListener('keydown', (evt) => {
      if (evt.key === 'Enter' || evt.key === ' ') {
        evt.preventDefault();
        openChecklist(stage);
      }
    });

    nodesList.appendChild(li);
    nodeElById.set(stage.id, li);
  });

  const ocElement = document.getElementById('stageChecklistCanvas');
  const ocTitle = document.getElementById('stageChecklistLabel');
  const ocMeta = document.getElementById('checklistMeta');
  const ocList = document.getElementById('checklistArea');
  const ocActions = document.getElementById('checklistActions');
  const offcanvas = ocElement ? bootstrap.Offcanvas.getOrCreateInstance(ocElement) : null;

  if (!ocElement || !ocTitle || !ocMeta || !ocList || !offcanvas) {
    return;
  }

  if (ocActions && canEdit) {
    ocActions.classList.remove('d-none');
  }

  let currentStage = null;

  function drawConnectors() {
    const defs = svg.querySelector('defs');
    svg.replaceChildren();
    if (defs) {
      svg.appendChild(defs);
    }

    const canvasBox = nodesList.getBoundingClientRect();
    const svgWidth = Math.max(canvasBox.width, nodesList.scrollWidth);
    const svgHeight = Math.max(canvasBox.height, nodesList.scrollHeight);
    svg.setAttribute('width', String(svgWidth));
    svg.setAttribute('height', String(svgHeight));
    svg.style.width = `${svgWidth}px`;
    svg.style.height = `${svgHeight}px`;

    edges.forEach((edge) => {
      const fromEl = nodeElById.get(edge.fromId ?? edge.fromid ?? edge.fromID);
      const toEl = nodeElById.get(edge.toId ?? edge.toid ?? edge.toID);
      if (!fromEl || !toEl) {
        return;
      }

      const fromMid = midRight(fromEl);
      const toMid = midLeft(toEl);
      const midX = (fromMid.x + toMid.x) / 2;
      const path = `M ${fromMid.x},${fromMid.y} L ${midX},${fromMid.y} L ${midX},${toMid.y} L ${toMid.x},${toMid.y}`;

      const seg = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      seg.setAttribute('d', path);
      svg.appendChild(seg);
    });
  }

  function midRight(el) {
    const elRect = el.getBoundingClientRect();
    const parentRect = nodesList.getBoundingClientRect();
    return {
      x: elRect.right - parentRect.left,
      y: elRect.top - parentRect.top + elRect.height / 2,
    };
  }

  function midLeft(el) {
    const elRect = el.getBoundingClientRect();
    const parentRect = nodesList.getBoundingClientRect();
    return {
      x: elRect.left - parentRect.left,
      y: elRect.top - parentRect.top + elRect.height / 2,
    };
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, (char) => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;',
    }[char] ?? char));
  }

  async function fetchChecklist(stageId) {
    const response = await fetch(`/process/stages/${stageId}/checklist`, {
      credentials: 'same-origin',
      headers: {
        'Accept': 'application/json'
      }
    });

    if (!response.ok) {
      throw new Error(`Failed to load checklist for stage ${stageId}`);
    }
    return response.json();
  }

  function renderChecklist(items) {
    ocList.innerHTML = '';
    if (!items.length) {
      ocList.innerHTML = '<div class="text-muted">No items yet.</div>';
      return;
    }

    const sorted = [...items].sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0));
    sorted.forEach((item, index) => {
      const row = document.createElement('div');
      row.className = 'pci d-flex align-items-start justify-content-between';
      row.setAttribute('role', 'listitem');

      const textWrapper = document.createElement('div');
      textWrapper.innerHTML = `<div class="fw-semibold">${index + 1}. ${escapeHtml(item.text)}</div>`;
      row.appendChild(textWrapper);

      if (canEdit) {
        const actions = document.createElement('div');
        actions.className = 'pci-actions';
        actions.appendChild(makeButton('Edit checklist item', 'bi-pencil', 'btn btn-outline-secondary btn-sm', () => editItem(item)));
        actions.appendChild(makeButton('Delete checklist item', 'bi-trash', 'btn btn-outline-danger btn-sm', () => deleteItem(item)));
        row.appendChild(actions);
      }

      ocList.appendChild(row);
    });
  }

  async function openChecklist(stage) {
    currentStage = stage;
    nodeElById.forEach((el) => el.classList.remove('is-selected'));
    const selected = nodeElById.get(stage.id);
    if (selected) {
      selected.classList.add('is-selected');
    }

    ocTitle.textContent = stage.name;
    if (ocMeta) {
      ocMeta.textContent = stage.isOptional ? 'Optional stage' : '';
    }

    ocList.innerHTML = '<div class="text-muted">Loading…</div>';
    try {
      const items = await fetchChecklist(stage.id);
      renderChecklist(items);
    } catch (err) {
      console.error(err);
      ocList.innerHTML = '<div class="text-danger">Unable to load checklist.</div>';
    }

    offcanvas.show();
  }

  function makeButton(label, iconClass, className, action) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = `${className} pci-action-btn`;
    button.setAttribute('aria-label', label);
    button.setAttribute('title', label);
    button.innerHTML = `<i class="bi ${iconClass}" aria-hidden="true"></i><span class="visually-hidden">${label}</span>`;
    button.addEventListener('click', action);
    return button;
  }

  async function postJson(url, method, body) {
    const headers = {
      'Content-Type': 'application/json',
      'RequestVerificationToken': antiToken
    };
    const response = await fetch(url, {
      method,
      headers,
      credentials: 'same-origin',
      body: body ? JSON.stringify(body) : null
    });
    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Request failed: ${response.status} ${errorText}`);
    }
    return response.status === 204 ? null : response.json();
  }

  async function addItem() {
    if (!currentStage) {
      return;
    }
    const text = await promptModal('Add checklist item', '');
    if (text == null) {
      return;
    }
    const trimmed = text.trim();
    if (!trimmed) {
      return;
    }
    await postJson(`/process/stages/${currentStage.id}/checklist`, 'POST', { text: trimmed });
    const items = await fetchChecklist(currentStage.id);
    renderChecklist(items);
  }

  async function editItem(item) {
    if (!currentStage) {
      return;
    }
    const text = await promptModal('Edit checklist item', item.text);
    if (text == null) {
      return;
    }
    const trimmed = text.trim();
    if (!trimmed) {
      return;
    }
    await postJson(`/process/checklist/${item.id}`, 'PUT', { text: trimmed });
    const items = await fetchChecklist(currentStage.id);
    renderChecklist(items);
  }

  async function deleteItem(item) {
    if (!currentStage) {
      return;
    }
    const confirm = await confirmModal('Delete this item?');
    if (!confirm) {
      return;
    }
    await postJson(`/process/checklist/${item.id}`, 'DELETE');
    const items = await fetchChecklist(currentStage.id);
    renderChecklist(items);
  }

  if (ocActions && canEdit) {
    ocActions.querySelector('[data-action="add"]')?.addEventListener('click', addItem);
  }

  function debounce(fn, delay) {
    let handle;
    return (...args) => {
      clearTimeout(handle);
      handle = window.setTimeout(() => fn(...args), delay);
    };
  }

  drawConnectors();
  window.addEventListener('resize', debounce(drawConnectors, 150));

  async function promptModal(title, value) {
    let modal = document.getElementById('pfPromptModal');
    if (!modal) {
      modal = document.createElement('div');
      modal.className = 'modal fade';
      modal.id = 'pfPromptModal';
      modal.tabIndex = -1;
      modal.innerHTML = `
<div class="modal-dialog">
  <div class="modal-content">
    <div class="modal-header">
      <h5 class="modal-title"></h5>
      <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
    </div>
    <div class="modal-body">
      <label class="form-label" for="pfPromptTextarea">Text</label>
      <textarea id="pfPromptTextarea" class="form-control" rows="3"></textarea>
    </div>
    <div class="modal-footer">
      <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
      <button type="button" class="btn btn-primary" data-act="ok">Save</button>
    </div>
  </div>
</div>`;
      document.body.appendChild(modal);
    }

    const modalInstance = bootstrap.Modal.getOrCreateInstance(modal);
    const titleEl = modal.querySelector('.modal-title');
    const textarea = modal.querySelector('#pfPromptTextarea');
    const okButton = modal.querySelector('[data-act="ok"]');

    return new Promise((resolve) => {
      const cleanup = () => {
        modal.removeEventListener('hidden.bs.modal', onHidden);
        okButton.removeEventListener('click', onOk);
      };

      const onHidden = () => {
        cleanup();
        resolve(null);
      };

      const onOk = () => {
        cleanup();
        modalInstance.hide();
        resolve(textarea.value);
      };

      titleEl.textContent = title;
      textarea.value = value ?? '';
      okButton.addEventListener('click', onOk);
      modal.addEventListener('hidden.bs.modal', onHidden, { once: true });
      modalInstance.show();
      window.setTimeout(() => textarea.focus(), 150);
    });
  }

  async function confirmModal(message) {
    let modal = document.getElementById('pfConfirmModal');
    if (!modal) {
      modal = document.createElement('div');
      modal.className = 'modal fade';
      modal.id = 'pfConfirmModal';
      modal.tabIndex = -1;
      modal.innerHTML = `
<div class="modal-dialog modal-sm">
  <div class="modal-content">
    <div class="modal-body">
      <p class="mb-0"></p>
    </div>
    <div class="modal-footer">
      <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
      <button type="button" class="btn btn-danger" data-act="ok">Delete</button>
    </div>
  </div>
</div>`;
      document.body.appendChild(modal);
    }

    const modalInstance = bootstrap.Modal.getOrCreateInstance(modal);
    const messageEl = modal.querySelector('p');
    const okButton = modal.querySelector('[data-act="ok"]');

    return new Promise((resolve) => {
      const cleanup = () => {
        modal.removeEventListener('hidden.bs.modal', onHidden);
        okButton.removeEventListener('click', onOk);
      };

      const onHidden = () => {
        cleanup();
        resolve(false);
      };

      const onOk = () => {
        cleanup();
        modalInstance.hide();
        resolve(true);
      };

      messageEl.textContent = message;
      okButton.addEventListener('click', onOk);
      modal.addEventListener('hidden.bs.modal', onHidden, { once: true });
      modalInstance.show();
    });
  }
};

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', ready, { once: true });
} else {
  ready();
}
