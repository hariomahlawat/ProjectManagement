(() => {
  'use strict';

  const CSRF_HEADER = 'X-CSRF-TOKEN';
  const POLL_INTERVAL_MS = 60_000;
  const SEARCH_DEBOUNCE_MS = 300;
  const STATUS_CLEAR_MS = 4_500;
  const IST_FORMATTER = new Intl.DateTimeFormat('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'Asia/Kolkata'
  });

  function getCsrfToken() {
    const value = document.querySelector('meta[name="csrf-token"]')
      ?.getAttribute('content')
      ?.trim();

    if (!value) {
      throw new Error('The security token is unavailable. Refresh the page and try again.');
    }

    return value;
  }

  function parseJsonScript(root) {
    const script = root.querySelector('script[data-notification-bootstrap]');
    if (!script) {
      return null;
    }

    try {
      return JSON.parse(script.textContent || 'null');
    } catch (error) {
      console.error('Unable to parse notification bootstrap data.', error);
      return null;
    }
  }

  function asArray(value) {
    return Array.isArray(value) ? value : [];
  }

  function asInteger(value, fallback = 0) {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  function safeRoute(route, fallback) {
    if (typeof route !== 'string') {
      return fallback;
    }

    const trimmed = route.trim();
    if (!trimmed.startsWith('/') || trimmed.startsWith('//') || trimmed.includes('\\')) {
      return fallback;
    }

    return trimmed;
  }

  function normalizeNotification(raw) {
    const createdUtc = raw?.createdUtc || raw?.CreatedUtc || new Date().toISOString();
    const readUtc = raw?.readUtc ?? raw?.ReadUtc ?? null;
    const seenUtc = raw?.seenUtc ?? raw?.SeenUtc ?? null;

    return {
      id: asInteger(raw?.id ?? raw?.Id),
      kind: raw?.kind ?? raw?.Kind ?? null,
      module: raw?.module ?? raw?.Module ?? null,
      eventType: raw?.eventType ?? raw?.EventType ?? null,
      scopeType: raw?.scopeType ?? raw?.ScopeType ?? null,
      scopeId: raw?.scopeId ?? raw?.ScopeId ?? null,
      projectId: raw?.projectId ?? raw?.ProjectId ?? null,
      projectName: raw?.projectName ?? raw?.ProjectName ?? null,
      actorUserId: raw?.actorUserId ?? raw?.ActorUserId ?? null,
      actorDisplayName: raw?.actorDisplayName ?? raw?.ActorDisplayName ?? null,
      route: raw?.route ?? raw?.Route ?? null,
      title: raw?.title ?? raw?.Title ?? 'Notification',
      summary: raw?.summary ?? raw?.Summary ?? null,
      createdUtc,
      createdDisplayIst: raw?.createdDisplayIst ?? raw?.CreatedDisplayIst ?? formatExactIst(createdUtc),
      deliveredUtc: raw?.deliveredUtc ?? raw?.DeliveredUtc ?? null,
      seenUtc,
      readUtc,
      isRead: Boolean(readUtc),
      isSeen: Boolean(seenUtc),
      isProjectMuted: Boolean(raw?.isProjectMuted ?? raw?.IsProjectMuted),
      category: raw?.category ?? raw?.Category ?? 'General',
      iconCssClass: raw?.iconCssClass ?? raw?.IconCssClass ?? 'bi bi-bell',
      priority: raw?.priority ?? raw?.Priority ?? 'Normal',
      isActionRequired: Boolean(raw?.isActionRequired ?? raw?.IsActionRequired)
    };
  }

  function normalizePage(raw) {
    return {
      items: asArray(raw?.items ?? raw?.Items).map(normalizeNotification),
      totalCount: asInteger(raw?.totalCount ?? raw?.TotalCount),
      unreadCount: asInteger(raw?.unreadCount ?? raw?.UnreadCount),
      nextCursor: raw?.nextCursor ?? raw?.NextCursor ?? null,
      hasMore: Boolean(raw?.hasMore ?? raw?.HasMore),
      projects: asArray(raw?.projects ?? raw?.Projects).map(project => ({
        id: asInteger(project?.id ?? project?.Id),
        label: project?.label ?? project?.Label ?? '',
        isMuted: Boolean(project?.isMuted ?? project?.IsMuted)
      })),
      modules: asArray(raw?.modules ?? raw?.Modules).map(String)
    };
  }

  function formatExactIst(value) {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    return `${IST_FORMATTER.format(date)} IST`;
  }

  function formatRelativeTime(value) {
    const date = new Date(value);
    const milliseconds = Date.now() - date.getTime();
    if (Number.isNaN(milliseconds)) {
      return '';
    }

    if (milliseconds < 0 && Math.abs(milliseconds) < 60_000) {
      return 'Just now';
    }

    const seconds = Math.max(0, Math.floor(milliseconds / 1000));
    if (seconds < 45) {
      return 'Just now';
    }

    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) {
      return `${minutes} min${minutes === 1 ? '' : 's'} ago`;
    }

    const hours = Math.floor(minutes / 60);
    if (hours < 24) {
      return `${hours} hr${hours === 1 ? '' : 's'} ago`;
    }

    const days = Math.floor(hours / 24);
    if (days < 7) {
      return `${days} day${days === 1 ? '' : 's'} ago`;
    }

    return date.toLocaleDateString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric',
      timeZone: 'Asia/Kolkata'
    });
  }

  function setTimeElement(element, notification) {
    if (!element) {
      return;
    }

    element.setAttribute('datetime', notification.createdUtc || '');
    element.textContent = formatRelativeTime(notification.createdUtc);
    element.title = notification.createdDisplayIst || formatExactIst(notification.createdUtc);
  }

  function setIcon(element, cssClass) {
    if (!element) {
      return;
    }

    element.className = cssClass || 'bi bi-bell';
  }

  function setHidden(element, hidden) {
    if (element) {
      element.hidden = hidden;
    }
  }

  function setText(element, value) {
    if (element) {
      element.textContent = value ?? '';
    }
  }

  function debounce(callback, delay) {
    let timer = null;
    return (...args) => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => callback(...args), delay);
    };
  }

  async function parseResponse(response) {
    if (response.status === 204) {
      return null;
    }

    const contentType = response.headers.get('content-type') || '';
    if (!contentType.includes('application/json')) {
      return null;
    }

    return response.json();
  }

  class NotificationRuntime {
    constructor(config) {
      this.apiBase = config.apiBase;
      this.unreadUrl = config.unreadUrl;
      this.hubUrl = config.hubUrl;
      this.views = new Set();
      this.connection = null;
      this.pollTimer = null;
      this.reconnectTimer = null;
      this.unreadCount = config.unreadCount || 0;
    }

    register(view) {
      this.views.add(view);
      view.setUnreadCount(this.unreadCount);
    }

    async request(path, options = {}) {
      const method = (options.method || 'GET').toUpperCase();
      const headers = new Headers(options.headers || {});

      if (method !== 'GET' && method !== 'HEAD') {
        headers.set(CSRF_HEADER, getCsrfToken());
      }

      if (options.body != null && !headers.has('Content-Type')) {
        headers.set('Content-Type', 'application/json');
      }

      const response = await fetch(path, {
        credentials: 'same-origin',
        cache: 'no-store',
        ...options,
        method,
        headers
      });

      const payload = await parseResponse(response);
      if (!response.ok) {
        const message = payload?.error
          || payload?.title
          || `Notification request failed (${response.status}).`;
        throw new Error(message);
      }

      return payload;
    }

    async fetchPage(parameters = {}) {
      const url = new URL(this.apiBase, window.location.origin);
      Object.entries(parameters).forEach(([key, value]) => {
        if (value !== null && value !== undefined && value !== '') {
          url.searchParams.set(key, String(value));
        }
      });

      return normalizePage(await this.request(url.toString()));
    }

    async markRead(ids, options = {}) {
      return this.mutateRead('/read', ids, options);
    }

    async markUnread(ids, options = {}) {
      return this.mutateRead('/unread', ids, options);
    }

    async mutateRead(path, ids, options = {}) {
      const uniqueIds = Array.from(new Set(asArray(ids).map(value => asInteger(value)).filter(id => id > 0)));
      if (uniqueIds.length === 0) {
        return { affectedCount: 0, notificationIds: [], unreadCount: this.unreadCount };
      }

      const mutation = await this.request(`${this.apiBase}${path}`, {
        method: 'POST',
        body: JSON.stringify({ ids: uniqueIds }),
        keepalive: Boolean(options.keepalive)
      });

      this.handleStateMutation(mutation);
      return mutation;
    }

    async markAllRead() {
      const mutation = await this.request(`${this.apiBase}/read-all`, {
        method: 'POST'
      });
      this.handleStateMutation(mutation);
      return mutation;
    }

    async markSeen(ids) {
      const uniqueIds = Array.from(new Set(asArray(ids).map(value => asInteger(value)).filter(id => id > 0)));
      if (uniqueIds.length === 0) {
        return null;
      }

      const mutation = await this.request(`${this.apiBase}/seen`, {
        method: 'POST',
        body: JSON.stringify({ ids: uniqueIds })
      });
      this.handleSeenMutation(mutation);
      return mutation;
    }

    async muteProject(projectId, muted) {
      const id = asInteger(projectId);
      if (id <= 0) {
        throw new Error('A valid project is required.');
      }

      const mutation = await this.request(`${this.apiBase}/projects/${id}/mute`, {
        method: muted ? 'POST' : 'DELETE'
      });
      this.handleMuteMutation(mutation);
      return mutation;
    }

    setUnreadCount(count) {
      this.unreadCount = Math.max(0, asInteger(count));
      this.views.forEach(view => view.setUnreadCount(this.unreadCount));
    }

    handleNewNotification(raw) {
      const notification = normalizeNotification(raw);
      this.views.forEach(view => view.handleNewNotification(notification));
    }

    handleNotificationList(raw) {
      const notifications = asArray(raw).map(normalizeNotification);
      this.views.forEach(view => view.handleNotificationList(notifications));
    }

    handleStateMutation(raw) {
      if (!raw) {
        return;
      }

      const mutation = {
        notificationIds: asArray(raw.notificationIds ?? raw.NotificationIds).map(value => asInteger(value)),
        isRead: Boolean(raw.isRead ?? raw.IsRead),
        readUtc: raw.readUtc ?? raw.ReadUtc ?? null,
        seenUtc: raw.seenUtc ?? raw.SeenUtc ?? null,
        appliesToAll: Boolean(raw.appliesToAll ?? raw.AppliesToAll),
        affectedCount: asInteger(raw.affectedCount ?? raw.AffectedCount),
        unreadCount: asInteger(raw.unreadCount ?? raw.UnreadCount, this.unreadCount)
      };

      this.setUnreadCount(mutation.unreadCount);
      this.views.forEach(view => view.handleStateMutation(mutation));
    }

    handleSeenMutation(raw) {
      if (!raw) {
        return;
      }

      const mutation = {
        notificationIds: asArray(raw.notificationIds ?? raw.NotificationIds).map(value => asInteger(value)),
        seenUtc: raw.seenUtc ?? raw.SeenUtc ?? new Date().toISOString(),
        affectedCount: asInteger(raw.affectedCount ?? raw.AffectedCount)
      };

      this.views.forEach(view => view.handleSeenMutation(mutation));
    }

    handleMuteMutation(raw) {
      if (!raw) {
        return;
      }

      const mutation = {
        projectId: asInteger(raw.projectId ?? raw.ProjectId),
        isMuted: Boolean(raw.isMuted ?? raw.IsMuted),
        changedNotificationIds: asArray(raw.changedNotificationIds ?? raw.ChangedNotificationIds).map(value => asInteger(value)),
        unreadCount: asInteger(raw.unreadCount ?? raw.UnreadCount, this.unreadCount)
      };

      this.setUnreadCount(mutation.unreadCount);
      this.views.forEach(view => view.handleMuteMutation(mutation));
    }

    async start() {
      if (!this.hubUrl || !window.signalR?.HubConnectionBuilder) {
        this.startPolling();
        return;
      }

      this.connection = new window.signalR.HubConnectionBuilder()
        .withUrl(this.hubUrl)
        .withAutomaticReconnect([0, 2_000, 5_000, 15_000, 30_000])
        .build();

      this.connection.on('ReceiveUnreadCount', count => this.setUnreadCount(count));
      this.connection.on('ReceiveNotification', notification => this.handleNewNotification(notification));
      this.connection.on('ReceiveNotifications', notifications => this.handleNotificationList(notifications));
      this.connection.on('ReceiveNotificationStateChanged', mutation => this.handleStateMutation(mutation));
      this.connection.on('ReceiveNotificationSeen', mutation => this.handleSeenMutation(mutation));
      this.connection.on('ReceiveProjectMuteChanged', mutation => this.handleMuteMutation(mutation));

      this.connection.onreconnecting(() => this.startPolling());
      this.connection.onreconnected(() => {
        this.stopPolling();
        this.refreshAll();
      });
      this.connection.onclose(() => {
        this.startPolling();
        this.scheduleReconnect();
      });

      try {
        await this.connection.start();
        this.stopPolling();
      } catch (error) {
        console.warn('Notification realtime connection is unavailable; polling will be used.', error);
        this.startPolling();
        this.scheduleReconnect();
      }
    }

    scheduleReconnect() {
      if (!this.connection || this.reconnectTimer) {
        return;
      }

      this.reconnectTimer = window.setTimeout(async () => {
        this.reconnectTimer = null;
        if (this.connection.state !== window.signalR.HubConnectionState.Disconnected) {
          return;
        }

        try {
          await this.connection.start();
          this.stopPolling();
          this.refreshAll();
        } catch {
          this.scheduleReconnect();
        }
      }, 15_000);
    }

    startPolling() {
      if (this.pollTimer) {
        return;
      }

      this.pollTimer = window.setInterval(() => this.refreshAll(), POLL_INTERVAL_MS);
    }

    stopPolling() {
      if (this.pollTimer) {
        window.clearInterval(this.pollTimer);
        this.pollTimer = null;
      }
    }

    refreshAll() {
      this.views.forEach(view => view.refresh({ silent: true }).catch(() => {}));
    }
  }

  class NotificationBellView {
    constructor(root, runtime) {
      this.root = root;
      this.runtime = runtime;
      this.apiBase = root.dataset.apiBase;
      this.limit = Math.max(1, asInteger(root.dataset.notificationLimit, 10));
      this.centerUrl = root.dataset.notificationCenterUrl || '/Notifications';
      this.authenticated = root.dataset.isAuthenticated === 'true';
      this.items = asArray(parseJsonScript(root)).map(normalizeNotification);
      this.list = root.querySelector('[data-notification-list]');
      this.template = root.querySelector('template[data-notification-item-template]');
      this.empty = root.querySelector('[data-notification-empty]');
      this.status = root.querySelector('[data-notification-status]');
      this.unreadBadge = root.querySelector('[data-notification-unread]');
      this.unreadCount = root.querySelector('[data-notification-unread-count]');
      this.headerCount = root.querySelector('[data-notification-header-count]');
      this.markAllButton = root.querySelector('[data-notification-action="mark-all-read"]');
      this.statusTimer = null;

      this.bind();
      this.render();
    }

    bind() {
      this.root.addEventListener('click', event => {
        const actionButton = event.target.closest('[data-notification-action]');
        if (actionButton && this.root.contains(actionButton)) {
          const action = actionButton.dataset.notificationAction;
          if (action === 'refresh') {
            event.preventDefault();
            this.refresh();
          } else if (action === 'mark-all-read') {
            event.preventDefault();
            this.markAllRead();
          } else if (action === 'toggle-read') {
            event.preventDefault();
            event.stopPropagation();
            this.toggleRead(actionButton);
          }
          return;
        }

        const link = event.target.closest('[data-notification-link]');
        if (link && this.root.contains(link)) {
          this.openNotification(event, link);
        }
      });

      const dropdown = this.root.querySelector('.notification-bell__trigger');
      dropdown?.addEventListener('shown.bs.dropdown', () => {
        const unseenIds = this.items
          .filter(item => !item.isSeen && !item.isProjectMuted)
          .slice(0, this.limit)
          .map(item => item.id);

        if (unseenIds.length > 0) {
          this.runtime.markSeen(unseenIds).catch(() => {});
        }
      });
    }

    async refresh(options = {}) {
      if (!this.authenticated) {
        return;
      }

      if (!options.silent) {
        this.setStatus('Refreshing…', 'muted');
      }

      try {
        const page = await this.runtime.fetchPage({
          limit: this.limit,
          includeMuted: false,
          includeFilterOptions: false
        });
        this.items = page.items;
        this.runtime.setUnreadCount(page.unreadCount);
        this.render();
        if (!options.silent) {
          this.setStatus('Updated', 'success');
        }
      } catch (error) {
        if (!options.silent) {
          this.setStatus(error.message || 'Unable to refresh.', 'danger');
        }
      }
    }

    async markAllRead() {
      this.markAllButton?.setAttribute('disabled', 'disabled');
      this.setStatus('Marking all as read…', 'muted');
      try {
        const mutation = await this.runtime.markAllRead();
        this.setStatus(
          mutation.affectedCount > 0
            ? `${mutation.affectedCount} marked as read`
            : 'No unread notifications',
          'success');
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notifications.', 'danger');
      } finally {
        if (this.markAllButton) {
          this.markAllButton.disabled = this.runtime.unreadCount <= 0;
        }
      }
    }

    async toggleRead(button) {
      const id = asInteger(button.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      if (!notification) {
        return;
      }

      button.disabled = true;
      try {
        if (notification.isRead) {
          await this.runtime.markUnread([id]);
        } else {
          await this.runtime.markRead([id]);
        }
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notification.', 'danger');
      } finally {
        button.disabled = false;
      }
    }

    openNotification(event, link) {
      const row = link.closest('[data-notification-item]');
      const id = asInteger(row?.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      if (!notification || notification.isRead) {
        return;
      }

      const route = safeRoute(link.getAttribute('href'), this.centerUrl);
      const modifiedClick = event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0;
      if (modifiedClick) {
        this.runtime.markRead([id], { keepalive: true }).catch(() => {});
        return;
      }

      event.preventDefault();
      Promise.race([
        this.runtime.markRead([id]),
        new Promise(resolve => window.setTimeout(resolve, 500))
      ])
        .catch(() => {})
        .finally(() => window.location.assign(route));
    }

    handleNewNotification(notification) {
      if (notification.isProjectMuted) {
        return;
      }

      this.items = [notification, ...this.items.filter(item => item.id !== notification.id)]
        .sort((a, b) => new Date(b.createdUtc) - new Date(a.createdUtc))
        .slice(0, this.limit);
      this.render();
    }

    handleNotificationList(notifications) {
      this.items = notifications
        .filter(item => !item.isProjectMuted)
        .sort((a, b) => new Date(b.createdUtc) - new Date(a.createdUtc))
        .slice(0, this.limit);
      this.render();
    }

    handleStateMutation(mutation) {
      const ids = new Set(mutation.notificationIds);
      this.items.forEach(item => {
        if ((!mutation.appliesToAll && !ids.has(item.id))
            || (mutation.appliesToAll && item.isProjectMuted)) {
          return;
        }
        item.isRead = mutation.isRead;
        item.readUtc = mutation.isRead ? mutation.readUtc || new Date().toISOString() : null;
        if (mutation.seenUtc) {
          item.isSeen = true;
          item.seenUtc = mutation.seenUtc;
        }
      });
      this.render();
    }

    handleSeenMutation(mutation) {
      const ids = new Set(mutation.notificationIds);
      this.items.forEach(item => {
        if (ids.has(item.id)) {
          item.isSeen = true;
          item.seenUtc = mutation.seenUtc;
        }
      });
    }

    handleMuteMutation(mutation) {
      this.items.forEach(item => {
        if (item.projectId === mutation.projectId) {
          item.isProjectMuted = mutation.isMuted;
        }
        if (mutation.changedNotificationIds.includes(item.id)) {
          item.isRead = true;
          item.readUtc = new Date().toISOString();
        }
      });
      this.items = this.items.filter(item => !item.isProjectMuted);
      this.render();
      if (mutation.isMuted) {
        this.refresh({ silent: true }).catch(() => {});
      }
    }

    setUnreadCount(count) {
      setText(this.unreadCount, count);
      setText(this.headerCount, count);
      this.unreadBadge?.classList.toggle('d-none', count <= 0);
      if (this.markAllButton) {
        this.markAllButton.disabled = count <= 0;
      }
    }

    render() {
      if (!this.list || !this.template) {
        return;
      }

      const visible = this.items
        .filter(item => !item.isProjectMuted)
        .sort((a, b) => new Date(b.createdUtc) - new Date(a.createdUtc))
        .slice(0, this.limit);

      this.list.innerHTML = '';
      visible.forEach(notification => {
        const row = this.template.content.firstElementChild.cloneNode(true);
        this.populateRow(row, notification);
        this.list.appendChild(row);
      });

      setHidden(this.empty, visible.length !== 0);
    }

    populateRow(row, notification) {
      row.dataset.notificationId = String(notification.id);
      row.dataset.projectId = notification.projectId == null ? '' : String(notification.projectId);
      row.dataset.read = String(notification.isRead);
      row.dataset.seen = String(notification.isSeen);
      row.dataset.muted = String(notification.isProjectMuted);
      row.classList.toggle('is-read', notification.isRead);
      row.classList.toggle('is-unread', !notification.isRead);

      setIcon(row.querySelector('[data-notification-icon]'), notification.iconCssClass);
      setText(row.querySelector('[data-notification-project]'), notification.projectName || notification.category);
      setText(row.querySelector('[data-notification-title]'), notification.title || 'Notification');

      const summary = row.querySelector('[data-notification-summary]');
      setText(summary, notification.summary);
      setHidden(summary, !notification.summary);

      const actor = row.querySelector('[data-notification-actor]');
      const actorSeparator = row.querySelector('[data-notification-actor-separator]');
      setText(actor, notification.actorDisplayName);
      setHidden(actor, !notification.actorDisplayName);
      setHidden(actorSeparator, !notification.actorDisplayName);

      setTimeElement(row.querySelector('[data-notification-created]'), notification);

      const link = row.querySelector('[data-notification-link]');
      link?.setAttribute('href', safeRoute(notification.route, this.centerUrl));

      const button = row.querySelector('[data-notification-action="toggle-read"]');
      if (button) {
        button.dataset.notificationId = String(notification.id);
        const label = notification.isRead ? 'Mark as unread' : 'Mark as read';
        button.title = label;
        button.setAttribute('aria-label', label);
        setIcon(button.querySelector('.bi'), notification.isRead ? 'bi bi-envelope-open' : 'bi bi-envelope');
      }

      setHidden(row.querySelector('[data-notification-unread-dot]'), notification.isRead);
    }

    setStatus(message, tone = 'muted') {
      if (!this.status) {
        return;
      }

      window.clearTimeout(this.statusTimer);
      this.status.textContent = message || '';
      this.status.className = `notification-menu__status text-${tone}`;
      if (message) {
        this.statusTimer = window.setTimeout(() => {
          this.status.textContent = '';
        }, STATUS_CLEAR_MS);
      }
    }
  }

  class NotificationCenterView {
    constructor(root, runtime) {
      this.root = root;
      this.runtime = runtime;
      this.pageSize = Math.max(1, asInteger(root.dataset.notificationLimit, 30));
      this.centerUrl = root.dataset.notificationCenterUrl || '/Notifications';
      this.rows = root.querySelector('[data-notification-rows]');
      this.template = root.querySelector('template[data-notification-row-template]');
      this.empty = root.querySelector('[data-notification-empty]');
      this.loading = root.querySelector('[data-notification-loading]');
      this.summary = root.querySelector('[data-notification-list-summary]');
      this.status = root.querySelector('[data-notification-status]');
      this.totalCountElement = root.querySelector('[data-notification-total-count]');
      this.unreadCountElement = root.querySelector('[data-notification-unread-count]');
      this.loadMoreButton = root.querySelector('[data-notification-load-more]');
      this.selectAll = root.querySelector('[data-select-all]');
      this.selectionCount = root.querySelector('[data-selection-count]');
      this.bulkButtons = Array.from(root.querySelectorAll('[data-notification-bulk]'));
      this.markAllButton = root.querySelector('[data-notification-action="mark-all-read"]');
      this.filtersForm = root.querySelector('[data-notification-filters]');
      this.statusTimer = null;
      this.newNotificationRefresh = debounce(() => this.refresh({ silent: true }), 500);
      this.selectedIds = new Set();
      this.filters = {
        status: 'all',
        projectId: '',
        module: '',
        search: ''
      };

      const initial = normalizePage(parseJsonScript(root) || {});
      this.items = initial.items;
      this.totalCount = initial.totalCount;
      this.nextCursor = initial.nextCursor;
      this.hasMore = initial.hasMore;
      this.projects = initial.projects;
      this.modules = initial.modules;

      this.bind();
      this.render();
    }

    bind() {
      this.root.addEventListener('change', event => {
        const checkbox = event.target.closest('[data-notification-select]');
        if (checkbox && this.root.contains(checkbox)) {
          const id = asInteger(checkbox.value);
          if (checkbox.checked) {
            this.selectedIds.add(id);
          } else {
            this.selectedIds.delete(id);
          }
          this.updateBulkState();
        }
      });

      this.root.addEventListener('click', event => {
        const bulk = event.target.closest('[data-notification-bulk]');
        if (bulk && this.root.contains(bulk)) {
          event.preventDefault();
          this.handleBulkAction(bulk.dataset.notificationBulk);
          return;
        }

        const action = event.target.closest('[data-notification-action]');
        if (action && this.root.contains(action)) {
          event.preventDefault();
          if (action.dataset.notificationAction === 'refresh') {
            this.refresh();
          } else if (action.dataset.notificationAction === 'clear-filters') {
            this.clearFilters();
          } else if (action.dataset.notificationAction === 'mark-all-read') {
            this.markAllRead();
          }
          return;
        }

        const link = event.target.closest('[data-notification-link]');
        if (link && this.root.contains(link)) {
          this.openNotification(event, link);
        }
      });

      this.selectAll?.addEventListener('change', () => {
        const checked = this.selectAll.checked;
        this.items.forEach(item => {
          if (checked) {
            this.selectedIds.add(item.id);
          } else {
            this.selectedIds.delete(item.id);
          }
        });
        this.renderSelection();
      });

      this.loadMoreButton?.addEventListener('click', event => {
        event.preventDefault();
        this.loadMore();
      });

      if (this.filtersForm) {
        const status = this.filtersForm.querySelector('[data-filter-status]');
        const project = this.filtersForm.querySelector('[data-filter-project]');
        const module = this.filtersForm.querySelector('[data-filter-module]');
        const search = this.filtersForm.querySelector('[data-filter-search]');

        status?.addEventListener('change', () => {
          this.filters.status = status.value || 'all';
          this.refresh();
        });
        project?.addEventListener('change', () => {
          this.filters.projectId = project.value || '';
          this.refresh();
        });
        module?.addEventListener('change', () => {
          this.filters.module = module.value || '';
          this.refresh();
        });
        search?.addEventListener('input', debounce(() => {
          this.filters.search = search.value.trim();
          this.refresh();
        }, SEARCH_DEBOUNCE_MS));
      }
    }

    queryParameters(cursor = null) {
      return {
        limit: this.pageSize,
        cursor,
        status: this.filters.status === 'all' ? '' : this.filters.status,
        projectId: this.filters.projectId,
        module: this.filters.module,
        search: this.filters.search,
        includeMuted: true,
        includeFilterOptions: cursor == null
      };
    }

    async refresh(options = {}) {
      this.setLoading(true);
      if (!options.silent) {
        this.setStatus('Refreshing…', 'muted');
      }

      try {
        const page = await this.runtime.fetchPage(this.queryParameters());
        this.applyPage(page, false);
        if (!options.silent) {
          this.setStatus('Notifications updated.', 'success');
        }
      } catch (error) {
        if (!options.silent) {
          this.setStatus(error.message || 'Unable to refresh notifications.', 'danger');
        }
      } finally {
        this.setLoading(false);
      }
    }

    async loadMore() {
      if (!this.hasMore || !this.nextCursor) {
        return;
      }

      this.setLoading(true);
      this.loadMoreButton.disabled = true;
      try {
        const page = await this.runtime.fetchPage(this.queryParameters(this.nextCursor));
        this.applyPage(page, true);
      } catch (error) {
        this.setStatus(error.message || 'Unable to load more notifications.', 'danger');
      } finally {
        this.setLoading(false);
        this.loadMoreButton.disabled = false;
      }
    }

    applyPage(page, append) {
      if (append) {
        const existing = new Set(this.items.map(item => item.id));
        this.items = [...this.items, ...page.items.filter(item => !existing.has(item.id))];
      } else {
        this.items = page.items;
        this.selectedIds.clear();
      }

      this.totalCount = page.totalCount;
      this.nextCursor = page.nextCursor;
      this.hasMore = page.hasMore;
      if (!append) {
        this.projects = page.projects;
        this.modules = page.modules;
        this.updateFilterOptions();
      }
      this.runtime.setUnreadCount(page.unreadCount);
      this.render();
    }

    clearFilters() {
      this.filters = { status: 'all', projectId: '', module: '', search: '' };
      const status = this.filtersForm?.querySelector('[data-filter-status]');
      const project = this.filtersForm?.querySelector('[data-filter-project]');
      const module = this.filtersForm?.querySelector('[data-filter-module]');
      const search = this.filtersForm?.querySelector('[data-filter-search]');
      if (status) status.value = 'all';
      if (project) project.value = '';
      if (module) module.value = '';
      if (search) search.value = '';
      this.refresh();
    }


    async markAllRead() {
      if (!this.markAllButton || this.runtime.unreadCount <= 0) {
        return;
      }

      this.markAllButton.disabled = true;
      this.setStatus('Marking all notifications as read…', 'muted');
      try {
        const result = await this.runtime.markAllRead();
        this.selectedIds.clear();
        this.setStatus(
          result.affectedCount > 0
            ? `${result.affectedCount} notification${result.affectedCount === 1 ? '' : 's'} marked read.`
            : 'No unread notifications remain.',
          'success');
      } catch (error) {
        this.setStatus(error.message || 'Unable to mark all notifications as read.', 'danger');
      } finally {
        this.markAllButton.disabled = this.runtime.unreadCount <= 0;
        this.renderSelection();
      }
    }

    async handleBulkAction(action) {
      const ids = Array.from(this.selectedIds);
      if (ids.length === 0) {
        return;
      }

      this.setBulkDisabled(true);
      try {
        if (action === 'mark-read') {
          const result = await this.runtime.markRead(ids);
          this.setStatus(`${result.affectedCount} notification${result.affectedCount === 1 ? '' : 's'} marked read.`, 'success');
        } else if (action === 'mark-unread') {
          const result = await this.runtime.markUnread(ids);
          this.setStatus(`${result.affectedCount} notification${result.affectedCount === 1 ? '' : 's'} marked unread.`, 'success');
        } else if (action === 'mute-project' || action === 'unmute-project') {
          const muted = action === 'mute-project';
          const projects = Array.from(new Set(this.items
            .filter(item => this.selectedIds.has(item.id) && item.projectId != null && item.isProjectMuted !== muted)
            .map(item => item.projectId)));

          if (projects.length === 0) {
            this.setStatus('No eligible project is selected for this action.', 'danger');
            return;
          }

          for (const projectId of projects) {
            await this.runtime.muteProject(projectId, muted);
          }
          this.setStatus(`${projects.length} project${projects.length === 1 ? '' : 's'} ${muted ? 'muted' : 'unmuted'}.`, 'success');
        }
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notifications.', 'danger');
      } finally {
        this.setBulkDisabled(false);
        this.updateBulkState();
      }
    }

    openNotification(event, link) {
      const row = link.closest('[data-notification-row]');
      const id = asInteger(row?.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      if (!notification || notification.isRead) {
        return;
      }

      const route = safeRoute(link.getAttribute('href'), this.centerUrl);
      const modifiedClick = event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0;
      if (modifiedClick) {
        this.runtime.markRead([id], { keepalive: true }).catch(() => {});
        return;
      }

      event.preventDefault();
      Promise.race([
        this.runtime.markRead([id]),
        new Promise(resolve => window.setTimeout(resolve, 500))
      ])
        .catch(() => {})
        .finally(() => window.location.assign(route));
    }

    handleNewNotification(notification) {
      const hasActiveFilter = this.filters.status !== 'all'
        || this.filters.projectId
        || this.filters.module
        || this.filters.search;

      if (hasActiveFilter) {
        this.newNotificationRefresh();
        return;
      }

      const isExisting = this.items.some(item => item.id === notification.id);
      this.items = [notification, ...this.items.filter(item => item.id !== notification.id)]
        .sort((a, b) => new Date(b.createdUtc) - new Date(a.createdUtc));
      if (!isExisting) {
        this.totalCount += 1;
      }
      this.render();
    }

    handleNotificationList() {
      // The full-list hub message is intended for compact bell clients. The centre keeps its
      // server-side filters and cursor state and therefore refreshes instead of replacing rows.
    }

    handleStateMutation(mutation) {
      const ids = new Set(mutation.notificationIds);
      this.items.forEach(item => {
        if ((!mutation.appliesToAll && !ids.has(item.id))
            || (mutation.appliesToAll && item.isProjectMuted)) {
          return;
        }
        item.isRead = mutation.isRead;
        item.readUtc = mutation.isRead ? mutation.readUtc || new Date().toISOString() : null;
        if (mutation.seenUtc) {
          item.isSeen = true;
          item.seenUtc = mutation.seenUtc;
        }
      });

      if (mutation.appliesToAll && this.filters.status === 'unread' && mutation.isRead) {
        this.items = this.items.filter(item => item.isProjectMuted);
        this.totalCount = Math.max(0, this.totalCount - mutation.affectedCount);
        this.nextCursor = null;
        this.hasMore = this.totalCount > this.items.length;
        this.newNotificationRefresh();
      } else if (mutation.appliesToAll && this.filters.status === 'read' && mutation.isRead) {
        // Previously-unread rows now qualify for this server-side filter. Refresh rather than
        // presenting an incomplete local projection.
        this.newNotificationRefresh();
      } else if ((this.filters.status === 'unread' && mutation.isRead)
          || (this.filters.status === 'read' && !mutation.isRead)) {
        const removed = this.items.filter(item => ids.has(item.id)).length;
        this.items = this.items.filter(item => !ids.has(item.id));
        this.totalCount = Math.max(0, this.totalCount - removed);
      }

      this.selectedIds = new Set(Array.from(this.selectedIds).filter(id => this.items.some(item => item.id === id)));
      this.render();
    }

    handleSeenMutation(mutation) {
      const ids = new Set(mutation.notificationIds);
      this.items.forEach(item => {
        if (ids.has(item.id)) {
          item.isSeen = true;
          item.seenUtc = mutation.seenUtc;
        }
      });
    }

    handleMuteMutation(mutation) {
      const changed = new Set(mutation.changedNotificationIds);
      this.items.forEach(item => {
        if (item.projectId === mutation.projectId) {
          item.isProjectMuted = mutation.isMuted;
        }
        if (changed.has(item.id)) {
          item.isRead = true;
          item.readUtc = new Date().toISOString();
        }
      });

      this.projects = this.projects.map(project => project.id === mutation.projectId
        ? { ...project, isMuted: mutation.isMuted }
        : project);
      this.updateFilterOptions();

      if (this.filters.status === 'muted' && !mutation.isMuted) {
        const removed = this.items.filter(item => item.projectId === mutation.projectId).length;
        this.items = this.items.filter(item => item.projectId !== mutation.projectId);
        this.totalCount = Math.max(0, this.totalCount - removed);
      } else if (this.filters.status === 'unread' && mutation.changedNotificationIds.length > 0) {
        const changed = new Set(mutation.changedNotificationIds);
        const removed = this.items.filter(item => changed.has(item.id)).length;
        this.items = this.items.filter(item => !changed.has(item.id));
        this.totalCount = Math.max(0, this.totalCount - removed);
      }

      this.selectedIds = new Set(Array.from(this.selectedIds).filter(id => this.items.some(item => item.id === id)));
      this.render();
    }

    setUnreadCount(count) {
      setText(this.unreadCountElement, count);
      if (this.markAllButton) {
        this.markAllButton.disabled = count <= 0;
      }
    }

    render() {
      if (!this.rows || !this.template) {
        return;
      }

      this.rows.innerHTML = '';
      this.items.forEach(notification => {
        const row = this.template.content.firstElementChild.cloneNode(true);
        this.populateRow(row, notification);
        this.rows.appendChild(row);
      });

      setHidden(this.empty, this.items.length !== 0);
      setText(this.totalCountElement, this.totalCount);
      if (this.summary) {
        this.summary.textContent = `Showing ${this.items.length} of ${this.totalCount} notification${this.totalCount === 1 ? '' : 's'}`;
      }
      if (this.loadMoreButton) {
        this.loadMoreButton.hidden = !this.hasMore;
      }
      this.renderSelection();
    }

    populateRow(row, notification) {
      row.dataset.notificationId = String(notification.id);
      row.dataset.projectId = notification.projectId == null ? '' : String(notification.projectId);
      row.dataset.read = String(notification.isRead);
      row.dataset.muted = String(notification.isProjectMuted);
      row.classList.toggle('is-read', notification.isRead);
      row.classList.toggle('is-unread', !notification.isRead);

      const checkbox = row.querySelector('[data-notification-select]');
      if (checkbox) {
        checkbox.value = String(notification.id);
        checkbox.checked = this.selectedIds.has(notification.id);
      }

      setIcon(row.querySelector('[data-notification-icon]'), notification.iconCssClass);
      setText(row.querySelector('[data-notification-category]'), notification.category);

      const project = row.querySelector('[data-notification-project]');
      const projectSeparator = row.querySelector('[data-notification-project-separator]');
      setText(project, notification.projectName);
      setHidden(project, !notification.projectName);
      setHidden(projectSeparator, !notification.projectName);

      const actionRequired = row.querySelector('[data-notification-action-required]');
      setHidden(actionRequired, !notification.isActionRequired);

      const title = row.querySelector('[data-notification-title]');
      setText(title, notification.title || 'Notification');
      title?.setAttribute('href', safeRoute(notification.route, this.centerUrl));

      const summary = row.querySelector('[data-notification-summary]');
      setText(summary, notification.summary);
      setHidden(summary, !notification.summary);

      const actor = row.querySelector('[data-notification-actor]');
      const actorSeparator = row.querySelector('[data-notification-actor-separator]');
      setText(actor, notification.actorDisplayName);
      setHidden(actor, !notification.actorDisplayName);
      setHidden(actorSeparator, !notification.actorDisplayName);

      setTimeElement(row.querySelector('[data-notification-created]'), notification);
      setHidden(row.querySelector('[data-notification-muted]'), !notification.isProjectMuted);

      const state = row.querySelector('[data-notification-state]');
      if (state) {
        state.textContent = notification.isRead ? 'Read' : 'Unread';
        state.classList.toggle('is-read', notification.isRead);
        state.classList.toggle('is-unread', !notification.isRead);
      }
    }

    renderSelection() {
      this.rows?.querySelectorAll('[data-notification-select]').forEach(checkbox => {
        checkbox.checked = this.selectedIds.has(asInteger(checkbox.value));
      });

      if (this.selectAll) {
        const loadedIds = this.items.map(item => item.id);
        const selectedLoaded = loadedIds.filter(id => this.selectedIds.has(id)).length;
        this.selectAll.checked = loadedIds.length > 0 && selectedLoaded === loadedIds.length;
        this.selectAll.indeterminate = selectedLoaded > 0 && selectedLoaded < loadedIds.length;
      }

      setText(this.selectionCount, `${this.selectedIds.size} selected`);
      this.updateBulkState();
    }

    updateBulkState() {
      const selected = this.items.filter(item => this.selectedIds.has(item.id));
      const hasSelection = selected.length > 0;
      const hasUnread = selected.some(item => !item.isRead);
      const hasRead = selected.some(item => item.isRead);
      const canMute = selected.some(item => item.projectId != null && !item.isProjectMuted);
      const canUnmute = selected.some(item => item.projectId != null && item.isProjectMuted);

      this.bulkButtons.forEach(button => {
        const action = button.dataset.notificationBulk;
        button.disabled = !hasSelection
          || (action === 'mark-read' && !hasUnread)
          || (action === 'mark-unread' && !hasRead)
          || (action === 'mute-project' && !canMute)
          || (action === 'unmute-project' && !canUnmute);
      });
    }

    setBulkDisabled(disabled) {
      this.bulkButtons.forEach(button => {
        button.disabled = disabled;
      });
    }

    updateFilterOptions() {
      const projectSelect = this.filtersForm?.querySelector('[data-filter-project]');
      if (projectSelect) {
        const selected = projectSelect.value;
        projectSelect.innerHTML = '<option value="">All projects</option>';
        this.projects.forEach(project => {
          const option = document.createElement('option');
          option.value = String(project.id);
          option.textContent = `${project.label}${project.isMuted ? ' — muted' : ''}`;
          projectSelect.appendChild(option);
        });
        projectSelect.value = selected;
      }

      const moduleSelect = this.filtersForm?.querySelector('[data-filter-module]');
      if (moduleSelect) {
        const selected = moduleSelect.value;
        moduleSelect.innerHTML = '<option value="">All modules</option>';
        this.modules.forEach(module => {
          const option = document.createElement('option');
          option.value = module;
          option.textContent = module;
          moduleSelect.appendChild(option);
        });
        moduleSelect.value = selected;
      }
    }

    setLoading(loading) {
      setHidden(this.loading, !loading);
    }

    setStatus(message, tone = 'muted') {
      if (!this.status) {
        return;
      }

      window.clearTimeout(this.statusTimer);
      this.status.textContent = message || '';
      this.status.className = `notification-centre__status text-${tone}`;
      if (message) {
        this.statusTimer = window.setTimeout(() => {
          this.status.textContent = '';
        }, STATUS_CLEAR_MS);
      }
    }
  }

  function initialize() {
    const bellRoots = Array.from(document.querySelectorAll('[data-notification-bell]'));
    const centerRoots = Array.from(document.querySelectorAll('[data-notification-center]'));
    const firstRoot = bellRoots[0] || centerRoots[0];
    if (!firstRoot) {
      return;
    }

    const runtime = new NotificationRuntime({
      apiBase: firstRoot.dataset.apiBase || '/api/notifications',
      unreadUrl: firstRoot.dataset.unreadUrl || '/api/notifications/count',
      hubUrl: firstRoot.dataset.hubUrl || '/hubs/notifications',
      unreadCount: asInteger(firstRoot.dataset.unreadCount)
    });

    bellRoots.forEach(root => runtime.register(new NotificationBellView(root, runtime)));
    centerRoots.forEach(root => runtime.register(new NotificationCenterView(root, runtime)));
    runtime.start();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize, { once: true });
  } else {
    initialize();
  }
})();
