// Tests only this app's bridge and WebViews. Does not send OS mouse/key events.
const { chromium } = require('@playwright/test');
const assert = require('node:assert/strict');
const pause = ms => new Promise(resolve => setTimeout(resolve,ms));
let browser,root,original;
(async()=> {
  browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  const readyDeadline=Date.now()+15000;
  do {
    root=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
    if(root) break;
    await pause(200);
  } while(Date.now()<readyDeadline);
  assert(root,'Normal dashboard missing');
  await root.waitForFunction(()=>!!window.dashboardTest&&document.querySelector('#display-select').options.length>0);
  original=await root.evaluate(()=>window.dashboardTest.getSettings());
  const displays=await root.locator('#display-select option').evaluateAll(options=>options.map(o=>({id:o.value,label:o.textContent})));
  const selected=displays.find(d=>d.label.includes('Hochformat'))??displays[0];
  await root.evaluate(({settings,id})=>window.chrome.webview.postMessage({type:'saveSettings',settings:{...settings,screenSaverTimerEnabled:true,screenSaverIdleMinutes:1,screenSaverDashboardDisplayIds:[id]}}),{settings:original,id:selected.id});
  await root.waitForFunction(()=>window.dashboardTest.getSettings().screenSaverTimerEnabled&&window.dashboardTest.getSettings().screenSaverIdleMinutes===1);
  console.log('Testing automatic start with dashboard selected on the portrait monitor; input restarts the one-minute wait.');
  const end=Date.now()+100000;
  let savers=[];
  while(Date.now()<end) {
    savers=[];
    for(const page of browser.contexts().flatMap(c=>c.pages())) {
      if(page.isClosed()||!page.url().startsWith('https://pc-ai-dashboard.local/')) continue;
      try { if(await page.evaluate(()=>document.body.classList.contains('screensaver'))) savers.push(page); } catch {}
    }
    if(savers.length===displays.length) break;
    await pause(200);
  }
  assert.equal(savers.length,displays.length,'Automatic start requires a full minute without Windows input');
  for(const page of savers) {
    const id=await page.evaluate(()=>document.body.dataset.displayId);
    assert.equal(await page.locator('#stage').isVisible(),id===selected.id,`Wrong dashboard visibility on ${id}`);
  }
  console.log(`Automatic start passed: ${savers.length} fullscreen backgrounds, dashboard only on the selected portrait monitor.`);
})().catch(e=> { console.error(e);process.exitCode=1; }).finally(async()=> {
  if(root&&!root.isClosed()&&original) {
    await root.evaluate(settings=>{
      window.chrome.webview.postMessage({type:'saveSettings',settings});
      window.chrome.webview.postMessage({type:'stopScreenSaver'});
    },original);
    await root.waitForFunction(s=>{const now=window.dashboardTest.getSettings();return now.screenSaverTimerEnabled===s.screenSaverTimerEnabled&&now.screenSaverIdleMinutes===s.screenSaverIdleMinutes&&JSON.stringify(now.screenSaverDashboardDisplayIds??null)===JSON.stringify(s.screenSaverDashboardDisplayIds??null)},original);
  }
  await browser?.close();
});
