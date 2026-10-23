/*
 * PRISM chat, file attachments (docs/chat-attachments.md): the attach button, uploads with
 * progress, the files waiting to be sent, files in messages, and removing a sent file.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 *
 * Server contract:
 *   POST {api}/attachments                       multipart "file": checks it, returns its id
 *   GET  {api}/attachments/{id}[?thumbnail=true] the file (checked on every request)
 *   POST {api}/attachments/{id}/remove           the sender removes a file
 *   hub  in: AttachmentRemoved { attachmentId, messageId, groupId }
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, ARM_MS, el, icon, iconButton, newClientId,
  } = global.PMChatParts;

  const ACCEPT = '.pdf,.docx,.xlsx,.pptx,.jpg,.jpeg,.png,.webp,.txt,.csv';

  /** "840 KB", "12.5 MB". */
  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
    const mb = bytes / (1024 * 1024);
    return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`;
  }

  function fileIcon(attachment) {
    const type = attachment.contentType || '';
    if (type === 'application/pdf') return 'file-earmark-pdf';
    if (type.includes('wordprocessingml')) return 'file-earmark-word';
    if (type.includes('spreadsheetml') || type === 'text/csv') return 'file-earmark-spreadsheet';
    if (type.includes('presentationml')) return 'file-earmark-slides';
    return 'file-earmark-text';
  }

  global.PMChatParts.formatSize = formatSize;

  // ----- composing --------------------------------------------------------------------------

  /** Adds the attach button and the waiting-files list to a window (when attachments are on). */
  ChatApp.prototype.buildAttach = function (win) {
    win.pending = [];
    if (!this.attachments.enabled) return;

    const picker = el('input');
    picker.type = 'file';
    picker.multiple = true;
    picker.accept = ACCEPT;
    picker.hidden = true;
    picker.tabIndex = -1;
    const button = iconButton('paperclip', 'Attach files', 'pm-chat__attach');
    button.type = 'button';
    button.addEventListener('click', () => picker.click());
    picker.addEventListener('change', () => {
      this.addFiles(win, Array.from(picker.files || []));
      picker.value = '';
    });

    const list = el('ul', 'pm-chat__pending');
    list.setAttribute('aria-label', 'Files to send');
    list.hidden = true;
    const note = el('p', 'pm-chat__pending-note');
    note.setAttribute('aria-live', 'polite');
    win.pendingList = list;
    win.pendingNote = note;
    win.attachButton = button;
    win.form.insertBefore(button, win.input);
    win.form.append(picker);
    win.node.insertBefore(list, win.form);
    win.node.insertBefore(note, win.form);
  };

  ChatApp.prototype.addFiles = function (win, files) {
    let note = '';
    for (const file of files) {
      if (win.pending.length >= this.attachments.maxFiles) {
        note = `A message can carry at most ${this.attachments.maxFiles} files.`;
        break;
      }
      const item = { key: newClientId(), file, name: file.name, size: file.size, progress: 0, xhr: null, attachment: null, error: null };
      win.pending.push(item);
      if (file.size > this.attachments.maxBytes) {
        item.error = `Larger than ${formatSize(this.attachments.maxBytes)}.`;
      } else {
        this.uploadFile(win, item);
      }
    }
    win.pendingNote.textContent = note;
    this.renderPending(win);
  };

  /** One upload, with progress (XMLHttpRequest: fetch cannot report upload progress). */
  ChatApp.prototype.uploadFile = function (startedIn, item) {
    // The window may close (to make room) and reopen while this runs; report to the current one.
    const view = () => this.windows.get(startedIn.peerId) || startedIn;
    const xhr = new global.XMLHttpRequest();
    item.xhr = xhr;
    item.error = null;
    item.progress = 0;
    xhr.open('POST', `${this.api}/attachments`);
    const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
    if (token) xhr.setRequestHeader('X-CSRF-TOKEN', token);
    xhr.setRequestHeader('Accept', 'application/json');
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        item.progress = event.loaded / event.total;
        this.renderPending(view());
      }
    };
    xhr.onload = () => {
      item.xhr = null;
      let body = null;
      try {
        body = JSON.parse(xhr.responseText || 'null');
      } catch (_) { /* not JSON */ }
      if (xhr.status === 200 && body && body.id) {
        item.attachment = body;
      } else if (xhr.status === 401 || (xhr.status === 404 && xhr.getResponseHeader('X-Chat-Disabled'))) {
        this.shutdown();
        return;
      } else {
        item.error = (body && body.detail) || (xhr.status === 404 ? 'Files cannot be sent at the moment.' : 'The file could not be sent.');
        this.announce(`${item.name} not sent. ${item.error}`);
      }
      this.renderPending(view());
    };
    xhr.onerror = () => {
      item.xhr = null;
      item.error = 'The file could not be uploaded. Check the connection and try again.';
      this.renderPending(view());
    };
    const data = new global.FormData();
    data.append('file', item.file, item.name);
    xhr.send(data);
    this.renderPending(view());
  };

  /** Takes a file off the list; one already uploaded is removed on the server, freeing the space. */
  ChatApp.prototype.dropPending = function (win, item) {
    if (item.xhr) item.xhr.abort();
    if (item.attachment) {
      this.request('POST', `/attachments/${item.attachment.id}/remove`, {}).catch(() => {});
    }
    win.pending = win.pending.filter((p) => p !== item);
    win.pendingNote.textContent = '';
    this.renderPending(win);
    win.input.focus();
  };

  ChatApp.prototype.renderPending = function (win) {
    const list = win.pendingList;
    if (!list) return;
    list.hidden = win.pending.length === 0;
    list.replaceChildren(...win.pending.map((item) => {
      const row = el('li', `pm-chat__pending-item${item.error ? ' is-failed' : ''}`);
      row.append(icon('file-earmark'));
      const text = el('span', 'pm-chat__pending-text');
      text.append(el('span', 'pm-chat__pending-name', item.name));
      let status;
      if (item.error) status = item.error;
      else if (item.xhr) status = `Uploading… ${Math.round(item.progress * 100)}%`;
      else status = formatSize(item.size);
      text.append(el('span', 'pm-chat__pending-status', status));
      row.append(text);
      if (item.error && item.size <= this.attachments.maxBytes) {
        const retry = el('button', 'btn btn-link btn-sm p-0', 'Retry');
        retry.type = 'button';
        retry.setAttribute('aria-label', `Retry ${item.name}`);
        retry.addEventListener('click', () => this.uploadFile(win, item));
        row.append(retry);
      }
      const remove = iconButton('x-lg', item.xhr ? `Cancel ${item.name}` : `Remove ${item.name}`);
      remove.addEventListener('click', () => this.dropPending(win, item));
      row.append(remove);
      return row;
    }));
  };

  /**
   * The files ready to go with the next message. busy: some are still uploading (the message
   * waits). Files that failed stay on the list for Retry or Remove.
   */
  ChatApp.prototype.readyFiles = function (win) {
    const pending = win.pending || [];
    return {
      busy: pending.some((p) => p.xhr),
      attachments: pending.filter((p) => p.attachment).map((p) => p.attachment),
    };
  };

  /** After a send: the files that went with it leave the list. */
  ChatApp.prototype.clearSentFiles = function (win, sent) {
    const ids = new Set(sent.map((a) => a.id));
    win.pending = (win.pending || []).filter((p) => !(p.attachment && ids.has(p.attachment.id)));
    if (win.pendingNote) win.pendingNote.textContent = '';
    this.renderPending(win);
  };

  ChatApp.prototype.composeNote = function (win, text) {
    if (win.pendingNote) win.pendingNote.textContent = text;
    this.announce(text);
  };

  // ----- files in messages --------------------------------------------------------------------

  ChatApp.prototype.attachmentUrl = function (attachment, thumbnail) {
    return `${this.api}/attachments/${attachment.id}${thumbnail ? '?thumbnail=true' : ''}`;
  };

  /** The files of a message: image thumbnails and file cards; removed ones say so. */
  ChatApp.prototype.attachmentNodes = function (win, message, mine) {
    const files = message.attachments || [];
    if (!files.length || message.isUnsent) return [];
    const box = el('div', 'pm-chat__files');
    for (const file of files) {
      if (file.isRemoved) {
        box.append(el('span', 'pm-chat__file-removed', 'File removed'));
        continue;
      }
      const wrap = el('div', 'pm-chat__file');
      if (file.isImage) {
        const link = el('a', 'pm-chat__thumb');
        link.href = this.attachmentUrl(file, false);
        link.target = '_blank';
        link.rel = 'noopener';
        link.setAttribute('aria-label', `Open image ${file.fileName}`);
        const image = el('img');
        image.src = this.attachmentUrl(file, true);
        image.alt = file.fileName;
        image.loading = 'lazy';
        if (file.width && file.height) {
          image.width = Math.min(220, file.width);
          image.height = Math.round(image.width * (file.height / file.width));
        }
        link.append(image);
        wrap.append(link);
      } else {
        const card = el('a', 'pm-chat__file-card');
        card.href = this.attachmentUrl(file, false);
        card.setAttribute('download', file.fileName);
        card.setAttribute('aria-label', `Download ${file.fileName}, ${formatSize(file.sizeBytes)}`);
        card.append(icon(fileIcon(file)));
        const text = el('span', 'pm-chat__file-text');
        text.append(el('span', 'pm-chat__file-name', file.fileName), el('span', 'pm-chat__file-size', formatSize(file.sizeBytes)));
        card.append(text);
        wrap.append(card);
      }
      if (mine && message.id) wrap.append(this.removeFileButton(win, message, file));
      box.append(wrap);
    }
    return [box];
  };

  /** "Remove" on one of your own sent files: a second click within five seconds removes it. */
  ChatApp.prototype.removeFileButton = function (win, message, file) {
    const armed = win.armedFile === file.id;
    const button = el('button', `btn btn-link btn-sm p-0 pm-chat__file-remove${armed ? ' text-danger' : ''}`, armed ? 'Remove?' : 'Remove');
    button.type = 'button';
    button.dataset.focusKey = `remove-file:${file.id}`;
    button.setAttribute('aria-label', armed ? `Confirm removing ${file.fileName}` : `Remove ${file.fileName} from this message`);
    button.addEventListener('click', async () => {
      if (!armed) {
        win.armedFile = file.id;
        if (win.armTimer) global.clearTimeout(win.armTimer);
        win.armTimer = global.setTimeout(() => {
          win.armedFile = 0;
          this.renderMessages(win, { stickToBottom: false });
        }, ARM_MS);
        this.renderMessages(win, { stickToBottom: false });
        return;
      }
      win.armedFile = 0;
      try {
        await this.request('POST', `/attachments/${file.id}/remove`, {});
        this.onAttachmentRemoved({ attachmentId: file.id, messageId: message.id });
      } catch (error) {
        this.composeNote(win, error.message);
        this.renderMessages(win, { stickToBottom: false });
      }
    });
    return button;
  };

  /**
   * A file was removed from a message (by its sender, here or in another tab or browser). The
   * removal is remembered and applied before every render (applyTombstones), so a history page or
   * send response already in flight, or a message held back while history loads, cannot bring
   * the file back.
   */
  ChatApp.prototype.onAttachmentRemoved = function (removed) {
    this.removedFiles.set(removed.attachmentId, { id: removed.attachmentId, isRemoved: true });
    const shows = (list) => list.some((m) => m.attachments && m.attachments.some((a) => a.id === removed.attachmentId && !a.isRemoved));
    this.windows.forEach((win) => {
      if (!shows(win.messages) && !shows(win.heldLive)) return;
      this.applyTombstones(win);
      this.renderMessages(win, { stickToBottom: false });
    });
  };
})(window);
