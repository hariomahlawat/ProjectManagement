(function () {
  'use strict';

  const DEFAULT_POLL_INTERVAL = 60_000;
  const MAX_STORE_SIZE = 200;

  function onReady(callback) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', callback, { once: true });
    } else {
      callback();
    }
  }

  function parseJsonAttribute(value) {
    if (!value) {
      return [];
    }

    try {
      const parsed = JSON.parse(value);
      if (Array.isArray(parsed)) {
        return parsed;
      }

      return [];
    } catch (error) {
      console.warn('Unable to parse notifications JSON:', error);
      return [];
    }
  }

  function normalizeNotification(raw) {
    if (!raw) {
      return null;
    }

    const createdUtc = typeof raw.createdUtc === 'string' ? raw.createdUtc : null;
    const createdAt = createdUtc ? new Date(createdUtc) : new Date();
    const readUtc = typeof raw.readUtc === 'string' ? raw.readUtc : null;
    const seenUtc = typeof raw.seenUtc === 'string' ? raw.seenUtc : null;
    const readAt = readUtc ? new Date(readUtc) : null;

    return {
      id: Number.parseInt(raw.id, 10),
      module: raw.module ?? null,
      eventType: raw.eventType ?? null,
      scopeType: raw.scopeType ?? null,
      scopeId: raw.scopeId ?? null,
      projectId: typeof raw.projectId === 'number' ? raw.projectId : null,
      projectName: raw.projectName ?? null,
      actorUserId: raw.actorUserId ?? null,
      route: raw.route ?? null,
      title: raw.title ?? null,
      summary: raw.summary ?? null,
      createdUtc: createdUtc ?? createdAt.toISOString(),
      createdAt,
      seenUtc,
      readUtc,
      readAt,
      isProjectMuted: Boolean(raw.isProjectMuted),
      isRead: Boolean(readUtc),
    };
  }

  function sortByCreatedDesc(a, b) {
    return b.createdAt.getTime() - a.createdAt.getTime();
  }

  function dedupeNotifications(notifications) {
    const map = new Map();
    notifications.forEach(notification => {
      const normalised = normalizeNotification(notification);
      if (!normalised || !Number.isFinite(normalised.id)) {
        return;
      }

      const existing = map.get(normalised.id);
      if (!existing || existing.createdAt < normalised.createdAt) {
        map.set(normalised.id, normalised);
      }
    });

    return Array.from(map.values()).sort(sortByCreatedDesc);
  }

  class NotificationsApp {
    constructor(config) {
      this.apiBase = config.apiBase;
      this.unreadUrl = config.unreadUrl;
      this.hubUrl = config.hubUrl;
      this.isAuthenticated = config.isAuthenticated;
      this.fetchLimit = config.fetchLimit;
      this.pollInterval = config.pollInterval ?? DEFAULT_POLL_INTERVAL;
      this.storeLimit = config.storeLimit ?? MAX_STORE_SIZE;
      this.listeners = new Set();
      this.items = new Map();
      this.unreadCount = config.initialUnread ?? 0;
      this.pollTimer = null;
      this.hubConnection = null;
      this.notificationCenterUrl = config.notificationCenterUrl;

      const initialNotifications = dedupeNotifications(config.initialNotifications ?? []);
      initialNotifications.slice(0, this.storeLimit).forEach(notification => {
        this.items.set(notification.id, notification);
      });
    }

    start() {
      if (!this.isAuthenticated) {
        return;
      }

      this.refresh().catch(error => console.warn('Failed to refresh notifications:', error));

      this.startHub().then(connected => {
        if (!connected) {
          this.ensurePolling();
        }
      });
    }

    getSnapshot() {
      return Array.from(this.items.values()).sort(sortByCreatedDesc);
    }

    register(listener) {
      this.listeners.add(listener);
      listener.update(this.getSnapshot(), this.unreadCount);
    }

    notifyListeners() {
      const snapshot = this.getSnapshot();
      this.listeners.forEach(listener => listener.update(snapshot, this.unreadCount));
    }

    async refresh() {
      if (!this.apiBase) {
        return;
      }

      const params = new URLSearchParams();
      if (Number.isFinite(this.fetchLimit) && this.fetchLimit > 0) {
        params.set('limit', String(this.fetchLimit));
      }

      const url = params.size > 0 ? `${this.apiBase}?${params}` : this.apiBase;
      const response = await fetch(url, {
        headers: { Accept: 'application/json' },
        credentials: 'same-origin',
      });

      if (!response.ok) {
        throw new Error(`Notifications request failed with status ${response.status}`);
      }

      const data = await response.json();
      this.replaceStore(data);
      await this.fetchUnreadCount();
    }

    replaceStore(notifications) {
      this.items.clear();
      const normalised = dedupeNotifications(notifications).slice(0, this.storeLimit);
      normalised.forEach(notification => {
        this.items.set(notification.id, notification);
      });
      this.notifyListeners();
    }

    mergeStore(notifications) {
      const normalised = dedupeNotifications(notifications);
      normalised.forEach(notification => {
        this.items.set(notification.id, notification);
      });
      this.trimStore();
      this.notifyListeners();
    }

    trimStore() {
      const snapshot = this.getSnapshot();
      if (snapshot.length <= this.storeLimit) {
        return;
      }

      const toKeep = snapshot.slice(0, this.storeLimit);
      this.items.clear();
      toKeep.forEach(notification => this.items.set(notification.id, notification));
    }

    async fetchUnreadCount() {
      if (!this.unreadUrl) {
        return;
      }

      try {
        const response = await fetch(this.unreadUrl, {
          headers: { Accept: 'application/json' },
          credentials: 'same-origin',
        });

        if (!response.ok) {
          throw new Error(`Unread count request failed with status ${response.status}`);
        }

        const payload = await response.json();
        if (typeof payload?.count === 'number') {
          this.unreadCount = payload.count;
          this.notifyListeners();
        }
      } catch (error) {
        console.warn('Failed to fetch unread count:', error);
      }
    }

    async markRead(ids) {
      if (!this.apiBase || !Array.isArray(ids)) {
        return 0;
      }

      const uniqueIds = Array.from(new Set(ids.map(id => Number.parseInt(id, 10)).filter(Number.isFinite)));
      if (uniqueIds.length === 0) {
        return 0;
      }

      const requests = uniqueIds.map(id => fetch(`${this.apiBase}/${id}/read`, {
        method: 'POST',
        credentials: 'same-origin',
      }));

      const results = await Promise.allSettled(requests);
      const failed = results.find(result => result.status === 'fulfilled' ? !result.value.ok && result.value.status !== 404 : true);
      if (failed) {
        throw new Error('Failed to mark notifications as read.');
      }

      const now = new Date();
      const nowIso = now.toISOString();
      uniqueIds.forEach(id => {
        const existing = this.items.get(id);
        if (existing) {
          existing.readUtc = nowIso;
          existing.readAt = now;
          existing.isRead = true;
        }
      });

      await this.fetchUnreadCount();
      this.notifyListeners();
      return uniqueIds.length;
    }

    async markUnread(ids) {
      if (!this.apiBase || !Array.isArray(ids)) {
        return 0;
      }

      const uniqueIds = Array.from(new Set(ids.map(id => Number.parseInt(id, 10)).filter(Number.isFinite)));
      if (uniqueIds.length === 0) {
        return 0;
      }

      const requests = uniqueIds.map(id => fetch(`${this.apiBase}/${id}/read`, {
        method: 'DELETE',
        credentials: 'same-origin',
      }));

      const results = await Promise.allSettled(requests);
      const failed = results.find(result => result.status === 'fulfilled' ? !result.value.ok && result.value.status !== 404 : true);
      if (failed) {
        throw new Error('Failed to mark notifications as unread.');
      }

      uniqueIds.forEach(id => {
        const existing = this.items.get(id);
        if (existing) {
          existing.readUtc = null;
          existing.readAt = null;
          existing.isRead = false;
        }
      });

      await this.fetchUnreadCount();
      this.notifyListeners();
      return uniqueIds.length;
    }

    async markAllRead() {
      const unreadIds = this.getSnapshot()
        .filter(notification => !notification.isRead)
        .map(notification => notification.id);

      if (unreadIds.length === 0) {
        return 0;
      }

      return this.markRead(unreadIds);
    }

    async muteProject(projectId, muted) {
      if (!this.apiBase || !Number.isFinite(projectId)) {
        return false;
      }

      const url = `${this.apiBase}/projects/${projectId}/mute`;
      const response = await fetch(url, {
        method: muted ? 'POST' : 'DELETE',
        credentials: 'same-origin',
      });

      if (!response.ok && response.status !== 404) {
        throw new Error('Failed to update project mute preference.');
      }

      this.items.forEach(item => {
        if (item.projectId === projectId) {
          item.isProjectMuted = muted;
        }
      });

      this.notifyListeners();
      return true;
    }

    getItem(id) {
      const numericId = Number.parseInt(id, 10);
      return Number.isFinite(numericId) ? this.items.get(numericId) ?? null : null;
    }

    ensurePolling() {
      if (this.pollTimer || !this.isAuthenticated) {
        return;
      }

      this.pollTimer = window.setInterval(() => {
        this.refresh().catch(error => console.warn('Polling refresh failed:', error));
      }, this.pollInterval);
    }

    stopPolling() {
      if (this.pollTimer) {
        window.clearInterval(this.pollTimer);
        this.pollTimer = null;
      }
    }

    async startHub() {
      const signalR = window.signalR;
      if (!this.hubUrl || !signalR || typeof signalR.HubConnectionBuilder !== 'function') {
        return false;
      }

      const connection = new signalR.HubConnectionBuilder()
        .withUrl(this.hubUrl)
        .withAutomaticReconnect()
        .build();

      connection.on('ReceiveUnreadCount', count => {
        if (typeof count === 'number') {
          this.unreadCount = count;
          this.notifyListeners();
        }
      });

      connection.on('ReceiveNotifications', notifications => {
        this.mergeStore(Array.isArray(notifications) ? notifications : []);
      });

      connection.on('ReceiveNotification', notification => {
        if (!notification) {
          return;
        }

        this.mergeStore([notification]);
      });

      connection.onreconnecting(() => {
        this.ensurePolling();
      });

      connection.onreconnected(() => {
        this.stopPolling();
        this.requestHubSync(connection);
      });

      connection.onclose(() => {
        this.hubConnection = null;
        this.ensurePolling();
      });

      try {
        await connection.start();
        this.hubConnection = connection;
        this.stopPolling();
        await this.requestHubSync(connection);
        return true;
      } catch (error) {
        console.warn('Failed to connect to notifications hub:', error);
        this.hubConnection = null;
        return false;
      }
    }

    async requestHubSync(connection) {
      if (!connection) {
        return;
      }

      try {
        if (Number.isFinite(this.fetchLimit) && this.fetchLimit > 0) {
          await connection.invoke('RequestRecentNotifications', this.fetchLimit);
        } else {
          await connection.invoke('RequestRecentNotifications');
        }
        await connection.invoke('RequestUnreadCount');
      } catch (error) {
        console.warn('Failed to request updates from notifications hub:', error);
      }
    }
  }

  class NotificationBellView {
    constructor(element, app) {
      this.element = element;
      this.app = app;
      this.limit = Number.parseInt(element.getAttribute('data-notification-limit') ?? '10', 10) || 10;
      this.notificationCenterUrl = element.getAttribute('data-notification-center-url') || '/Notifications';
      this.isAuthenticated = element.getAttribute('data-is-authenticated') === 'true';
      this.badge = element.querySelector('[data-notification-unread]');
      this.badgeCount = this.badge ? this.badge.querySelector('[data-notification-unread-count]') : null;
      this.listElement = element.querySelector('[data-notification-list]');
      this.template = element.querySelector('[data-notification-item-template]');
      this.emptyElement = element.querySelector('[data-notification-empty]');
      this.signInElement = element.querySelector('[data-notification-signin]');
      this.statusElement = element.querySelector('[data-notification-status]');
      this.menuBody = element.querySelector('[data-notification-list-container]');

      this.handleDropdownShown = this.handleDropdownShown.bind(this);
      this.handleActionClick = this.handleActionClick.bind(this);

      this.bindEvents();
      this.app.register(this);
    }

    bindEvents() {
      const dropdown = this.element.querySelector('[data-bs-toggle="dropdown"]');
      if (dropdown) {
        dropdown.addEventListener('shown.bs.dropdown', this.handleDropdownShown);
      }

      this.element.addEventListener('click', event => {
        const target = event.target.closest('[data-notification-action]');
        if (!target) {
          return;
        }

        event.preventDefault();
        this.handleActionClick(target.getAttribute('data-notification-action'));
      });
    }

    handleDropdownShown() {
      if (this.isAuthenticated) {
        this.app.refresh().catch(error => this.setStatus('Unable to refresh notifications.', 'danger'));
      }
    }

    handleActionClick(action) {
      if (!this.isAuthenticated) {
        return;
      }

      if (action === 'refresh') {
        this.setStatus('Refreshing…', 'muted');
        this.app.refresh()
          .then(() => this.setStatus('Notifications updated.', 'success'))
          .catch(() => this.setStatus('Unable to refresh notifications.', 'danger'));
        return;
      }

      if (action === 'mark-all-read') {
        this.setStatus('Marking notifications…', 'muted');
        this.app.markAllRead()
          .then(count => {
            if (count === 0) {
              this.setStatus('No unread notifications to mark.', 'muted');
            } else {
              this.setStatus(`Marked ${count} notification${count === 1 ? '' : 's'} as read.`, 'success');
            }
          })
          .catch(() => this.setStatus('Unable to update notifications.', 'danger'));
      }
    }

    setStatus(message, tone) {
      if (!this.statusElement) {
        return;
      }

      const classList = this.statusElement.classList;
      classList.remove('text-success', 'text-danger', 'text-muted');
      this.statusElement.textContent = message ?? '';

      if (tone === 'success') {
        classList.add('text-success');
      } else if (tone === 'danger') {
        classList.add('text-danger');
      } else {
        classList.add('text-muted');
      }
    }

    update(notifications, unreadCount) {
      if (!this.isAuthenticated) {
        if (this.badge) {
          this.badge.classList.add('d-none');
        }
        return;
      }

      if (this.badge) {
        if (unreadCount > 0) {
          this.badge.classList.remove('d-none');
        } else {
          this.badge.classList.add('d-none');
        }
      }

      const badgeTarget = this.badgeCount ?? this.badge;
      if (badgeTarget) {
        badgeTarget.textContent = String(unreadCount > 0 ? unreadCount : 0);
      }

      if (!this.listElement || !this.template) {
        return;
      }

      const items = notifications.slice(0, this.limit);
      this.listElement.innerHTML = '';

      if (this.emptyElement) {
        this.emptyElement.hidden = items.length !== 0;
      }

      items.forEach(item => {
        const clone = this.template.content.firstElementChild.cloneNode(true);
        clone.setAttribute('data-notification-id', String(item.id));
        clone.setAttribute('data-project-id', item.projectId != null ? String(item.projectId) : '');
        clone.setAttribute('data-read', item.isRead ? 'true' : 'false');

        const link = clone.querySelector('[data-notification-link]');
        if (link) {
          link.setAttribute('href', item.route || this.notificationCenterUrl);
          link.addEventListener('click', () => {
            if (!item.isRead) {
              this.app.markRead([item.id]).catch(() => this.setStatus('Unable to update notification.', 'danger'));
            }
          });
        }

        const title = clone.querySelector('[data-notification-title]');
        if (title) {
          title.textContent = item.title || 'Notification';
        }

        const summary = clone.querySelector('[data-notification-summary]');
        if (summary) {
          if (item.summary) {
            summary.textContent = item.summary;
            summary.hidden = false;
          } else {
            summary.textContent = '';
            summary.hidden = true;
          }
        }

        const created = clone.querySelector('[data-notification-created]');
        if (created) {
          created.setAttribute('datetime', item.createdUtc);
          created.textContent = formatDate(item.createdAt);
        }

        const toggleButton = clone.querySelector('[data-notification-action="toggle-read"]');
        if (toggleButton) {
          toggleButton.setAttribute('data-notification-id', String(item.id));
          const icon = toggleButton.querySelector('.bi');
          if (icon) {
            icon.classList.toggle('bi-envelope-open', item.isRead);
            icon.classList.toggle('bi-envelope', !item.isRead);
          }

          toggleButton.addEventListener('click', event => {
            event.preventDefault();
            const action = item.isRead ? this.app.markUnread([item.id]) : this.app.markRead([item.id]);
            action
              .then(() => this.setStatus('Notification updated.', 'success'))
              .catch(() => this.setStatus('Unable to update notification.', 'danger'));
          });
        }

        this.listElement.appendChild(clone);
      });
    }
  }

  class NotificationCenterView {
    constructor(element, app) {
      this.element = element;
      this.app = app;
      this.tableBody = element.querySelector('[data-notification-tbody]');
      this.emptyMessage = element.querySelector('[data-notification-empty]');
      this.summaryElement = element.querySelector('[data-notification-summary]');
      this.unreadBadge = element.querySelector('[data-notification-unread]');
      this.template = document.querySelector('[data-notification-row-template]');
      this.selectAll = element.querySelector('[data-select-all]');
      this.bulkButtons = Array.from(element.querySelectorAll('[data-notification-bulk]'));
      this.refreshButton = element.querySelector('[data-notification-action="refresh"]');
      this.notificationCenterUrl = element.getAttribute('data-notification-center-url') || '/Notifications';
      this.filtersForm = document.querySelector('[data-notification-filters]');
      this.filters = {
        status: 'all',
        projectId: '',
        search: '',
      };
      this.selectedIds = new Set();
      this.currentRows = new Map();
      this.lastNotifications = [];
      this.actionMessage = '';

      this.bindEvents();
      this.app.register(this);
    }

    bindEvents() {
      if (this.selectAll) {
        this.selectAll.addEventListener('change', () => {
          const filteredIds = Array.from(this.currentRows.keys());
          const shouldSelect = this.selectAll.checked;
          filteredIds.forEach(id => {
            if (shouldSelect) {
              this.selectedIds.add(id);
            } else {
              this.selectedIds.delete(id);
            }
          });
          this.applySelectionToRows();
          this.updateBulkState();
        });
      }

      this.bulkButtons.forEach(button => {
        button.addEventListener('click', event => {
          event.preventDefault();
          const action = button.getAttribute('data-notification-bulk');
          this.handleBulkAction(action);
        });
      });

      if (this.refreshButton) {
        this.refreshButton.addEventListener('click', event => {
          event.preventDefault();
          this.setActionMessage('Refreshing…', 'muted');
          this.app.refresh()
            .then(() => this.setActionMessage('Notifications updated.', 'success'))
            .catch(() => this.setActionMessage('Unable to refresh notifications.', 'danger'));
        });
      }

      if (this.filtersForm) {
        const statusSelect = this.filtersForm.querySelector('[data-filter-status]');
        if (statusSelect) {
          statusSelect.addEventListener('change', () => {
            this.filters.status = statusSelect.value || 'all';
            this.render(this.lastNotifications);
          });
        }

        const projectSelect = this.filtersForm.querySelector('[data-filter-project]');
        if (projectSelect) {
          projectSelect.addEventListener('change', () => {
            this.filters.projectId = projectSelect.value || '';
            this.render(this.lastNotifications);
          });
        }

        const searchInput = this.filtersForm.querySelector('[data-filter-search]');
        if (searchInput) {
          searchInput.addEventListener('input', () => {
            this.filters.search = searchInput.value?.trim() ?? '';
            this.render(this.lastNotifications);
          });
        }
      }
    }

    handleBulkAction(action) {
      if (!action || this.selectedIds.size === 0) {
        return;
      }

      const ids = Array.from(this.selectedIds);

      if (action === 'mark-read') {
        this.setActionMessage('Marking notifications as read…', 'muted');
        this.app.markRead(ids)
          .then(count => this.setActionMessage(`Marked ${count} notification${count === 1 ? '' : 's'} as read.`, 'success'))
          .catch(() => this.setActionMessage('Unable to update notifications.', 'danger'));
        return;
      }

      if (action === 'mark-unread') {
        this.setActionMessage('Reopening notifications…', 'muted');
        this.app.markUnread(ids)
          .then(count => this.setActionMessage(`Marked ${count} notification${count === 1 ? '' : 's'} as unread.`, 'success'))
          .catch(() => this.setActionMessage('Unable to update notifications.', 'danger'));
        return;
      }

      if (action === 'mute-project' || action === 'unmute-project') {
        const projectIds = this.collectSelectedProjectIds();
        if (projectIds.length === 0) {
          this.setActionMessage('Select notifications linked to a project to change mute preferences.', 'danger');
          return;
        }

        const mute = action === 'mute-project';
        this.setActionMessage(mute ? 'Muting project notifications…' : 'Restoring project notifications…', 'muted');
        Promise.all(projectIds.map(projectId => this.app.muteProject(projectId, mute)))
          .then(() => this.setActionMessage(mute ? 'Project muted.' : 'Project unmuted.', 'success'))
          .catch(() => this.setActionMessage('Unable to update project preferences.', 'danger'));
      }
    }

    collectSelectedProjectIds() {
      const ids = new Set();
      this.selectedIds.forEach(id => {
        const notification = this.lastNotifications.find(item => item.id === id);
        if (notification?.projectId != null) {
          ids.add(notification.projectId);
        }
      });
      return Array.from(ids.values());
    }

    update(notifications, unreadCount) {
      this.lastNotifications = notifications;
      if (this.unreadBadge) {
        this.unreadBadge.textContent = String(unreadCount);
      }

      const existingIds = new Set(notifications.map(notification => notification.id));
      Array.from(this.selectedIds).forEach(id => {
        if (!existingIds.has(id)) {
          this.selectedIds.delete(id);
        }
      });

      this.render(notifications);
    }

    render(notifications) {
      if (!this.tableBody || !this.template) {
        return;
      }

      const filtered = this.applyFilters(notifications);

      this.tableBody.innerHTML = '';
      this.currentRows.clear();

      filtered.forEach(notification => {
        const row = this.template.content.firstElementChild.cloneNode(true);
        row.setAttribute('data-notification-id', String(notification.id));
        row.setAttribute('data-project-id', notification.projectId != null ? String(notification.projectId) : '');
        row.setAttribute('data-read', notification.isRead ? 'true' : 'false');

        const checkbox = row.querySelector('[data-notification-select]');
        if (checkbox) {
          checkbox.value = String(notification.id);
          checkbox.checked = this.selectedIds.has(notification.id);
          checkbox.addEventListener('change', () => {
            if (checkbox.checked) {
              this.selectedIds.add(notification.id);
            } else {
              this.selectedIds.delete(notification.id);
            }
            this.updateBulkState();
          });
        }

        const title = row.querySelector('[data-notification-title]');
        if (title) {
          title.textContent = notification.title || 'Notification';
        }

        const summary = row.querySelector('[data-notification-summary]');
        if (summary) {
          if (notification.summary) {
            summary.textContent = notification.summary;
            summary.hidden = false;
          } else {
            summary.textContent = '';
            summary.hidden = true;
          }
        }

        const project = row.querySelector('[data-notification-project]');
        if (project) {
          if (notification.projectId != null) {
            project.hidden = false;
            const label = notification.projectName && notification.projectName.trim().length > 0
              ? notification.projectName
              : `Project #${notification.projectId}`;
            project.textContent = label;
          } else {
            project.hidden = true;
            project.textContent = '';
          }
        }

        const link = row.querySelector('[data-notification-link]');
        if (link) {
          link.setAttribute('href', notification.route || this.notificationCenterUrl);
          link.addEventListener('click', () => {
            if (!notification.isRead) {
              this.app.markRead([notification.id]).catch(() => this.setActionMessage('Unable to update notification.', 'danger'));
            }
          });
        }

        const created = row.querySelector('[data-notification-created]');
        if (created) {
          created.setAttribute('datetime', notification.createdUtc);
          created.textContent = formatDate(notification.createdAt);
        }

        const state = row.querySelector('[data-notification-state]');
        if (state) {
          state.textContent = notification.isRead ? 'Read' : 'Unread';
          state.classList.remove('bg-primary', 'bg-secondary-subtle', 'text-secondary');
          if (notification.isRead) {
            state.classList.add('bg-secondary-subtle', 'text-secondary');
          } else {
            state.classList.add('bg-primary');
          }
        }

        const muted = row.querySelector('[data-notification-muted]');
        if (muted) {
          muted.hidden = !notification.isProjectMuted;
        }

        this.tableBody.appendChild(row);
        this.currentRows.set(notification.id, row);
      });

      if (this.emptyMessage) {
        this.emptyMessage.hidden = filtered.length !== 0;
      }

      this.applySelectionToRows();
      this.updateBulkState();
      this.updateSummary(filtered.length, notifications.length);
    }

    applyFilters(notifications) {
      let filtered = notifications;

      if (this.filters.status === 'unread') {
        filtered = filtered.filter(notification => !notification.isRead);
      } else if (this.filters.status === 'muted') {
        filtered = filtered.filter(notification => notification.isProjectMuted);
      }

      if (this.filters.projectId) {
        const projectId = Number.parseInt(this.filters.projectId, 10);
        if (Number.isFinite(projectId)) {
          filtered = filtered.filter(notification => notification.projectId === projectId);
        }
      }

      if (this.filters.search) {
        const query = this.filters.search.toLowerCase();
        filtered = filtered.filter(notification => {
          return (notification.title ?? '').toLowerCase().includes(query) ||
            (notification.summary ?? '').toLowerCase().includes(query);
        });
      }

      return filtered;
    }

    applySelectionToRows() {
      this.currentRows.forEach((row, id) => {
        const checkbox = row.querySelector('[data-notification-select]');
        const isSelected = this.selectedIds.has(id);
        if (checkbox) {
          checkbox.checked = isSelected;
        }
        row.classList.toggle('table-active', isSelected);
      });
    }

    updateBulkState() {
      const filteredIds = Array.from(this.currentRows.keys());
      if (this.selectAll) {
        const selectedInView = filteredIds.filter(id => this.selectedIds.has(id));
        this.selectAll.indeterminate = selectedInView.length > 0 && selectedInView.length < filteredIds.length;
        this.selectAll.checked = filteredIds.length > 0 && selectedInView.length === filteredIds.length;
      }

      const hasSelection = this.selectedIds.size > 0;
      this.bulkButtons.forEach(button => {
        button.disabled = !hasSelection;
      });
    }

    updateSummary(filteredCount, totalCount) {
      if (!this.summaryElement) {
        return;
      }

      const baseText = `Showing ${filteredCount} of ${totalCount} notification${totalCount === 1 ? '' : 's'}.`;
      this.summaryElement.textContent = this.actionMessage
        ? `${baseText} ${this.actionMessage}`
        : baseText;
    }

    setActionMessage(message, tone) {
      this.actionMessage = message ?? '';
      if (!this.summaryElement) {
        return;
      }

      this.summaryElement.classList.remove('text-success', 'text-danger', 'text-muted');
      if (tone === 'success') {
        this.summaryElement.classList.add('text-success');
      } else if (tone === 'danger') {
        this.summaryElement.classList.add('text-danger');
      } else {
        this.summaryElement.classList.add('text-muted');
      }

      this.updateSummary(this.currentRows.size, this.lastNotifications.length);
    }
  }

  function formatDate(date) {
    if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
      return '';
    }

    return date.toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    });
  }

  function deriveConfig(elements) {
    const config = {
      apiBase: '',
      unreadUrl: '',
      hubUrl: '',
      isAuthenticated: false,
      initialUnread: 0,
      initialNotifications: [],
      fetchLimit: 20,
      pollInterval: DEFAULT_POLL_INTERVAL,
      storeLimit: MAX_STORE_SIZE,
      notificationCenterUrl: '/Notifications',
    };

    const limits = [];

    elements.forEach(element => {
      const apiBase = element.getAttribute('data-api-base');
      const unreadUrl = element.getAttribute('data-unread-url');
      const hubUrl = element.getAttribute('data-hub-url');
      const unreadCount = Number.parseInt(element.getAttribute('data-unread-count') ?? '0', 10);
      const notifications = parseJsonAttribute(element.getAttribute('data-notifications'));
      const limit = Number.parseInt(element.getAttribute('data-notification-limit') ?? '0', 10);
      const isAuthenticated = element.getAttribute('data-is-authenticated');
      const centerUrl = element.getAttribute('data-notification-center-url');

      if (apiBase) {
        config.apiBase = apiBase;
      }

      if (unreadUrl) {
        config.unreadUrl = unreadUrl;
      }

      if (hubUrl) {
        config.hubUrl = hubUrl;
      }

      if (Number.isFinite(unreadCount) && unreadCount > config.initialUnread) {
        config.initialUnread = unreadCount;
      }

      if (Array.isArray(notifications) && notifications.length > 0) {
        config.initialNotifications = config.initialNotifications.concat(notifications);
      }

      if (Number.isFinite(limit) && limit > 0) {
        limits.push(limit);
      }

      if (isAuthenticated === 'true') {
        config.isAuthenticated = true;
      }

      if (centerUrl) {
        config.notificationCenterUrl = centerUrl;
      }
    });

    if (limits.length > 0) {
      config.fetchLimit = Math.max(...limits);
      config.storeLimit = Math.max(config.fetchLimit, MAX_STORE_SIZE / 2);
    }

    config.initialNotifications = dedupeNotifications(config.initialNotifications);
    return config;
  }

  onReady(() => {
    const elements = Array.from(document.querySelectorAll('[data-notification-bell], [data-notification-center]'));
    if (elements.length === 0) {
      return;
    }

    const config = deriveConfig(elements);
    const app = new NotificationsApp(config);

    elements.forEach(element => {
      if (element.hasAttribute('data-notification-bell')) {
        new NotificationBellView(element, app);
      }

      if (element.hasAttribute('data-notification-center')) {
        new NotificationCenterView(element, app);
      }
    });

    app.start();
  });
})();
