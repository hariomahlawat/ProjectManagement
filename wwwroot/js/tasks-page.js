// wwwroot/js/tasks-page.js
// Handles: inline-actions visibility, drag reordering, and done checkbox auto-submit
// Requires: Bootstrap (for CSS only), Anti-forgery token in forms

(function () {
  function qs(sel, root) { return (root || document).querySelector(sel); }
  function qsa(sel, root) { return Array.from((root || document).querySelectorAll(sel)); }

  // ---- 1) Show row actions whenever a row's edit form changes ----
  function initRowActionReveal() {
    qsa('li[data-id]').forEach(row => {
      const form = qs('form[method="post"][id^="f-"]', row);
      const actions = qs('.row-actions', row);
      if (!form || !actions) return;
      const show = () => actions.classList.remove('d-none');
      const hide = () => actions.classList.add('d-none');
      hide();
      form.addEventListener('input', show, { passive: true });
      form.addEventListener('reset', () => setTimeout(hide, 0));
      form.addEventListener('keydown', e => {
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
          e.preventDefault();
          form.requestSubmit();
        }
      });
    });
  }

  // ---- 2) Drag reorder for open items ----
  function initDragReorder() {
    const lists = qsa('.todo-list');
    if (lists.length === 0) return;
    let dragEl = null;

    lists.forEach(list => {
      list.addEventListener('dragstart', e => {
        const li = e.target.closest('.todo-row[draggable="true"]');
        if (!li) return;
        dragEl = li;
        e.dataTransfer.effectAllowed = 'move';
        li.classList.add('opacity-50');
      });

      list.addEventListener('dragover', e => {
        if (!dragEl) return;
        e.preventDefault();
        const li = e.target.closest('.todo-row[draggable="true"]');
        if (!li || li === dragEl) return;
        const rect = li.getBoundingClientRect();
        const before = (e.clientY - rect.top) < rect.height / 2;
        li.parentNode.insertBefore(dragEl, before ? li : li.nextSibling);
      });

      list.addEventListener('dragend', async () => {
        if (dragEl) dragEl.classList.remove('opacity-50');
        dragEl = null;
        // Only include draggable rows (i.e., open items)
        const ids = qsa('li[draggable="true"][data-id]', list).map(r => r.dataset.id);
        if (ids.length === 0) return;
        const fd = new FormData();
        ids.forEach(id => fd.append('ids', id));
        const token = qs('input[name="__RequestVerificationToken"]')?.value;
        if (token) fd.append('__RequestVerificationToken', token);
        try {
          await fetch('?handler=Reorder', { method: 'POST', body: fd, credentials: 'same-origin' });
        } catch (_) { /* swallow network errors silently */ }
      });
    });
  }

  // ---- 0) Auto-submit done/undo checkboxes ----
  function initDoneAutosubmit() {
    function markVisualDone(cb) {
      const row = cb.closest('li[data-id]');
      if (!row) return;
      if (cb.checked) {
        row.setAttribute('data-status', 'done');
        // If this is a non-completed tab (rows are draggable), give an instant vanish hint.
        if (row.getAttribute('draggable') === 'true') {
          row.classList.add('vanish');
        }
      } else {
        row.removeAttribute('data-status');
        row.classList.remove('vanish');
      }
    }
    document.addEventListener('change', (e) => {
      const cb = e.target.closest('.js-done-checkbox');
      if (cb) markVisualDone(cb);
      if (!cb) return;
      const form = cb.closest('form');
      if (form) form.requestSubmit();
    }, { passive: true });
  }

  // ---- Notes modal ----
  function initNotes() {
    const modalEl = qs('#noteEditor');
    if (!modalEl || !window.bootstrap) return;
    const modal = new bootstrap.Modal(modalEl);
    const listEl = qs('#notesList', modalEl);
    const form = qs('#noteEditorForm', modalEl);
    const titleInput = qs('#noteTitle', form);
    const bodyInput = qs('#noteBody', form);
    const idInput = qs('#noteId', form);
    const todoIdInput = qs('#noteTodoId', form);
    const token = qs('input[name="__RequestVerificationToken"]')?.value;

    const esc = s => s.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c]));

    async function loadNotes(todoId){
      listEl.innerHTML='';
      let notes=[];
      try{
        const res=await fetch(`/tasks/${todoId}/notes`);
        if(res.ok) notes=await res.json();
      }catch(_){ }
      if(notes.length===0){ listEl.innerHTML='<div class="text-muted small">No notes yet.</div>'; }
      notes.forEach(n=>{
        const div=document.createElement('div');
        div.className='border rounded p-2 mb-2';
        div.dataset.id=n.id;
        div.innerHTML=`<div class="d-flex justify-content-between align-items-start"><div><div class="fw-semibold">${esc(n.title)}</div>${n.body?`<div class=\"small text-muted\">${esc(n.body.substring(0,100))}</div>`:''}</div><div class="btn-group btn-group-sm"><button class="btn btn-outline-secondary js-note-edit">Edit</button><button class="btn btn-outline-danger js-note-delete">Delete</button></div></div>`;
        listEl.appendChild(div);
      });
    }

    function updateBadge(btn,count){
      if(!btn) return;
      let badge=btn.querySelector('.badge');
      if(count>0){
        if(!badge){
          badge=document.createElement('span');
          badge.className='badge bg-secondary ms-1';
          btn.appendChild(badge);
        }
        badge.textContent=count;
      } else if(badge){ badge.remove(); }
    }

    document.addEventListener('click', async e=>{
      const openBtn=e.target.closest('.js-open-notes');
      if(openBtn){
        const todoId=openBtn.dataset.todoId;
        todoIdInput.value=todoId;
        idInput.value=''; titleInput.value=''; bodyInput.value='';
        await loadNotes(todoId);
        modal.show();
        return;
      }
      const editBtn=e.target.closest('.js-note-edit');
      if(editBtn){
        const row=editBtn.closest('[data-id]');
        const noteId=row?.dataset.id;
        if(!noteId) return;
        const res=await fetch(`/tasks/${todoIdInput.value}/notes`);
        const notes=res.ok?await res.json():[];
        const note=notes.find(n=>n.id===noteId);
        if(!note) return;
        idInput.value=note.id;
        titleInput.value=note.title;
        bodyInput.value=note.body||'';
        return;
      }
      const delBtn=e.target.closest('.js-note-delete');
      if(delBtn){
        const row=delBtn.closest('[data-id]');
        if(!row) return;
        window.pm?.askConfirm('Delete this note?', async ()=>{
          await fetch(`/notes/${row.dataset.id}`, { method:'DELETE', headers:{'RequestVerificationToken':token} });
          await loadNotes(todoIdInput.value);
          const btn=document.querySelector(`.js-open-notes[data-todo-id="${todoIdInput.value}"]`);
          updateBadge(btn, listEl.querySelectorAll('[data-id]').length);
        });
      }
    });

    form.addEventListener('submit', async e=>{
      e.preventDefault();
      const data={ TodoId: todoIdInput.value || null, Title: titleInput.value.trim(), Body: bodyInput.value };
      const id=idInput.value;
      const res=await fetch(id?`/notes/${id}`:'/notes', {
        method:id?'PUT':'POST',
        headers:{'Content-Type':'application/json','RequestVerificationToken':token},
        body:JSON.stringify(data)
      });
      if(res.ok){
        await loadNotes(todoIdInput.value);
        const btn=document.querySelector(`.js-open-notes[data-todo-id="${todoIdInput.value}"]`);
        updateBadge(btn, listEl.querySelectorAll('[data-id]').length);
        idInput.value=''; titleInput.value=''; bodyInput.value='';
      }
    });
  }

  // Kick everything off
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
      initRowActionReveal();
      initDoneAutosubmit();
      initDragReorder();
      initNotes();
    });
  } else {
    initRowActionReveal();
    initDoneAutosubmit();
    initDragReorder();
    initNotes();
  }
})();
