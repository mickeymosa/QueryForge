(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const dom = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text !== undefined) node.textContent = String(text);
    return node;
  };
  const svg = (tag, attrs = {}, text) => {
    const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
    Object.entries(attrs).forEach(([key, value]) => node.setAttribute(key, String(value)));
    if (text !== undefined) node.textContent = String(text);
    return node;
  };
  let dataset = null;
  let result = null;

  function notify(message, error = false) {
    $('pivotMessage').textContent = message;
    $('pivotMessage').classList.toggle('is-error', error);
  }
  function reset() {
    result = null;
    $('pivotResult').hidden = true;
    $('pivotStats').hidden = true;
    $('exportPivot').disabled = true;
  }
  function selectOptions(select, names, value, emptyLabel) {
    select.replaceChildren();
    if (emptyLabel) {
      const option = dom('option', null, emptyLabel);
      option.value = '';
      select.append(option);
    }
    names.forEach(name => {
      const option = dom('option', null, name);
      option.value = name;
      select.append(option);
    });
    if (value !== undefined && (names.includes(value) || value === '' && emptyLabel))
      select.value = value;
  }
  function initFields(details) {
    dataset = details;
    reset();
    const names = details.headers;
    const recommended = names.find(name =>
      ['region','city','category','department','product_line','country','status','currency']
        .includes(name.toLowerCase())) || names[0];
    const other = names.find(name =>
      name !== recommended && ['product','product_line','currency','status','category']
        .includes(name.toLowerCase())) || '';
    const measure = names.find(name =>
      ['amount','revenue','total','sales','quantity','price'].includes(name.toLowerCase()))
      || names[0];
    selectOptions($('pivotRows'), names, recommended);
    selectOptions($('pivotColumns'), names.filter(name => name !== recommended), other,
      'None — one dimension');
    selectOptions($('pivotMeasure'), names, measure);
    $('pivotAggregate').value = 'Count';
    $('pivotMeasure').disabled = true;
    $('pivotExclude').checked = true;
    notify('Ready to explore ' + details.fileName + '. Pick dimensions and click Build pivot table.');
  }
  function setRowDimension() {
    if (!dataset) return;
    const previous = $('pivotColumns').value;
    const names = dataset.headers.filter(name => name !== $('pivotRows').value);
    selectOptions($('pivotColumns'), names, names.includes(previous) ? previous : '',
      'None — one dimension');
    reset();
  }
  function onAggregateChanged() {
    $('pivotMeasure').disabled = $('pivotAggregate').value === 'Count';
    reset();
  }
  function exact(value) {
    return value === null || value === undefined ? '—' : String(value);
  }
  function rowCell(tag, value, scope) {
    const cell = dom(tag, null, exact(value));
    if (scope) cell.scope = scope;
    return cell;
  }
  function makePivotTable(data) {
    const holder = $('pivotTable'); holder.replaceChildren();
    const table = dom('table','pivot-matrix');
    const caption = dom('caption',null,
      data.aggregate + ' by ' + data.rowDimension +
      (data.columnDimension ? ' and ' + data.columnDimension : '') +
      '. Values in ' + data.unit + '.');
    table.append(caption);
    const head=dom('thead'), header=dom('tr');
    header.append(rowCell('th',data.rowDimension,'col'));
    data.columnLabels.forEach(label=>header.append(rowCell('th',label,'col')));
    header.append(rowCell('th','Total','col'));
    head.append(header);table.append(head);
    const body=dom('tbody');
    data.rows.forEach(row=>{
      const tr=dom('tr');
      tr.append(rowCell('th',row.label,'row'));
      row.values.forEach(value=>tr.append(rowCell('td',value)));
      const total=rowCell('td',row.total);total.className='pivot-total';
      tr.append(total);body.append(tr);
    });
    if (!data.rows.length) {
      const tr=dom('tr'),empty=rowCell('td','No eligible records');
      empty.colSpan=data.columnLabels.length+2;
      tr.append(empty);body.append(tr);
    }
    table.append(body);
    const footer=dom('tfoot'),sum=dom('tr');
    sum.append(rowCell('th','Grand total','row'));
    data.columnTotals.forEach(value=>sum.append(rowCell('td',value)));
    sum.append(rowCell('td',data.grandTotal));
    footer.append(sum);table.append(footer);holder.append(table);
  }
  function renderStats(data) {
    const holder=$('pivotStats');holder.replaceChildren();holder.hidden=false;
    [
      ['Grand total',exact(data.grandTotal)],
      ['Rows included',data.includedRows],
      ['Quality excluded',data.excludedRows],
      ['Invalid numeric',data.invalidNumericRows]
    ].forEach(([label,value])=>{
      const card=dom('div','pivot-stat');
      card.append(dom('strong',null,value),dom('span',null,label));holder.append(card);
    });
  }
  function drawBars(data) {
    const rows=data.rows;
    const width=860, left=190, chartWidth=515, height=Math.max(185,rows.length*43+64);
    const graph=svg('svg',{viewBox:'0 0 '+width+' '+height,role:'img',
      'aria-label':'Row totals of '+data.aggregate+' by '+data.rowDimension});
    graph.append(svg('title',{},'Pivot row totals'));
    const valid=rows.map(r=>r.total===null?null:Number(r.total));
    const numeric=valid.filter(value=>value!==null&&Number.isFinite(value));
    const min=Math.min(0,...numeric),max=Math.max(0,...numeric);
    const range=max-min || 1;
    const pos=value=>left+(value-min)/range*chartWidth;
    const zero=pos(0);
    graph.append(svg('line',{x1:zero,x2:zero,y1:20,y2:height-18,stroke:'#708e9a','stroke-width':1}));
    rows.forEach((row,index)=>{
      const y=25+index*43,value=valid[index];
      const label=row.label.length>23?row.label.slice(0,22)+'…':row.label;
      graph.append(svg('text',{x:10,y:y+20,'font-size':13,fill:'#4a6175'},label));
      if(value!==null && Number.isFinite(value)){
        const x=pos(value);
        const bar=svg('rect',{
          x:Math.min(x,zero),y,width:Math.max(1,Math.abs(x-zero)),
          height:26,rx:5,fill:value<0?'#dc817e':'#108e80'
        });
        bar.append(svg('title',{},row.label+': '+exact(row.total)+' '+data.unit));
        graph.append(bar);
      }
      graph.append(svg('text',{x:left+chartWidth+16,y:y+19,
        'font-size':12,'font-weight':700,fill:'#2a435b'},exact(row.total)));
    });
    return graph;
  }
  function drawLine(data) {
    const rows=data.rows,width=Math.max(650,rows.length*75),height=310,
      left=72,right=46,top=35,bottom=68;
    const graph=svg('svg',{viewBox:'0 0 '+width+' '+height,role:'img',
      'aria-label':'Connected pivot row totals in sorted category order'});
    graph.append(svg('title',{},'Pivot row totals — connecting categories in alphabetical order'));
    const values=rows.map(row=>row.total===null?null:Number(row.total));
    const nums=values.filter(x=>x!==null&&Number.isFinite(x));
    const min=Math.min(0,...nums),max=Math.max(0,...nums),range=max-min||1;
    const x=i=>left+i*(width-left-right)/Math.max(1,rows.length-1);
    const y=n=>top+(max-n)/range*(height-top-bottom);
    graph.append(svg('line',{x1:left,x2:width-right,y1:y(0),y2:y(0),stroke:'#acc5cc'}));
    let path='',connected=false;
    rows.forEach((row,index)=>{
      const value=values[index],xx=x(index);
      if(value!==null && Number.isFinite(value)){
        path+=(connected?' L ':' M ')+xx+' '+y(value);
        connected=true;
        const circle=svg('circle',{cx:xx,cy:y(value),r:5,fill:'#118f80'});
        circle.append(svg('title',{},row.label+': '+exact(row.total)+' '+data.unit));
        graph.append(circle);
        graph.append(svg('text',{x:xx,y:y(value)-13,'font-size':11,fill:'#3e5c71',
          'text-anchor':'middle'},exact(row.total)));
      }else connected=false;
      const short=row.label.length>12?row.label.slice(0,11)+'…':row.label;
      graph.append(svg('text',{x:xx,y:height-34,'font-size':11,fill:'#536b7c',
        transform:'rotate(-22 '+xx+' '+(height-34)+')',
        'text-anchor':'end'},short));
    });
    if(path)graph.insertBefore(svg('path',{d:path,fill:'none',stroke:'#108e80',
      'stroke-width':2.5,'stroke-linecap':'round'}),graph.children[1]||null);
    return graph;
  }
  function drawLinkedChart(data) {
    const holder=$('pivotChart');holder.replaceChildren();
    if (!data.rows.length){
      holder.append(dom('p','pivot-empty','No row groups available to chart.'));return;
    }
    holder.append($('pivotChartKind').value==='line'?drawLine(data):drawBars(data));
  }
  function display(data) {
    result=data;
    $('pivotResult').hidden=false;
    $('exportPivot').disabled=false;
    renderStats(data);
    $('pivotMeta').textContent=data.rows.length+' row groups × '+data.columnLabels.length+
      ' column groups · '+data.unit+' · Dataset SHA-256 '+data.datasetSha256.slice(0,12)+'…';
    makePivotTable(data);
    drawLinkedChart(data);
    $('pivotNotes').textContent=(data.notes||[]).join(' ') +
      ($('pivotChartKind').value==='line'
        ? ' A line connects alphabetically ordered categories; it does not imply chronological order.'
        : '');
    notify('Pivot calculated by the C# backend. Nothing was saved or modified.');
  }
  function escapeCell(value, isText=false) {
    let content=exact(value);
    if(value===null || value===undefined) content='';
    // Prevent CSV formula injection when a category/header originates in untrusted data.
    if(isText && /^[\s]*[=+\-@]/.test(content)) content="'"+content;
    return '"'+content.replaceAll('"','""')+'"';
  }
  function exportCsv() {
    if(!result)return;
    const d=result,header=[d.rowDimension,...d.columnLabels,'Total'];
    const output=[header.map(x=>escapeCell(x,true)).join(',')];
    d.rows.forEach(row=>
      output.push([escapeCell(row.label,true),...row.values.map(v=>escapeCell(v)),
        escapeCell(row.total)].join(',')));
    output.push([escapeCell('Grand total',true),...d.columnTotals.map(v=>escapeCell(v)),
      escapeCell(d.grandTotal)].join(','));
    const blob=new Blob(['\ufeff',output.join('\r\n'),'\r\n'],{
      type:'text/csv;charset=utf-8'
    });
    const url=URL.createObjectURL(blob),a=dom('a');
    a.href=url;a.download='queryforge-pivot-'+d.datasetId+'.csv';
    document.body.append(a);a.click();a.remove();
    URL.revokeObjectURL(url);
  }
  async function build() {
    if(!dataset||dataset.large){
      notify('Choose an editable dataset before building a pivot.',true);return;
    }
    const sourceId=dataset.id;
    const payload={
      rowDimension:$('pivotRows').value,
      columnDimension:$('pivotColumns').value || null,
      aggregate:$('pivotAggregate').value,
      measure:$('pivotAggregate').value==='Count'?null:$('pivotMeasure').value,
      excludeFlaggedRows:$('pivotExclude').checked
    };
    reset();$('buildPivot').disabled=true;notify('Calculating a bounded pivot on the server…');
    try{
      const response=await fetch('/api/datasets/'+encodeURIComponent(sourceId)+'/pivot/preview',{
        method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)
      });
      let body;
      try{body=await response.json();}catch{throw new Error('Unexpected response from the pivot API.');}
      if(!response.ok)throw new Error(body.error||body.title||'Pivot request failed.');
      if(!dataset||sourceId!==dataset.id)return;
      display(body);
    }catch(error){
      if(dataset&&sourceId===dataset.id)notify(error.message,true);
    }finally{$('buildPivot').disabled=false;}
  }
  window.addEventListener('queryforge:dataset-selected',event=>initFields(event.detail));
  $('pivotRows').addEventListener('change',setRowDimension);
  ['pivotColumns','pivotMeasure','pivotExclude'].forEach(id=>
    $(id).addEventListener('change',reset));
  $('pivotAggregate').addEventListener('change',onAggregateChanged);
  $('buildPivot').addEventListener('click',build);
  $('exportPivot').addEventListener('click',exportCsv);
  $('pivotChartKind').addEventListener('change',()=>{
    if(!result)return;
    drawLinkedChart(result);
    $('pivotNotes').textContent=(result.notes||[]).join(' ') +
      ($('pivotChartKind').value==='line'
        ? ' A line connects alphabetically ordered categories; it does not imply chronological order.'
        : '');
  });
})();
