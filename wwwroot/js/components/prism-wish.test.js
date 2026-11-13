"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const { shouldPlay, HOUR_MS } = require("./prism-wish.js");

const wish = { date: "2026-11-13", signin: "100" };
const now = Date.UTC(2026, 10, 13, 6, 0, 0);

test("the wish plays the first time on the day", () => {
    assert.equal(shouldPlay(null, wish, now), true);
    assert.equal(shouldPlay({ date: "2025-11-13", signin: "100", at: now - 1000 }, wish, now), true);
});

test("the wish plays again after a new sign-in", () => {
    assert.equal(shouldPlay({ date: wish.date, signin: "99", at: now - 1000 }, wish, now), true);
});

test("the wish waits an hour between page opens", () => {
    const last = at => ({ ...wish, at });
    assert.equal(shouldPlay(last(now - 1000), wish, now), false);
    assert.equal(shouldPlay(last(now - HOUR_MS + 1), wish, now), false);
    assert.equal(shouldPlay(last(now - HOUR_MS), wish, now), true);
});

test("a damaged record plays the wish instead of hiding it", () => {
    assert.equal(shouldPlay({ ...wish }, wish, now), true);
    assert.equal(shouldPlay({ ...wish, at: "soon" }, wish, now), true);
});
