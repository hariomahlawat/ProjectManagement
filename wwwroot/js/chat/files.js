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
    ChatApp, ARM_MS, el, icon, iconButton, newClientId, focusKeyIn, refocus,
  } = global.PMChatParts;

  const ACCEPT = '.pdf,.docx,.xlsx,.pptx,.jpg,.jpeg,.png,.webp,.txt,.csv';
  // An upload that makes no progress for this long has stalled (a dropped connection the
  // browser has not noticed); after the last byte, the server checks the file (and makes a
  // thumbnail), which gets longer. Either way the file shows Retry instead of hanging.
  const STALL_MS = 60000;
  const CHECK_MS = 180000;

  /** What a removed file says in the conversation, by who or what removed it. */
  function removedText(reason) {
    if (reason === 'Admin') return 'File removed by an administrator';
    if (reason === 'Retention') return 'File removed automatically (old)';
    return 'File removed';
  }

  const timeFormat = new Intl.DateTimeFormat('en-IN', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'Asia/Kolkata' });
  const untilFormat = new Intl.DateTimeFormat('en-IN', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Kolkata' });

  /** "840 KB", "12.5 MB". */
  function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;
    if (bytes < 1024 * 1024 * 1024) {
      const mb = bytes / (1024 * 1024);
      return `${mb < 10 ? mb.toFixed(1) : Math.round(mb)} MB`;
    }
    return `${(bytes / (1024 * 1024 * 1024)).toFixed(1).replace(/\.0$/, '')} GB`;
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

  const ALLOWED = ACCEPT.split(',');

  function releasePreview(item) {
    if (item.preview && global.URL && typeof global.URL.revokeObjectURL === 'function') {
      global.URL.revokeObjectURL(item.preview);
    }
    item.preview = null;
  }
  const REFUSED_TYPE = 'Only PDF, Word, Excel, PowerPoint, images (.jpg, .png, .webp), text and CSV files can be sent.';

  /** Why a file cannot be sent, checked before uploading (the server checks again), or null. */
  function refusal(file, maxBytes) {
    const name = (file.name || '').toLowerCase();
    const dot = name.lastIndexOf('.');
    if (dot < 0 || !ALLOWED.includes(name.slice(dot))) return REFUSED_TYPE;
    if (file.size === 0) return 'The file is empty.';
    if (file.size > maxBytes) return `Larger than ${formatSize(maxBytes)}.`;
    return null;
  }

  const shotFormat = new Intl.DateTimeFormat('en-IN', {
    day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Kolkata',
  });

  /**
   * A pasted screenshot arrives as "image.png" (or with no name): give it one that says what it
   * is and when, e.g. "Screenshot 18 Nov 2026 14.32.png". Named files keep their names.
   */
  function namePasted(file, now) {
    const name = file.name || '';
    const generic = !name || /^image\.(png|jpe?g|webp)$/i.test(name);
    if (!generic || !/^image\/(png|jpeg|webp)$/.test(file.type || '')) return file;
    const extension = file.type === 'image/jpeg' ? 'jpg' : file.type.slice('image/'.length);
    const stamp = shotFormat.format(now || new Date()).replace(',', '').replace(':', '.');
    try {
      return new global.File([file], `Screenshot ${stamp}.${extension}`, { type: file.type, lastModified: file.lastModified });
    } catch (_) {
      return file; // a browser without the File constructor: the server accepts "image.png" too
    }
  }

  /** Whether a drag carries files (not text or a link being dragged around the page). */
  function carriesFiles(event) {
    const types = event.dataTransfer && event.dataTransfer.types;
    return !!types && Array.prototype.indexOf.call(types, 'Files') >= 0;
  }

  /**
   * The files of a drop. Folders cannot be sent: the browser offers one as a file without a
   * type, which reading would fail on, so they are counted and left out.
   */
  function droppedFiles(dataTransfer) {
    const files = [];
    let folders = 0;
    const items = dataTransfer.items ? Array.from(dataTransfer.items) : [];
    if (items.length && items.some((item) => typeof item.webkitGetAsEntry === 'function')) {
      for (const item of items) {
        if (item.kind !== 'file') continue;
        const entry = item.webkitGetAsEntry();
        if (entry && entry.isDirectory) {
          folders += 1;
          continue;
        }
        const file = item.getAsFile();
        if (file) files.push(file);
      }
    } else {
      files.push(...Array.from(dataTransfer.files || []));
    }
    return { files, folders };
  }

  global.PMChatParts.namePasted = namePasted;
  global.PMChatParts.fileRefusal = refusal;

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

    // Paste: a screenshot or files copied in Explorer are attached; text pastes as text.
    win.input.addEventListener('paste', (event) => {
      const clip = event.clipboardData;
      const files = clip ? Array.from(clip.files || []) : [];
      if (!files.length || win.form.hidden) return;
      // Copying from Word or a web page carries text and a picture of it: that is a text paste.
      if (clip.getData && clip.getData('text/plain')) return;
      event.preventDefault();
      const now = new Date();
      this.addFiles(win, files.map((file) => namePasted(file, now)));
    });

    this.bindDrop(win.node, () => (win.form.hidden ? null : win), win);
  };

  /**
   * Dropping files on `zone` attaches them to the conversation `target()` names (null: not
   * now, e.g. a deactivated contact). `win` (when given) shows the "Drop to attach" overlay.
   */
  ChatApp.prototype.bindDrop = function (zone, target, win) {
    let depth = 0; // dragenter/leave fire for every child crossed; count to know when it left
    const overlay = win ? el('div', 'pm-chat__drop') : null;
    if (overlay) {
      overlay.setAttribute('aria-hidden', 'true');
      overlay.append(icon('cloud-arrow-up'), el('span', null, 'Drop files to attach'));
      overlay.hidden = true;
      zone.append(overlay);
    }
    const show = (on) => {
      if (overlay) overlay.hidden = !on;
      zone.classList.toggle('is-dropping', on);
    };
    zone.addEventListener('dragenter', (event) => {
      if (!carriesFiles(event) || !target(event)) return;
      event.preventDefault();
      depth += 1;
      show(true);
    });
    zone.addEventListener('dragover', (event) => {
      if (!carriesFiles(event) || !target(event)) return;
      event.preventDefault(); // without this the browser opens the file instead of dropping it
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
    });
    zone.addEventListener('dragleave', () => {
      depth = Math.max(0, depth - 1);
      if (!depth) show(false);
    });
    zone.addEventListener('drop', (event) => {
      depth = 0;
      show(false);
      const into = carriesFiles(event) ? target(event) : null;
      if (!into) return;
      event.preventDefault();
      event.stopPropagation();
      const { files, folders } = droppedFiles(event.dataTransfer);
      const destination = typeof into === 'function' ? into() : into;
      if (!destination) return;
      if (files.length) this.addFiles(destination, files);
      if (folders) {
        this.composeNote(destination, 'Folders cannot be sent. Open the folder and drop the files themselves.');
      }
      if (!destination.minimized) destination.input.focus({ preventScroll: true });
    });
  };

  /**
   * Files dropped on a person or group in the contact list open that conversation with them
   * attached (people with inactive accounts cannot be sent anything).
   */
  ChatApp.prototype.bindContactDrop = function (container) {
    if (!this.attachments.enabled || !container) return;
    const keyAt = (event) => {
      const button = event.target && event.target.closest ? event.target.closest('[data-focus-key]') : null;
      const focusKey = button ? button.dataset.focusKey : '';
      if (focusKey.startsWith('group:')) return `g:${focusKey.slice('group:'.length)}`;
      if (!focusKey.startsWith('contact:')) return null;
      const contact = this.contacts.get(focusKey.slice('contact:'.length));
      return contact && contact.isActive ? contact.userId : null;
    };
    let marked = null;
    const mark = (button) => {
      if (marked && marked !== button) marked.classList.remove('is-drop-target');
      marked = button;
      if (button) button.classList.add('is-drop-target');
    };
    container.addEventListener('dragover', (event) => {
      const key = carriesFiles(event) ? keyAt(event) : null;
      if (!key) {
        mark(null);
        return;
      }
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
      mark(event.target.closest('[data-focus-key]'));
    });
    container.addEventListener('dragleave', (event) => {
      if (!container.contains(event.relatedTarget)) mark(null);
    });
    container.addEventListener('drop', (event) => {
      mark(null);
      const key = carriesFiles(event) ? keyAt(event) : null;
      if (!key) return;
      event.preventDefault();
      const { files, folders } = droppedFiles(event.dataTransfer);
      const win = this.openWindow(key, { focus: true });
      if (!win || !win.pendingList || win.form.hidden) return;
      if (files.length) this.addFiles(win, files);
      if (folders) this.composeNote(win, 'Folders cannot be sent. Open the folder and drop the files themselves.');
    });
  };

  ChatApp.prototype.addFiles = function (win, files) {
    let note = '';
    for (const file of files) {
      if (win.pending.length >= this.attachments.maxFiles) {
        note = `A message can carry at most ${this.attachments.maxFiles} files.`;
        break;
      }
      const item = { key: newClientId(), file, name: file.name, size: file.size, progress: 0, xhr: null, attachment: null, error: null, refused: false, preview: null };
      win.pending.push(item);
      const refused = refusal(file, this.attachments.maxBytes);
      if (refused) {
        // Not uploaded at all: no Retry, only Remove.
        item.error = refused;
        item.refused = true;
      } else {
        if (/^image\/(png|jpeg|webp)$/.test(file.type || '') && global.URL && typeof global.URL.createObjectURL === 'function') {
          item.preview = global.URL.createObjectURL(file); // shown while it waits; released when it goes
        }
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
    const stop = () => {
      if (item.stallTimer) global.clearTimeout(item.stallTimer);
      item.stallTimer = null;
    };
    const watch = (ms) => {
      stop();
      item.stallTimer = global.setTimeout(() => {
        item.stallTimer = null;
        if (item.xhr !== xhr) return;
        item.stalled = true;
        xhr.abort();
      }, ms);
    };
    xhr.upload.onprogress = (event) => {
      watch(STALL_MS);
      if (event.lengthComputable) {
        item.progress = event.loaded / event.total;
        this.renderPending(view());
      }
    };
    xhr.upload.onload = () => watch(CHECK_MS);
    xhr.onabort = () => {
      stop();
      if (!item.stalled || item.xhr !== xhr) return; // Cancel: the item is already gone
      item.stalled = false;
      item.xhr = null;
      item.error = 'The upload stopped. Check the connection and try again.';
      this.announce(`${item.name} not sent. ${item.error}`);
      this.renderPending(view());
    };
    xhr.onload = () => {
      stop();
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
      stop();
      item.xhr = null;
      item.error = 'The file could not be uploaded. Check the connection and try again.';
      this.renderPending(view());
    };
    const data = new global.FormData();
    data.append('file', item.file, item.name);
    item.stalled = false;
    watch(STALL_MS);
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
    releasePreview(item);
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
      if (item.preview) {
        const image = el('img', 'pm-chat__pending-thumb');
        image.src = item.preview;
        image.alt = '';
        row.append(image);
      } else {
        row.append(icon(item.refused ? 'file-earmark-x' : 'file-earmark'));
      }
      const text = el('span', 'pm-chat__pending-text');
      text.append(el('span', 'pm-chat__pending-name', item.name));
      let status;
      if (item.error) status = item.error;
      else if (item.xhr) status = `Uploading… ${Math.round(item.progress * 100)}%`;
      else status = formatSize(item.size);
      text.append(el('span', 'pm-chat__pending-status', status));
      row.append(text);
      if (item.error && !item.refused) {
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
    win.pending = (win.pending || []).filter((p) => {
      const gone = p.attachment && ids.has(p.attachment.id);
      if (gone) releasePreview(p);
      return !gone;
    });
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
        box.append(el('span', 'pm-chat__file-removed', removedText(file.removalReason)));
        continue;
      }
      const wrap = el('div', 'pm-chat__file');
      if (file.isImage) {
        const link = el('a', 'pm-chat__thumb');
        link.href = this.attachmentUrl(file, false);
        link.target = '_blank';
        link.rel = 'noopener';
        link.setAttribute('aria-label', `Open image ${file.fileName}`);
        link.dataset.focusKey = `thumb:${file.id}`;
        const image = el('img');
        image.src = this.attachmentUrl(file, true);
        image.alt = file.fileName;
        image.loading = 'lazy';
        if (file.width && file.height) {
          image.width = Math.min(220, file.width);
          image.height = Math.round(image.width * (file.height / file.width));
        }
        link.append(image);
        // A plain click opens the viewer; Ctrl/Shift/middle click still opens a new tab.
        link.addEventListener('click', (event) => {
          if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
          event.preventDefault();
          this.openViewer(win, file.id, link);
        });
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
    this.fileEventAt.set(removed.attachmentId, ++this.fileEventSeq);
    this.restoredFiles.delete(removed.attachmentId);
    this.removedFiles.set(removed.attachmentId, { id: removed.attachmentId, isRemoved: true, removalReason: removed.removalReason || null });
    const shows = (list) => list.some((m) => m.attachments && m.attachments.some((a) => a.id === removed.attachmentId && !a.isRemoved));
    this.windows.forEach((win) => {
      if (!shows(win.messages) && !shows(win.heldLive)) return;
      this.applyTombstones(win);
      this.renderMessages(win, { stickToBottom: false });
    });
    if (this.filesPanelOpen) this.filesPanelOpen.refresh();
  };

  /** A removed file came back (restored within the undo window). */
  ChatApp.prototype.onAttachmentRestored = function (restored) {
    const file = restored.attachment;
    // Remembered like a removal, so a history page or send response still in flight (or a
    // message not loaded yet) shows the file too.
    this.fileEventAt.set(file.id, ++this.fileEventSeq);
    this.removedFiles.delete(file.id);
    this.restoredFiles.set(file.id, file);
    const bringBack = (list) => {
      let changed = false;
      const next = list.map((message) => {
        if (message.id !== restored.messageId || !message.attachments) return message;
        if (!message.attachments.some((a) => a.id === file.id)) return message;
        changed = true;
        return { ...message, attachments: message.attachments.map((a) => (a.id === file.id ? file : a)) };
      });
      return changed ? next : null;
    };
    this.windows.forEach((win) => {
      const held = bringBack(win.heldLive);
      if (held) win.heldLive = held;
      const messages = bringBack(win.messages);
      if (!messages) return;
      win.messages = messages;
      this.renderMessages(win, { stickToBottom: false });
    });
  };

  // ----- My files ---------------------------------------------------------------------------

  /**
   * My files: the files I sent, with how much of my allowance they use. Remove a selection or
   * everything older than an age (each a second click to confirm), restore within the undo
   * window; files an administrator removed say so and are restored by an administrator.
   * Returns { node, refresh }. openConversation(key) opens a file's chat.
   */
  ChatApp.prototype.myFilesPanel = function (openConversation) {
    const node = el('section', 'pm-chat-files');
    node.setAttribute('aria-labelledby', 'pmChatFilesTitle');
    const title = el('h2', 'pm-chat-files__title', 'My files');
    title.id = 'pmChatFilesTitle';
    const lede = el('p', 'pm-chat-files__lede', 'Files you sent in chat. Only what you keep counts against your allowance; a removed file can be restored for a day.');
    const usage = el('div', 'pm-chat-files__usage');
    const meter = el('div', 'pm-chat-files__meter');
    meter.setAttribute('role', 'progressbar');
    meter.setAttribute('aria-valuemin', '0');
    meter.setAttribute('aria-valuemax', '100');
    const fill = el('span', 'pm-chat-files__meter-fill');
    meter.append(fill);
    const usageText = el('span', 'pm-chat-files__usage-text');
    usage.append(meter, usageText);

    const tools = el('div', 'pm-chat-files__tools');
    const sort = el('select', 'form-select form-select-sm');
    sort.setAttribute('aria-label', 'Sort files');
    for (const [value, label] of [['newest', 'Newest first'], ['largest', 'Largest first']]) {
      const option = el('option', null, label);
      option.value = value;
      sort.append(option);
    }
    const removeSelected = el('button', 'btn btn-outline-danger btn-sm');
    removeSelected.type = 'button';
    const ageSelect = el('select', 'form-select form-select-sm');
    ageSelect.setAttribute('aria-label', 'Remove files older than');
    for (const days of [30, 90, 180, 365]) {
      const option = el('option', null, days === 365 ? 'Older than a year' : `Older than ${days} days`);
      option.value = String(days);
      ageSelect.append(option);
    }
    const removeOlder = el('button', 'btn btn-outline-danger btn-sm');
    removeOlder.type = 'button';
    tools.append(sort, removeSelected, ageSelect, removeOlder);

    const note = el('p', 'pm-chat-files__note');
    note.setAttribute('aria-live', 'polite');
    const list = el('ul', 'pm-chat-files__list');
    list.setAttribute('aria-label', 'Your files');
    const empty = el('p', 'pm-chat-page__empty', 'Loading…');
    node.append(title, lede, usage, tools, note, list, empty);

    const state = { files: [], selected: new Set(), armed: '', armTimer: null };
    const arm = (which) => {
      state.armed = which;
      if (state.armTimer) global.clearTimeout(state.armTimer);
      state.armTimer = global.setTimeout(() => { state.armed = ''; draw(); }, ARM_MS);
      draw();
    };

    const change = async (path, body, done) => {
      state.armed = '';
      try {
        const result = await this.request('POST', `/attachments/mine/${path}`, body);
        note.textContent = done(result);
        this.announce(note.textContent);
        state.selected.clear();
      } catch (error) {
        note.textContent = error.message;
        this.announce(error.message);
      }
      await refresh();
    };

    removeSelected.addEventListener('click', () => {
      if (!state.selected.size) return;
      if (state.armed !== 'selected') return arm('selected');
      change('remove', { ids: Array.from(state.selected) },
        (r) => `Removed ${r.files} ${r.files === 1 ? 'file' : 'files'} (${formatSize(r.bytes)}). You can restore them until ${untilFormat.format(new Date(r.restorableUntil))}.`);
    });
    removeOlder.addEventListener('click', () => {
      if (state.armed !== 'older') return arm('older');
      change('remove-older', { days: Number(ageSelect.value) },
        (r) => `Removed ${r.files} ${r.files === 1 ? 'file' : 'files'} (${formatSize(r.bytes)}). You can restore them until ${untilFormat.format(new Date(r.restorableUntil))}.`);
    });
    sort.addEventListener('change', () => draw());
    ageSelect.addEventListener('change', () => { state.armed = ''; draw(); });

    const draw = () => {
      const kept = state.files.filter((f) => !f.removedUtc);
      removeSelected.textContent = state.armed === 'selected'
        ? `Remove ${state.selected.size} selected?`
        : `Remove selected (${state.selected.size})`;
      removeSelected.disabled = state.selected.size === 0;
      removeOlder.textContent = state.armed === 'older' ? 'Remove them?' : 'Remove';
      const ordered = state.files.slice().sort(sort.value === 'largest'
        ? (a, b) => b.sizeBytes - a.sizeBytes
        : (a, b) => Date.parse(b.sentUtc) - Date.parse(a.sentUtc));
      const focused = focusKeyIn(list);
      list.replaceChildren(...ordered.map((file) => {
        const row = el('li', `pm-chat-files__item${file.removedUtc ? ' is-removed' : ''}`);
        if (!file.removedUtc) {
          const box = el('input', 'form-check-input');
          box.type = 'checkbox';
          box.checked = state.selected.has(file.id);
          box.dataset.focusKey = `file:${file.id}`;
          box.setAttribute('aria-label', `Select ${file.fileName}`);
          box.addEventListener('change', () => {
            if (box.checked) state.selected.add(file.id); else state.selected.delete(file.id);
            state.armed = '';
            draw();
          });
          row.append(box);
        } else {
          row.append(el('span', 'pm-chat-files__spacer'));
        }
        row.append(icon(file.isImage ? 'file-earmark-image' : fileIcon(file)));
        const text = el('span', 'pm-chat-files__text');
        text.append(el('span', 'pm-chat-files__name', file.fileName));
        const where = el('button', 'btn btn-link btn-sm p-0 pm-chat-files__where', file.conversationName);
        where.type = 'button';
        where.setAttribute('aria-label', `Open the chat with ${file.conversationName}`);
        where.addEventListener('click', () => openConversation(file.conversationKey));
        const meta = el('span', 'pm-chat-files__meta', `${formatSize(file.sizeBytes)} · ${timeFormat.format(new Date(file.sentUtc))} · `);
        meta.append(where);
        text.append(meta);
        row.append(text);
        if (file.removedUtc) {
          if (file.restorableUntil) {
            const restore = el('button', 'btn btn-sm btn-outline-secondary', 'Restore');
            restore.type = 'button';
            restore.dataset.focusKey = `restore:${file.id}`;
            restore.setAttribute('aria-label', `Restore ${file.fileName}, possible until ${untilFormat.format(new Date(file.restorableUntil))}`);
            restore.addEventListener('click', () => change('restore', { ids: [file.id] }, () => `Restored ${file.fileName}.`));
            row.append(el('span', 'pm-chat-files__status', `Removed · restore until ${untilFormat.format(new Date(file.restorableUntil))}`), restore);
          } else {
            row.append(el('span', 'pm-chat-files__status', removedText(file.removalReason)));
          }
        }
        return row;
      }));
      refocus(list, focused);
      empty.hidden = state.files.length > 0;
      empty.textContent = 'You have not sent any files.';
      removeSelected.hidden = kept.length === 0;
    };

    const refresh = async () => {
      try {
        const mine = await this.request('GET', '/attachments/mine');
        state.files = mine.files || [];
        const live = new Set(state.files.filter((f) => !f.removedUtc).map((f) => f.id));
        state.selected.forEach((id) => { if (!live.has(id)) state.selected.delete(id); });
        const percent = mine.allowanceBytes ? Math.min(100, Math.round((mine.usedBytes / mine.allowanceBytes) * 100)) : 0;
        fill.style.width = `${percent}%`;
        meter.classList.toggle('is-high', percent >= 90);
        meter.setAttribute('aria-valuenow', String(percent));
        meter.setAttribute('aria-label', `${percent}% of your allowance used`);
        usageText.textContent = `${formatSize(mine.usedBytes)} of ${formatSize(mine.allowanceBytes)} used`;
      } catch (error) {
        empty.textContent = error.message;
      }
      draw();
    };

    const panel = { node, refresh };
    this.filesPanelOpen = panel;
    return panel;
  };
  // ----- image viewer ---------------------------------------------------------------------

  /** The images of a conversation that can still be opened, oldest first. */
  ChatApp.prototype.viewerImages = function (win) {
    const images = [];
    for (const message of win.messages) {
      if (message.isUnsent || !message.id) continue;
      for (const file of message.attachments || []) {
        if (file.isImage && !file.isRemoved) images.push({ file, message });
      }
    }
    return images;
  };

  /**
   * Shows an image of the conversation full size, over the page: arrows (or ← →) step through
   * the conversation's other images, Download and "Open in new tab" offer the original, Esc or
   * the backdrop closes it and focus returns to the thumbnail that opened it.
   */
  ChatApp.prototype.openViewer = function (win, attachmentId, opener) {
    const images = this.viewerImages(win);
    let index = images.findIndex((entry) => entry.file.id === attachmentId);
    if (index < 0) return;
    this.closeViewer();

    const dialog = el('div', 'pm-chat-viewer');
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');
    const caption = el('p', 'pm-chat-viewer__caption');
    caption.id = `pm-chat-viewer-caption-${newClientId()}`;
    dialog.setAttribute('aria-labelledby', caption.id);
    const figure = el('div', 'pm-chat-viewer__stage');
    const image = el('img', 'pm-chat-viewer__image');
    const missing = el('p', 'pm-chat-viewer__missing', 'This image is no longer available. It may have been removed.');
    missing.hidden = true;
    image.addEventListener('error', () => {
      image.hidden = true;
      missing.hidden = false;
    });
    figure.append(image, missing);

    const previous = iconButton('chevron-left', 'Previous image', 'pm-chat-viewer__nav is-previous');
    const next = iconButton('chevron-right', 'Next image', 'pm-chat-viewer__nav is-next');
    const download = el('a', 'pm-chat-viewer__action');
    download.append(icon('download'), el('span', null, 'Download'));
    const open = el('a', 'pm-chat-viewer__action');
    open.target = '_blank';
    open.rel = 'noopener';
    open.append(icon('box-arrow-up-right'), el('span', null, 'Open in new tab'));
    const close = iconButton('x-lg', 'Close image', 'pm-chat-viewer__close');
    const bar = el('div', 'pm-chat-viewer__bar');
    bar.append(caption, download, open, close);
    dialog.append(bar, figure, previous, next);

    const show = () => {
      const { file, message } = images[index];
      image.hidden = false;
      missing.hidden = true;
      image.src = this.attachmentUrl(file, false);
      image.alt = file.fileName;
      const who = this.nameOf(message.senderUserId, win);
      const position = images.length > 1 ? ` · ${index + 1} of ${images.length}` : '';
      caption.textContent = `${file.fileName} · ${who}${position}`;
      download.href = this.attachmentUrl(file, false);
      download.setAttribute('download', file.fileName);
      download.setAttribute('aria-label', `Download ${file.fileName}`);
      open.href = this.attachmentUrl(file, false);
      open.setAttribute('aria-label', `Open ${file.fileName} in a new tab`);
      previous.hidden = index === 0;
      next.hidden = index === images.length - 1;
    };
    const step = (by) => {
      const to = index + by;
      if (to < 0 || to >= images.length) return;
      index = to;
      show();
    };
    previous.addEventListener('click', () => step(-1));
    next.addEventListener('click', () => step(1));
    close.addEventListener('click', () => this.closeViewer());
    dialog.addEventListener('click', (event) => {
      if (event.target === dialog || event.target === figure) this.closeViewer();
    });
    dialog.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation(); // not also the window's Esc (minimise)
        this.closeViewer();
      } else if (event.key === 'ArrowLeft') {
        step(-1);
      } else if (event.key === 'ArrowRight') {
        step(1);
      } else if (event.key === 'Tab') {
        // Keep focus inside while it is open (aria-modal).
        const stops = Array.from(dialog.querySelectorAll('a[href], button')).filter((node) => !node.hidden);
        if (!stops.length) return;
        const first = stops[0];
        const last = stops[stops.length - 1];
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault();
          last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault();
          first.focus();
        }
      }
    });

    show();
    document.body.append(dialog);
    this.viewer = { dialog, opener, win, current: () => images[index].file.id };
    close.focus();
  };

  ChatApp.prototype.closeViewer = function () {
    const viewer = this.viewer;
    if (!viewer) return;
    this.viewer = null;
    viewer.dialog.remove();
    // Back to the thumbnail of the image last shown (the log may have been redrawn meanwhile,
    // replacing the one that opened the viewer), else the one that opened it.
    const shown = viewer.win && viewer.win.log
      ? viewer.win.log.querySelector(`[data-focus-key="thumb:${viewer.current()}"]`)
      : null;
    const opener = shown || (viewer.opener && viewer.opener.isConnected ? viewer.opener : null);
    if (opener) opener.focus({ preventScroll: true });
    else if (viewer.win && !viewer.win.form.hidden) viewer.win.input.focus({ preventScroll: true });
  };
})(window);
