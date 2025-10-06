// wwwroot/js/init.js

(function () {
  function loadTodo() {
    if (!document.querySelector('.todo-list')) return;
    const script = document.currentScript;
    const src = script?.dataset.todoSrc || '/js/todo.js';
    const s = document.createElement('script');
    s.src = src;
    s.defer = true;
    document.head.appendChild(s);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => { loadTodo(); });
  } else {
    loadTodo();
  }
})();

