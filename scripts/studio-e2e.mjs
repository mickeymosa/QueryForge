import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
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

  await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:'artifacts/studio-mobile.png',fullPage:true});
  const overflow=await page.evaluate(()=>({
    width:document.documentElement.scrollWidth,
    elements:[...document.querySelectorAll('body *')].map(el=>{
      const r=el.getBoundingClientRect();
      return {tag:el.tagName,id:el.id,cls:String(el.className||'').slice(0,70),
        left:Math.round(r.left),right:Math.round(r.right),width:Math.round(r.width)};
    }).filter(x=>x.right>window.innerWidth+3||x.left< -3)
      .sort((a,b)=>b.right-a.right).slice(0,15)
  }));
  console.log('Mobile width diagnosis:',JSON.stringify(overflow));
  assert.ok(overflow.width<=396,'Mobile page overflows width: '+overflow.width);
  console.log('Browser mobile viewport overflow check passed');

  await page.setViewportSize({width:1440,height:900});
  await page.locator('[data-view="data"]').click();
  await page.locator('#fileInput').setInputFiles('/tmp/queryforge-studio-test.xlsx');
  await page.locator('#dataDetails:not([hidden])').waitFor();
  await page.waitForFunction(()=>
    document.getElementById('dataHeading')?.textContent?.includes('queryforge-studio-test'));
  assert.match(await page.locator('#dataDescription').innerText(),/2 rows/);
  console.log('Browser XLSX import and quality preview passed');
  assert.deepEqual(errors,[],'Uncaught browser JS exceptions');
  console.log('All QueryForge Studio headless browser checks passed');
}finally{
  await browser.close();
}
