"use strict";

// =============================
// PRISM WISH
// The birthday or anniversary wish for the signed-in person: balloons and confetti over the page
// and one glass card (wwwroot/css/components/prism-wish.css). The server renders the card only on
// the day (Pages/Shared/Components/CelebrationWish); this script decides when it plays:
//
//   - on the first page after every sign-in on the day;
//   - after that at most once an hour, and only as a page opens, never in the middle of one;
//   - again whenever the balloon beside the name is clicked.
//
// It never plays while something is shown full screen (a briefing being presented). Reduce
// Motion shows the card without the balloons. Nothing is sent anywhere: when it last played is
// kept in this browser only. Behaviour: docs/ux/celebration-wish.md.
// =============================
(() => {
    const HOUR_MS = 60 * 60 * 1000;
    const SHOW_SECONDS = 7;
    const FADE_MS = 600;

    // Hue, saturation, lightness: party colours for a birthday, gold and rose for an anniversary.
    const PALETTES = {
        birthday: [[352, 82, 58], [26, 92, 56], [46, 95, 55], [150, 62, 45], [200, 85, 52], [262, 72, 62], [322, 75, 60]],
        anniversary: [[42, 88, 54], [346, 78, 60], [12, 85, 68], [48, 80, 70], [330, 65, 66]]
    };

    /**
     * Whether the wish plays as this page opens.
     * @param {{date?: string, signin?: string, at?: number}|null} last what was stored when it last played
     * @param {{date: string, signin: string}} wish today's wish as the server rendered it
     * @param {number} now milliseconds since the epoch
     */
    function shouldPlay(last, wish, now) {
        if (!last || last.date !== wish.date) return true; // not yet today
        if (last.signin !== wish.signin) return true; // signed in again since
        return !(Number(last.at) > 0) || now - Number(last.at) >= HOUR_MS;
    }

    if (typeof module !== "undefined" && module.exports) {
        module.exports = { shouldPlay, HOUR_MS };
    }
    if (typeof document === "undefined") return;

    const wish = document.getElementById("prismWish");
    const sky = document.getElementById("prismWishSky");
    if (!wish || !sky) return;

    const kind = wish.dataset.kind === "anniversary" ? "anniversary" : "birthday";
    const today = { date: wish.dataset.date || "", signin: wish.dataset.signin || "" };
    const storageKey = `prism.wish.${wish.dataset.user || "me"}`;
    const reduceMotion = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;

    const readLast = () => {
        try {
            return JSON.parse(window.localStorage.getItem(storageKey) || "null");
        } catch {
            return null;
        }
    };
    const remember = () => {
        try {
            window.localStorage.setItem(storageKey, JSON.stringify({ ...today, at: Date.now() }));
        } catch {
            // Storage blocked: the wish simply plays again on the next page.
        }
    };

    // ---- the sky ------------------------------------------------------------------------------
    const ctx = sky.getContext("2d");
    let width = 0;
    let height = 0;
    let run = null;
    let hideTimer = 0;
    let closeTimer = 0;

    const rnd = (a, b) => a + Math.random() * (b - a);
    const pick = list => list[Math.floor(Math.random() * list.length)];
    const hsl = (c, lighter = 0, alpha = 1) => `hsla(${c[0]}, ${c[1]}%, ${Math.min(96, c[2] + lighter)}%, ${alpha})`;

    function resize() {
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        width = window.innerWidth;
        height = window.innerHeight;
        sky.width = width * ratio;
        sky.height = height * ratio;
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    }

    function build() {
        const colors = PALETTES[kind];
        const small = width < 500;
        const balloons = [];
        const confetti = [];
        const count = Math.max(12, Math.min(26, Math.round(width / 55)));
        for (let i = 0; i < count; i += 1) {
            const r = rnd(26, 44) * (small ? 0.8 : 1);
            balloons.push({
                x: rnd(0.04, 0.96) * width, y: height + r * 2 + rnd(0, 80), r, c: pick(colors),
                vy: rnd(95, 165), amp: rnd(10, 34), freq: rnd(0.5, 1.1), phase: rnd(0, 6.28),
                delay: rnd(0, 2.6), string: rnd(60, 110)
            });
        }
        balloons.sort((a, b) => a.r - b.r);

        // Two bursts of confetti, one from each bottom corner.
        const perSide = small ? 55 : 95;
        for (const side of [0, 1]) {
            for (let i = 0; i < perSide; i += 1) {
                const angle = side ? Math.PI - rnd(0.75, 1.3) : rnd(0.75, 1.3);
                const speed = rnd(0.55, 1.15) * Math.sqrt(height) * 44;
                confetti.push({
                    x: side ? width + 10 : -10, y: height + 10,
                    vx: Math.cos(angle) * speed * 0.55, vy: -Math.sin(angle) * speed,
                    w: rnd(6, 11), h: rnd(9, 16), c: pick(colors), rot: rnd(0, 6.28), spin: rnd(-7, 7),
                    flip: rnd(0, 6.28), flipSpeed: rnd(5, 12), delay: rnd(0, 0.25)
                });
            }
        }
        return { balloons, confetti, start: 0, last: 0, fadeAt: 0, frame: 0 };
    }

    function heartPath(s) {
        ctx.beginPath();
        ctx.moveTo(0, s * 0.95);
        ctx.bezierCurveTo(-s * 1.45, -s * 0.05, -s * 0.7, -s * 1.2, 0, -s * 0.4);
        ctx.bezierCurveTo(s * 0.7, -s * 1.2, s * 1.45, -s * 0.05, 0, s * 0.95);
        ctx.closePath();
    }

    function drawBalloon(b, t) {
        const x = b.x + Math.sin(t * b.freq + b.phase) * b.amp;
        const r = b.r;
        const sway = Math.sin(t * 2 + b.phase) * 7;

        ctx.strokeStyle = "hsla(220, 12%, 60%, 0.65)";
        ctx.lineWidth = 1.2;
        ctx.beginPath();
        ctx.moveTo(x, b.y + r + 5);
        ctx.bezierCurveTo(x + sway, b.y + r + b.string * 0.35, x - sway, b.y + r + b.string * 0.7, x + sway * 0.4, b.y + r + b.string);
        ctx.stroke();

        ctx.save();
        ctx.translate(x, b.y);
        ctx.rotate(Math.cos(t * b.freq + b.phase) * 0.09);
        const shade = ctx.createRadialGradient(-r * 0.3, -r * 0.4, r * 0.1, 0, 0, r * 1.15);
        shade.addColorStop(0, hsl(b.c, 22, 0.97));
        shade.addColorStop(0.55, hsl(b.c, 0, 0.94));
        shade.addColorStop(1, hsl(b.c, -14, 0.94));
        ctx.fillStyle = shade;
        if (kind === "anniversary") {
            heartPath(r * 0.92);
        } else {
            ctx.beginPath();
            ctx.ellipse(0, 0, r * 0.84, r, 0, 0, Math.PI * 2);
        }
        ctx.fill();

        ctx.fillStyle = hsl(b.c, -14, 0.95); // the knot
        ctx.beginPath();
        ctx.moveTo(0, r - 2);
        ctx.lineTo(-5, r + 6);
        ctx.lineTo(5, r + 6);
        ctx.closePath();
        ctx.fill();

        ctx.fillStyle = "hsla(0, 0%, 100%, 0.42)"; // the shine
        ctx.beginPath();
        ctx.ellipse(-r * 0.34, -r * 0.42, r * 0.13, r * 0.26, -0.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }

    function frame(now) {
        if (!run) return;
        if (!run.start) {
            run.start = now;
            run.last = now;
        }
        const t = (now - run.start) / 1000;
        const dt = Math.min(0.05, (now - run.last) / 1000);
        run.last = now;
        let alive = false;

        ctx.clearRect(0, 0, width, height);
        const alpha = run.fadeAt ? Math.max(0, 1 - (now - run.fadeAt) / FADE_MS) : 1;
        ctx.globalAlpha = alpha;

        for (const b of run.balloons) {
            if (t < b.delay) {
                alive = true;
                continue;
            }
            b.y -= b.vy * dt;
            if (b.y + b.r + b.string > -20) {
                alive = true;
                drawBalloon(b, t);
            }
        }
        for (const p of run.confetti) {
            if (t < p.delay) {
                alive = true;
                continue;
            }
            p.vy += 620 * dt;
            p.vx *= 1 - 0.9 * dt;
            p.vy *= 1 - 0.9 * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;
            p.rot += p.spin * dt;
            p.flip += p.flipSpeed * dt;
            if (p.y > height + 30 && p.vy > 0) continue;
            alive = true;
            ctx.save();
            ctx.translate(p.x, p.y);
            ctx.rotate(p.rot);
            ctx.scale(1, Math.cos(p.flip));
            ctx.fillStyle = hsl(p.c, 0, 0.95);
            ctx.fillRect(-p.w / 2, -p.h / 2, p.w, p.h);
            ctx.restore();
        }
        ctx.globalAlpha = 1;

        if (!alive || (run.fadeAt && alpha <= 0)) {
            clearSky();
            return;
        }
        run.frame = window.requestAnimationFrame(frame);
    }

    function clearSky() {
        if (run) window.cancelAnimationFrame(run.frame);
        run = null;
        ctx.clearRect(0, 0, width, height);
        sky.hidden = true;
    }

    // ---- the card -----------------------------------------------------------------------------
    const isOpen = () => !wish.hidden;

    function closeCard() {
        window.clearTimeout(closeTimer);
        if (!isOpen()) return;
        wish.classList.remove("is-on");
        hideTimer = window.setTimeout(() => {
            wish.hidden = true;
        }, 480);
    }

    // A click or Esc: the card goes and the sky fades out quickly.
    function dismiss() {
        closeCard();
        if (run && !run.fadeAt) run.fadeAt = window.performance.now();
    }

    function play() {
        if (document.fullscreenElement) return;
        clearSky();
        window.clearTimeout(hideTimer);
        window.clearTimeout(closeTimer);
        remember();

        wish.hidden = false;
        window.requestAnimationFrame(() => window.requestAnimationFrame(() => wish.classList.add("is-on")));
        closeTimer = window.setTimeout(closeCard, SHOW_SECONDS * 1000);

        if (reduceMotion()) return;
        sky.hidden = false;
        resize();
        run = build();
        run.frame = window.requestAnimationFrame(frame);
    }

    // ---- the balloon beside the name ------------------------------------------------------------
    function addMark() {
        const userItem = document.getElementById("userMenu")?.closest("li");
        if (!userItem?.parentNode) return null;
        const item = document.createElement("li");
        item.className = "nav-item prism-wish-mark-item";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "prism-wish-mark";
        button.dataset.kind = kind;
        button.title = "Play today's wish again";
        button.setAttribute("aria-label", "Play today's wish again");
        const icon = document.createElement("i");
        icon.className = kind === "anniversary" ? "bi bi-balloon-heart-fill" : "bi bi-balloon-fill";
        icon.setAttribute("aria-hidden", "true");
        button.append(icon);
        item.append(button);
        userItem.parentNode.insertBefore(item, userItem);
        return button;
    }

    const mark = addMark();
    mark?.addEventListener("click", play);

    // The click that closes the wish still does whatever it was aimed at.
    document.addEventListener("click", event => {
        if (mark && mark.contains(event.target)) return;
        if (isOpen()) dismiss();
    }, true);
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && isOpen()) dismiss();
    });
    document.addEventListener("fullscreenchange", () => {
        if (document.fullscreenElement && isOpen()) dismiss();
    });
    window.addEventListener("resize", () => {
        if (run) resize();
    });

    const playIfDue = () => {
        if (shouldPlay(readLast(), today, Date.now())) play();
    };
    if (document.visibilityState === "hidden") {
        // Opened in a background tab: wait until someone is looking.
        document.addEventListener("visibilitychange", function onVisible() {
            if (document.visibilityState !== "visible") return;
            document.removeEventListener("visibilitychange", onVisible);
            playIfDue();
        });
    } else {
        playIfDue();
    }
})();
