(() => {
  'use strict';

  const CSRF_HEADER = 'X-CSRF-TOKEN';
  const POLL_INTERVAL_MS = 60_000;
  const SEARCH_DEBOUNCE_MS = 300;
  const STATUS_CLEAR_MS = 3_000;
  const IST_FORMATTER = new Intl.DateTimeFormat('en-IN', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'Asia/Kolkata'
  });
  const IST_DATE_PARTS_FORMATTER = new Intl.DateTimeFormat('en-CA', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
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

  function formatCompactCount(value) {
    const count = Math.max(0, asInteger(value));
    if (count > 999) {
      return '999+';
    }

    return String(count);
  }

  function isModifiedActivation(event) {
    const button = typeof event?.button === 'number' ? event.button : 0;
    return Boolean(event?.ctrlKey || event?.metaKey || event?.shiftKey || event?.altKey || button !== 0);
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
      summaryTooltip: raw?.summaryTooltip ?? raw?.SummaryTooltip ?? null,
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
      modules: asArray(raw?.modules ?? raw?.Modules).map(String),
      folders: asArray(raw?.folders ?? raw?.Folders).map(folder => ({
        key: String(folder?.key ?? folder?.Key ?? '').toLowerCase(),
        totalCount: asInteger(folder?.totalCount ?? folder?.TotalCount),
        unreadCount: asInteger(folder?.unreadCount ?? folder?.UnreadCount)
      })).filter(folder => folder.key)
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

  function dateParts(value) {
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) {
      return null;
    }

    const parts = Object.fromEntries(
      IST_DATE_PARTS_FORMATTER.formatToParts(date)
        .filter(part => part.type !== 'literal')
        .map(part => [part.type, asInteger(part.value)]));

    if (!parts.year || !parts.month || !parts.day) {
      return null;
    }

    return parts;
  }

  function notificationDateGroup(value) {
    const item = dateParts(value);
    const today = dateParts(new Date());
    if (!item || !today) {
      return { key: 'earlier', label: 'Earlier' };
    }

    const itemDay = Date.UTC(item.year, item.month - 1, item.day);
    const todayDay = Date.UTC(today.year, today.month - 1, today.day);
    const difference = Math.round((todayDay - itemDay) / 86_400_000);

    if (difference <= 0) {
      return { key: 'today', label: 'Today' };
    }

    if (difference === 1) {
      return { key: 'yesterday', label: 'Yesterday' };
    }

    return { key: 'earlier', label: 'Earlier' };
  }

  function appendDateGroup(container, group, className, tagName = 'div') {
    const heading = document.createElement(tagName);
    heading.className = className;
    heading.dataset.notificationDateGroup = group.key;
    heading.setAttribute('role', 'heading');
    heading.setAttribute('aria-level', '2');
    heading.textContent = group.label;
    container.appendChild(heading);
  }

  function statusToneClass(tone) {
    if (tone === 'danger' || tone === 'error') {
      return 'is-error';
    }

    if (tone === 'success') {
      return 'is-success';
    }

    return 'is-muted';
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
    if (!contentType.toLowerCase().includes('json')) {
      return null;
    }

    try {
      return await response.json();
    } catch (error) {
      console.warn('Notification API returned an invalid JSON response.', error);
      return null;
    }
  }


  function firstApiError(payload) {
    if (!payload || typeof payload !== 'object') {
      return null;
    }

    if (typeof payload.error === 'string' && payload.error.trim()) {
      return payload.error.trim();
    }

    const errors = payload.errors;
    if (errors && typeof errors === 'object') {
      for (const value of Object.values(errors)) {
        const messages = Array.isArray(value) ? value : [value];
        const message = messages.find(item => typeof item === 'string' && item.trim());
        if (message) {
          return message.trim();
        }
      }
    }

    if (typeof payload.detail === 'string' && payload.detail.trim()) {
      return payload.detail.trim();
    }

    if (typeof payload.title === 'string' && payload.title.trim()) {
      return payload.title.trim();
    }

    return null;
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
      this.startPromise = null;
      this.reconnectAttempt = 0;
      this.lifecycleBound = false;
      this.unreadCount = config.unreadCount || 0;
    }

    register(view) {
      this.views.add(view);
      view.setUnreadCount(this.unreadCount);
    }

    async request(path, options = {}) {
      const method = (options.method || 'GET').toUpperCase();
      const headers = new Headers(options.headers || {});
      if (!headers.has('Accept')) {
        headers.set('Accept', 'application/json');
      }

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
        let message = firstApiError(payload);
        if (!message && response.status === 401) {
          message = 'Your sign-in session has expired. Refresh the page and sign in again.';
        } else if (!message && response.status === 403) {
          message = 'You are not authorised to update this notification.';
        } else if (!message && response.status === 400 && method !== 'GET' && method !== 'HEAD') {
          message = 'Security validation failed. Refresh the page and try again.';
        }

        throw new Error(message || `Notification request failed (${response.status}).`);
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
      this.bindLifecycle();

      if (!this.hubUrl || !window.signalR?.HubConnectionBuilder) {
        this.startPolling();
        return;
      }

      const reconnectPolicy = {
        nextRetryDelayInMilliseconds: context => {
          if (!this.canAttemptRealtime()) {
            return null;
          }

          const delays = [0, 2_000, 10_000, 30_000, 60_000];
          const baseDelay = delays[Math.min(context.previousRetryCount, delays.length - 1)];
          return baseDelay + Math.floor(Math.random() * 750);
        }
      };

      let builder = new window.signalR.HubConnectionBuilder()
        .withUrl(this.hubUrl)
        .withAutomaticReconnect(reconnectPolicy);

      if (typeof builder.configureLogging === 'function' && window.signalR.LogLevel) {
        // SignalR logs the same transient transport interruption at several layers. PRISM owns the
        // lifecycle and reports one actionable message only when the failure is not a known browser
        // suspension/disconnect condition.
        builder = builder.configureLogging(window.signalR.LogLevel.None);
      }

      this.connection = builder.build();
      this.connection.serverTimeoutInMilliseconds = 90_000;
      this.connection.keepAliveIntervalInMilliseconds = 15_000;

      this.connection.on('ReceiveUnreadCount', count => this.setUnreadCount(count));
      this.connection.on('ReceiveNotification', notification => this.handleNewNotification(notification));
      this.connection.on('ReceiveNotifications', notifications => this.handleNotificationList(notifications));
      this.connection.on('ReceiveNotificationStateChanged', mutation => this.handleStateMutation(mutation));
      this.connection.on('ReceiveNotificationSeen', mutation => this.handleSeenMutation(mutation));
      this.connection.on('ReceiveProjectMuteChanged', mutation => this.handleMuteMutation(mutation));

      this.connection.onreconnecting(error => {
        this.startPolling();
        this.reportRealtimeIssue(error);
      });
      this.connection.onreconnected(() => {
        this.reconnectAttempt = 0;
        this.clearReconnectTimer();
        this.stopPolling();
        this.refreshAll();
      });
      this.connection.onclose(error => {
        this.startPolling();
        this.reportRealtimeIssue(error);
        this.scheduleReconnect();
      });

      await this.tryStartRealtime();
    }

    bindLifecycle() {
      if (this.lifecycleBound) {
        return;
      }

      this.lifecycleBound = true;
      window.addEventListener('online', () => {
        this.refreshAll();
        this.tryStartRealtime();
      });
      window.addEventListener('offline', () => {
        this.clearReconnectTimer();
        this.stopPolling();
      });
      document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
          this.clearReconnectTimer();
          this.stopPolling();
          return;
        }

        this.refreshAll();
        if (this.connection?.state === window.signalR?.HubConnectionState?.Disconnected) {
          this.tryStartRealtime();
        } else if (!this.connection) {
          this.startPolling();
        }
      });
    }

    canAttemptRealtime() {
      return navigator.onLine !== false && document.visibilityState !== 'hidden';
    }

    async tryStartRealtime() {
      if (!this.connection || !this.canAttemptRealtime()) {
        this.startPolling();
        return;
      }

      if (this.connection.state !== window.signalR.HubConnectionState.Disconnected) {
        return;
      }

      if (this.startPromise) {
        return this.startPromise;
      }

      this.startPromise = this.connection.start()
        .then(() => {
          this.reconnectAttempt = 0;
          this.clearReconnectTimer();
          this.stopPolling();
          this.refreshAll();
        })
        .catch(error => {
          this.startPolling();
          this.reportRealtimeIssue(error);
          this.scheduleReconnect();
        })
        .finally(() => {
          this.startPromise = null;
        });

      return this.startPromise;
    }

    scheduleReconnect() {
      if (!this.connection || this.reconnectTimer || !this.canAttemptRealtime()) {
        return;
      }

      const delays = [15_000, 30_000, 60_000, 120_000, 300_000];
      const delay = delays[Math.min(this.reconnectAttempt, delays.length - 1)]
        + Math.floor(Math.random() * 1_000);
      this.reconnectAttempt += 1;

      this.reconnectTimer = window.setTimeout(() => {
        this.reconnectTimer = null;
        this.tryStartRealtime();
      }, delay);
    }

    clearReconnectTimer() {
      if (!this.reconnectTimer) {
        return;
      }

      window.clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    reportRealtimeIssue(error) {
      const message = String(error?.message || error || '');
      if (!message || /failed to fetch|network_io_suspended|websocket closed with status code: 1006|connection disconnected/i.test(message)) {
        return;
      }

      console.warn('Notification realtime connection was interrupted; polling remains active.', error);
    }

    startPolling() {
      if (this.pollTimer || navigator.onLine === false || document.visibilityState === 'hidden') {
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
      if (navigator.onLine === false || document.visibilityState === 'hidden') {
        return;
      }

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
      this.trigger = root.querySelector('.notification-bell__trigger');
      this.dropdownMenu = root.querySelector('.notification-bell__dropdown');
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

      this.trigger?.addEventListener('shown.bs.dropdown', () => {
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
      if (!this.markAllButton || this.runtime.unreadCount <= 0) {
        return;
      }

      this.markAllButton.disabled = true;
      this.markAllButton.setAttribute('aria-busy', 'true');
      this.setStatus('Marking all as read…', 'muted');
      try {
        const mutation = await this.runtime.markAllRead();
        this.setStatus(
          mutation.affectedCount > 0
            ? 'All notifications marked as read'
            : 'No unread notifications',
          'success');
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notifications.', 'danger');
      } finally {
        this.markAllButton.removeAttribute('aria-busy');
        this.updateMarkAllButton(this.runtime.unreadCount);
      }
    }

    async toggleRead(button) {
      const id = asInteger(button.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      if (!notification) {
        return;
      }

      button.disabled = true;
      button.setAttribute('aria-busy', 'true');
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
        button.removeAttribute('aria-busy');
      }
    }

    hideDropdown() {
      if (!this.trigger) {
        return;
      }

      if (window.bootstrap?.Dropdown) {
        window.bootstrap.Dropdown.getOrCreateInstance(this.trigger).hide();
        return;
      }

      this.trigger.setAttribute('aria-expanded', 'false');
      this.trigger.classList.remove('show');
      this.dropdownMenu?.classList.remove('show');
    }

    openNotification(event, link) {
      const row = link.closest('[data-notification-item]');
      const id = asInteger(row?.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      const route = safeRoute(link.getAttribute('href'), this.centerUrl);
      const modifiedClick = isModifiedActivation(event);

      if (modifiedClick) {
        if (notification && !notification.isRead) {
          this.runtime.markRead([id], { keepalive: true }).catch(() => {});
        }
        return;
      }

      event.preventDefault();
      this.hideDropdown();

      if (!notification || notification.isRead) {
        window.location.assign(route);
        return;
      }

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
      setText(this.unreadCount, formatCompactCount(count));
      setText(this.headerCount, count);
      this.unreadBadge?.classList.toggle('d-none', count <= 0);
      this.updateMarkAllButton(count);
    }

    updateMarkAllButton(count) {
      if (!this.markAllButton) {
        return;
      }

      const disabled = count <= 0;
      const label = disabled
        ? 'No unread notifications'
        : 'Mark all notifications as read';
      this.markAllButton.disabled = disabled;
      this.markAllButton.setAttribute('aria-disabled', String(disabled));
      this.markAllButton.setAttribute('aria-label', label);
      this.markAllButton.title = label;
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
      let currentGroup = null;
      visible.forEach(notification => {
        const group = notificationDateGroup(notification.createdUtc);
        if (group.key !== currentGroup) {
          appendDateGroup(this.list, group, 'notification-menu__date-group', 'li');
          currentGroup = group.key;
        }

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
      if (summary) {
        const tooltip = notification.summaryTooltip || notification.summary || '';
        if (tooltip) {
          summary.title = tooltip;
        } else {
          summary.removeAttribute('title');
        }
      }

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
        setIcon(button.querySelector('.bi'), notification.isRead ? 'bi bi-envelope-open' : 'bi bi-envelope-check');
      }

      setHidden(row.querySelector('[data-notification-unread-dot]'), notification.isRead);
    }

    setStatus(message, tone = 'muted') {
      if (!this.status) {
        return;
      }

      window.clearTimeout(this.statusTimer);
      this.status.textContent = message || '';
      this.status.className = `notification-menu__status ${statusToneClass(tone)}`;
      if (message) {
        this.statusTimer = window.setTimeout(() => {
          this.status.textContent = '';
          this.status.className = 'notification-menu__status';
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
      this.totalCountElements = Array.from(root.querySelectorAll('[data-notification-total-count]'));
      this.totalLabelElements = Array.from(root.querySelectorAll('[data-notification-total-label]'));
      this.unreadCountElements = Array.from(root.querySelectorAll('[data-notification-unread-count]'));
      this.loadMoreButton = root.querySelector('[data-notification-load-more]');
      this.selectAll = root.querySelector('[data-select-all]');
      this.selectionCount = root.querySelector('[data-selection-count]');
      this.bulkButtons = Array.from(root.querySelectorAll('[data-notification-bulk]'));
      this.markAllButton = root.querySelector('[data-notification-action="mark-all-read"]');
      this.filtersForm = root.querySelector('[data-notification-filters]');
      this.clearFiltersButton = root.querySelector('[data-notification-action="clear-filters"]');
      this.activeFilterChips = root.querySelector('[data-active-filter-chips]');
      this.defaultActions = root.querySelector('[data-default-actions]');
      this.selectionActions = root.querySelector('[data-selection-actions]');
      this.folderButtons = Array.from(root.querySelectorAll('[data-notification-folder]'));
      this.folderCountElements = Array.from(root.querySelectorAll('[data-folder-count]'));
      this.statusTimer = null;
      this.newNotificationRefresh = debounce(() => this.refresh({ silent: true }), 450);
      this.selectedIds = new Set();
      this.filters = {
        folder: root.dataset.activeFolder || 'inbox',
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
      this.folders = initial.folders;

      this.bind();
      this.updateFolderNavigation();
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
          this.renderSelection();
        }
      });

      this.root.addEventListener('click', event => {
        const folder = event.target.closest('[data-notification-folder]');
        if (folder && this.root.contains(folder)) {
          event.preventDefault();
          this.setFolder(folder.dataset.notificationFolder);
          return;
        }

        const filterChip = event.target.closest('[data-notification-filter-chip]');
        if (filterChip && this.root.contains(filterChip)) {
          event.preventDefault();
          this.clearSingleFilter(filterChip.dataset.notificationFilterChip);
          return;
        }

        const rowAction = event.target.closest('[data-notification-row-action]');
        if (rowAction && this.root.contains(rowAction)) {
          event.preventDefault();
          event.stopPropagation();
          this.handleRowAction(rowAction);
          return;
        }

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

        const openNew = event.target.closest('[data-notification-open-new]');
        if (openNew && this.root.contains(openNew)) {
          const row = openNew.closest('[data-notification-row]');
          const id = asInteger(row?.dataset.notificationId);
          const notification = this.items.find(item => item.id === id);
          if (notification && !notification.isRead) {
            this.runtime.markRead([id], { keepalive: true }).catch(() => {});
          }
          return;
        }

        const link = event.target.closest('[data-notification-link]');
        if (link && this.root.contains(link)) {
          this.openNotification(event, link);
          return;
        }

        const row = event.target.closest('[data-notification-row]');
        if (row && this.root.contains(row)
            && !event.target.closest('a, button, input, label, select, textarea')) {
          const rowLink = row.querySelector('[data-notification-link]');
          if (rowLink) {
            this.openNotification(event, rowLink);
          }
        }
      });

      this.root.addEventListener('keydown', event => {
        const row = event.target.closest('[data-notification-row]');
        if (!row || !this.root.contains(row)
            || event.target.closest('a, button, input, select, textarea')) {
          return;
        }

        if (event.key === 'Enter') {
          const link = row.querySelector('[data-notification-link]');
          if (link) {
            this.openNotification(event, link);
          }
        } else if (event.key === ' ') {
          event.preventDefault();
          const checkbox = row.querySelector('[data-notification-select]');
          if (checkbox) {
            checkbox.checked = !checkbox.checked;
            checkbox.dispatchEvent(new Event('change', { bubbles: true }));
          }
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
        this.filtersForm.addEventListener('submit', event => event.preventDefault());
        const status = this.filtersForm.querySelector('[data-filter-status]');
        const project = this.filtersForm.querySelector('[data-filter-project]');
        const module = this.filtersForm.querySelector('[data-filter-module]');
        const search = this.filtersForm.querySelector('[data-filter-search]');

        status?.addEventListener('change', () => {
          this.filters.status = status.value || 'all';
          this.renderFilterState();
          this.refresh();
        });
        project?.addEventListener('change', () => {
          this.filters.projectId = project.value || '';
          this.renderFilterState();
          this.refresh();
        });
        module?.addEventListener('change', () => {
          this.filters.module = module.value || '';
          this.renderFilterState();
          this.refresh();
        });
        search?.addEventListener('input', debounce(() => {
          this.filters.search = search.value.trim();
          this.renderFilterState();
          this.refresh();
        }, SEARCH_DEBOUNCE_MS));
      }
    }

    setFolder(folder) {
      const normalized = String(folder || 'inbox').toLowerCase();
      if (normalized === this.filters.folder) {
        return;
      }

      this.filters.folder = normalized;
      this.root.dataset.activeFolder = normalized;
      this.selectedIds.clear();

      if (normalized === 'unread' || normalized === 'muted') {
        this.filters.status = 'all';
        const status = this.filtersForm?.querySelector('[data-filter-status]');
        if (status) {
          status.value = 'all';
        }
      }

      this.updateFolderNavigation();
      this.renderFilterState();
      this.refresh();
    }

    queryParameters(cursor = null) {
      return {
        limit: this.pageSize,
        cursor,
        folder: this.filters.folder,
        status: this.filters.status === 'all' ? '' : this.filters.status,
        projectId: this.filters.projectId,
        module: this.filters.module,
        search: this.filters.search,
        includeMuted: this.filters.folder === 'all' || this.filters.folder === 'muted',
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
      if (!this.hasMore || !this.nextCursor || !this.loadMoreButton) {
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
        if (page.folders.length > 0) {
          this.folders = page.folders;
        }
        this.updateFilterOptions();
        this.updateFolderNavigation();
      }
      this.runtime.setUnreadCount(page.unreadCount);
      this.render();
    }

    hasActiveFilters() {
      return this.filters.folder !== 'inbox'
        || this.filters.status !== 'all'
        || Boolean(this.filters.projectId)
        || Boolean(this.filters.module)
        || Boolean(this.filters.search);
    }

    clearFilters() {
      this.filters.folder = 'inbox';
      this.root.dataset.activeFolder = 'inbox';
      this.filters.status = 'all';
      this.filters.projectId = '';
      this.filters.module = '';
      this.filters.search = '';
      this.syncFilterControls();
      this.selectedIds.clear();
      this.updateFolderNavigation();
      this.renderFilterState();
      this.refresh();
    }

    clearSingleFilter(key) {
      switch (key) {
        case 'folder':
          this.filters.folder = 'inbox';
          this.root.dataset.activeFolder = 'inbox';
          break;
        case 'status':
          this.filters.status = 'all';
          break;
        case 'project':
          this.filters.projectId = '';
          break;
        case 'module':
          this.filters.module = '';
          break;
        case 'search':
          this.filters.search = '';
          break;
        default:
          return;
      }

      this.syncFilterControls();
      this.selectedIds.clear();
      this.updateFolderNavigation();
      this.renderFilterState();
      this.refresh();
    }

    syncFilterControls() {
      const status = this.filtersForm?.querySelector('[data-filter-status]');
      const project = this.filtersForm?.querySelector('[data-filter-project]');
      const module = this.filtersForm?.querySelector('[data-filter-module]');
      const search = this.filtersForm?.querySelector('[data-filter-search]');
      if (status) status.value = this.filters.status;
      if (project) project.value = this.filters.projectId;
      if (module) module.value = this.filters.module;
      if (search) search.value = this.filters.search;
    }

    selectedOptionText(selector) {
      const select = this.filtersForm?.querySelector(selector);
      return select?.selectedOptions?.[0]?.textContent?.trim() || '';
    }

    folderLabel() {
      const active = this.folderButtons.find(button => button.dataset.notificationFolder === this.filters.folder);
      const label = active?.querySelector('span:not(.notification-folder__count)');
      return label?.textContent?.trim() || 'Inbox';
    }

    renderFilterState() {
      const active = this.hasActiveFilters();
      if (this.clearFiltersButton) {
        this.clearFiltersButton.disabled = !active;
        const label = active ? 'Clear search and filters' : 'No active filters';
        this.clearFiltersButton.title = label;
        this.clearFiltersButton.setAttribute('aria-label', label);
      }

      this.totalLabelElements.forEach(element => {
        element.textContent = active
          ? 'matching'
          : (this.totalCount === 1 ? 'notification' : 'notifications');
      });

      if (!this.activeFilterChips) {
        return;
      }

      this.activeFilterChips.innerHTML = '';
      const chips = [];
      if (this.filters.folder !== 'inbox') {
        chips.push({ key: 'folder', label: this.folderLabel() });
      }
      if (this.filters.status !== 'all') {
        chips.push({ key: 'status', label: this.selectedOptionText('[data-filter-status]') || this.filters.status });
      }
      if (this.filters.projectId) {
        chips.push({ key: 'project', label: this.selectedOptionText('[data-filter-project]') || 'Project' });
      }
      if (this.filters.module) {
        chips.push({ key: 'module', label: this.selectedOptionText('[data-filter-module]') || this.filters.module });
      }
      if (this.filters.search) {
        chips.push({ key: 'search', label: `Search: “${this.filters.search}”` });
      }

      chips.forEach(chip => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'notification-inbox__filter-chip';
        button.dataset.notificationFilterChip = chip.key;
        button.title = `Remove ${chip.label} filter`;
        button.setAttribute('aria-label', `Remove ${chip.label} filter`);

        const label = document.createElement('span');
        label.textContent = chip.label;
        button.appendChild(label);

        const icon = document.createElement('i');
        icon.className = 'bi bi-x';
        icon.setAttribute('aria-hidden', 'true');
        button.appendChild(icon);
        this.activeFilterChips.appendChild(button);
      });

      this.activeFilterChips.hidden = chips.length === 0;
    }

    async markAllRead() {
      if (!this.markAllButton || this.runtime.unreadCount <= 0) {
        return;
      }

      this.markAllButton.disabled = true;
      this.markAllButton.setAttribute('aria-busy', 'true');
      this.setStatus('Marking all notifications as read…', 'muted');
      try {
        const result = await this.runtime.markAllRead();
        this.selectedIds.clear();
        this.setStatus(
          result.affectedCount > 0
            ? 'All notifications marked as read.'
            : 'No unread notifications remain.',
          'success');
        this.newNotificationRefresh();
      } catch (error) {
        this.setStatus(error.message || 'Unable to mark all notifications as read.', 'danger');
      } finally {
        this.markAllButton.removeAttribute('aria-busy');
        this.setUnreadCount(this.runtime.unreadCount);
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
        this.newNotificationRefresh();
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notifications.', 'danger');
      } finally {
        this.setBulkDisabled(false);
        this.updateBulkState();
      }
    }

    async handleRowAction(button) {
      const row = button.closest('[data-notification-row]');
      const id = asInteger(row?.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      if (!notification) {
        return;
      }

      button.disabled = true;
      button.setAttribute('aria-busy', 'true');
      try {
        if (button.dataset.notificationRowAction === 'toggle-read') {
          if (notification.isRead) {
            await this.runtime.markUnread([id]);
            this.setStatus('Notification marked unread.', 'success');
          } else {
            await this.runtime.markRead([id]);
            this.setStatus('Notification marked read.', 'success');
          }
        } else if (button.dataset.notificationRowAction === 'toggle-mute') {
          if (notification.projectId == null) {
            throw new Error('This notification is not linked to a project.');
          }
          await this.runtime.muteProject(notification.projectId, !notification.isProjectMuted);
          this.setStatus(notification.isProjectMuted ? 'Project muted.' : 'Project unmuted.', 'success');
        }
        this.newNotificationRefresh();
      } catch (error) {
        this.setStatus(error.message || 'Unable to update notification.', 'danger');
      } finally {
        if (button.isConnected) {
          button.disabled = false;
          button.removeAttribute('aria-busy');
        }
      }
    }

    openNotification(event, link) {
      const row = link.closest('[data-notification-row]');
      const id = asInteger(row?.dataset.notificationId);
      const notification = this.items.find(item => item.id === id);
      const route = safeRoute(link.getAttribute('href'), this.centerUrl);

      if (isModifiedActivation(event)) {
        if (notification && !notification.isRead) {
          this.runtime.markRead([id], { keepalive: true }).catch(() => {});
        }
        return;
      }

      event.preventDefault();
      if (!notification || notification.isRead) {
        window.location.assign(route);
        return;
      }

      Promise.race([
        this.runtime.markRead([id]),
        new Promise(resolve => window.setTimeout(resolve, 500))
      ])
        .catch(() => {})
        .finally(() => window.location.assign(route));
    }

    handleNewNotification(notification) {
      const hasActiveFilter = this.hasActiveFilters();

      if (!hasActiveFilter && !notification.isProjectMuted) {
        const isExisting = this.items.some(item => item.id === notification.id);
        this.items = [notification, ...this.items.filter(item => item.id !== notification.id)]
          .sort((a, b) => new Date(b.createdUtc) - new Date(a.createdUtc));
        if (!isExisting) {
          this.totalCount += 1;
        }
        this.render();
      }

      this.newNotificationRefresh();
    }

    handleNotificationList() {
      // The centre keeps its server-side folder, filter and cursor state.
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

      const folderRequiresUnread = this.filters.folder === 'unread';
      if ((folderRequiresUnread || this.filters.status === 'unread') && mutation.isRead) {
        const removed = mutation.appliesToAll
          ? this.items.filter(item => !item.isProjectMuted).length
          : this.items.filter(item => ids.has(item.id)).length;
        this.items = mutation.appliesToAll
          ? this.items.filter(item => item.isProjectMuted)
          : this.items.filter(item => !ids.has(item.id));
        this.totalCount = Math.max(0, this.totalCount - removed);
      } else if (this.filters.status === 'read' && !mutation.isRead) {
        const removed = this.items.filter(item => ids.has(item.id)).length;
        this.items = this.items.filter(item => !ids.has(item.id));
        this.totalCount = Math.max(0, this.totalCount - removed);
      }

      this.selectedIds = new Set(Array.from(this.selectedIds).filter(id => this.items.some(item => item.id === id)));
      this.render();
      this.newNotificationRefresh();
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

      if (this.filters.folder === 'inbox' && mutation.isMuted) {
        const removed = this.items.filter(item => item.projectId === mutation.projectId).length;
        this.items = this.items.filter(item => item.projectId !== mutation.projectId);
        this.totalCount = Math.max(0, this.totalCount - removed);
      } else if (this.filters.folder === 'muted' && !mutation.isMuted) {
        const removed = this.items.filter(item => item.projectId === mutation.projectId).length;
        this.items = this.items.filter(item => item.projectId !== mutation.projectId);
        this.totalCount = Math.max(0, this.totalCount - removed);
      }

      this.selectedIds = new Set(Array.from(this.selectedIds).filter(id => this.items.some(item => item.id === id)));
      this.render();
      this.newNotificationRefresh();
    }

    setUnreadCount(count) {
      this.unreadCountElements.forEach(element => setText(element, count));
      if (this.markAllButton) {
        const disabled = count <= 0;
        const label = disabled
          ? 'No unread notifications'
          : 'Mark all notifications as read';
        this.markAllButton.disabled = disabled;
        this.markAllButton.setAttribute('aria-disabled', String(disabled));
        this.markAllButton.setAttribute('aria-label', label);
        this.markAllButton.title = label;
      }

      const inbox = this.folders.find(folder => folder.key === 'inbox');
      if (inbox) {
        inbox.unreadCount = count;
      }
      const unread = this.folders.find(folder => folder.key === 'unread');
      if (unread) {
        unread.totalCount = count;
        unread.unreadCount = count;
      }
      this.updateFolderNavigation();
    }

    render() {
      if (!this.rows || !this.template) {
        return;
      }

      this.rows.innerHTML = '';
      let currentGroup = null;
      this.items.forEach(notification => {
        const group = notificationDateGroup(notification.createdUtc);
        if (group.key !== currentGroup) {
          appendDateGroup(this.rows, group, 'notification-inbox__date-group');
          currentGroup = group.key;
        }

        const row = this.template.content.firstElementChild.cloneNode(true);
        this.populateRow(row, notification);
        this.rows.appendChild(row);
      });

      setHidden(this.empty, this.items.length !== 0);
      this.totalCountElements.forEach(element => setText(element, this.totalCount));
      if (this.summary) {
        this.summary.textContent = this.totalCount === 0
          ? '0 of 0'
          : `1–${this.items.length} of ${this.totalCount}`;
      }
      this.renderFilterState();
      if (this.loadMoreButton) {
        this.loadMoreButton.hidden = !this.hasMore;
      }
      this.renderSelection();
      this.updateFolderNavigation();
    }

    populateRow(row, notification) {
      row.dataset.notificationId = String(notification.id);
      row.dataset.projectId = notification.projectId == null ? '' : String(notification.projectId);
      row.dataset.read = String(notification.isRead);
      row.dataset.muted = String(notification.isProjectMuted);
      row.classList.toggle('is-read', notification.isRead);
      row.classList.toggle('is-unread', !notification.isRead);
      row.classList.toggle('is-muted', notification.isProjectMuted);

      const checkbox = row.querySelector('[data-notification-select]');
      if (checkbox) {
        checkbox.value = String(notification.id);
        checkbox.checked = this.selectedIds.has(notification.id);
      }

      setIcon(row.querySelector('[data-notification-icon]'), notification.iconCssClass);
      const project = row.querySelector('[data-notification-project]');
      const projectLabel = notification.projectName || notification.category;
      setText(project, projectLabel);
      if (projectLabel) {
        project?.setAttribute('title', projectLabel);
      }
      setText(row.querySelector('[data-notification-category]'), notification.category);
      setHidden(row.querySelector('[data-notification-action-required]'), !notification.isActionRequired);
      setHidden(row.querySelector('[data-notification-muted]'), !notification.isProjectMuted);

      const title = row.querySelector('[data-notification-title]');
      setText(title, notification.title || 'Notification');
      const route = safeRoute(notification.route, this.centerUrl);
      title?.setAttribute('href', route);

      const summary = row.querySelector('[data-notification-summary]');
      const summarySeparator = row.querySelector('[data-notification-summary-separator]');
      setText(summary, notification.summary);
      setHidden(summary, !notification.summary);
      setHidden(summarySeparator, !notification.summary);
      if (summary) {
        const tooltip = notification.summaryTooltip || notification.summary || '';
        if (tooltip) {
          summary.title = tooltip;
        } else {
          summary.removeAttribute('title');
        }
      }

      const actor = row.querySelector('[data-notification-actor]');
      setText(actor, notification.actorDisplayName);
      setHidden(actor, !notification.actorDisplayName);
      if (notification.actorDisplayName) {
        actor?.setAttribute('title', notification.actorDisplayName);
      } else {
        actor?.removeAttribute('title');
      }

      setTimeElement(row.querySelector('[data-notification-created]'), notification);
      setHidden(row.querySelector('[data-notification-unread-dot]'), notification.isRead);

      const readButton = row.querySelector('[data-notification-row-action="toggle-read"]');
      if (readButton) {
        const label = notification.isRead ? 'Mark as unread' : 'Mark as read';
        readButton.title = label;
        readButton.setAttribute('aria-label', label);
        setIcon(readButton.querySelector('.bi'), notification.isRead ? 'bi bi-envelope' : 'bi bi-envelope-open');
      }

      const muteButton = row.querySelector('[data-notification-row-action="toggle-mute"]');
      if (muteButton) {
        setHidden(muteButton, notification.projectId == null);
        const label = notification.isProjectMuted ? 'Unmute project' : 'Mute project';
        muteButton.title = label;
        muteButton.setAttribute('aria-label', label);
        setIcon(muteButton.querySelector('.bi'), notification.isProjectMuted ? 'bi bi-bell' : 'bi bi-bell-slash');
      }

      const openNew = row.querySelector('[data-notification-open-new]');
      openNew?.setAttribute('href', route);

      const state = row.querySelector('[data-notification-state]');
      setText(state, notification.isRead ? 'Read' : 'Unread');
      row.setAttribute(
        'aria-label',
        `${notification.isRead ? 'Read' : 'Unread'} notification: ${notification.title || 'Notification'}`);
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

      const hasSelection = this.selectedIds.size > 0;
      setHidden(this.defaultActions, hasSelection);
      setHidden(this.selectionActions, !hasSelection);
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

    updateFolderNavigation() {
      this.folderButtons.forEach(button => {
        const active = button.dataset.notificationFolder === this.filters.folder;
        button.classList.toggle('is-active', active);
        if (active) {
          button.setAttribute('aria-current', 'page');
        } else {
          button.removeAttribute('aria-current');
        }
      });

      this.folderCountElements.forEach(element => {
        const folder = this.folders.find(item => item.key === element.dataset.folderKey);
        const mode = element.dataset.folderCountMode || 'unread';
        const count = folder
          ? (mode === 'total' ? folder.totalCount : folder.unreadCount)
          : 0;
        element.textContent = count > 0 ? formatCompactCount(count) : '';
      });
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
      this.status.className = `notification-inbox__status ${statusToneClass(tone)}`;
      if (message) {
        this.statusTimer = window.setTimeout(() => {
          this.status.textContent = '';
          this.status.className = 'notification-inbox__status';
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
    runtime.start().catch(error => {
      // Initialization must never surface as an unhandled promise rejection. Polling remains the
      // durable fallback whenever realtime startup is unavailable.
      runtime.startPolling();
      runtime.reportRealtimeIssue(error);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initialize, { once: true });
  } else {
    initialize();
  }
})();
