/* QueryForge Analytics Studio v0.4
   Rendering is entirely local. No remote script/CSS dependency.
   Only source-backed aggregates returned by our API are visualized. */
(() => {
  const $ = id => document.getElementById(id);
  const dataSetId = new URLSearchParams(window.location.search).get('dataset');
  const svgNs = 'http://www.w3.org/2000/svg';
  let dataset = null, latestChart = null, advisory = null;
  const humanCount = number => Number(number).toLocaleString(undefined, {maximumFractionDigits:2});

  const status = (value, fail = false) => {
    $('pageStatus').textContent = value;
    $('pageStatus').classList.toggle('error', fail);
  };
  const chartStatus = (value, fail = false) => {
    $('chartStatus').textContent = value;
    $('chartStatus').classList.toggle('error', fail);
  };
  const element = (tag, text, className) => {
    const el = document.createElement(tag);
    if (text !== undefined) el.textContent = text;
    if (className) el.className = className;
    return el;
  };
  const svgElement = (tag, attributes = {}, label) => {
    const el = document.createElementNS(svgNs, tag);
    Object.entries(attributes).forEach(([key, value]) => el.setAttribute(key, String(value)));
    if (label !== undefined) el.textContent = label;
    return el;
  };
  const downloadJson = (name, value) => {
    const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = element('a');
    a.href = url; a.download = name; a.click();
    // Revoke after the synchronous click, with a small delay for navigation.
    setTimeout(() => URL.revokeObjectURL(url), 2000);
  };
  const getJson = async url => {
    const response = await fetch(url);
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || result.title || 'Request failed');
    return result;
  };
  const setColumnOptions = (id, headers) => {
    const select = $(id); select.replaceChildren();
    headers.forEach(col => {
      const option = element('option', col);
      option.value = col; select.append(option);
    });
  };
  const buildKpi = (label, value) => {
    const card = element('div', undefined, 'kpi');
    card.append(element('strong', String(value)), element('span', label));
    return card;
  };
  function setSuggestion(suggestion) {
    $('chartKind').value = suggestion.kind;
    $('column').value = suggestion.column;
    if (suggestion.measure) $('measure').value = suggestion.measure;
    updateForm();
    buildChart();
  }
  function updateForm() {
    const kind = $('chartKind').value;
    $('measure').disabled = kind !== 'SumByCategory';
    $('measure').parentElement.classList.toggle('hidden', kind !== 'SumByCategory');
    $('topN').querySelector('option[value="20"]').disabled = kind === 'Histogram';
    if (kind === 'Histogram' && $('topN').value === '20') $('topN').value = '12';
  }
  function renderSuggestions(suggestions) {
    $('suggestions').replaceChildren();
    if (!suggestions.length) {
      $('suggestions').append(element('span', 'No reliable automatic chart matches; choose a column manually.', 'qf-note'));
      return;
    }
    suggestions.forEach(s => {
      const button = element('button', s.kind === 'Histogram' ? 'Distribution: ' + s.column : 'Count: ' + s.column, 'studio-tag');
      button.type = 'button';
      button.classList.add('studio-tag');
      button.title = s.reason;
      button.addEventListener('click', () => setSuggestion(s));
      $('suggestions').append(button);
    });
  }

  function renderSvg(chart) {
    const svg = $('chartSvg'); svg.replaceChildren();
    const points = chart.points;
    svg.setAttribute('aria-label', chart.title + ': ' + points.length + ' categories; ' + chart.unit);
    const width = 900;
    const rowHeight = 45;
    const top = 50;
    const height = Math.max(200, top + rowHeight * points.length + 42);
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    const title = svgElement('title', {}, chart.title);
    const desc = svgElement('desc', {}, 'Aggregated values from this dataset. Detailed values follow below the chart.');
    svg.append(title, desc);
    if (!points.length) {
      svg.append(svgElement('text', {x:35,y:100,fill:'#657493','font-size':18}, 'No eligible data for this chart.'));
      return;
    }
    svg.append(svgElement('text',{x:24,y:28,fill:'#23365f','font-size':20,'font-weight':700},chart.title));
    const values = points.map(p => Number(p.value));
    const min = Math.min(0, ...values), max = Math.max(0, ...values);
    const maxAbs = max - min || 1;
    const barStart = 275, barSize = 565;
    const zero = barStart + (-min / maxAbs) * barSize;
    svg.append(svgElement('line',{x1:zero,x2:zero,y1:top-12,y2:height-28,stroke:'#8999bb','stroke-width':1.3}));
    points.forEach((point, index) => {
      const y = top + index*rowHeight;
      const value = Number(point.value);
      const x = barStart + ((Math.min(0,value)-min)/maxAbs) * barSize;
      const barWidth = Math.max(value === 0 ? 1 : 2, Math.abs(value)/maxAbs*barSize);
      const label = point.label.length > 30 ? point.label.slice(0,28) + '…' : point.label;
      svg.append(svgElement('text',{x:20,y:y+23,fill:'#4d5f82','font-size':14},label));
      const bar = svgElement('rect',{
        x,y:y+7,width:barWidth,height:23,rx:6,
        fill:index%2===0?'#5265e9':'#1dafa8'
      });
      bar.append(svgElement('title',{},point.label + ': ' + humanCount(point.value) + ' ' + chart.unit));
      svg.append(bar);
      const valueX = Math.min(width-58, Math.max(barStart+8, (value>=0?x+barWidth+8:x-68)));
      svg.append(svgElement('text',{x:valueX,y:y+23,fill:'#223665','font-size':13,'font-weight':600},
        humanCount(point.value)));
    });
  }
  function renderDataTable(chart) {
    const info = $('chartNotes'); info.replaceChildren();
    info.append(element('p',
      'Based on ' + humanCount(chart.includedRows) + ' selected rows; ' +
      humanCount(chart.excludedRows) + ' quality-excluded rows; ' +
      humanCount(chart.invalidNumericRows) + ' values not usable as numbers. Units: ' + chart.unit + '.'));
    const details = element('details');
    details.append(element('summary','View source-backed chart values'));
    const table = element('table'); const head = element('thead');
    const hdr = element('tr');
    ['Category / range','Value','Records'].forEach(t => hdr.append(element('th',t)));
    head.append(hdr); table.append(head);
    const body = element('tbody');
    chart.points.forEach(item => {
      const row = element('tr');
      row.append(element('td',item.label), element('td',humanCount(item.value)), element('td',String(item.records)));
      body.append(row);
    });
    table.append(body); details.append(table);info.append(details);
    chart.notes.forEach(n => info.append(element('p',n,'qf-note')));
  }
  async function buildChart() {
    if (!dataset) return;
    $('buildBtn').disabled = true;
    $('exportChartBtn').disabled = true;
    chartStatus('Calculating source-backed chart…');
    const request = {
      kind: $('chartKind').value,
      column: $('column').value,
      measure: $('chartKind').value === 'SumByCategory' ? $('measure').value : null,
      topN: Number($('topN').value),
      excludeFlaggedRows: $('excludeFlagged').checked
    };
    try {
      const response = await fetch('/api/datasets/' + encodeURIComponent(dataset.id) + '/charts/preview', {
        method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || data.title || 'Chart could not be generated');
      latestChart = data;
      renderSvg(data);renderDataTable(data);
      chartStatus('Chart generated. Values are calculated by the C# backend.');
      $('exportChartBtn').disabled = false;
    } catch(e) { latestChart=null;chartStatus(e.message,true);$('chartSvg').replaceChildren();$('chartNotes').replaceChildren(); }
    finally { $('buildBtn').disabled=false; }
  }
  function renderAdvisor(report) {
    $('executiveSummary').textContent = report.executiveSummary;
    $('advice').replaceChildren();
    if (!report.suggestedTransformations.length) $('advice').append(element('p','No transformations suggested by enabled checks.'));
    report.suggestedTransformations.forEach(s => {
      const box = element('div',undefined,'studio-advice');
      box.append(element('strong',s.title),element('p',s.reason),element('p','Consideration: ' + s.risk));
      $('advice').append(box);
    });
    $('evidence').replaceChildren();
    report.evidence.forEach(line => $('evidence').append(element('p',line)));
    $('reportBtn').disabled = false;
  }
  async function load() {
    if (!dataSetId) { status('No dataset selected. Upload a CSV, then open Analytics Studio.',true);return; }
    try {
      dataset = await getJson('/api/datasets/' + encodeURIComponent(dataSetId));
      $('etlLink').href = '/etl.html?dataset=' + encodeURIComponent(dataset.id);
      const insights = await getJson('/api/datasets/' + encodeURIComponent(dataset.id) + '/insights');
      $('kpis').replaceChildren(
        buildKpi('Rows analyzed',humanCount(insights.rowCount)),
        buildKpi('Rows with issues',humanCount(insights.rowsWithIssues)),
        buildKpi('Rows without reported issues',insights.qualityScorePercent+'%'),
        buildKpi('Distinct issue types',humanCount(insights.issues.length))
      );
      setColumnOptions('column',dataset.headers);setColumnOptions('measure',dataset.headers);
      renderSuggestions(insights.suggestedCharts);
      status('Loaded ' + dataset.fileName + '. Select a suggested chart, or customize one below.');
      advisory = await getJson('/api/datasets/' + encodeURIComponent(dataset.id) + '/advisor/report');
      renderAdvisor(advisory);
      if (insights.suggestedCharts.length) setSuggestion(insights.suggestedCharts[0]);
      else updateForm();
    } catch(e) {status('Unable to load dashboard: '+e.message,true);}
  }
  $('chartKind').addEventListener('change',updateForm);
  $('buildBtn').addEventListener('click',buildChart);
  $('exportChartBtn').addEventListener('click',()=>{
    if(latestChart) downloadJson('queryforge-chart-'+dataSetId+'.json',latestChart);
  });
  $('reportBtn').addEventListener('click',()=>{
    if(advisory) downloadJson('queryforge-evidence-report-'+dataSetId+'.json',advisory);
  });
  load();
})();
