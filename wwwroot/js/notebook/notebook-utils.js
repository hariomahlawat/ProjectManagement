// SECTION: Notebook shared DOM helpers
export const qs = (root, selector) => root ? root.querySelector(selector) : null;
export const qsa = (root, selector) => Array.from(root ? root.querySelectorAll(selector) : []);
export const closestAction = (event) => event.target.closest('[data-action]');
