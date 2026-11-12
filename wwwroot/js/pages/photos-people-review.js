(() => {
    "use strict";

    // Asks in the shared PRISM dialog (docs/ux/confirm-dialogs.md); the browser box only if it is missing.
    const askConfirm = options => (window.PrismConfirm?.show
        ? window.PrismConfirm.show(options)
        : Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' '))));

    const modal = document.getElementById("assignExistingModal");
    if (modal) {
        modal.addEventListener("show.bs.modal", event => {
            const trigger = event.relatedTarget;
            if (!(trigger instanceof HTMLElement)) return;

            const faceIdInput = modal.querySelector('input[name="faceId"]');
            const context = modal.querySelector("[data-face-context]");
            const personSelect = modal.querySelector('select[name="personId"]');

            if (faceIdInput instanceof HTMLInputElement) {
                faceIdInput.value = trigger.dataset.faceId ?? "";
            }
            if (context instanceof HTMLElement) {
                const title = trigger.dataset.faceContext?.trim();
                context.textContent = title
                    ? `Choose the confirmed identity for the face detected in “${title}”.`
                    : "Choose the confirmed identity for this face.";
            }
            if (personSelect instanceof HTMLSelectElement) {
                personSelect.value = "";
            }
        });

        modal.addEventListener("shown.bs.modal", () => {
            const personSelect = modal.querySelector('select[name="personId"]');
            if (personSelect instanceof HTMLSelectElement) {
                personSelect.focus();
            }
        });
    }

    // "Question? Consequence." in data-confirm-message: hold the click, ask, and press the button
    // again on yes.
    const approvedControls = new WeakSet();
    document.querySelectorAll("[data-confirm-message]").forEach(control => {
        if (!(control instanceof HTMLButtonElement)) return;
        control.addEventListener("click", event => {
            if (approvedControls.has(control)) {
                approvedControls.delete(control);
                return;
            }
            const text = control.dataset.confirmMessage?.trim();
            if (!text) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            const split = /^([^?]{1,160}\?)\s+([\s\S]+)$/.exec(text);
            askConfirm({
                tone: /not (a )?face|suppress|remove/i.test(text) ? "danger" : "primary",
                title: split ? split[1] : text,
                message: split ? split[2] : "",
                confirmText: /not (a )?face/i.test(text) ? "Mark not a face" : "Continue",
                cancelText: "Cancel"
            }).then(accepted => {
                if (!accepted) return;
                approvedControls.add(control);
                control.click();
            });
        });
    });

    document.querySelectorAll("[data-use-group-candidate]").forEach(button => {
        if (!(button instanceof HTMLButtonElement)) return;
        button.addEventListener("click", () => {
            const targetId = button.dataset.targetSelect;
            const personId = button.dataset.personId;
            if (!targetId || !personId) return;
            const select = document.getElementById(targetId);
            if (!(select instanceof HTMLSelectElement)) return;
            select.value = personId;
            select.focus({ preventScroll: true });
            select.scrollIntoView({ block: "nearest", behavior: "smooth" });
        });
    });

    document.querySelectorAll("[data-group-decision]").forEach(form => {
        if (!(form instanceof HTMLFormElement) || !form.id) return;

        const selector = `input[type="checkbox"][name="faceIds"][form="${CSS.escape(form.id)}"]`;
        const checkboxes = Array.from(document.querySelectorAll(selector))
            .filter(input => input instanceof HTMLInputElement);
        const count = form.querySelector("[data-selected-count]");
        const toggle = form.querySelector("[data-toggle-group-selection]");
        const internalSubmitButtons = Array.from(form.querySelectorAll('button[type="submit"]'))
            .filter(button => button instanceof HTMLButtonElement);
        const refreshLocked = form.closest("[data-review-workload]")?.dataset.groupingRefreshing === "true";
        const externalCandidateButtons = Array.from(
            document.querySelectorAll(`[form="${CSS.escape(form.id)}"][data-group-candidate-submit]`))
            .filter(button => button instanceof HTMLButtonElement);

        const updateTile = input => {
            if (!(input instanceof HTMLInputElement)) return;
            const tile = input.closest("[data-group-face-tile]");
            if (tile instanceof HTMLElement) {
                tile.classList.toggle("is-selected", input.checked);
            }
        };

        const update = () => {
            const selected = checkboxes.filter(input => input.checked).length;
            const allSelected = checkboxes.length > 0 && selected === checkboxes.length;

            if (count instanceof HTMLElement) {
                count.textContent = `${selected} selected`;
            }
            if (toggle instanceof HTMLButtonElement) {
                toggle.textContent = allSelected ? "Clear all" : "Select all";
                toggle.disabled = refreshLocked || checkboxes.length === 0;
            }
            [...internalSubmitButtons, ...externalCandidateButtons].forEach(button => {
                button.disabled = refreshLocked || selected === 0;
            });
            checkboxes.forEach(updateTile);
        };

        checkboxes.forEach(input => input.addEventListener("change", update));
        if (toggle instanceof HTMLButtonElement) {
            toggle.addEventListener("click", () => {
                const allSelected = checkboxes.length > 0 && checkboxes.every(input => input.checked);
                checkboxes.forEach(input => {
                    input.checked = !allSelected;
                });
                update();
            });
        }

        form.addEventListener("submit", event => {
            const submitter = event.submitter;
            if (!(submitter instanceof HTMLButtonElement)) return;

            const selected = checkboxes.filter(input => input.checked).length;
            if (selected === 0) {
                event.preventDefault();
                return;
            }

            const action = submitter.formAction || submitter.getAttribute("formaction") || "";
            if (action.includes("handler=AssignGroup") || action.includes("handler%3DAssignGroup")) {
                const select = form.querySelector('select[name="personId"]');
                if (select instanceof HTMLSelectElement && !select.value) {
                    event.preventDefault();
                    select.focus();
                    select.setCustomValidity("Select an existing person before assigning the selected appearances.");
                    select.reportValidity();
                    window.setTimeout(() => select.setCustomValidity(""), 0);
                    return;
                }
            }
            if (action.includes("handler=CreateGroup") || action.includes("handler%3DCreateGroup")) {
                const name = form.querySelector('input[name="displayName"]');
                if (name instanceof HTMLInputElement && !name.value.trim()) {
                    event.preventDefault();
                    name.focus();
                    name.setCustomValidity("Enter the person's name before creating the identity.");
                    name.reportValidity();
                    window.setTimeout(() => name.setCustomValidity(""), 0);
                }
            }
        });

        update();
    });
})();

