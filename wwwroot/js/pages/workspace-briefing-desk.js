(function () {
    'use strict';

    // SECTION: Card expand and collapse
    // The toggle button is the accessible control; a click anywhere on the card head
    // that is not itself a link or control is forwarded to it.
    function setExpanded(card, expanded) {
        const toggle = card.querySelector('[data-bd-toggle]');
        const body = toggle ? document.getElementById(toggle.getAttribute('aria-controls')) : null;
        if (!toggle || !body) {
            return;
        }

        toggle.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        body.hidden = !expanded;
        card.classList.toggle('is-expanded', expanded);
    }

    function wireCard(card) {
        const toggle = card.querySelector('[data-bd-toggle]');
        if (!toggle) {
            return;
        }

        toggle.addEventListener('click', function () {
            setExpanded(card, toggle.getAttribute('aria-expanded') !== 'true');
        });

        const head = card.querySelector('.bd-card__head');
        if (head) {
            head.addEventListener('click', function (event) {
                if (event.target instanceof Element && event.target.closest('a, button, input, textarea, select, summary')) {
                    return;
                }

                toggle.click();
            });
        }

        // SECTION: Conference preview
        // Shows the card as the Conference view presents it, in place of the working area.
        const previewToggle = card.querySelector('[data-bd-preview-toggle]');
        const preview = previewToggle ? document.getElementById(previewToggle.getAttribute('aria-controls')) : null;
        const work = card.querySelector('.bd-card__work');
        const label = previewToggle ? previewToggle.querySelector('[data-bd-preview-label]') : null;
        if (previewToggle && preview) {
            previewToggle.addEventListener('click', function () {
                const showing = previewToggle.getAttribute('aria-expanded') === 'true';
                previewToggle.setAttribute('aria-expanded', showing ? 'false' : 'true');
                preview.hidden = showing;
                if (work) {
                    work.hidden = !showing;
                }
                if (label) {
                    label.textContent = showing ? 'See as the Comdt sees it' : 'Back to preparing';
                }
            });
        }

        // SECTION: Add another remark
        // Opening the folded box puts the cursor in it.
        card.querySelectorAll('[data-bd-say-more]').forEach(function (more) {
            more.addEventListener('toggle', function () {
                const input = more.open ? more.querySelector('textarea') : null;
                if (input) {
                    input.focus();
                }
            });
        });

        // SECTION: Double-submit guard
        card.querySelectorAll('form').forEach(function (form) {
            form.addEventListener('submit', function () {
                const submit = form.querySelector('button[type="submit"]');
                if (submit) {
                    submit.disabled = true;
                    submit.setAttribute('aria-busy', 'true');
                }
            });
        });
    }

    // SECTION: Toasts
    // The same markup as the server's toasts (_Toasts.cshtml), for messages from a post that
    // did not reload the page.
    // The container is created empty when the desk loads: a live region that appears already
    // holding its message is often not announced by screen readers.
    function toastContainer() {
        let container = document.querySelector('[data-bd-toasts]');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container position-fixed top-0 end-0 p-3 app-toast-container';
            container.setAttribute('data-bd-toasts', '');
            container.setAttribute('aria-live', 'polite');
            document.body.appendChild(container);
        }
        return container;
    }

    function showToast(message, level) {
        const container = toastContainer();
        const toast = document.createElement('div');
        toast.className = 'toast show app-toast app-toast-' + level;
        // The container announces a success politely; a refusal interrupts as an alert.
        if (level === 'danger') {
            toast.setAttribute('role', 'alert');
        }
        toast.innerHTML = '<div class="toast-header"><span class="app-toast-indicator" aria-hidden="true"></span>'
            + '<strong class="me-auto"></strong>'
            + '<button type="button" class="btn-close" data-bs-dismiss="toast" aria-label="Close"></button></div>'
            + '<div class="toast-body"></div>';
        toast.querySelector('strong').textContent = level === 'danger' ? 'Action required' : 'Success';
        toast.querySelector('.toast-body').textContent = message;
        container.appendChild(toast);

        const remove = function () { toast.remove(); };
        toast.querySelector('.btn-close').addEventListener('click', remove);
        window.setTimeout(remove, level === 'danger' ? 8000 : 4500);
    }

    // SECTION: Posting without a reload
    // A desk form marked data-bd-ajax is sent with fetch. The server answers with the desk
    // rendered by the same partial as the page, which replaces this one, so the readiness rules
    // stay on the server. A refusal comes back as a message and the typed text stays. Any other
    // answer (signed out, a server error) loads the page again; the post is never sent twice.
    const DESK_FETCH_HEADER = 'X-Briefing-Desk';

    function releaseButton(form) {
        const submit = form.querySelector('button[type="submit"]');
        if (submit) {
            submit.disabled = false;
            submit.removeAttribute('aria-busy');
        }
    }

    function focusQuietly(element) {
        if (!element) {
            return;
        }
        element.focus({ preventScroll: true });
        if (typeof element.scrollIntoView === 'function') {
            element.scrollIntoView({ block: 'nearest' });
        }
    }

    function swapDesk(html, anchor, message, ready) {
        const current = document.querySelector('[data-briefing-desk]');
        const template = document.createElement('template');
        template.innerHTML = html;
        const fresh = template.content.querySelector('[data-briefing-desk]');
        if (!current || !fresh) {
            return false;
        }

        current.replaceWith(fresh);
        wireDesk(fresh);
        // The procurement drawers are part of the desk; their rupee previews bind per field,
        // so the new fields are bound here (procurement-inr-preview.js).
        if (window.ProcurementInrPreview && typeof window.ProcurementInrPreview.init === 'function') {
            window.ProcurementInrPreview.init(fresh);
        }

        const card = document.getElementById(anchor);
        const title = card ? card.dataset.bdTitle : '';
        if (ready) {
            // The card has folded into "Ready for conference"; the next card that needs work is open.
            showToast(message + (title ? ' ' + title + ' is ready for conference.' : ''), 'success');
            const group = card ? card.closest('[data-bd-ready-group]') : null;
            const summary = group ? group.querySelector('summary') : null;
            if (summary) {
                summary.classList.add('bd-ready--updated');
                window.setTimeout(function () { summary.classList.remove('bd-ready--updated'); }, 2000);
            }
            focusQuietly(fresh.querySelector('.bd-card.is-expanded [data-bd-toggle]') || summary);
        } else {
            showToast(message, 'success');
            if (card) {
                card.classList.add('bd-card--updated');
                window.setTimeout(function () { card.classList.remove('bd-card--updated'); }, 2000);
                focusQuietly(card.querySelector('[data-bd-toggle]'));
            }
        }
        return true;
    }

    function postWithoutReload(event) {
        const form = event.target;
        if (!(form instanceof window.HTMLFormElement) || !form.matches('[data-bd-ajax]') || event.defaultPrevented) {
            return;
        }
        if (typeof window.fetch !== 'function' || typeof window.FormData !== 'function') {
            return;
        }
        const card = form.closest('[data-bd-card]');
        if (!card) {
            return;
        }

        event.preventDefault();
        const anchor = card.id;
        const headers = {};
        headers[DESK_FETCH_HEADER] = 'fetch';
        // Lets the server tell "this post made the card ready" from "it was ready already".
        headers['X-Desk-Was-Ready'] = card.dataset.bdReady === 'true' ? 'true' : 'false';

        window.fetch(form.action, { method: 'POST', body: new window.FormData(form), headers: headers, credentials: 'same-origin' })
            .then(function (response) {
                if (response.ok && response.headers.get('X-Desk-Fragment') === '1') {
                    return response.text().then(function (html) {
                        const message = decodeURIComponent(response.headers.get('X-Desk-Message') || '') || 'Saved.';
                        if (!swapDesk(html, anchor, message, response.headers.get('X-Desk-Became-Ready') === 'true')) {
                            window.location.reload();
                        }
                    });
                }

                if (response.status === 422 || response.status === 403 || response.status === 404) {
                    return response.json()
                        .catch(function () { return {}; })
                        .then(function (data) {
                            releaseButton(form);
                            showToast(data.message || 'This could not be posted.', 'danger');
                        });
                }

                if (response.redirected) {
                    window.location.assign(response.url);
                } else {
                    window.location.reload();
                }
                return undefined;
            }, function () {
                // Only a failed request lands here: it may not have reached PRISM, so nothing is
                // resent automatically and the text stays in the box.
                releaseButton(form);
                showToast('PRISM could not be reached. Check the connection and post again; your text is still in the box.', 'danger');
            })
            .catch(function () {
                // The post was answered but showing the result failed (for example while swapping
                // the desk in). It may well have been saved, so load the page again rather than
                // invite a second post.
                window.location.reload();
            });
    }

    function wireDesk(desk) {
        desk.querySelectorAll('[data-bd-card]').forEach(wireCard);
    }

    function init() {
        const desk = document.querySelector('[data-briefing-desk]');
        if (!desk) {
            return;
        }

        wireDesk(desk);
        toastContainer();
        // Delegated, so forms in a swapped-in desk are handled too.
        document.addEventListener('submit', postWithoutReload);

        // A post returns here with the card's id in the hash: open it and move focus to it.
        const hash = window.location.hash ? window.location.hash.slice(1) : '';
        const target = hash ? document.getElementById(hash) : null;
        if (target && target.matches('[data-bd-card]')) {
            // A ready card sits in the folded "Ready for conference" group; open it first.
            const group = target.closest('details');
            if (group) {
                group.open = true;
            }
            setExpanded(target, true);
            const toggle = target.querySelector('[data-bd-toggle]');
            if (toggle) {
                toggle.focus({ preventScroll: true });
            }
            if (typeof target.scrollIntoView === 'function') {
                target.scrollIntoView({ block: 'start' });
            }
        }

    }

    // The script is loaded at the end of the body, so the desk is normally already parsed.
    if (document.querySelector('[data-briefing-desk]')) {
        init();
    } else {
        document.addEventListener('DOMContentLoaded', init);
    }
})();
