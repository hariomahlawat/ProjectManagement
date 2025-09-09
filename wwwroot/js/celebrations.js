// wwwroot/js/celebrations.js
(function(){
  const widget = document.querySelector('.celebrations-widget');
  if(!widget) return;
  const token = document.querySelector('#celebrate-token input[name="__RequestVerificationToken"]')?.value;
  fetch('/celebrations/upcoming?window=30', {credentials:'same-origin'})
    .then(r => r.ok ? r.json() : [])
    .then(list => render(list))
    .catch(() => {});

  function render(list){
    const sections = {today:[], next7:[], month:[]};
    list.forEach(it => {
      if(it.daysAway === 0) sections.today.push(it);
      else if(it.daysAway < 8) sections.next7.push(it);
      else sections.month.push(it);
    });
    renderSection('today', sections.today);
    renderSection('next7', sections.next7);
    renderSection('month', sections.month);
  }

  function typeClass(t){ return t === 'Birthday' ? 'cele-dot-birthday' : 'cele-dot-anniversary'; }

  function renderSection(name, items){
    const ul = widget.querySelector(`ul[data-section="${name}"]`);
    const wrap = ul.closest('div');
    ul.innerHTML='';
    if(items.length===0){ wrap.classList.add('d-none'); return; }
    wrap.classList.remove('d-none');
    let display = items;
    let extra = 0;
    if(name==='today' && items.length>2){ display = items.slice(0,2); extra = items.length-2; }
    display.forEach(it => {
      const li = document.createElement('li');
      li.className = 'd-flex align-items-center justify-content-between mb-1';
      const left = document.createElement('div');
      left.className='d-flex align-items-center';
      const dot=document.createElement('span'); dot.className='cele-dot '+typeClass(it.eventType);
      const nameSpan=document.createElement('span'); nameSpan.textContent=it.name;
      left.appendChild(dot); left.appendChild(nameSpan);
      if(it.daysAway>0){
        const badge=document.createElement('span'); badge.className='badge bg-light text-secondary ms-1'; badge.textContent=`${it.daysAway}d`;
        left.appendChild(badge);
      }
      li.appendChild(left);
      const btn=document.createElement('button'); btn.className='btn btn-sm btn-link p-0 cele-add-task'; btn.dataset.id=it.id; btn.innerHTML='<i class="bi bi-plus-circle"></i>';
      li.appendChild(btn);
      ul.appendChild(li);
    });
    if(extra>0){
      const more=document.createElement('li'); more.className='text-muted'; more.textContent=`+${extra} more`; ul.appendChild(more);
    }
  }

  widget.addEventListener('click', e => {
    const btn = e.target.closest('.cele-add-task');
    if(!btn) return;
    e.preventDefault();
    const id = btn.dataset.id;
    if(!id) return;
    fetch(`/celebrations/${id}/task`, {method:'POST', headers:{'RequestVerificationToken':token}, credentials:'same-origin'})
      .then(r => { if(r.ok) btn.disabled=true; })
      .catch(() => {});
  }, {passive:false});
})();
