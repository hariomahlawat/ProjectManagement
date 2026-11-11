// Search suggestions for the Photos library: people, projects and years, offered as the user
// types. The lists come from the page itself (the same people, projects and years the filter
// drawer offers), so nothing new is asked of the server and nothing the user cannot already
// see is shown. Each suggestion is a plain link; Enter on the box still runs a text search.
(() => {
    'use strict';

    const input = document.querySelector('[data-photos-search]');
    const panel = document.querySelector('[data-photos-suggest]');
    const dataBlock = document.querySelector('[data-photos-suggest-data]');
    if (!input || !panel || !dataBlock) return;

    let data;
    try {
        data = JSON.parse(dataBlock.textContent || '{}');
    } catch {
        return;
    }

    const people = Array.isArray(data.people) ? data.people : [];
    const projects = Array.isArray(data.projects) ? data.projects : [];
    const years = Array.isArray(data.years) ? data.years : [];
    if (people.length + projects.length + years.length === 0) return;

    const MAX_PER_GROUP = 5;
    const form = input.closest('form');
    let options = [];
    let activeIndex = -1;

    // Names that start with the typed text come before names that only contain it.
    const matches = (list, term, nameOf) => {
        if (!term) return [];
        const starts = [];
        const contains = [];
        list.forEach((entry) => {
            const name = String(nameOf(entry) || '').toLowerCase();
            const at = name.indexOf(term);
            if (at < 0) return;
            const wordStart = at === 0 || /[\s\-/(.,]/.test(name[at - 1]);
            (wordStart ? starts : contains).push(entry);
        });
        return starts.concat(contains).slice(0, MAX_PER_GROUP);
    };

    const element = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    };

    function addGroup(title, entries, build) {
        if (entries.length === 0) return;
        panel.append(element('div', 'photos-suggest__title', title));
        entries.forEach((entry) => {
            const option = build(entry);
            option.setAttribute('role', 'option');
            option.id = `photos-suggest-${options.length}`;
            options.push(option);
            panel.append(option);
        });
    }

    const link = (href, label, detail, picture) => {
        const anchor = element('a', 'photos-suggest__option');
        anchor.href = href;
        if (picture !== undefined) {
            const face = element('span', 'photos-suggest__face');
            if (picture) {
                const image = document.createElement('img');
                image.src = picture;
                image.alt = '';
                image.loading = 'lazy';
                face.append(image);
            }
            anchor.append(face);
        }
        anchor.append(element('span', 'photos-suggest__label', label));
        if (detail) anchor.append(element('span', 'photos-suggest__detail', detail));
        return anchor;
    };

    function close() {
        panel.hidden = true;
        panel.replaceChildren();
        options = [];
        activeIndex = -1;
        input.setAttribute('aria-expanded', 'false');
        input.removeAttribute('aria-activedescendant');
    }

    function setActive(index) {
        activeIndex = index;
        options.forEach((option, position) => {
            const active = position === index;
            option.classList.toggle('is-active', active);
            option.setAttribute('aria-selected', String(active));
        });
        if (index >= 0) input.setAttribute('aria-activedescendant', options[index].id);
        else input.removeAttribute('aria-activedescendant');
    }

    function render() {
        const term = input.value.trim().toLowerCase();
        panel.replaceChildren();
        options = [];
        activeIndex = -1;

        if (term) {
            addGroup('People', matches(people, term, entry => entry.name),
                entry => link(entry.url, entry.name, `${entry.count} photo${entry.count === 1 ? '' : 's'}`, entry.portrait || ''));
            addGroup('Projects', matches(projects, term, entry => entry.name), entry => link(entry.url, entry.name));
            addGroup('Years', matches(years, term, entry => String(entry.year)), entry => link(entry.url, String(entry.year)));
        } else {
            // Before anything is typed: the people with most photos, and the years.
            addGroup('People', people.slice().sort((a, b) => b.count - a.count).slice(0, MAX_PER_GROUP),
                entry => link(entry.url, entry.name, `${entry.count} photo${entry.count === 1 ? '' : 's'}`, entry.portrait || ''));
            addGroup('Years', years.slice(0, MAX_PER_GROUP + 1), entry => link(entry.url, String(entry.year)));
        }

        if (options.length === 0) {
            close();
            return;
        }
        panel.hidden = false;
        input.setAttribute('aria-expanded', 'true');
    }

    input.setAttribute('role', 'combobox');
    input.setAttribute('aria-autocomplete', 'list');
    input.setAttribute('aria-expanded', 'false');
    if (panel.id) input.setAttribute('aria-controls', panel.id);

    input.addEventListener('focus', render);
    input.addEventListener('input', render);
    input.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            if (!panel.hidden) {
                // This Escape only closes the suggestions; it must not also end a selection
                // or close anything else that listens for the key further up.
                event.preventDefault();
                event.stopPropagation();
                close();
            }
            return;
        }
        if (panel.hidden || options.length === 0) return;
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            setActive((activeIndex + 1) % options.length);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setActive(activeIndex <= 0 ? options.length - 1 : activeIndex - 1);
        } else if (event.key === 'Enter' && activeIndex >= 0) {
            // Enter with a suggestion highlighted opens it; otherwise the form searches as before.
            event.preventDefault();
            options[activeIndex].click();
        }
    });

    document.addEventListener('click', (event) => {
        if (form && form.contains(event.target)) return;
        close();
    });
    form?.addEventListener('submit', close);
})();