(() => {
    "use strict";

    const form = document.querySelector("[data-batch-identity-form]");
    if (!(form instanceof HTMLFormElement)) return;

    const checkboxes = Array.from(document.querySelectorAll("[data-batch-identity-face]"))
        .filter(item => item instanceof HTMLInputElement);
    const count = form.querySelector("[data-batch-identity-count]");
    const person = form.querySelector('select[name="personId"]');
    const selectPage = document.querySelector("[data-batch-select-all]");
    const clear = form.querySelector("[data-batch-clear]");
    const submitButtons = Array.from(form.querySelectorAll('button[type="submit"]'))
        .filter(button => button instanceof HTMLButtonElement);

    const update = () => {
        const selected = checkboxes.filter(item => item.checked).length;
        const allSelected = checkboxes.length > 0 && selected === checkboxes.length;

        form.hidden = selected === 0;
        if (count instanceof HTMLElement) count.textContent = String(selected);
        submitButtons.forEach(button => {
            button.disabled = selected === 0;
        });
        if (selectPage instanceof HTMLButtonElement) {
            selectPage.disabled = checkboxes.length === 0 || allSelected;
            selectPage.innerHTML = allSelected
                ? '<i class="bi bi-check2-all"></i> Page selected'
                : '<i class="bi bi-check2-square"></i> Select page';
        }
    };

    checkboxes.forEach(item => item.addEventListener("change", update));
    if (selectPage instanceof HTMLButtonElement) {
        selectPage.addEventListener("click", () => {
            checkboxes.forEach(item => { item.checked = true; });
            update();
            form.scrollIntoView({ block: "nearest", behavior: "smooth" });
        });
    }
    if (clear instanceof HTMLButtonElement) {
        clear.addEventListener("click", () => {
            checkboxes.forEach(item => { item.checked = false; });
            update();
        });
    }

    form.addEventListener("submit", event => {
        const submitter = event.submitter;
        if (!(submitter instanceof HTMLButtonElement)) return;
        const selected = checkboxes.filter(item => item.checked).length;
        if (selected === 0) {
            event.preventDefault();
            return;
        }

        if (submitter.hasAttribute("data-requires-person")
            && person instanceof HTMLSelectElement
            && !person.value) {
            event.preventDefault();
            person.focus();
            person.setCustomValidity("Select the confirmed person for the selected appearances.");
            person.reportValidity();
            window.setTimeout(() => person.setCustomValidity(""), 0);
        }
    });

    update();
})();

