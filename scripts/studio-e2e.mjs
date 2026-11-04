import assert from 'node:assert/strict';
import { mkdir, readFile } from 'node:fs/promises';
import { chromium } from 'playwright-core';

const base=process.env.STUDIO_BASE_URL || 'http://127.0.0.1:5080';
const chrome=process.env.CHROME_BIN || '/usr/bin/google-chrome';
const browser=await chromium.launch({
  executablePath:chrome,
  headless:true,
  args:['--no-sandbox','--disable-dev-shm-usage']
});
const page=await browser.newPage({viewport:{width:1440,height:900}});
const errors=[];
page.on('pageerror',error=>errors.push(error.message));
page.setDefaultTimeout(15000);
await mkdir('artifacts',{recursive:true});

try{
  await page.goto(base,{waitUntil:'networkidle'});
  assert.match(await page.title(),/QueryForge/);
  assert.equal(await page.locator('#uploadZone').count(),1);
  await page.locator('#sampleBtn').click();
  await page.locator('#dataDetails:not([hidden])').waitFor();
  assert.match(await page.locator('#dataHeading').innerText(),/orders\.csv/i);
  await page.screenshot({path:'artifacts/studio-import.png',fullPage:true});
  console.log('Browser import + data quality view passed');

  await page.locator('[data-view="prepare"]').click();
  await page.locator('#promptInput').fill('trim extra spaces and remove duplicate order_id');
  await page.locator('#guidedPlan').click();
  assert.equal(await page.locator('.flow-node').count(),2);
  await page.locator('#advancedMode').click();
  await page.locator('#toolPanel:not([hidden])').waitFor();
  // Actual HTML5 drag of a palette transformation into the connected flow.
  await page.locator('.tool-item').filter({hasText:'Sort ascending'})
    .dragTo(page.locator('.flow-end'));
  assert.equal(await page.locator('.flow-node').count(),3);
  await page.locator('.flow-node').last().click();
  assert.equal(await page.locator('#stepOperation').inputValue(),'SortAscending');
  // Exercise drag-reorder of existing transform nodes without changing their type.
  await page.locator('.flow-node').last().dragTo(page.locator('.flow-node').first());
  assert.equal(await page.locator('.flow-node').count(),3);
  await page.screenshot({path:'artifacts/studio-etl.png',fullPage:true});
  console.log('Browser Easy/Advanced guided commands + drag/drop passed');

  await page.locator('#previewEtl').click();
  await page.locator('#etlResultPanel:not([hidden])').waitFor();
  assert.equal(await page.locator('.audit-row').count(),3);
  assert.equal(await page.locator('#applyEtl').isEnabled(),true);
  await page.screenshot({path:'artifacts/studio-preview.png',fullPage:true});
  await page.locator('#applyEtl').click();
  await page.locator('#visualizeView:not([hidden])').waitFor();
  console.log('Browser ETL preview/fingerprint apply/derived dataset passed');

  await page.locator('#renderChart').click();
  await page.locator('#chartArea svg').waitFor();
  assert.ok(await page.locator('#chartTable tbody tr').count()>0);
  await page.screenshot({path:'artifacts/studio-chart.png',fullPage:true});
  console.log('Browser source-backed chart and report view passed');

  // A real pivot request, not a hardcoded frontend matrix.
  await page.locator('#pivotRows').selectOption('currency');
  await page.locator('#pivotColumns').selectOption('paid');
  await page.locator('#pivotExclude').uncheck();
  const pivotResponse=page.waitForResponse(r=>
    r.url().includes('/pivot/preview') && r.request().method()==='POST');
  await page.locator('#buildPivot').click();
  const pivotHttp=await pivotResponse;
  assert.equal(pivotHttp.status(),200,'Pivot endpoint must return HTTP 200');
  const pivot=await pivotHttp.json();
  assert.equal(pivot.grandTotal,'6','Count must use actual 6-row post-ETL dataset');
  assert.equal(pivot.includedRows,6);
  assert.deepEqual(pivot.columnLabels,['false','true']);
  assert.equal(await page.locator('#pivotResult').isVisible(),true);
  assert.equal(await page.locator('#pivotTable tbody tr').count(),pivot.rows.length);
  assert.equal(await page.locator('#exportPivot').isEnabled(),true);
  await page.locator('#pivotChartKind').selectOption('line');
  await page.locator('#pivotChart svg').waitFor();
  await page.screenshot({path:'artifacts/studio-pivot.png',fullPage:true});
  console.log('Browser real pivot, source counts, matrix and linked chart passed');

  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:'artifacts/studio-mobile.png',fullPage:true});
  const overflow=await page.evaluate(()=>({
    width:document.documentElement.scrollWidth,
    innerWidth:window.innerWidth,
    bodyWidth:document.body.scrollWidth,
    elements:[...document.querySelectorAll('body *')].map(el=>{
      const r=el.getBoundingClientRect();
      const outside=r.right>window.innerWidth+3||r.left< -3;
      if(!outside)return null;
      // Wide pivot matrices and charts are scrollable within their own panels.
      // Ignore their off-screen children only when a visible ancestor clips them.
      for(let ancestor=el.parentElement;ancestor;ancestor=ancestor.parentElement){
        const style=getComputedStyle(ancestor);
        const clipped=['auto','scroll','hidden','clip'].includes(style.overflowX) ||
          style.contain.includes('paint');
        if(clipped){
          const box=ancestor.getBoundingClientRect();
          if(box.left>=-3 && box.right<=window.innerWidth+3)return null;
        }
      }
      return {tag:el.tagName,id:el.id,cls:String(el.className||'').slice(0,70),
        left:Math.round(r.left),right:Math.round(r.right),width:Math.round(r.width)};
    }).filter(Boolean).sort((a,b)=>b.right-a.right).slice(0,15)
  }));
  const sideways=await page.evaluate(()=>{
    window.scrollTo({left:1000,top:0,behavior:'instant'});
    return window.scrollX;
  });
  console.log('Mobile width diagnosis:',JSON.stringify({...overflow,sideways}));
  assert.ok(overflow.elements.length===0,
    'One or more visible elements extend beyond the mobile viewport: '+JSON.stringify(overflow.elements));
  assert.ok(sideways<=3,'Page can be scrolled horizontally on mobile: '+sideways+'px');
  console.log('Browser mobile viewport overflow check passed');

  await page.setViewportSize({width:1440,height:900});
  await page.locator('[data-view="data"]').click();
  await page.locator('#fileInput').setInputFiles('/tmp/queryforge-studio-test.xlsx');
  await page.locator('#dataDetails:not([hidden])').waitFor();
  await page.waitForFunction(()=>
    document.getElementById('dataHeading')?.textContent?.includes('queryforge-studio-test'));
  assert.match(await page.locator('#dataDescription').innerText(),/2 rows/);
  console.log('Browser XLSX import and quality preview passed');

  // A purpose-made sales fixture proves 2D monetary pivot accuracy and linked charts.
  await page.locator('#pivotSampleBtn').click();
  await page.waitForFunction(()=>
    document.getElementById('dataHeading')?.textContent==='pivot-sales.csv');
  await page.locator('[data-view="visualize"]').click();
  await page.locator('#pivotRows').selectOption('city');
  await page.locator('#pivotColumns').selectOption('category');
  await page.locator('#pivotAggregate').selectOption('Sum');
  await page.locator('#pivotMeasure').selectOption('amount');
  const salesResponse=page.waitForResponse(r=>
    r.url().includes('/pivot/preview') && r.request().method()==='POST');
  await page.locator('#buildPivot').click();
  const salesHttp=await salesResponse;
  assert.equal(salesHttp.status(),200);
  const sales=await salesHttp.json();
  assert.equal(sales.grandTotal,'605');
  assert.equal(sales.unit,'EUR');
  assert.deepEqual(sales.columnLabels,['Hardware','Software']);
  assert.deepEqual(sales.rows.map(r=>r.label),['Al Wakrah','Doha','Lusail']);
  assert.deepEqual(sales.rows.find(r=>r.label==='Doha').values,['150','80']);
  assert.equal(sales.rows.find(r=>r.label==='Lusail').total,'275');
  assert.equal(await page.locator('#pivotTable tfoot td').last().innerText(),'605');
  const [exported]=await Promise.all([
    page.waitForEvent('download'),
    page.locator('#exportPivot').click()
  ]);
  assert.match(exported.suggestedFilename(),/^queryforge-pivot-.*\.csv$/);
  const exportedCsv=await readFile(await exported.path(),'utf8');
  assert.ok(exportedCsv.includes('"Doha","150","80","230"'),
    'Pivot CSV must preserve source-backed Doha totals');
  assert.ok(exportedCsv.includes('"Grand total","315","290","605"'),
    'Pivot CSV grand totals must match the backend matrix');
  console.log('Browser pivot CSV download contains exact matrix values');
  await page.screenshot({path:'artifacts/studio-pivot-sales.png',fullPage:true});
  console.log('Browser sales pivot: Doha 230, Lusail 275, Al Wakrah 100, total EUR 605');

  assert.deepEqual(errors,[],'Uncaught browser JS exceptions');
  console.log('All QueryForge Studio headless browser checks passed');
}finally{
  await browser.close();
}
