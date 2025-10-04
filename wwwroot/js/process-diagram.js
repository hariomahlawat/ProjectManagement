/* global ELK, bootstrap */
(() => {
  const container = document.getElementById('proc-diagram');
  if (!container) {
    return;
  }

  if (typeof ELK === 'undefined') {
    console.error('elkjs is required to render the procurement diagram.');
    return;
  }

  let graphData;
  try {
    graphData = JSON.parse(container.dataset.graph ?? '{}');
  } catch (error) {
    console.error('Invalid procurement diagram data payload.', error);
    return;
  }

  const nodes = Array.isArray(graphData.nodes) ? graphData.nodes : [];
  const edges = Array.isArray(graphData.edges) ? graphData.edges : [];
  const canEdit = Boolean(graphData.canEdit);

  const stageTitleEl = document.getElementById('proc-stage-title');
  const stageSubEl = document.getElementById('proc-stage-sub');
  const stageMetaEl = document.getElementById('proc-stage-meta');
  const checklistHolder = document.getElementById('proc-checklist');
  const addButton = document.getElementById('proc-checklist-add');

  if (checklistHolder) {
    checklistHolder.dataset.canEdit = canEdit ? '1' : '0';
  }

  if (addButton) {
    if (!canEdit) {
      addButton.remove();
    } else {
      addButton.disabled = true;
    }
  }

  const antiToken = document.querySelector('meta[name="request-verification-token"]')?.content ?? '';
  const bootstrapRef = typeof bootstrap !== 'undefined' ? bootstrap : null;

  const offcanvasElement = document.getElementById('checklistCanvas');
  const offcanvasBody = document.getElementById('checklistCanvasBody');
  const offcanvasTitle = document.getElementById('checklistCanvasLabel');
  const offcanvas = offcanvasElement && bootstrapRef
    ? bootstrapRef.Offcanvas.getOrCreateInstance(offcanvasElement)
    : null;

  const elk = new ELK();
  const elkGraph = {
    id: 'root',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': 'RIGHT',
      'elk.layered.spacing.nodeNodeBetweenLayers': '36',
      'elk.spacing.nodeNode': '24',
      'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
      'elk.edgeRouting': 'ORTHOGONAL',
      'elk.layered.feedbackEdges': 'true',
      'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP'
    },
    children: nodes.map((node) => ({
      id: node.id,
      width: node.type === 'decision' ? 100 : 160,
      height: node.type === 'decision' ? 70 : 48
    })),
    edges: edges.map((edge, index) => ({
      id: edge.id ?? `e${index}`,
      sources: [edge.source ?? edge.Source],
      targets: [edge.target ?? edge.Target]
    }))
  };

  const svgNS = 'http://www.w3.org/2000/svg';
  const nodeElements = new Map();
  let currentStage = null;
  let loadToken = 0;

  elk.layout(elkGraph)
    .then((layout) => {
      const svg = document.createElementNS(svgNS, 'svg');
      svg.setAttribute('viewBox', `0 0 ${layout.width} ${layout.height}`);
      svg.setAttribute('width', '100%');
      svg.setAttribute('height', '100%');
      svg.setAttribute('role', 'img');
      svg.setAttribute('aria-label', 'Procurement process flowchart');
      container.innerHTML = '';
      container.appendChild(svg);

      const defs = document.createElementNS(svgNS, 'defs');
      const marker = document.createElementNS(svgNS, 'marker');
      marker.setAttribute('id', 'proc-arrow');
      marker.setAttribute('markerWidth', '10');
      marker.setAttribute('markerHeight', '10');
      marker.setAttribute('refX', '8');
      marker.setAttribute('refY', '3');
      marker.setAttribute('orient', 'auto');
      const markerPath = document.createElementNS(svgNS, 'path');
      markerPath.setAttribute('d', 'M0,0 L8,3 L0,6 Z');
      markerPath.setAttribute('class', 'proc-arrow');
      marker.appendChild(markerPath);
      defs.appendChild(marker);
      svg.appendChild(defs);

      (layout.edges ?? []).forEach((edge) => {
        const path = document.createElementNS(svgNS, 'path');
        path.setAttribute('class', 'proc-edge');
        path.setAttribute('marker-end', 'url(#proc-arrow)');
        const sections = edge.sections ?? [];
        const d = sections.map((section) => {
          let segment = `M ${section.startPoint.x} ${section.startPoint.y}`;
          (section.bendPoints ?? []).forEach((point) => {
            segment += ` L ${point.x} ${point.y}`;
          });
          segment += ` L ${section.endPoint.x} ${section.endPoint.y}`;
          return segment;
        }).join(' ');
        path.setAttribute('d', d);
        svg.appendChild(path);
      });

      const childById = new Map();
      (layout.children ?? []).forEach((child) => childById.set(child.id, child));

      nodes.forEach((node) => {
        const layoutNode = childById.get(node.id);
        if (!layoutNode) {
          return;
        }

        const group = document.createElementNS(svgNS, 'g');
        group.setAttribute('transform', `translate(${layoutNode.x},${layoutNode.y})`);
        group.classList.add('proc-node');
        group.dataset.nodeId = String(node.id);
        if (node.type === 'decision') {
          group.classList.add('decision');
        }
        if (node.isOptional) {
          group.classList.add('optional');
        }
        group.setAttribute('tabindex', '0');
        group.setAttribute('role', 'button');
        const ariaLabel = node.isOptional
          ? `${node.label} (optional stage)`
          : node.label;
        group.setAttribute('aria-label', ariaLabel);
        group.setAttribute('aria-pressed', 'false');

        const rect = document.createElementNS(svgNS, 'rect');
        rect.setAttribute('x', '0');
        rect.setAttribute('y', '0');
        rect.setAttribute('width', String(layoutNode.width));
        rect.setAttribute('height', String(layoutNode.height));

        if (node.type === 'decision') {
          rect.setAttribute('rx', '4');
          rect.setAttribute('ry', '4');
        }

        if (node.type === 'decision') {
          rect.setAttribute('transform', `rotate(45 ${layoutNode.width / 2} ${layoutNode.height / 2})`);
        }

        const label = document.createElementNS(svgNS, 'text');
        label.setAttribute('text-anchor', 'middle');
        label.setAttribute('x', String(layoutNode.width / 2));
        const lines = wrapLabel(node.label ?? '', 18);
        const lineHeight = 14;
        const startY = layoutNode.height / 2 - ((lines.length - 1) * lineHeight) / 2;
        lines.forEach((line, index) => {
          const tspan = document.createElementNS(svgNS, 'tspan');
          tspan.setAttribute('x', String(layoutNode.width / 2));
          tspan.setAttribute('y', String(startY + index * lineHeight));
          tspan.textContent = line;
          label.appendChild(tspan);
        });

        const hit = document.createElementNS(svgNS, 'rect');
        hit.setAttribute('class', 'hit');
        hit.setAttribute('x', '-8');
        hit.setAttribute('y', '-8');
        hit.setAttribute('width', String(layoutNode.width + 16));
        hit.setAttribute('height', String(layoutNode.height + 16));
        hit.setAttribute('fill', 'transparent');
        hit.setAttribute('pointer-events', 'all');

        group.appendChild(rect);
        group.appendChild(label);
        group.appendChild(hit);

        group.addEventListener('click', () => selectStage(node));
        group.addEventListener('keydown', (event) => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            selectStage(node);
          }
        });

        nodeElements.set(node.id, group);
        svg.appendChild(group);
      });
    })
    .catch((error) => {
      console.error('Unable to render procurement process layout.', error);
    });

  function selectStage(node) {
    if (!node || !checklistHolder) {
      return;
    }

    currentStage = {
      id: node.id,
      label: node.label,
      isOptional: Boolean(node.isOptional)
    };

    nodeElements.forEach((element) => {
      element.classList.remove('is-selected');
      element.setAttribute('aria-pressed', 'false');
    });
    const element = nodeElements.get(node.id);
    if (element) {
      element.classList.add('is-selected');
      element.setAttribute('aria-pressed', 'true');
    }

    if (stageTitleEl) {
      stageTitleEl.textContent = node.label;
    }
    if (stageSubEl) {
      stageSubEl.textContent = 'Recommended actions:';
    }
    if (stageMetaEl) {
      stageMetaEl.textContent = node.isOptional ? 'Optional stage' : '';
    }
    if (addButton) {
      addButton.disabled = false;
    }

    checklistHolder.innerHTML = '<div class="text-muted">Loading…</div>';
    const requestId = ++loadToken;
    fetchChecklist(node.id)
      .then((items) => {
        if (requestId !== loadToken) {
          return;
        }
        renderChecklist(items ?? []);
      })
      .catch((error) => {
        console.error(error);
        if (requestId === loadToken) {
          checklistHolder.innerHTML = '<div class="text-danger">Unable to load checklist.</div>';
        }
      });
  }

  async function fetchChecklist(stageId) {
    const response = await fetch(`/process/stages/${stageId}/checklist`, {
      headers: {
        'Accept': 'application/json'
      },
      credentials: 'same-origin'
    });
    if (!response.ok) {
      throw new Error(`Failed to load checklist for stage ${stageId}`);
    }
    return response.json();
  }

  function renderChecklist(items) {
    if (!checklistHolder) {
      return;
    }

    checklistHolder.innerHTML = '';
    if (!items.length) {
      checklistHolder.innerHTML = '<div class="text-muted">No checklist items yet.</div>';
      return;
    }

    const sorted = [...items].sort((a, b) => {
      const sortA = toNumber(a?.sortOrder);
      const sortB = toNumber(b?.sortOrder);
      if (sortA !== sortB) {
        return sortA - sortB;
      }
      return toNumber(a?.id) - toNumber(b?.id);
    });

    const list = document.createElement('ol');
    list.className = 'ps-3 mb-0';
    sorted.forEach((item) => {
      const li = document.createElement('li');
      li.innerHTML = `<div>${escapeHtml(item.text)}</div>`;
      if (canEdit) {
        const actions = document.createElement('div');
        actions.className = 'd-flex gap-2 flex-wrap';
        const editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'btn btn-outline-secondary btn-sm';
        editBtn.textContent = 'Edit';
        editBtn.addEventListener('click', () => openEditor('edit', item));
        const deleteBtn = document.createElement('button');
        deleteBtn.type = 'button';
        deleteBtn.className = 'btn btn-outline-danger btn-sm';
        deleteBtn.textContent = 'Delete';
        deleteBtn.addEventListener('click', () => deleteItem(item));
        actions.appendChild(editBtn);
        actions.appendChild(deleteBtn);
        li.appendChild(actions);
      }
      list.appendChild(li);
    });

    checklistHolder.appendChild(list);
  }

  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>"']/g, (char) => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }[char] ?? char));
  }

  async function postJson(url, method, body) {
    const headers = {
      'Content-Type': 'application/json'
    };
    if (antiToken) {
      headers['RequestVerificationToken'] = antiToken;
    }
    const response = await fetch(url, {
      method,
      headers,
      credentials: 'same-origin',
      body: body ? JSON.stringify(body) : null
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Request failed with status ${response.status}`);
    }
    return response.status === 204 ? null : response.json();
  }

  if (addButton && canEdit) {
    addButton.addEventListener('click', () => {
      if (!currentStage) {
        return;
      }
      openEditor('add');
    });
  }

  function openEditor(mode, item) {
    if (!offcanvas || !offcanvasBody || !offcanvasTitle || !currentStage) {
      return;
    }

    const heading = mode === 'edit' ? 'Edit checklist item' : 'Add checklist item';
    offcanvasTitle.textContent = heading;
    const form = document.createElement('form');
    form.noValidate = true;
    form.innerHTML = `
      <div class="mb-3">
        <label class="form-label" for="procChecklistEditorText">Checklist item</label>
        <textarea class="form-control" id="procChecklistEditorText" rows="4" required></textarea>
        <div class="invalid-feedback" data-role="error"></div>
      </div>
      <div class="d-flex gap-2">
        <button type="submit" class="btn btn-primary">Save</button>
        <button type="button" class="btn btn-outline-secondary" data-action="cancel">Cancel</button>
      </div>
    `;

    offcanvasBody.replaceChildren(form);
    const textarea = form.querySelector('textarea');
    const errorEl = form.querySelector('[data-role="error"]');
    const cancelBtn = form.querySelector('[data-action="cancel"]');
    if (textarea) {
      textarea.value = mode === 'edit' ? item?.text ?? '' : '';
      window.setTimeout(() => textarea.focus(), 150);
    }
    if (cancelBtn) {
      cancelBtn.addEventListener('click', () => offcanvas.hide());
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      if (!textarea) {
        return;
      }
      const value = textarea.value.trim();
      if (!value) {
        textarea.classList.add('is-invalid');
        if (errorEl) {
          errorEl.textContent = 'Text is required.';
        }
        return;
      }
      textarea.classList.remove('is-invalid');
      if (errorEl) {
        errorEl.textContent = '';
      }

      try {
        if (mode === 'edit' && item) {
          await postJson(`/process/checklist/${item.id}`, 'PUT', { text: value });
        } else {
          await postJson(`/process/stages/${currentStage.id}/checklist`, 'POST', { text: value });
        }
        offcanvas.hide();
        const refreshed = await fetchChecklist(currentStage.id);
        renderChecklist(refreshed ?? []);
      } catch (error) {
        console.error(error);
        textarea.classList.add('is-invalid');
        if (errorEl) {
          errorEl.textContent = 'Unable to save the checklist item.';
        }
      }
    });

    offcanvas.show();
  }

  async function deleteItem(item) {
    if (!currentStage) {
      return;
    }
    const confirmed = await confirmModal('Delete this checklist item?');
    if (!confirmed) {
      return;
    }
    try {
      await postJson(`/process/checklist/${item.id}`, 'DELETE');
      const refreshed = await fetchChecklist(currentStage.id);
      renderChecklist(refreshed ?? []);
    } catch (error) {
      console.error(error);
      if (checklistHolder) {
        const previousAlert = checklistHolder.querySelector('.text-danger.mt-2');
        if (previousAlert) {
          previousAlert.remove();
        }
        const alert = document.createElement('div');
        alert.className = 'text-danger mt-2';
        alert.textContent = 'Unable to delete the checklist item.';
        checklistHolder.appendChild(alert);
      }
    }
  }

  function wrapLabel(text, maxLength) {
    const words = String(text ?? '').split(/\s+/).filter(Boolean);
    if (!words.length) {
      return [''];
    }
    const lines = [];
    let current = '';
    words.forEach((word) => {
      if (!current) {
        current = word;
        return;
      }
      const tentative = `${current} ${word}`;
      if (tentative.length <= maxLength) {
        current = tentative;
      } else {
        lines.push(current);
        current = word;
      }
    });
    if (current) {
      lines.push(current);
    }
    return lines;
  }

  function toNumber(value) {
    const num = Number(value);
    return Number.isFinite(num) ? num : 0;
  }

  async function confirmModal(message) {
    if (!bootstrapRef) {
      return window.confirm(message);
    }
    let modal = document.getElementById('procConfirmModal');
    if (!modal) {
      modal = document.createElement('div');
      modal.className = 'modal fade';
      modal.id = 'procConfirmModal';
      modal.tabIndex = -1;
      modal.innerHTML = `
        <div class="modal-dialog modal-sm">
          <div class="modal-content">
            <div class="modal-body">
              <p class="mb-0"></p>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
              <button type="button" class="btn btn-danger" data-action="ok">Delete</button>
            </div>
          </div>
        </div>`;
      document.body.appendChild(modal);
    }

    const modalInstance = bootstrapRef.Modal.getOrCreateInstance(modal);
    const messageEl = modal.querySelector('p');
    const okButton = modal.querySelector('[data-action="ok"]');

    return new Promise((resolve) => {
      if (!messageEl || !okButton) {
        resolve(false);
        return;
      }

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
      okButton.addEventListener('click', onOk, { once: true });
      modal.addEventListener('hidden.bs.modal', onHidden, { once: true });
      modalInstance.show();
    });
  }
})();