(() => {
    "use strict";

    const root = document.querySelector("[data-review-workload]");
    if (!(root instanceof HTMLElement)) return;

    const statusUrl = root.dataset.workloadStatusUrl?.trim();
    if (!statusUrl) return;

    const liveStatus = root.querySelector("[data-workload-live-status]");
    const matchingWrap = root.querySelector("[data-workload-matching-wrap]");
    const failureWrap = root.querySelector("[data-workload-failures-wrap]");
    const closedWrap = root.querySelector("[data-workload-closed-wrap]");
    const groupingPreparing = root.querySelector("[data-grouping-preparing]");
    const groupingRefreshNotice = root.querySelector("[data-grouping-refresh-notice]");
    const groupingRefreshAction = root.querySelector("[data-grouping-refresh-action]");
    const matchingEmptyState = root.querySelector("[data-matching-empty-state]");
    const matchingEmptyTitle = matchingEmptyState?.querySelector("[data-matching-empty-title]");
    const matchingEmptyCopy = matchingEmptyState?.querySelector("[data-matching-empty-copy]");
    const matchingEmptyAction = matchingEmptyState?.querySelector("[data-matching-empty-action]");
    const workerWarning = root.querySelector("[data-matching-worker-warning]");

    let previousMatching = Number.parseInt(root.dataset.initialMatching ?? "0", 10) || 0;
    let previousGroupingRefreshing = root.dataset.groupingRefreshing === "true";
    let timerId = 0;
    let stopped = previousMatching <= 0 && !previousGroupingRefreshing;

    const setText = (selector, value) => {
        root.querySelectorAll(selector).forEach(element => {
            if (element instanceof HTMLElement) element.textContent = String(value);
        });
    };

    const setHiddenForCount = (element, count, keepVisible = false) => {
        if (element instanceof HTMLElement) {
            element.hidden = !keepVisible && count <= 0;
        }
    };

    const plural = (count, singular, pluralForm = `${singular}s`) => count === 1 ? singular : pluralForm;

    const updateLiveSummary = data => {
        if (!(liveStatus instanceof HTMLElement)) return;

        if (previousMatching > 0 && data.matching === 0) {
            liveStatus.textContent = `Checking finished: ${data.knownMatches} ${plural(data.knownMatches, "suggestion")} and ${data.individualReview} unnamed ${plural(data.individualReview, "face")}.`;
            if (matchingEmptyState instanceof HTMLElement) {
                if (matchingEmptyTitle instanceof HTMLElement) matchingEmptyTitle.textContent = "Checking finished";
                if (matchingEmptyCopy instanceof HTMLElement) {
                    matchingEmptyCopy.textContent = `${data.knownMatches} ${plural(data.knownMatches, "suggestion")} and ${data.individualReview} unnamed ${plural(data.individualReview, "face")} are ready to review.`;
                }
                if (matchingEmptyAction instanceof HTMLElement) matchingEmptyAction.hidden = false;
            }
            return;
        }

        if (previousGroupingRefreshing && !data.groupingRefreshPending && data.groupingSnapshotAvailable) {
            liveStatus.textContent = "The groups have changed. Reload to see them.";
            if (groupingRefreshAction instanceof HTMLElement) groupingRefreshAction.hidden = false;
            return;
        }

        if (data.matching > 0 && data.matchingWorkerDelayed) {
            liveStatus.textContent = `Checking is running late: ${data.matching} ${plural(data.matching, "face")} still waiting.`;
        } else if (data.matching > 0) {
            liveStatus.textContent = `${data.matching} ${plural(data.matching, "face")} being checked in the background.`;
        } else if (data.groupingRefreshPending) {
            liveStatus.textContent = "PRISM is grouping the faces again.";
        } else {
            liveStatus.textContent = "";
        }
    };

    const apply = data => {
        setText("[data-workload-known]", data.knownMatches);
        setText("[data-workload-individual]", data.individualReview);
        setText("[data-workload-matching]", data.matching);
        setText("[data-workload-failures]", data.matchingFailures);
        setText("[data-workload-closed]", data.closedUnidentified);
        setText("[data-workload-total]", data.totalUnresolved);
        setText("[data-workload-groups]", data.suggestedGroups);
        setText("[data-workload-grouped]", data.groupedAppearances);
        setText("[data-workload-ungrouped]", data.ungroupedAppearances);
        setText("[data-workload-individual-summary]", data.individualReview);

        setHiddenForCount(matchingWrap, data.matching);
        setHiddenForCount(failureWrap, data.matchingFailures);
        setHiddenForCount(closedWrap, data.closedUnidentified, closedWrap?.classList.contains("is-active") === true);
        if (workerWarning instanceof HTMLElement) {
            workerWarning.hidden = !data.matchingWorkerDelayed;
        }

        if (data.groupingRefreshPending) {
            root.querySelectorAll(
                "[data-group-face-checkbox], [data-toggle-group-selection], [data-use-group-candidate], [data-group-candidate-submit], " +
                "[data-group-decision] select, [data-group-decision] input:not([type=hidden]), [data-group-decision] button[type=submit]")
                .forEach(control => {
                    if (control instanceof HTMLInputElement
                        || control instanceof HTMLSelectElement
                        || control instanceof HTMLButtonElement) {
                        control.disabled = true;
                    }
                });
        }
        if (groupingPreparing instanceof HTMLElement && data.groupingSnapshotAvailable) {
            groupingPreparing.hidden = true;
        }
        if (groupingRefreshNotice instanceof HTMLElement) {
            const groupingRefreshFailed = Boolean(data.groupingFailureReason);
            groupingRefreshNotice.classList.toggle("is-refreshing", data.groupingRefreshPending && !groupingRefreshFailed);
            groupingRefreshNotice.classList.toggle("has-failure", groupingRefreshFailed);
            // Said only while the groups are being rebuilt or could not be.
            groupingRefreshNotice.hidden = !groupingRefreshFailed && !data.groupingRefreshPending;
            const copy = groupingRefreshNotice.querySelector("[data-grouping-refresh-copy]");
            if (copy instanceof HTMLElement) {
                if (groupingRefreshFailed) {
                    copy.textContent = data.groupingSnapshotAvailable
                        ? "These groups are from the last time grouping worked. The latest attempt failed; PRISM will try again by itself."
                        : "PRISM could not group the faces this time. It will try again by itself.";
                } else if (data.groupingRefreshPending) {
                    copy.textContent = data.groupingSnapshotAvailable
                        ? "PRISM is grouping the faces again. These groups stay until the new ones are ready."
                        : "PRISM is grouping the faces. The groups appear here when they are ready.";
                } else if (data.groupingSnapshotAvailable) {
                    copy.textContent = "The groups are up to date.";
                }
            }
            const stamp = groupingRefreshNotice.querySelector("[data-grouping-refreshed-at]");
            if (stamp instanceof HTMLElement && data.groupingRefreshedAtUtc) {
                const parsed = new Date(data.groupingRefreshedAtUtc);
                if (!Number.isNaN(parsed.valueOf())) {
                    stamp.textContent = `Last grouped ${parsed.toLocaleString()}`;
                    stamp.hidden = false;
                }
            }
        }

        updateLiveSummary(data);
        previousMatching = data.matching;
        previousGroupingRefreshing = data.groupingRefreshPending;
        stopped = data.matching <= 0 && !data.groupingRefreshPending;
    };

    const schedule = delay => {
        window.clearTimeout(timerId);
        timerId = window.setTimeout(poll, delay);
    };

    const poll = async () => {
        if (document.hidden) {
            schedule(3000);
            return;
        }

        try {
            const response = await fetch(statusUrl, {
                method: "GET",
                cache: "no-store",
                headers: { "Accept": "application/json" }
            });
            if (!response.ok) {
                schedule(5000);
                return;
            }

            const data = await response.json();
            if (data?.enabled === false) return;
            apply(data);
            if (!stopped) schedule(3000);
        } catch {
            schedule(5000);
        }
    };

    document.addEventListener("visibilitychange", () => {
        if (!document.hidden && !stopped) schedule(100);
    });

    if (!stopped) schedule(1200);
})();
