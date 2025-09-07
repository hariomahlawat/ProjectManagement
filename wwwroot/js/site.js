// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('.password-toggle').forEach(btn => {
    const container = btn.closest('.password-container');
    if (!container) return;
    const input = container.querySelector('input');
    if (!input) return;
    const show = () => { input.type = 'text'; };
    const hide = () => { input.type = 'password'; };
    btn.addEventListener('mousedown', show);
    btn.addEventListener('touchstart', show);
    btn.addEventListener('mouseup', hide);
    btn.addEventListener('mouseleave', hide);
    btn.addEventListener('touchend', hide);
  });
});
