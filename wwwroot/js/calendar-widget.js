const list = document.getElementById('calendar-widget-list');
if(list){
  const start = new Date().toISOString();
  const end = new Date(Date.now() + 30*24*60*60*1000).toISOString();
  fetch(`/calendar/events?start=${start}&end=${end}`, {credentials:'same-origin'})
    .then(r => r.ok ? r.json() : [])
    .then(evts => render(evts))
    .catch(() => {});
}

function render(evts){
  evts.sort((a,b) => new Date(a.start) - new Date(b.start));
  const top = evts.slice(0,5);
  list.innerHTML='';
  if(top.length===0){
    const li=document.createElement('li');
    li.className='text-muted';
    li.textContent='No upcoming events';
    list.appendChild(li);
    return;
  }
  top.forEach(ev => {
    const li=document.createElement('li');
    li.className='d-flex justify-content-between mb-1';
    const left=document.createElement('span'); left.textContent=ev.title;
    const right=document.createElement('span'); right.className='text-muted small'; right.textContent=new Date(ev.start).toLocaleDateString();
    li.appendChild(left); li.appendChild(right);
    list.appendChild(li);
  });
}
