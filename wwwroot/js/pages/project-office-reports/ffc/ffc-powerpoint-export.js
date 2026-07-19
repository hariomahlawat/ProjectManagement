(function (window, document) {
    'use strict';

    const form = document.querySelector('[data-ffc-ppt-form]');
    if (!form) return;

    const submitButton = form.querySelector('[data-ffc-ppt-submit]');
    const submitText = form.querySelector('[data-ffc-ppt-submit-text]');
    const errorBox = form.querySelector('[data-ffc-ppt-error]');
    const successBox = form.querySelector('[data-ffc-ppt-success]');
    const countryPanel = form.querySelector('[data-ffc-ppt-countries]');
    const countryError = form.querySelector('[data-ffc-ppt-country-error]');
    const detailOptions = form.querySelector('[data-ffc-ppt-detail-options]');
    const includeProjects = form.querySelector('[name="PowerPoint.IncludeProjects"]');
    const includeProgress = form.querySelector('[name="PowerPoint.IncludeProgress"]');
    const progressOption = form.querySelector('[data-ffc-ppt-progress-option]');
    const countryCheckboxes = Array.from(form.querySelectorAll('[name="PowerPoint.SelectedCountryIds"]'));
    const defaultSubmitHtml = submitButton?.innerHTML || '';

    function selectedScope() {
        return form.querySelector('[name="PowerPoint.Scope"]:checked')?.value || 'current';
    }

    function selectedType() {
        return form.querySelector('[name="PowerPoint.PresentationType"]:checked')?.value || 'executive';
    }

    function updateScope() {
        const selected = selectedScope() === 'selected';
        if (countryPanel) countryPanel.hidden = !selected;
        if (!selected && countryError) countryError.hidden = true;
    }

    function updateType() {
        const full = selectedType() === 'full';
        if (detailOptions) detailOptions.classList.toggle('is-disabled', !full);
        detailOptions?.querySelectorAll('input').forEach(input => {
            input.disabled = !full;
        });
        updateProgress();
    }

    function updateProgress() {
        const enabled = selectedType() === 'full' && Boolean(includeProjects?.checked);
        if (includeProgress) includeProgress.disabled = !enabled;
        if (progressOption) progressOption.classList.toggle('text-muted', !enabled);
    }

    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message || 'The PowerPoint could not be generated.';
        errorBox.hidden = false;
        successBox && (successBox.hidden = true);
        errorBox.focus();
    }

    function showSuccess(message) {
        if (!successBox) return;
        successBox.textContent = message;
        successBox.hidden = false;
        errorBox && (errorBox.hidden = true);
    }

    function setSubmitting(submitting) {
        if (!submitButton) return;
        submitButton.disabled = submitting;
        submitButton.setAttribute('aria-busy', submitting ? 'true' : 'false');
        if (submitting) {
            submitButton.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span>Preparing PowerPoint…</span>';
        } else {
            submitButton.innerHTML = defaultSubmitHtml;
        }
    }

    function filenameFromResponse(response) {
        const disposition = response.headers.get('content-disposition') || '';
        const utfMatch = disposition.match(/filename\*=UTF-8''([^;]+)/i);
        if (utfMatch) return decodeURIComponent(utfMatch[1]);
        const match = disposition.match(/filename="?([^";]+)"?/i);
        return match ? match[1] : 'FFC_Portfolio.pptx';
    }

    function validateSelection() {
        if (selectedScope() !== 'selected') return true;
        const valid = countryCheckboxes.some(checkbox => checkbox.checked);
        if (countryError) countryError.hidden = valid;
        if (!valid) countryPanel?.scrollIntoView?.({ block: 'nearest' });
        return valid;
    }

    form.querySelectorAll('[name="PowerPoint.Scope"]').forEach(input => input.addEventListener('change', updateScope));
    form.querySelectorAll('[name="PowerPoint.PresentationType"]').forEach(input => input.addEventListener('change', updateType));
    includeProjects?.addEventListener('change', updateProgress);

    form.querySelector('[data-ffc-ppt-select-all]')?.addEventListener('click', () => {
        countryCheckboxes.forEach(checkbox => { checkbox.checked = true; });
        if (countryError) countryError.hidden = true;
    });
    form.querySelector('[data-ffc-ppt-clear-all]')?.addEventListener('click', () => {
        countryCheckboxes.forEach(checkbox => { checkbox.checked = false; });
    });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        errorBox && (errorBox.hidden = true);
        successBox && (successBox.hidden = true);

        form.classList.add('was-validated');
        if (!form.checkValidity() || !validateSelection()) return;

        setSubmitting(true);
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            if (!response.ok) {
                let detail = 'The PowerPoint could not be generated.';
                try {
                    const problem = await response.json();
                    detail = problem.detail || problem.title || detail;
                } catch (_) {
                    // Preserve the safe fallback message.
                }
                throw new Error(detail);
            }

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = filenameFromResponse(response);
            document.body.append(link);
            link.click();
            link.remove();
            window.setTimeout(() => window.URL.revokeObjectURL(url), 1000);
            const slides = response.headers.get('X-FFC-Presentation-Slides');
            showSuccess(slides
                ? `PowerPoint prepared successfully · ${slides} slides.`
                : 'PowerPoint prepared successfully.');
        } catch (error) {
            showError(error?.message);
        } finally {
            setSubmitting(false);
        }
    });

    updateScope();
    updateType();
})(window, document);
