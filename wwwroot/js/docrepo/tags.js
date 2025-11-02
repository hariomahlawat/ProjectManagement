(function () {
    const host = document.getElementById('tagsInput');
    if (!host) {
        return;
    }

    const fieldName = host.dataset.fieldName;
    const tags = [];

    const presetValues = Array.from(host.querySelectorAll('input[type="hidden"]'))
        .map((input) => (input.value || '').trim().toLowerCase())
        .filter((value) => value.length > 0);

    host.querySelectorAll('input[type="hidden"]').forEach((element) => element.remove());

    presetValues.forEach((value) => {
        if (!tags.includes(value) && tags.length < 5) {
            tags.push(value);
        }
    });

    const container = document.createElement('div');
    container.className = 'pm-tags__container';

    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'pm-tags__input';
    input.placeholder = 'Add tag…';

    container.appendChild(input);
    host.appendChild(container);

    function syncHiddenInputs() {
        host.querySelectorAll('input[type="hidden"]').forEach((element) => element.remove());
        tags.forEach((tag, index) => {
            const hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = `${fieldName}[${index}]`;
            hidden.value = tag;
            host.appendChild(hidden);
        });
    }

    function renderTags() {
        container.querySelectorAll('.pm-tag').forEach((chip) => chip.remove());

        tags.forEach((tag, index) => {
            const chip = document.createElement('span');
            chip.className = 'pm-tag';
            chip.textContent = tag;

            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'pm-tag__remove';
            button.setAttribute('aria-label', `Remove tag ${tag}`);
            button.textContent = '×';
            button.addEventListener('click', () => {
                tags.splice(index, 1);
                render();
            });

            chip.appendChild(button);
            container.insertBefore(chip, input);
        });

        input.disabled = tags.length >= 5;
        syncHiddenInputs();
    }

    function render() {
        renderTags();
    }

    function addTag(raw) {
        const value = (raw || '').trim().toLowerCase();
        if (!value) {
            return;
        }
        if (value.length > 32) {
            return;
        }
        if (!/^[a-z0-9 _-]+$/.test(value)) {
            return;
        }
        if (tags.includes(value)) {
            return;
        }
        if (tags.length >= 5) {
            return;
        }

        tags.push(value);
        input.value = '';
        render();
    }

    input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter' || event.key === ',') {
            event.preventDefault();
            addTag(input.value);
        } else if (event.key === 'Backspace' && !input.value && tags.length) {
            tags.pop();
            render();
        }
    });

    input.addEventListener('blur', () => {
        if (input.value) {
            addTag(input.value);
        }
    });

    render();
})();
