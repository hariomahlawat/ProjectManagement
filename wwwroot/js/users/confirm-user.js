export default function init(){
  const input=document.getElementById('confirmUser');
  if(!input) return;
  const expected=input.dataset.expected;
  const buttonId=input.dataset.button;
  const button=document.getElementById(buttonId);
  const ack=document.getElementById('ack');
  function update(){
    button.disabled=!(input.value===expected && ack.checked);
  }
  input.addEventListener('input',update);
  ack.addEventListener('change',update);
  update();
}

document.addEventListener('DOMContentLoaded',init);
