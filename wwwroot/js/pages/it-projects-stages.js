// IT project stage chain, browser side (docs/ux/it-projects.md, "Stage rules").
//
// The server (ItProjectStageRules) is the authority; this module keeps the editor's ticks
// consistent as the user works, so what is posted is what the server will accept, and it names
// the stages a save will also complete or reopen for the confirm dialog.
//
// A state is { code, label, completed, date, skipped } in stage order. Pure functions only.
(function (root, factory) {
    if (typeof module === 'object' && module.exports) {
        module.exports = factory();
    } else {
        root.ItProjectStageChain = factory();
    }
})(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    function clone(states) {
        return states.map(function (s) {
            return { code: s.code, label: s.label, completed: !!s.completed, date: s.date || null, skipped: !!s.skipped };
        });
    }

    // Rule 1 and 2: the highest completed (or dated) non-skipped stage decides the chain.
    function chain(states) {
        var s = clone(states);
        var highest = -1;
        s.forEach(function (stage, i) {
            if (!stage.skipped && (stage.completed || stage.date)) {
                highest = i;
            }
        });
        return s.map(function (stage, i) {
            var completed = !stage.skipped && i <= highest;
            return { code: stage.code, label: stage.label, completed: completed, date: completed ? stage.date : null, skipped: stage.skipped };
        });
    }

    // Ticking completes the stage (and, through chain, the earlier ones); unticking reopens it
    // and every later stage, clearing their dates.
    function tick(states, index, checked) {
        var s = clone(states);
        if (checked) {
            s[index].completed = true;
        } else {
            for (var i = index; i < s.length; i++) {
                s[i].completed = false;
                s[i].date = null;
            }
        }
        return chain(s);
    }

    // A date means done.
    function setDate(states, index, date) {
        var s = clone(states);
        s[index].date = date || null;
        if (date) {
            s[index].completed = true;
        }
        return chain(s);
    }

    // Including an optional stage puts it into the chain; skipping it clears it.
    function setSkipped(states, index, skipped) {
        var s = clone(states);
        s[index].skipped = !!skipped;
        if (skipped) {
            s[index].completed = false;
            s[index].date = null;
        }
        return chain(s);
    }

    // Which stages the current state would complete or reopen compared with the saved one.
    function diff(saved, current) {
        var before = {};
        saved.forEach(function (s) { before[s.code] = !!s.completed; });
        var completed = [];
        var reopened = [];
        current.forEach(function (s) {
            var was = !!before[s.code];
            if (!was && s.completed) {
                completed.push(s.code);
            } else if (was && !s.completed) {
                reopened.push(s.code);
            }
        });
        return { completed: completed, reopened: reopened };
    }

    // The stages the user did not touch themselves but the chain changed: for the note and the
    // confirm dialog. `touched` is the set of codes the user ticked or dated directly.
    function cascade(saved, current, touched) {
        var d = diff(saved, current);
        var untouched = function (code) { return !touched || !touched[code]; };
        return { completed: d.completed.filter(untouched), reopened: d.reopened.filter(untouched) };
    }

    function labels(codes, states) {
        var byCode = {};
        states.forEach(function (s) { byCode[s.code] = s.label || s.code; });
        return codes.map(function (c) { return byCode[c] || c; });
    }

    function join(items) {
        if (items.length <= 1) {
            return items.join('');
        }
        return items.slice(0, -1).join(', ') + ' and ' + items[items.length - 1];
    }

    // One sentence for the note under the editor, or null when nothing else changes.
    function summary(cascadeResult, states) {
        var parts = [];
        if (cascadeResult.completed.length > 0) {
            parts.push('also complete ' + join(labels(cascadeResult.completed, states)));
        }
        if (cascadeResult.reopened.length > 0) {
            parts.push('also reopen ' + join(labels(cascadeResult.reopened, states)));
        }
        if (parts.length === 0) {
            return null;
        }
        return 'Saving will ' + parts.join(' and ') + '.';
    }

    return { chain: chain, tick: tick, setDate: setDate, setSkipped: setSkipped, diff: diff, cascade: cascade, summary: summary, join: join };
});
