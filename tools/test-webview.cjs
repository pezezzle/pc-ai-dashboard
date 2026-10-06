// Run against an explicitly started test instance with CDP bound to loopback.
const { chromium } = require('@playwright/test');
const fs = require('node:fs');
let browser;
(async () => {
  browser = await chromium.connectOverCDP('http://127.0.0.1:9321');
  const page = browser.contexts().flatMap(c => c.pages()).find(p => p.url().startsWith('https://pc-ai-dashboard.local/'));
  if (!page) throw new Error('Dashboard page missing');
  const errors=[]; page.on('pageerror',e=>errors.push(e.message));
  await page.waitForFunction(()=>document.querySelector('[data-metric="coolantPump"]').textContent!=='—');
  await page.waitForFunction(()=>document.querySelector('#claude-card .provider-state').textContent==='Live',{},{timeout:30000});
  const values=await page.locator('[data-metric]').evaluateAll(es=>es.map(e=>({id:e.dataset.metric,value:e.textContent})));
  if(values.some(v=>v.value==='—')) throw new Error('Missing live metric '+JSON.stringify(values));
  console.log('All hardware values and Claude/Codex live:',values.length);
  const original=await page.evaluate(()=>window.dashboardTest.getSettings());
  await page.evaluate(()=>document.querySelector('#settings-dialog').close());
  await page.locator('#settings-button').click();
  await page.locator('[name="backgroundMode"]').selectOption('youtube');
  await page.locator('[name="youtubeUrl"]').fill('https://www.youtube.com/watch?v=M7lc1UVf-VE');
  await page.getByRole('button',{name:'Speichern',exact:true}).click();
  let videoPassed=false;
  try {
    await page.waitForFunction(()=>youtubeReady,{},{timeout:20000});
    if(await page.evaluate(()=>player.getPlayerState()!==1)) await page.locator('#play').click();
    await page.waitForFunction(()=>playing && youtubeReady,{},{timeout:20000});
    await page.locator('#mute').click();
    await page.waitForFunction(()=>player && !player.isMuted(),{},{timeout:5000});
    await page.locator('#mute').click();
    await page.waitForFunction(()=>player && player.isMuted(),{},{timeout:5000});
    await page.locator('#play').click();
    await page.waitForFunction(()=>player.getPlayerState()===2,{},{timeout:5000});
    await page.locator('#play').click();
    await page.waitForFunction(()=>player.getPlayerState()===1,{},{timeout:10000});
    await page.screenshot({path:'artifacts/youtube-live.png'});
    videoPassed=true; console.log('YouTube playback, mute/unmute, pause/resume passed.');
  } catch(e) {
    console.log('Video diagnostic:',await page.evaluate(()=>({ready:youtubeReady,state:player?.getPlayerState(),media:document.querySelector('#media-state').textContent,notice:document.querySelector('#notice').textContent,iframe:document.querySelector('#background iframe')?.getAttribute('src')})));
    await page.screenshot({path:'artifacts/youtube-error.png'});
    throw e;
  } finally {
    await page.evaluate(()=>document.querySelector('#settings-dialog').close());
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),original);
    await page.waitForFunction(()=>window.dashboardTest.getSettings().backgroundMode==='gradient');
    await page.screenshot({path:'artifacts/dashboard-live.png'});
    fs.writeFileSync('artifacts/webview-check.local.json',JSON.stringify({videoPassed,errors},null,2));
    await browser.close();
  }
  if(errors.length) throw new Error(errors.join('\n'));
})().catch(e=>{ console.error(e.message); process.exitCode=1; }).finally(()=>browser?.close());
