(() => {
  const list = document.getElementById('stepList');
  const canvas = document.getElementById('etlCanvas');
  if (!list || !canvas) return;
  const node = (label, kind, click) => {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'qf-etl-node ' + kind;
    btn.textContent = label;
    if (click) {
      btn.addEventListener('click', () => {
        click.scrollIntoView({behavior:'smooth',block:'center'});
        const field = click.querySelector('select,input');
        field?.focus({preventScroll:true});
        click.classList.add('qf-node-highlight');
        setTimeout(() => click.classList.remove('qf-node-highlight'), 1200);
      });
    } else btn.disabled = true;
    return btn;
  };
  const arrow = () => {
    const el=document.createElement('span');
    el.className='qf-etl-arrow';el.textContent='→';el.setAttribute('aria-hidden','true');
    return el;
  };
  function render() {
    canvas.replaceChildren();
    canvas.append(node('CSV Source','source'));
    Array.from(list.children).forEach((step,i)=>{
      canvas.append(arrow());
      const selected = step.querySelector('select')?.value || 'Trim';
      canvas.append(node((i+1)+'. '+selected,'transform',step));
    });
    canvas.append(arrow(),node('Revalidate','validation'),arrow(),node('Save version','save'));
  }
  // Step list mutation signals reorder, insert or delete. Inputs signal operation change.
  const watcher=new MutationObserver(render);
  watcher.observe(list,{childList:true});
  list.addEventListener('change',render);
  render();
})();