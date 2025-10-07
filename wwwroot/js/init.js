// wwwroot/js/init.js

(function () {
  const TODO_FLAG = '__todoLoaded';
  const scriptEl = document.currentScript;
  const todoSrc = scriptEl?.dataset.todoSrc || '/js/todo.js';
  let observer;

  function stopWatching() {
    if (observer) {
      observer.disconnect();
      observer = undefined;
    }
    document.removeEventListener('htmx:afterSwap', handleDynamicUpdate);
  }

  function injectTodoScript() {
    window[TODO_FLAG] = true;
    const s = document.createElement('script');
    s.src = todoSrc;
    s.defer = true;
    document.head.appendChild(s);
  }

  function ensureTodoLoaded() {
    if (window[TODO_FLAG]) {
      return true;
    }

    if (!document.querySelector('.todo-list')) {
      return false;
    }

    injectTodoScript();
    stopWatching();
    return true;
  }

  function handleDynamicUpdate() {
    if (ensureTodoLoaded()) {
      stopWatching();
    }
  }

  function startWatching() {
    if (observer || window[TODO_FLAG]) {
      return;
    }

    observer = new MutationObserver(handleDynamicUpdate);
    observer.observe(document.body, { childList: true, subtree: true });
    document.addEventListener('htmx:afterSwap', handleDynamicUpdate);
  }

  function init() {
    if (!ensureTodoLoaded()) {
      startWatching();
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();

