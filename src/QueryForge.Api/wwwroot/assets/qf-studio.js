(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const MAX_STEPS = 12;
  const OPS = [
    ['Trim','Trim whitespace','↧'],['DropDuplicates','Remove duplicates','⊘'],
    ['DropMissing','Remove empty rows','−'],['FillMissing','Fill missing values','＋'],
    ['FilterEquals','Filter equals','＝'],['FilterContains','Filter contains','⌕'],
    ['FilterAmountAbove','Filter amount above','>'],['SortAscending','Sort ascending','↥'],
    ['Uppercase','Uppercase text','A'],['Lowercase','Lowercase text','a'],
    ['Replace','Replace exact values','⇄'],['ReplaceText','Replace text','⇄'],
    ['RenameColumn','Rename column','✎'],['AddColumn','Add constant column','＋'],
    ['CopyColumn','Copy column','⧉'],['KeepColumns','Keep columns','▦']
  ];
  const labelFor = op => OPS.find(x => x[0] === op)?.[1] || op;
  const state = {
    view:'data',mode:'easy',dataset:null,large:false,steps:[],selected:-1,
    preview:null,previewData:null,aiAvailable:false,latestChart:null,report:null,pendingLink:null,draggedNode:null
  };
  const ui = (tag,cls,text) => {
    const e=document.createElement(tag);
    if(cls)e.className=cls;
    if(text!==undefined)e.textContent=String(text);
    return e;
  };
  function notice(message,error=false){
    const el=$('notice');el.textContent=message;el.classList.toggle('is-error',error);
    el.hidden=false;
  }
  async function json(url,options){
    const response=await fetch(url,options);
    let body;
    try{body=await response.json();}catch{throw new Error('Unexpected server response (HTTP '+response.status+').');}
    if(!response.ok)throw new Error(body.error || body.title || 'Request failed (HTTP '+response.status+').');
    return body;
  }
  const post=(url,body)=>json(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  function fillSelect(element,values,chosen,allowBlank=false){
    element.replaceChildren();
    if(allowBlank){const o=ui('option',null,'All columns');o.value='';element.append(o);}
    values.forEach(name=>{const o=ui('option',null,name);o.value=name;element.append(o);});
    if(chosen!==undefined && values.includes(chosen))element.value=chosen;
    else if(chosen==='' && allowBlank)element.value='';
  }
  const finite=n=>Number.isFinite(Number(n))?Number(n).toLocaleString():String(n??'—');
  function go(view){
    state.view=view;
    const names={data:['Data sources','Start with your data','Import, profile and inspect your dataset before making changes.'],
      prepare:['Prepare & ETL','Shape your data','Visually configure a real, executable transformation pipeline.'],
      visualize:['Visualize & report','Discover the story in your data','Generate evidence-based charts and reports without leaving the workspace.']};
    ['data','prepare','visualize'].forEach(v=>{
      $(v+'View').hidden=v!==view;
      document.querySelector('[data-view="'+v+'"]').classList.toggle('is-active',v===view);
    });
    $('topTitle').textContent=names[view][0];
    $('pageTitle').textContent=names[view][1]+'.';
    $('pageSubtitle').textContent=names[view][2];
    if(view==='visualize' && state.dataset && !state.large)populateChartControls();
    window.scrollTo({top:0,behavior:'auto'});
  }
  function setMode(mode){
    state.mode=mode;
    $('easyMode').classList.toggle('is-active',mode==='easy');
    $('advancedMode').classList.toggle('is-active',mode==='advanced');
    $('easyMode').setAttribute('aria-pressed',String(mode==='easy'));
    $('advancedMode').setAttribute('aria-pressed',String(mode==='advanced'));
    $('toolPanel').hidden=mode==='easy';
    document.body.classList.toggle('easy-mode',mode==='easy');
    $('canvasHelp').textContent=mode==='easy'
      ? 'Use the guided assistant to build a step-by-step plan. Select any node to adjust its settings.'
      : 'Drag operations from the library, reorder nodes, then preview before applying.';
    renderCanvas();
  }
  const draftKey=()=>state.dataset?'queryforge:v0.9:draft:'+state.dataset.id:null;
  function persistDraft(){
    try{
      const key=draftKey();
      if(key)localStorage.setItem(key,JSON.stringify({
        version:1,steps:state.steps,pipelineName:$('pipelineName').value
      }));
    }catch{notice('Browser storage unavailable. Your unsaved workflow will not survive a page reload.',true);}
  }
  function restoreDraft(){
    try{
      const stored=localStorage.getItem(draftKey());
      if(!stored)return;
      const d=JSON.parse(stored);
      if(d.version!==1||!Array.isArray(d.steps)||d.steps.length>MAX_STEPS ||
        !d.steps.every(s=>s&&OPS.some(op=>op[0]===s.operation)))return;
      state.steps=d.steps.map((s,i)=>({
        ...s,__id:String(s.__id||'n'+i),__x:Number.isFinite(s.__x)?s.__x:null,
        __y:Number.isFinite(s.__y)?s.__y:null
      }));
      $('pipelineName').value=String(d.pipelineName||'My clean dataset').slice(0,80);
      notice('Restored your unsaved ETL workflow for this dataset.');
    }catch{notice('Could not restore this browser draft. Your source data was not modified.',true);}
  }
  function table(container,headers,rows){
    container.replaceChildren();
    const t=ui('table'),th=ui('thead'),hr=ui('tr'),body=ui('tbody');
    headers.forEach(h=>hr.append(ui('th',null,h)));
    th.append(hr);t.append(th);
    rows.slice(0,30).forEach(row=>{
      const tr=ui('tr');
      headers.forEach(h=>{let v=row?.values?.[h] ?? row?.[h] ?? '';tr.append(ui('td',null,v));});
      body.append(tr);
    });
    t.append(body);container.append(t);
  }
  function renderStats(container,stats){
    container.replaceChildren();
    stats.forEach(([label,value])=>{
      const e=ui('div','stat-card');e.append(ui('b',null,finite(value)),ui('small',null,label));container.append(e);
    });
  }
  function renderQuality(){
    const d=state.dataset;if(!d)return;
    const quality=d.quality || {};
    const issues=quality.issues || [];
    renderStats($('stats'),[
      ['Total rows',d.rowCount],['Columns',d.headers?.length||0],
      ['Rows with issues',state.large?'—':quality.rowsWithIssues||0],
      ['Quality score',state.large?'—':(d.rowCount?Math.round(100*(1-(quality.rowsWithIssues||0)/d.rowCount)):100)+'%']
    ]);
    const groups=new Map();
    issues.forEach(x=>{
      const code=x.code || 'UNKNOWN';
      groups.set(code,(groups.get(code)||0)+1);
    });
    $('qualitySummary').replaceChildren();
    if(state.large){
      $('qualitySummary').append(ui('div','quality-item','Large CSV is stored as individual database rows. Quality details and ETL/analytics integration are still under development.'));
    }else if(!issues.length){
      $('qualitySummary').append(ui('div','quality-item','No issues reported by the current quality rules.'));
    }else{
      [...groups.entries()].sort((a,b)=>b[1]-a[1]).slice(0,7).forEach(([code,count])=>{
        const row=ui('div','quality-item');row.append(ui('strong',null,code.replaceAll('_',' ')),ui('small',null,count+' occurrence(s)'));$('qualitySummary').append(row);
      });
    }
  }
  function updateWorkspace(){
    const d=state.dataset;
    $('dataDetails').hidden=!d;
    $('prepareWorkspace').hidden=!d||state.large;
    $('visualizeWorkspace').hidden=!d||state.large;
    $('prepareNotice').hidden=!!d&&!state.large;
    $('visualizeNotice').hidden=!!d&&!state.large;
    if(!d){
      $('prepareNotice').textContent='Import a dataset first. Editable CSV and XLSX files can be cleaned and transformed here.';
      $('visualizeNotice').textContent='Import a dataset first to build charts and evidence-backed reports.';
      return;
    }
    $('dataHeading').textContent=d.fileName;
    $('dataDescription').textContent='Dataset '+d.id+' · '+finite(d.rowCount)+' rows · '+d.headers.length+' columns';
    $('workingDatasetName').textContent=d.fileName;
    $('workingDatasetMeta').textContent=finite(d.rowCount)+' rows · '+d.headers.length+' columns · Legacy editable engine';
    $('visualDatasetName').textContent=d.fileName;
    $('chartDatasetMeta').textContent=finite(d.rowCount)+' records. Backend-calculated aggregates; no AI-generated numbers.';
    if(state.large){
      $('prepareNotice').textContent='Read-only large CSV. This 25 MiB ingestion path does not yet run ETL. Use an editable dataset of 2 MiB or less.';
      $('visualizeNotice').textContent='Large CSV analytics is not connected yet. Use a 2 MiB-or-smaller editable dataset for charts.';
    }
    renderQuality();
    $('exportCsv').disabled=state.large;
    renderCanvas();renderEditor();
    if(!state.large)populateChartControls();
    // Other Studio modules receive the current, server-loaded dataset metadata.
    // Do not share mutable state or raw rows across modules.
    window.dispatchEvent(new CustomEvent('queryforge:dataset-selected',{
      detail:{id:d.id,headers:[...d.headers],fileName:d.fileName,large:state.large}
    }));
  }
  async function loadDataset(id,large=false){
    notice('Opening dataset…');
    try{
      const endpoint=large?'/api/v2/datasets/':'/api/datasets/';
      const d=await json(endpoint+encodeURIComponent(id));
      const preview=await json(endpoint+encodeURIComponent(id)+(large?'/rows?offset=0&limit=20':'/preview?limit=20'));
      state.dataset={...d,quality:d.quality||null};state.large=large;
      state.steps=[];state.selected=-1;state.preview=null;state.previewData=null;state.report=null;state.latestChart=null;state.pendingLink=null;
      restoreDraft();
      $('etlResultPanel').hidden=true;$('applyEtl').disabled=true;
      $('reportContent').textContent='Generate a chart or review the dataset report.';
      $('exportReport').disabled=true;
      table($('dataTable'),d.headers,preview.rows||[]);
      updateWorkspace();await loadSaved();
      notice((large?'Large read-only':'Editable')+' dataset loaded: '+d.fileName);
    }catch(error){notice('Could not load dataset: '+error.message,true);}
  }
  async function loadSaved(){
    try{
      const items=await json('/api/datasets');
      $('savedDatasets').replaceChildren();
      if(!items.length){$('savedDatasets').append(ui('p','side-muted','No datasets yet'));return;}
      items.slice().reverse().slice(0,16).forEach(x=>{
        const b=ui('button','saved-dataset'+(state.dataset?.id===x.id?' is-selected':''),x.fileName+' · '+finite(x.rowCount));
        b.type='button';b.title=x.fileName;
        b.addEventListener('click',()=>{go('data');loadDataset(x.id,false);});
        $('savedDatasets').append(b);
      });
    }catch{$('savedDatasets').replaceChildren(ui('p','side-muted','Dataset list unavailable'));}
  }
  async function importFile(file){
    if(!file)return;
    const suffix=(file.name.split('.').pop()||'').toLowerCase();
    if(!['csv','xlsx'].includes(suffix)){notice('Choose a .csv or .xlsx file.',true);return;}
    if(file.size>25*1024*1024 || (suffix==='xlsx' && file.size>2*1024*1024)){
      notice('CSV limit: 25 MiB. Editable Excel limit: 2 MiB.',true);return;
    }
    let large=file.size>2*1024*1024;
    let endpoint=large?'/api/v2/datasets':'/api/studio/import';
    const form=new FormData();form.append('file',file,file.name);
    $('chooseBtn').disabled=true;notice('Importing '+file.name+(large?' in read-only large-file mode':' for editing')+'…');
    try{
      let response=await fetch(endpoint,{method:'POST',body:form});
      let body;try{body=await response.json();}catch{body={};}
      // A compact CSV can still have >3,000 rows or >40 columns; offer safe
      // v2 read-only ingestion instead of pretending that editable ETL supports it.
      if(!response.ok && !large && suffix==='csv' &&
        /3,000 rows|1–40|2 MiB/.test(body.error||'')){
        large=true;endpoint='/api/v2/datasets';
        notice('CSV exceeds the editable row/column limits. Switching to safe read-only import…');
        response=await fetch(endpoint,{method:'POST',body:form});
        try{body=await response.json();}catch{body={};}
      }
      if(!response.ok)throw new Error(body.error||'Import failed (HTTP '+response.status+').');
      await loadDataset(body.id,large);
      if(large)notice('Imported '+file.name+' for paged preview. ETL and charts for large files are not supported yet.');
    }catch(e){notice('Import failed: '+e.message,true);}
    finally{$('chooseBtn').disabled=false;$('fileInput').value='';}
  }
  function inferHeaders(index){
    let cols=[...(state.dataset?.headers||[])];
    state.steps.slice(0,index).forEach(step=>{
      if(step.operation==='RenameColumn' && step.column && step.to){
        cols=cols.map(c=>c===step.column?step.to:c);
      }else if(step.operation==='AddColumn' && step.column && !cols.includes(step.column)){
        cols.push(step.column);
      }else if(step.operation==='CopyColumn' && step.to && !cols.includes(step.to)){
        cols.push(step.to);
      }
    });
    return cols;
  }
  function makeStep(op){
    const cols=inferHeaders(state.steps.length);
    const id=cols.find(c=>['order_id','invoice_id','id'].includes(c.toLowerCase()));
    let column=op==='Trim'?null:(op==='DropDuplicates' && id?id:(cols[0]||null));
    if(op==='AddColumn')column='new_field';
    if(op==='KeepColumns')column=null;
    const to=['FillMissing','Replace','ReplaceText','AddColumn','RenameColumn','CopyColumn','KeepColumns'].includes(op)
      ? (op==='AddColumn'?'value':op==='RenameColumn'?'renamed_field':op==='CopyColumn'?'copied_field':op==='KeepColumns'?cols.join(','):'') : null;
    return {operation:op,column,from:null,to};
  }
  function invalidate(){
    state.preview=null;state.previewData=null;
    $('applyEtl').disabled=true;$('etlResultPanel').hidden=true;
    persistDraft();
  }
  function addOperation(op,at=state.steps.length){
    if(state.large||!state.dataset){notice('Load an editable dataset first.',true);return;}
    if(!OPS.some(x=>x[0]===op) || state.steps.length>=MAX_STEPS){notice('Maximum 12 supported transformation steps.',true);return;}
    const added=makeStep(op);added.__id='node-'+Date.now()+'-'+Math.random().toString(36).slice(2,8);
    if(state.dropPosition){added.__x=state.dropPosition.x;added.__y=state.dropPosition.y;state.dropPosition=null;}
    state.steps.splice(at,0,added);state.selected=at;invalidate();
    renderCanvas();renderEditor();notice('Added '+labelFor(op)+'. Configure it and preview the complete plan.');
  }
  function renderPalette(){
    const term=$('operationSearch').value.trim().toLowerCase();$('operationPalette').replaceChildren();
    OPS.filter(op=>!term || op[1].toLowerCase().includes(term)).forEach(([id,title,glyph])=>{
      const b=ui('button','tool-item');b.type='button';b.draggable=true;b.title='Add '+title;
      b.append(ui('span','tool-icon',glyph),ui('span',null,title));
      b.addEventListener('click',()=>addOperation(id));
      b.addEventListener('dragstart',e=>{e.dataTransfer.setData('text/plain','op:'+id);e.dataTransfer.effectAllowed='copy';});
      $('operationPalette').append(b);
    });
  }
  function moveStep(from,to){
    if(from===to||from<0||to<0||from>=state.steps.length)return;
    const [item]=state.steps.splice(from,1);
    state.steps.splice(Math.min(to,state.steps.length),0,item);
    state.selected=state.steps.indexOf(item);invalidate();renderCanvas();renderEditor();
  }
  function insertDrop(event,at){
    event.preventDefault();
    const raw=event.dataTransfer.getData('text/plain');
    if(raw.startsWith('op:')){
      const board=$('graphBoard');if(board && event.target.closest('#graphBoard')){
        const rect=board.getBoundingClientRect();state.dropPosition={x:Math.max(20,event.clientX-rect.left-100),y:Math.max(20,event.clientY-rect.top-45)};
      }
      addOperation(raw.slice(3),at);
    }
    else if(raw.startsWith('step:')){
      const from=Number(raw.slice(5));
      moveStep(from,from<at?at-1:at);
    }
  }

  function graphPositions(){
    const steps=state.steps;
    steps.forEach((step,index)=>{
      if(!step.__id)step.__id='node-'+index;
      const col=Math.floor(index/3),row=index%3;
      if(!Number.isFinite(step.__x))step.__x=270+col*260;
      if(!Number.isFinite(step.__y))step.__y=55+row*150;
    });
    const endX=steps.length?Math.max(660,...steps.map(s=>s.__x+255)):425;
    return {start:{x:25,y:205},end:{x:endX,y:205}};
  }
  function connectAfter(sourceId,targetId){
    const steps=state.steps;
    const source=steps.findIndex(s=>s.__id===sourceId),target=steps.findIndex(s=>s.__id===targetId);
    if(sourceId!=='source' && source===-1)return notice('Connection source not found.',true);
    if(targetId!=='end' && target===-1)return notice('Connection target not found.',true);
    if(sourceId===targetId)return notice('A node cannot connect to itself.',true);
    if(targetId==='end' && sourceId==='source' && steps.length)return notice('Connect each transformation before the output.',true);
    if(targetId==='end' && source>=0)moveStep(source,steps.length-1);
    else if(sourceId==='source' && target>=0)moveStep(target,0);
    else if(source>=0 && target>=0){
      // Preserve one executable path; this editor does not pretend to support joins.
      const moving=steps[target];
      steps.splice(target,1);
      const after=steps.findIndex(s=>s.__id===sourceId);
      steps.splice(after+1,0,moving);
      state.selected=steps.indexOf(moving);
      invalidate();renderCanvas();renderEditor();
    }
    notice('Connection updated. Nodes execute in the connected path order. Preview to validate.');
  }
  function connectionPort(kind,nodeId){
    const button=ui('button','graph-port graph-port-'+kind,kind==='out'?'→':'←');
    button.type='button';button.title=kind==='out'?'Connect output to another node input':'Connect from the selected output';
    button.setAttribute('aria-label',button.title+' '+nodeId);
    button.addEventListener('click',e=>{
      e.stopPropagation();
      if(kind==='out'){
        state.pendingLink=nodeId;renderCanvas();
        notice('Connection started. Click the input (left circle) on the next node.');
      }else if(state.pendingLink){
        const from=state.pendingLink;state.pendingLink=null;connectAfter(from,nodeId);
        renderCanvas();
      }else notice('Choose an output (right circle) first.',true);
    });
    return button;
  }
  function renderCanvas(){
    const canvas=$('pipelineCanvas');canvas.replaceChildren();
    $('stepCount').textContent=state.steps.length+' / '+MAX_STEPS+' steps';
    canvas.classList.add('graph-viewport');
    const pos=graphPositions();
    const board=ui('div','graph-board');board.id='graphBoard';
    const boardWidth=Math.max(960,pos.end.x+265);
    board.style.width=boardWidth+'px';board.style.height='570px';
    const connections=document.createElementNS('http://www.w3.org/2000/svg','svg');
    connections.setAttribute('class','graph-lines');
    connections.setAttribute('viewBox','0 0 '+boardWidth+' 570');
    connections.setAttribute('aria-hidden','true');
    board.append(connections);
    function place(el,x,y){el.style.left=x+'px';el.style.top=y+'px';board.append(el);}
    const source=ui('div','flow-source graph-card');
    source.append(ui('span','node-index','▦'));
    const sourceText=ui('span','node-text');
    sourceText.append(ui('strong',null,'Input dataset'),ui('small',null,state.dataset?.fileName||'CSV / Excel'));
    source.append(sourceText,connectionPort('out','source'));
    place(source,pos.start.x,pos.start.y);
    state.steps.forEach((step,index)=>{
      const card=ui('div','flow-node graph-card'+(index===state.selected?' is-selected':''));
      card.draggable=state.mode==='advanced';card.dataset.index=String(index);
      card.tabIndex=0;card.setAttribute('role','button');
      card.setAttribute('aria-label','Step '+(index+1)+' '+labelFor(step.operation)+'. Select to edit.');
      card.append(connectionPort('in',step.__id),ui('span','node-index',String(index+1).padStart(2,'0')));
      const info=ui('span','node-text');info.append(ui('strong',null,labelFor(step.operation)));
      const extra=[step.column,step.from,step.to].filter(x=>x!==null&&x!=='').join(' · ');
      info.append(ui('small',null,extra||'Select to configure'));card.append(info);
      const grip=ui('span','node-move','⠿');grip.title='Drag to position node on canvas';
      card.append(grip,connectionPort('out',step.__id));
      card.addEventListener('click',e=>{
        if(e.target.closest('.graph-port'))return;
        state.selected=state.steps.indexOf(step);renderCanvas();renderEditor();
      });
      card.addEventListener('keydown',e=>{
        if(e.target!==card)return;
        if(e.key==='Enter'||e.key===' '){e.preventDefault();state.selected=state.steps.indexOf(step);renderCanvas();renderEditor();}
      });
      card.addEventListener('dragstart',e=>{
        if(state.mode!=='advanced'){e.preventDefault();return;}
        e.dataTransfer.setData('text/plain','step:'+state.steps.indexOf(step));
        e.dataTransfer.effectAllowed='move';
      });
      card.addEventListener('dragover',e=>e.preventDefault());
      card.addEventListener('drop',e=>{e.stopPropagation();insertDrop(e,state.steps.indexOf(step));});
      // Dedicated grip makes free positioning independent of HTML5 step reordering.
      grip.addEventListener('pointerdown',e=>{
        if(e.button!==0||state.mode!=='advanced')return;
        e.preventDefault();e.stopPropagation();
        grip.setPointerCapture(e.pointerId);
        const initial={clientX:e.clientX,clientY:e.clientY,x:step.__x,y:step.__y};
        function move(evt){
          const x=Math.max(5,Math.min(boardWidth-230,initial.x+evt.clientX-initial.clientX));
          const y=Math.max(5,Math.min(470,initial.y+evt.clientY-initial.clientY));
          step.__x=x;step.__y=y;card.style.left=x+'px';card.style.top=y+'px';
          paint();
        }
        function done(){grip.removeEventListener('pointermove',move);grip.removeEventListener('pointerup',done);grip.removeEventListener('pointercancel',done);persistDraft();}
        grip.addEventListener('pointermove',move);
        grip.addEventListener('pointerup',done);
        grip.addEventListener('pointercancel',done);
      });
      place(card,step.__x,step.__y);
    });
    const end=ui('div','flow-end graph-card');
    end.append(connectionPort('in','end'),ui('strong',null,'Output dataset'),ui('small',null,'Preview → Approve → Export'));
    end.addEventListener('dragover',e=>e.preventDefault());
    end.addEventListener('drop',e=>{e.stopPropagation();insertDrop(e,state.steps.length);});
    place(end,pos.end.x,pos.end.y);
    function paint(){
      connections.replaceChildren();
      let pairs=[];
      if(state.steps.length){
        pairs.push([{x:pos.start.x+220,y:pos.start.y+40},{x:state.steps[0].__x,y:state.steps[0].__y+40}]);
        for(let i=0;i<state.steps.length-1;i++)
          pairs.push([{x:state.steps[i].__x+220,y:state.steps[i].__y+40},{x:state.steps[i+1].__x,y:state.steps[i+1].__y+40}]);
        const last=state.steps.at(-1);pairs.push([{x:last.__x+220,y:last.__y+40},{x:pos.end.x,y:pos.end.y+40}]);
      }else pairs.push([{x:pos.start.x+220,y:pos.start.y+40},{x:pos.end.x,y:pos.end.y+40}]);
      for(const [a,b] of pairs){
        const path=document.createElementNS('http://www.w3.org/2000/svg','path');
        const bend=Math.max(55,Math.abs(b.x-a.x)/2);
        path.setAttribute('d','M '+a.x+' '+a.y+' C '+(a.x+bend)+' '+a.y+', '+(b.x-bend)+' '+b.y+', '+b.x+' '+b.y);
        path.setAttribute('stroke','#169c91');
        path.setAttribute('stroke-width','2.6');path.setAttribute('fill','none');
        path.setAttribute('marker-end','url(#qf-arrow)');
        connections.append(path);
      }
      let defs=connections.querySelector('defs');
      if(!defs){defs=document.createElementNS('http://www.w3.org/2000/svg','defs');connections.prepend(defs);}
      defs.innerHTML='<marker id="qf-arrow" markerWidth="9" markerHeight="9" refX="7" refY="3" orient="auto"><path d="M0 0 L7 3 L0 6 Z" fill="#169c91"/></marker>';
    }
    canvas.append(board);paint();
  }
  function renderEditor(){
    const index=state.selected,step=state.steps[index];
    $('stepEditor').hidden=!step;$('noSelectedStep').hidden=!!step;
    if(!step)return;
    const op=$('stepOperation');if(op.options.length===0){
      OPS.forEach(([id,title])=>{const opt=ui('option',null,title);opt.value=id;op.append(opt);});
    }
    op.value=step.operation;
    const cols=inferHeaders(index);
    fillSelect($('stepColumn'),cols,step.column,step.operation==='Trim');
    const isAdd=step.operation==='AddColumn',needsCol=step.operation!=='Trim';
    $('columnWrap').hidden=isAdd||step.operation==='KeepColumns';
    $('newColumnWrap').hidden=!isAdd;
    $('stepColumnFree').value=isAdd?(step.column||''):'';
    if(!isAdd && step.operation!=='Trim' && step.operation!=='KeepColumns' && cols.length>0 && !cols.includes(step.column)){
      // Display missing column error during preview rather than changing a user's plan silently.
      const option=ui('option',null,'Missing: '+step.column);option.value=step.column||'';
      $('stepColumn').append(option);$('stepColumn').value=step.column;
    }
    if(step.operation==='Trim')$('columnWrap').hidden=false;
    const from=['Replace','ReplaceText','FilterEquals','FilterContains','FilterAmountAbove'].includes(step.operation);
    const to=['Replace','ReplaceText','FillMissing','RenameColumn','AddColumn','CopyColumn','KeepColumns'].includes(step.operation);
    $('fromWrap').hidden=!from;$('toWrap').hidden=!to;
    $('stepFrom').value=step.from||'';$('stepTo').value=step.to||'';
    $('stepTo').placeholder=step.operation==='KeepColumns'?'order_id,amount,currency':'New value';
    $('moveUp').disabled=index===0;$('moveDown').disabled=index===state.steps.length-1;
  }
  function editStep(key,value){
    if(!state.steps[state.selected])return;
    state.steps[state.selected][key]=value===''?null:value;
    invalidate();renderCanvas();
  }
  function guidedSteps(prompt,cols){
    const text=prompt.toLowerCase(),selected=[];
    const nameInText=()=>cols.find(name=>new RegExp('(^|[^\\w])'+name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&').toLowerCase()+'(?=$|[^\\w])','i').test(text));
    const column=nameInText();
    if(/trim|spaces|whitespace/.test(text))selected.push({operation:'Trim',column:null,from:null,to:null});
    if(/duplicates?|deduplicat/.test(text)){
      if(!column)throw new Error('To remove duplicates, include the exact key column, e.g. "remove duplicate order_id".');
      selected.push({operation:'DropDuplicates',column,from:null,to:null});
    }
    if(/missing|empty|blank/.test(text)){
      if(!column)throw new Error('Specify the column to check, e.g. "remove empty customer".');
      if(/remove|drop|delete/.test(text))selected.push({operation:'DropMissing',column,from:null,to:null});
    }
    if(/sort|ascending/.test(text)){
      if(!column)throw new Error('To sort, name an existing column, e.g. "sort amount".');
      selected.push({operation:'SortAscending',column,from:null,to:null});
    }
    if(/uppercase|upper case/.test(text)){
      if(!column)throw new Error('To uppercase, specify the exact column name.');
      selected.push({operation:'Uppercase',column,from:null,to:null});
    }
    if(/lowercase|lower case/.test(text)){
      if(!column)throw new Error('To lowercase, specify the exact column name.');
      selected.push({operation:'Lowercase',column,from:null,to:null});
    }
    if(/keep (only )?(columns?|fields?)/.test(text)){
      const matched=cols.filter(c=>text.includes(c.toLowerCase()));
      if(!matched.length)throw new Error('Name exact columns to keep, e.g. "keep columns order_id, amount".');
      selected.push({operation:'KeepColumns',column:null,from:null,to:matched.join(',')});
    }
    if(!selected.length)throw new Error('Guided commands support trim, duplicates, missing, sort, text case, and keep columns. For more, use the visual operations or configure an LLM.');
    return selected;
  }
  function applySuggestions(steps,origin){
    if(state.steps.length+steps.length>MAX_STEPS)throw new Error('The plan would exceed 12 operations.');
    if(!Array.isArray(steps)||!steps.every(x=>OPS.some(o=>o[0]===x.operation)))
      throw new Error('The proposed steps include an unsupported operation.');
    state.steps.push(...steps.map(x=>({
      operation:x.operation,column:x.column??null,from:x.from??null,to:x.to??null
    })));
    state.selected=state.steps.length-1;invalidate();renderCanvas();renderEditor();
    go('prepare');notice(origin+' suggested '+steps.length+' transformation(s). Inspect every step, then preview. Nothing has been saved.');
  }
  function etlRequest(){
    return {pipelineName:$('pipelineName').value.trim(),steps:state.steps.map(step=>({
      operation:step.operation,column:step.column||null,from:step.from||null,to:step.to??null
    }))};
  }
  async function previewEtl(){
    if(!state.dataset||state.large){notice('Select an editable source.',true);return;}
    if(!state.steps.length){notice('Add at least one transformation.',true);return;}
    invalidate();$('previewEtl').disabled=true;notice('Running the actual C# ETL preview…');
    try{
      const request=etlRequest();
      const data=await post('/api/datasets/'+encodeURIComponent(state.dataset.id)+'/etl/preview',request);
      state.preview={request,fingerprint:data.previewFingerprint};
      state.previewData=data;
      $('etlResultPanel').hidden=false;$('applyEtl').disabled=false;
      $('etlResultText').textContent='Original source '+finite(data.rowCountBefore)+' rows → proposed '+finite(data.rowCountAfter)+' rows. No changes have been saved.';
      renderStats($('etlStats'),[['Rows before',data.rowCountBefore],['Rows after',data.rowCountAfter],
        ['Issues before',data.originalQuality?.issues?.length||0],['Issues after',data.proposedQuality?.issues?.length||0]]);
      $('auditList').replaceChildren();
      (data.audit||[]).forEach(a=>$('auditList').append(ui('div','audit-row',
        'Step '+a.step+' · '+labelFor(a.operation)+' · '+finite(a.affectedRows)+' affected · '+a.rowsBefore+' → '+a.rowsAfter+' rows')));
      table($('etlTable'),data.headers,data.sample||[]);
      notice('Preview ready. Review the evidence and approve to save a new dataset.');
    }catch(e){notice('ETL preview failed: '+e.message,true);}
    finally{$('previewEtl').disabled=false;}
  }
  async function applyEtl(){
    if(!state.preview)return;
    $('applyEtl').disabled=true;notice('Saving the approved transformation as a new dataset…');
    try{
      const req={...state.preview.request,previewFingerprint:state.preview.fingerprint};
      const saved=await post('/api/datasets/'+encodeURIComponent(state.dataset.id)+'/etl/apply',req);
      await loadDataset(saved.id,false);go('visualize');await getReport();
      notice('Created a new dataset version. Original source preserved; lineage recorded. Generate a chart from the saved result.');
    }catch(e){notice('Cannot save pipeline: '+e.message,true);$('applyEtl').disabled=false;}
  }
  function populateChartControls(){
    const d=state.dataset;if(!d||state.large)return;
    const cols=d.headers;
    const preferred=cols.find(c=>['currency','category','department','country','city','product_line'].includes(c.toLowerCase()))||cols[0];
    const amount=cols.find(c=>['amount','revenue','total','quantity'].includes(c.toLowerCase()))||cols[0];
    fillSelect($('chartColumn'),cols,preferred);
    fillSelect($('chartMeasure'),cols,amount);
    $('chartMeasure').disabled=$('chartKind').value!=='SumByCategory';
  }
  function svg(tag,attrs,text){
    const node=document.createElementNS('http://www.w3.org/2000/svg',tag);
    Object.entries(attrs||{}).forEach(([k,v])=>node.setAttribute(k,String(v)));
    if(text!==undefined)node.textContent=String(text);
    return node;
  }
  function drawChart(chart){
    const area=$('chartArea');area.replaceChildren();
    const points=(chart.points||[]).slice(0,20);
    if(!points.length){area.append(ui('p','chart-placeholder','No eligible data for these chart settings.'));return;}
    const width=760,height=Math.max(240,points.length*48+73),left=175,right=110,plot=width-left-right;
    const graphic=svg('svg',{viewBox:'0 0 '+width+' '+height,role:'img','aria-label':chart.title});
    graphic.append(svg('title',{},chart.title));
    const maximum=Math.max(1,...points.map(p=>Math.abs(Number(p.value)||0)));
    points.forEach((p,i)=>{
      const y=46+i*48,val=Number(p.value)||0,barWidth=Math.max(0,Math.abs(val)/maximum*plot);
      const label=String(p.label).length>21?String(p.label).slice(0,20)+'…':p.label;
      graphic.append(svg('text',{x:12,y:y+18,'font-size':13,fill:'#5e7389'},label));
      graphic.append(svg('rect',{x:left,y,width:plot,height:28,rx:6,fill:'#edf3f6'}));
      graphic.append(svg('rect',{x:left,y,width:barWidth,height:28,rx:6,fill:i%2?'#249f91':'#0c867c'}));
      graphic.append(svg('text',{x:left+plot+12,y:y+18,'font-size':13,'font-weight':750,fill:'#27475e'},finite(val)));
    });
    area.append(graphic);
  }
  async function renderChart(){
    if(!state.dataset||state.large)return;
    $('renderChart').disabled=true;notice('Calculating chart values in the C# backend…');
    try{
      const result=await post('/api/datasets/'+encodeURIComponent(state.dataset.id)+'/charts/preview',{
        kind:$('chartKind').value,column:$('chartColumn').value,
        measure:$('chartKind').value==='SumByCategory'?$('chartMeasure').value:null,
        topN:Number($('chartTop').value),excludeFlaggedRows:$('chartExclude').checked
      });
      state.latestChart=result;drawChart(result);
      $('chartNotes').textContent='Included '+finite(result.includedRows)+' rows; excluded '+finite(result.excludedRows)+
        '; invalid numeric '+finite(result.invalidNumericRows)+'. Units: '+result.unit+'. '+(result.notes||[]).join(' ');
      table($('chartTable'),['label','value','records'],result.points||[]);
      notice('Chart generated from actual backend aggregates.');
    }catch(e){notice('Chart failed: '+e.message,true);}
    finally{$('renderChart').disabled=false;}
  }
  async function getReport(){
    if(!state.dataset||state.large)return;
    try{
      const report=await json('/api/datasets/'+encodeURIComponent(state.dataset.id)+'/advisor/report');
      state.report=report;$('reportContent').textContent=report.executiveSummary+
        '\n\nEVIDENCE\n'+(report.evidence||[]).map(x=>'• '+x).join('\n');
      $('exportReport').disabled=false;
    }catch(e){$('reportContent').textContent='Could not generate report: '+e.message;}
  }
  function downloadReport(){
    if(!state.report)return;
    const blob=new Blob([JSON.stringify(state.report,null,2)],{type:'application/json'});
    const url=URL.createObjectURL(blob),link=ui('a');
    link.href=url;link.download='queryforge-evidence-'+state.dataset.id+'.json';document.body.append(link);link.click();link.remove();URL.revokeObjectURL(url);
  }
  function bind(){
    document.querySelectorAll('[data-view]').forEach(b=>b.addEventListener('click',()=>{
      const target=b.dataset.view;
      if(target==='visualize'&&state.dataset&&!state.large)getReport();
      go(target);
    }));
    $('easyMode').addEventListener('click',()=>setMode('easy'));
    $('advancedMode').addEventListener('click',()=>setMode('advanced'));
    $('chooseBtn').addEventListener('click',()=>$('fileInput').click());
    $('uploadZone').addEventListener('click',()=>$('fileInput').click());
    $('uploadZone').addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();$('fileInput').click();}});
    $('fileInput').addEventListener('change',()=>importFile($('fileInput').files[0]));
    const zone=$('uploadZone');
    for(const ev of ['dragenter','dragover'])zone.addEventListener(ev,e=>{e.preventDefault();zone.classList.add('drag-over');});
    for(const ev of ['dragleave','drop'])zone.addEventListener(ev,e=>{e.preventDefault();zone.classList.remove('drag-over');});
    zone.addEventListener('drop',e=>importFile(e.dataTransfer.files[0]));
    $('sampleBtn').addEventListener('click',async()=>{
      try{
        const response=await fetch('/samples/orders.csv');if(!response.ok)throw new Error('Sample file unavailable.');
        const blob=await response.blob();await importFile(new File([blob],'orders.csv',{type:'text/csv'}));
      }catch(e){notice(e.message,true);}
    });
    $('pivotSampleBtn').addEventListener('click',async()=>{
      try{
        const response=await fetch('/samples/pivot-sales.csv');
        if(!response.ok)throw new Error('Pivot demo file unavailable.');
        const blob=await response.blob();
        await importFile(new File([blob],'pivot-sales.csv',{type:'text/csv'}));
      }catch(e){notice(e.message,true);}
    });
    $('toPrepare').addEventListener('click',()=>go('prepare'));
    $('operationSearch').addEventListener('input',renderPalette);
    $('pipelineCanvas').addEventListener('dragover',e=>{if(state.mode==='advanced')e.preventDefault();});
    $('pipelineCanvas').addEventListener('drop',e=>{
      if(e.target.closest('.flow-node'))return;
      if(state.mode==='advanced')insertDrop(e,state.steps.length);
    });
    $('clearSteps').addEventListener('click',()=>{state.steps=[];state.selected=-1;invalidate();renderCanvas();renderEditor();notice('Unsaved pipeline steps cleared. Stored data was not affected.');});
    $('exportCsv').addEventListener('click',()=>{if(state.dataset&&!state.large)window.location.assign('/api/datasets/'+encodeURIComponent(state.dataset.id)+'/export.csv');});
    $('stepOperation').addEventListener('change',e=>{
      const replacement=makeStep(e.target.value);state.steps[state.selected]=replacement;
      invalidate();renderCanvas();renderEditor();
    });
    $('stepColumn').addEventListener('change',e=>editStep('column',e.target.value));
    $('stepColumnFree').addEventListener('input',e=>editStep('column',e.target.value));
    $('stepFrom').addEventListener('input',e=>editStep('from',e.target.value));
    $('stepTo').addEventListener('input',e=>editStep('to',e.target.value));
    $('moveUp').addEventListener('click',()=>moveStep(state.selected,state.selected-1));
    $('moveDown').addEventListener('click',()=>moveStep(state.selected,state.selected+1));
    $('removeStep').addEventListener('click',()=>{
      if(state.selected<0)return;state.steps.splice(state.selected,1);
      state.selected=state.steps.length?Math.min(state.selected,state.steps.length-1):-1;
      invalidate();renderCanvas();renderEditor();
    });
    $('pipelineName').addEventListener('input',invalidate);
    $('previewEtl').addEventListener('click',previewEtl);
    $('applyEtl').addEventListener('click',applyEtl);
    document.querySelectorAll('[data-prompt]').forEach(b=>b.addEventListener('click',()=>{
      $('promptInput').value=b.dataset.prompt;
      $('promptInput').focus();
    }));
    $('guidedPlan').addEventListener('click',()=>{
      if(!state.dataset||state.large){notice('Import an editable dataset first.',true);return;}
      try{
        const plan=guidedSteps($('promptInput').value,state.dataset.headers);
        applySuggestions(plan,'Guided commands');
      }catch(e){notice(e.message,true);}
    });
    $('ollamaPlan').addEventListener('click',async()=>{
      if(!state.dataset||state.large){notice('Import an editable dataset first.',true);return;}
      $('ollamaPlan').disabled=true;notice('Asking the configured AI model for a validated transformation plan…');
      try{
        const plan=await post('/api/studio/assistant/plan',{
          datasetId:state.dataset.id,prompt:$('promptInput').value.trim()
        });
        applySuggestions(plan.steps,'AI');
      }catch(e){notice('AI: '+e.message,true);}
      finally{$('ollamaPlan').disabled=false;}
    });
    $('chartKind').addEventListener('change',()=>{$('chartMeasure').disabled=$('chartKind').value!=='SumByCategory';});
    $('renderChart').addEventListener('click',renderChart);
    $('exportReport').addEventListener('click',downloadReport);
  }
  async function init(){
    bind();renderPalette();setMode('easy');go('data');await loadSaved();
    try{
      const ai=await json('/api/studio/assistant/status');
      state.aiAvailable=ai.configured;
      $('assistantInfo').textContent=ai.configured
        ? (ai.mode==='hosted-openai'?
            'Hosted OpenAI configured. Only your request and column names leave this machine; never raw rows. Review suggestions before applying.' :
            'Local Ollama configured. Proposals are validated before preview. No automatic changes.')
        : 'Offline guided commands work without AI. Optional Ollama or hosted OpenAI can be configured on your computer.';
      $('ollamaPlan').textContent=ai.mode==='hosted-openai'?'Ask hosted AI ✳':'Ask local AI ✳';
      $('ollamaPlan').disabled=!ai.configured;
    }catch{$('ollamaPlan').disabled=true;}
    const id=new URLSearchParams(location.search).get('dataset');
    if(id)loadDataset(id,false);
  }
  init();
})();
