(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const el = (tag, text) => { const e=document.createElement(tag); if(text!==undefined)e.textContent=text; return e; };
  let dataset=null, generation=0, preview=null, evidence=null, busy=false;
  const api = async (url, body) => {
    const r=await fetch(url,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
    const data=await r.json(); if(!r.ok)throw Error(data.error||'Request failed: '+r.status); return data;
  };
  const root=()=>'/api/datasets/'+encodeURIComponent(dataset.id);
  const ws=()=>'/api/workspace/'+encodeURIComponent(dataset.id);
  const status=s=>{$('guidedStatus').textContent=s;};
  function invalidate(){preview=null;$('guidedApply').disabled=true;$('guidedChanges').replaceChildren();}
  function clearAnswer(){evidence=null;$('guidedEvidence').disabled=true;$('guidedResult').replaceChildren();}
  function options(id, headers, blank){
    const select=$(id);select.replaceChildren();
    if(blank){const o=el('option','None');o.value='';select.append(o);}
    headers.forEach(h=>{const o=el('option',h);o.value=h;select.append(o);});
  }
  function sync(){ $('guidedMeasure').disabled=$('guidedAggregate').value==='count'; }
  const selected=()=>Array.from($('guidedSuggestions').querySelectorAll('input:checked')).map(e=>JSON.parse(e.value));
  function controls(){ $('guidedPreview').disabled=busy||!selected().length; $('guidedApply').disabled=busy||!preview; $('guidedRun').disabled=busy; $('guidedAsk').disabled=busy; }
  async function run(fn){
    if(busy||!dataset)return;
    const current=generation;busy=true;controls();status('');
    try{await fn(current);}catch(e){if(current===generation)status(e.message);}
    finally{busy=false;controls();}
  }
  window.addEventListener('queryforge:dataset-selected',async e=>{
    const current=++generation; dataset=e.detail; invalidate();clearAnswer();
    $('guidedWorkspace').hidden=dataset.large;
    $('guidedSuggestions').replaceChildren();$('guidedQuestion').value='';$('guidedPlan').textContent='Review the calculation below before running it.';
    if(dataset.large)return;
    options('guidedMeasure',dataset.headers,false);options('guidedGroup',dataset.headers,true);
    $('guidedAggregate').value='count';$('guidedExclude').checked=false;sync();controls();
    try{
      const suggestions=await api(ws()+'/recommend');
      if(current!==generation)return;
      if(!suggestions.length)$('guidedSuggestions').append(el('p','No whitespace or exact duplicate fixes detected. Other quality signals still need review.'));
      for(const s of suggestions){
        const label=el('label');label.style.display='block';
        const input=el('input');input.type='checkbox';input.value=JSON.stringify(s.step);
        input.addEventListener('change',()=>{invalidate();controls();});
        label.append(input,document.createTextNode(' '+s.description+' · '+s.affectedRows+' affected source rows'));
        $('guidedSuggestions').append(label);
      }
      controls();
    }catch(e){if(current===generation)status(e.message);}
  });
  $('guidedPreview').onclick=()=>run(async current=>{
    invalidate();const request={pipelineName:'Guided cleaning',steps:selected()};
    const result=await api(root()+'/etl/preview',request);
    if(current!==generation||JSON.stringify(request.steps)!==JSON.stringify(selected()))return;
    preview={...request,previewFingerprint:result.previewFingerprint};
    const area=$('guidedChanges');
    area.append(el('p','Rows: '+result.rowCountBefore+' → '+result.rowCountAfter+' | Flagged rows: '+result.originalQuality.rowsWithIssues+' → '+result.proposedQuality.rowsWithIssues));
    for(const a of result.audit)area.append(el('p',a.operation+': '+a.affectedRows+' affected rows'));
    const table=el('table');const head=el('tr');result.headers.forEach(h=>head.append(el('th',h)));table.append(head);
    for(const s of result.sample){const tr=el('tr');result.headers.forEach(h=>tr.append(el('td',s.values[h]??'')));table.append(tr);}
    const wrap=el('div');wrap.className='table-scroll';wrap.append(table);area.append(wrap);
    area.append(el('p','Preview only. Saving creates a separate dataset.'));
  });
  $('guidedApply').onclick=()=>run(async current=>{
    if(!preview)return;
    const saved=await api(root()+'/etl/apply',preview);
    if(current!==generation)return;
    invalidate();status('Saved cleaned copy: '+saved.fileName);
    // Use the existing dataset selection flow; no independent mutable store.
    window.dispatchEvent(new CustomEvent('queryforge:open-dataset',{detail:{id:saved.id}}));
  });
  $('guidedAggregate').onchange=()=>{sync();clearAnswer();};
  ['guidedMeasure','guidedGroup','guidedTop','guidedExclude'].forEach(id=>$(id).onchange=clearAnswer);
  $('guidedAsk').onclick=()=>run(async current=>{
    clearAnswer();const question=$('guidedQuestion').value;
    const plan=await api('/api/studio/assistant/analyze-plan',{datasetId:dataset.id,prompt:question});
    if(current!==generation||question!==$('guidedQuestion').value)return;
    $('guidedAggregate').value=plan.aggregate;$('guidedMeasure').value=plan.measure||dataset.headers[0];
    $('guidedGroup').value=plan.groupBy||''; if(!Array.from($('guidedTop').options).some(o=>o.value===String(plan.top)))$('guidedTop').append(el('option',String(plan.top)));
    $('guidedTop').value=String(plan.top);$('guidedExclude').checked=plan.excludeFlagged;sync();
    $('guidedPlan').textContent='AI interpreted your question as '+plan.aggregate+(plan.measure?' of '+plan.measure:' records')+(plan.groupBy?' grouped by '+plan.groupBy:'')+'. Confirm the controls, then calculate. No figures were generated by AI.';
  });
  const plan=()=>({aggregate:$('guidedAggregate').value,measure:$('guidedAggregate').value==='count'?null:$('guidedMeasure').value,groupBy:$('guidedGroup').value||null,top:Number($('guidedTop').value),excludeFlagged:$('guidedExclude').checked});
  $('guidedRun').onclick=()=>run(async current=>{
    clearAnswer();const request=plan();
    const result=await api(ws()+'/analyze',request);
    if(current!==generation||JSON.stringify(request)!==JSON.stringify(plan()))return;
    evidence={request,result};$('guidedEvidence').disabled=false;render(result);
  });
  function render(r){
    const area=$('guidedResult');area.replaceChildren();
    const label=r.aggregate==='count'?'Record count':r.aggregate+' of '+r.measure;
    area.append(el('h3',label+': '+(r.total??'No valid numeric values')+' '+r.unit));
    area.append(el('p',r.usedRows+' records used / '+r.sourceRows+' source records · '+r.flaggedExcluded+' quality-excluded · '+r.invalidMeasureRows+' invalid numeric values skipped'));
    if(r.groupBy===null)return;
    area.append(el('p','Showing '+r.groups.length+' of '+r.groupCount+' groups by '+r.groupBy+', ranked descending. Total above covers all eligible records.'));
    const table=el('table');const head=el('tr');[r.groupBy,label,'Records'].forEach(v=>head.append(el('th',v)));table.append(head);
    for(const g of r.groups){const tr=el('tr');[g.label||'(blank)',g.value,String(g.records)].forEach(v=>tr.append(el('td',v)));table.append(tr);}
    const wrap=el('div');wrap.className='table-scroll';wrap.append(table);area.append(wrap);
    if(r.chart!=='bar')return;
    const nums=r.groups.map(g=>Number(g.value));if(nums.some(n=>!Number.isFinite(n)))return;
    const lo=Math.min(0,...nums),hi=Math.max(0,...nums),span=hi-lo||1;
    const chart=el('div');chart.setAttribute('role','img');chart.setAttribute('aria-label',label+' by '+r.groupBy+'; exact values in table above');
    chart.append(el('h4',label+' by '+r.groupBy+' · '+r.unit));
    r.groups.forEach((g,i)=>{
      const line=el('div');line.style.margin='12px 0';line.append(el('div',(g.label||'(blank)')+' — '+g.value));
      const track=el('div');track.style.cssText='position:relative;height:20px;background:#edf1f5;border-radius:3px;';
      const zero=el('div');zero.style.cssText='position:absolute;top:0;bottom:0;width:1px;background:#334155;left:'+(-lo/span*100)+'%;';
      const bar=el('div');bar.style.cssText='position:absolute;top:2px;bottom:2px;background:#2563eb;left:'+((Math.min(0,nums[i])-lo)/span*100)+'%;width:'+(Math.abs(nums[i])/span*100)+'%;';
      track.append(bar,zero);line.append(track);chart.append(line);
    });area.append(chart);
  }
  $('guidedEvidence').onclick=()=>{
    if(!evidence)return;
    const url=URL.createObjectURL(new Blob([JSON.stringify(evidence,null,2)],{type:'application/json'}));
    const a=el('a');a.href=url;a.download='queryforge-analysis.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  };
})();
