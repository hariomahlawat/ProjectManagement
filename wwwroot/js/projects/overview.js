(function () {
    if (typeof bootstrap === 'undefined') {
        return;
    }

    const procurement = document.getElementById('offcanvasProcurement');
    if (procurement) {
        procurement.addEventListener('shown.bs.offcanvas', function () {
            const firstField = procurement.querySelector('input,select,textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const marker = document.getElementById('open-procurement');
        if (marker && marker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(procurement);
            instance.show();
        }
    }

    const stageBackfill = document.getElementById('offcanvasStageBackfill');
    if (stageBackfill) {
        const form = stageBackfill.querySelector('[data-stage-backfill-form]');
        const stageInput = stageBackfill.querySelector('[data-stage-backfill-stage]');
        const startInput = stageBackfill.querySelector('[data-stage-backfill-start]');
        const finishInput = stageBackfill.querySelector('[data-stage-backfill-finish]');
        const subtitle = stageBackfill.querySelector('[data-stage-backfill-subtitle]');
        const warning = stageBackfill.querySelector('[data-stage-backfill-warning]');

        function findStageRow(stageCode) {
            if (!stageCode) {
                return null;
            }

            let escaped = stageCode;
            if (window.CSS && typeof window.CSS.escape === 'function') {
                escaped = window.CSS.escape(stageCode);
            } else {
                escaped = stageCode.replace(/"/g, '\\"');
            }
            return document.querySelector(`[data-stage-row="${escaped}"]`);
        }

        function populateStageBackfill(stageCode, startValue, finishValue) {
            if (!stageInput || !form) {
                return;
            }

            stageInput.value = stageCode || '';

            const row = findStageRow(stageCode);
            const stageName = row?.dataset.stageNameValue || stageCode || '';
            if (subtitle) {
                subtitle.textContent = stageName ? `For ${stageName}` : '';
            }

            const defaultStart = row?.dataset.stageActualStart || '';
            const defaultFinish = row?.dataset.stageCompleted || '';

            if (startInput) {
                startInput.value = startValue || defaultStart || '';
            }

            if (finishInput) {
                finishInput.value = finishValue || defaultFinish || '';
            }

            if (warning) {
                if (row?.dataset.stageCompleted && !row.dataset.stageActualStart) {
                    warning.classList.remove('d-none');
                } else {
                    warning.classList.add('d-none');
                }
            }
        }

        stageBackfill.addEventListener('shown.bs.offcanvas', function () {
            if (startInput && !startInput.value) {
                startInput.focus();
                return;
            }

            if (finishInput) {
                finishInput.focus();
            }
        });

        stageBackfill.addEventListener('hidden.bs.offcanvas', function () {
            if (form) {
                form.reset();
            }

            if (subtitle) {
                subtitle.textContent = '';
            }

            if (warning) {
                warning.classList.add('d-none');
            }
        });

        const backfillMarker = document.getElementById('open-stage-backfill');
        if (backfillMarker && backfillMarker.dataset.open === '1') {
            populateStageBackfill(
                backfillMarker.dataset.stage || '',
                backfillMarker.dataset.start || '',
                backfillMarker.dataset.finish || ''
            );

            const instance = bootstrap.Offcanvas.getOrCreateInstance(stageBackfill);
            instance.show();
        }
    }

    const assignRoles = document.getElementById('offcanvasAssignRoles');
    if (assignRoles) {
        assignRoles.addEventListener('shown.bs.offcanvas', function () {
            const firstField = assignRoles.querySelector('select, input, textarea');
            if (firstField) {
                firstField.focus();
            }
        });

        const assignMarker = document.getElementById('open-assign-roles');
        if (assignMarker && assignMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(assignRoles);
            instance.show();
        }
    }

    const planEdit = document.getElementById('offcanvasPlanEdit');
    if (planEdit) {
        planEdit.addEventListener('shown.bs.offcanvas', function () {
            const firstDate = planEdit.querySelector('input[type="date"]');
            if (firstDate) {
                firstDate.focus();
            }
        });

        const planMarker = document.getElementById('open-plan-edit');
        if (planMarker && planMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planEdit);
            instance.show();
        }
    }

    const planReview = document.getElementById('offcanvasPlanReview');
    if (planReview) {
        planReview.addEventListener('shown.bs.offcanvas', function () {
            const firstAction = planReview.querySelector('button, input, select, textarea');
            if (firstAction) {
                firstAction.focus();
            }
        });

        planReview.addEventListener('hidden.bs.offcanvas', function () {
            planReview.querySelectorAll('[data-plan-review-note]').forEach(function (note) {
                note.setAttribute('hidden', '');
                const textarea = note.querySelector('textarea');
                if (textarea) {
                    textarea.value = '';
                }
            });
        });

        const reviewMarker = document.getElementById('open-plan-review');
        if (reviewMarker && reviewMarker.dataset.open === '1') {
            const instance = bootstrap.Offcanvas.getOrCreateInstance(planReview);
            instance.show();
        }

        planReview.querySelectorAll('[data-plan-review-form]').forEach(function (form) {
            const noteContainer = form.querySelector('[data-plan-review-note]');
            const rejectButton = form.querySelector('[data-plan-review-reject]');
            if (!noteContainer || !rejectButton) {
                return;
            }

            rejectButton.addEventListener('click', function (event) {
                if (rejectButton.disabled) {
                    return;
                }

                if (noteContainer.hasAttribute('hidden')) {
                    event.preventDefault();
                    noteContainer.removeAttribute('hidden');
                    const textarea = noteContainer.querySelector('textarea');
                    if (textarea) {
                        textarea.focus();
                    }
                }
            });
        });
    }
})();
