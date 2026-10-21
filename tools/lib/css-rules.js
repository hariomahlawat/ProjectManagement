'use strict';

// A small CSS reader for the theme tools: enough structure to know, for each declaration,
// its selector, its enclosing at-rules and its exact position in the source. It is not a
// general CSS parser: it assumes plain (un-nested) rules, which is what wwwroot/css uses.

// Same-length masking keeps positions aligned with the original source.
function mask(source) {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, (m) => m.replace(/[^\n]/g, ' '))
    .replace(/"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.)*'/g, (m) => m[0] + m.slice(1, -1).replace(/[^\n]/g, 'x') + m[0]);
}

function splitDeclarations(source, masked, start, end) {
  const declarations = [];
  let segmentStart = start;
  let depth = 0;
  for (let i = start; i <= end; i++) {
    const ch = i === end ? ';' : masked[i];
    if (ch === '(') depth++;
    else if (ch === ')') depth--;
    else if (ch === ';' && depth <= 0) {
      const text = masked.slice(segmentStart, i);
      const colon = text.indexOf(':');
      if (colon > 0 && text.trim()) {
        const property = text.slice(0, colon).trim();
        const valueStart = segmentStart + colon + 1;
        let rawValue = source.slice(valueStart, i);
        let important = false;
        const importantMatch = rawValue.match(/\s*!\s*important\s*$/i);
        if (importantMatch) {
          important = true;
          rawValue = rawValue.slice(0, rawValue.length - importantMatch[0].length);
        }
        declarations.push({ property, value: rawValue.trim(), important, valueStart, valueEnd: valueStart + rawValue.length });
      }
      segmentStart = i + 1;
    }
  }
  return declarations;
}

// Returns a flat list of style rules: { selector, atRules: [...preludes], declarations }.
function readRules(source) {
  const masked = mask(source);
  const rules = [];
  const stack = [];
  let segmentStart = 0;
  for (let i = 0; i < masked.length; i++) {
    const ch = masked[i];
    if (ch === '{') {
      // The header comes from the source (masking hides the "dark" in [data-bs-theme="dark"]).
      const header = source.slice(segmentStart, i).replace(/\/\*[\s\S]*?\*\//g, '').trim();
      stack.push({ header, bodyStart: i + 1, isAt: header.startsWith('@') });
      segmentStart = i + 1;
    } else if (ch === '}') {
      const frame = stack.pop();
      if (frame && !frame.isAt) {
        rules.push({
          selector: frame.header.replace(/\s+/g, ' '),
          atRules: stack.filter((f) => f.isAt).map((f) => f.header.replace(/\s+/g, ' ')),
          declarations: splitDeclarations(source, masked, frame.bodyStart, i),
        });
      }
      segmentStart = i + 1;
    } else if (ch === ';' && stack.length && stack[stack.length - 1].isAt && !masked.slice(segmentStart, i).includes(':')) {
      segmentStart = i + 1; // e.g. @import / @charset inside nothing
    } else if (ch === ';' && !stack.length) {
      segmentStart = i + 1;
    }
  }
  return rules;
}

const DARK = /\[data-bs-theme\s*=\s*["']?dark["']?\]/;

function isDarkSelector(selector) {
  return DARK.test(selector);
}

function splitSelector(selector) {
  const parts = [];
  let depth = 0;
  let current = '';
  for (const ch of selector) {
    if (ch === '(' || ch === '[') depth++;
    else if (ch === ')' || ch === ']') depth--;
    if (ch === ',' && depth === 0) {
      parts.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  if (current.trim()) parts.push(current.trim());
  return parts;
}

// The light selector a dark selector part repaints, e.g. '[data-bs-theme="dark"] .a' -> '.a'.
function lightSelectorOf(darkPart) {
  return darkPart
    .replace(/^:root\[data-bs-theme\s*=\s*["']?dark["']?\]/, ':root')
    .replace(/^html\[data-bs-theme\s*=\s*["']?dark["']?\]/, 'html')
    .replace(/^:where\(\[data-bs-theme\s*=\s*["']?dark["']?\]\)\s*/, '')
    .replace(/^\[data-bs-theme\s*=\s*["']?dark["']?\]\s*/, '')
    .trim();
}

function propertyFamily(property) {
  const p = property.toLowerCase();
  if (p.startsWith('--')) return p;
  if (p.startsWith('background')) return 'background';
  if (p.startsWith('border') || p === 'outline' || p === 'outline-color') return p.startsWith('outline') ? 'outline' : 'border';
  return p;
}

module.exports = { mask, readRules, isDarkSelector, splitSelector, lightSelectorOf, propertyFamily };
