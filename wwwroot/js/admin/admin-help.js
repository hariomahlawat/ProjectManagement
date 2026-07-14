(() => {
    "use strict";
    const requested = new URLSearchParams(window.location.search).get("section");
    if (!requested) return;
    const target = document.getElementById(requested);
    if (target) target.scrollIntoView({ block: "start" });
})();
