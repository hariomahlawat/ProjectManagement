(function () {
  const formatIso = (date) => {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  };

  document.addEventListener('DOMContentLoaded', function () {
    const modal = document.getElementById('completeModal');
    if (modal) {
      modal.addEventListener('show.bs.modal', function (event) {
        const trigger = event.relatedTarget;
        if (!trigger) {
          return;
        }

        const stageCode = trigger.getAttribute('data-stage') || '';
        const stageName = trigger.getAttribute('data-stage-name') || '';
        const defaultDate = trigger.getAttribute('data-default-date');

        const stageInput = modal.querySelector('#complete-stage');
        if (stageInput) {
          stageInput.value = stageCode;
        }

        const stageLabel = modal.querySelector('#complete-stage-label');
        if (stageLabel) {
          const label = stageName ? `${stageCode} — ${stageName}` : stageCode;
          stageLabel.textContent = label;
        }

        const dateInput = modal.querySelector('#complete-date');
        if (dateInput) {
          if (defaultDate) {
            dateInput.value = defaultDate;
          } else if (!dateInput.value) {
            dateInput.value = formatIso(new Date());
          }
        }

        const remark = modal.querySelector('#complete-remark');
        if (remark && !remark.value) {
          remark.focus();
        }
      });
    }

    document.querySelectorAll('[data-stage-scroll]').forEach((el) => {
      el.addEventListener('click', () => {
        const code = el.getAttribute('data-stage-scroll');
        if (!code) {
          return;
        }

        const row = document.querySelector(`[data-stage-row="${code}"]`);
        if (!row) {
          return;
        }

        row.scrollIntoView({ behavior: 'smooth', block: 'center' });
        row.classList.add('highlight');
        setTimeout(() => row.classList.remove('highlight'), 1600);
      });
    });

    document.querySelectorAll('[data-timeline]').forEach((el) => {
      const start = el.getAttribute('data-start');
      const due = el.getAttribute('data-due');
      const today = el.getAttribute('data-today');
      if (!start || !due || !today) {
        return;
      }

      const startDate = new Date(start);
      const dueDate = new Date(due);
      const todayDate = new Date(today);
      if (Number.isNaN(startDate.valueOf()) || Number.isNaN(dueDate.valueOf())) {
        return;
      }

      const span = dueDate.getTime() - startDate.getTime();
      if (span <= 0) {
        el.style.setProperty('--today-position', '0%');
        return;
      }

      const progress = Math.max(0, Math.min(1, (todayDate.getTime() - startDate.getTime()) / span));
      el.style.setProperty('--today-position', `${(progress * 100).toFixed(1)}%`);
    });

    // SECTION: Tooltip support
    const globalTooltips = window.projectManagement?.tooltips;
    if (globalTooltips?.refresh) {
      globalTooltips.refresh(document);
    } else if (window.bootstrap && typeof window.bootstrap.Tooltip === 'function') {
      document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach((el) => {
        window.bootstrap.Tooltip.getOrCreateInstance(el);
      });
    }
  });
})();
