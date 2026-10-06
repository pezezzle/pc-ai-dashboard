const { chromium } = require('@playwright/test');
const fs=require('node:fs'),path=require('node:path');
const backup=path.resolve('artifacts/appearance-test.local.json');
const settingsFile=path.join(process.env.LOCALAPPDATA,'PcAiDashboard','settings.json');
let browser;
(async()=>{
  browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  const page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
  await page.waitForFunction(()=>window.dashboardTest?.getSettings().displayId);
  if(process.argv[2]==='save') {
    const original=await page.evaluate(()=>window.dashboardTest.getSettings());
    fs.writeFileSync(backup,JSON.stringify(original,null,2));
    await page.locator('#settings-button').click();
    await page.locator('[name="textColor"]').fill('#eecc88');
    await page.waitForFunction(()=>window.dashboardTest.getSettings().textColor==='#eecc88');
    await page.locator('[name="accent"]').fill('#44aaff');
    await page.waitForFunction(()=>window.dashboardTest.getSettings().accent==='#44aaff');
    const disk=JSON.parse(fs.readFileSync(settingsFile,'utf8'));
    if(disk.textColor!=='#eecc88'||disk.accent!=='#44aaff') throw new Error('Appearance was not saved to disk');
    console.log('Colors saved immediately without pressing Save.');
  } else {
    const loaded=await page.evaluate(()=>({settings:window.dashboardTest.getSettings(),color:getComputedStyle(document.querySelector('#clock')).color}));
    if(loaded.settings.textColor!=='#eecc88'||loaded.settings.accent!=='#44aaff'||loaded.color!=='rgb(238, 204, 136)') throw new Error('Restart did not restore colors: '+JSON.stringify(loaded));
    console.log('Full process restart restored both colors and rendered text color.');
    const original=JSON.parse(fs.readFileSync(backup,'utf8'));
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),original);
    await page.waitForFunction(s=>window.dashboardTest.getSettings().accent===s.accent && window.dashboardTest.getSettings().textColor===s.textColor,original);
    console.log('Original user colors restored.');
  }
  await page.evaluate(()=>window.chrome.webview.postMessage({type:'exit'}));
})().catch(e=>{console.error(e);process.exitCode=1}).finally(async()=>{await browser?.close()});
