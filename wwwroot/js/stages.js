(function () {
  document.addEventListener('DOMContentLoaded', function () {
    const modal = document.getElementById('completeModal');
    if (!modal) {
      return;
    }

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
        let label;
        if (stageCode) {
          label = stageName ? `${stageCode} — ${stageName}` : stageCode;
        } else {
          label = stageName;
        }

        stageLabel.textContent = (label || '').trim();
      }

      const dateInput = modal.querySelector('#complete-date');
      if (dateInput) {
        if (defaultDate) {
          dateInput.value = defaultDate;
        } else if (!dateInput.value) {
          const today = new Date();
          const iso = today.toISOString().slice(0, 10);
          dateInput.value = iso;
        }
      }
    });
  });
})();
