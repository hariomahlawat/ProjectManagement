// One name field for naming faces (Review people). Typing offers the people already known and,
// for any other text, a new person. It sits on top of the plain controls the page already has,
// a select of known people and a text box for a new name, each with its own submit button, and
// sends the form to whichever of the two existing handlers fits. Without script those plain
// controls are what the reviewer sees and uses.
//
// Markup, inside the form's actions:
//   [data-name-field data-assign-action="…" data-create-action="…"]
//     [data-name-field-fallback]   one or more blocks holding select[name=personId],
//                                  input[name=displayName] and their submit buttons
//     [data-name-field-ui hidden]  input[data-name-field-text], [data-name-field-list],
//                                  button[type=submit][data-name-field-confirm]
(() => {
    'use strict';

    const normalise = (text) => text.trim().replace(/\s+/g, ' ').toLowerCase();

    // What the typed text means: a known person (their id), a new name, or nothing yet.
    const resolveName = (people, text) => {
        const wanted = normalise(text);
        if (!wanted) return { kind: 'empty' };
        const known = people.find(person => normalise(person.name) === wanted);
        return known ? { kind: 'known', id: known.id, name: known.name } : { kind: 'new', name: text.trim().replace(/\s+/g, ' ') };
    };

    // People whose name contains the text, those starting with it first; all of them for no text.
    const matchPeople = (people, text, limit = 8) => {
        const wanted = normalise(text);
        if (!wanted) return people.slice(0, limit);
        const starts = people.filter(person => normalise(person.name).startsWith(wanted));
        const contains = people.filter(person => !starts.includes(person) && normalise(person.name).includes(wanted));
        return [...starts, ...contains].slice(0, limit);
    };

    const enhance = (field) => {
        const ui = field.querySelector('[data-name-field-ui]');
        const text = ui?.querySelector('[data-name-field-text]');
        const list = ui?.querySelector('[data-name-field-list]');
        const confirm = ui?.querySelector('[data-name-field-confirm]');
        const select = field.querySelector('select[name="personId"]');
        const newName = field.querySelector('input[name="displayName"]');
        if (!ui || !text || !list || !confirm || !newName) return;

        const people = select
            ? [...select.options].filter(option => option.value).map(option => ({ id: option.value, name: option.textContent.trim(), portrait: option.dataset.portrait || '' }))
            : [];
        let active = -1;
        let offered = [];

        const close = () => {
            list.hidden = true;
            list.replaceChildren();
            offered = [];
            active = -1;
            text.setAttribute('aria-expanded', 'false');
            text.removeAttribute('aria-activedescendant');
        };

        const choose = (name) => {
            text.value = name;
            close();
            text.dispatchEvent(new Event('change', { bubbles: true }));
        };

        const setActive = (index) => {
            active = index;
            [...list.children].forEach((item, position) => {
                item.classList.toggle('is-active', position === index);
                item.setAttribute('aria-selected', String(position === index));
            });
            if (index >= 0) {
                text.setAttribute('aria-activedescendant', list.children[index].id);
                list.children[index].scrollIntoView?.({ block: 'nearest' });
            } else {
                text.removeAttribute('aria-activedescendant');
            }
        };

        const render = () => {
            const typed = text.value;
            const matches = matchPeople(people, typed);
            const meaning = resolveName(people, typed);
            list.replaceChildren();
            offered = matches.map(person => person.name);
            matches.forEach((person, index) => {
                const item = document.createElement('li');
                item.id = `${list.id}-${index}`;
                item.setAttribute('role', 'option');
                if (person.portrait) {
                    const portrait = document.createElement('img');
                    portrait.alt = '';
                    portrait.loading = 'lazy';
                    portrait.addEventListener('error', () => portrait.remove());
                    portrait.src = person.portrait;
                    item.append(portrait);
                }
                const name = document.createElement('span');
                name.textContent = person.name;
                item.append(name);
                list.append(item);
            });
            if (meaning.kind === 'new') {
                const item = document.createElement('li');
                item.id = `${list.id}-new`;
                item.className = 'is-new';
                item.setAttribute('role', 'option');
                item.textContent = `New person: ${meaning.name}`;
                offered.push(meaning.name);
                list.append(item);
            }
            list.hidden = list.children.length === 0;
            text.setAttribute('aria-expanded', String(!list.hidden));
            setActive(-1);
        };

        // Pressing an entry must not take focus from the box before the click lands.
        list.addEventListener('mousedown', event => event.preventDefault());
        list.addEventListener('click', (event) => {
            const item = event.target?.closest?.('li');
            const index = item ? [...list.children].indexOf(item) : -1;
            if (index >= 0) choose(offered[index]);
        });
        text.addEventListener('focus', render);
        text.addEventListener('input', render);
        text.addEventListener('blur', () => window.setTimeout(close, 120));
        text.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && !list.hidden) {
                event.preventDefault();
                event.stopPropagation();
                close();
            } else if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && offered.length > 0) {
                event.preventDefault();
                if (list.hidden) render();
                const step = event.key === 'ArrowDown' ? 1 : -1;
                setActive((active + step + offered.length) % offered.length);
            } else if (event.key === 'Enter' && active >= 0 && !list.hidden) {
                // Enter on a highlighted entry picks it; Enter otherwise confirms the form.
                event.preventDefault();
                choose(offered[active]);
            }
        });

        // A person picked elsewhere on the card (a suggestion's "Choose" button sets the select).
        select?.addEventListener('change', () => {
            const option = select.selectedOptions[0];
            if (option?.value) text.value = option.textContent.trim();
        });

        // The form goes to the handler that fits what was typed; the plain controls carry the value.
        confirm.addEventListener('click', (event) => {
            const meaning = resolveName(people, text.value);
            if (meaning.kind === 'empty') {
                event.preventDefault();
                text.setCustomValidity('Type the person\'s name, or pick someone from the list.');
                text.reportValidity();
                window.setTimeout(() => text.setCustomValidity(''), 0);
                text.focus();
                return;
            }
            if (meaning.kind === 'known' && select) {
                select.value = meaning.id;
                newName.value = '';
                confirm.formAction = field.dataset.assignAction;
            } else {
                if (select) select.value = '';
                newName.value = meaning.name;
                confirm.formAction = field.dataset.createAction;
            }
        });

        field.querySelectorAll('[data-name-field-fallback]').forEach(block => { block.hidden = true; });
        ui.hidden = false;
        field.classList.add('is-enhanced');
    };

    if (typeof document !== 'undefined') {
        document.querySelectorAll('[data-name-field]').forEach(enhance);
    }
    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { resolveName, matchPeople };
    }
})();
