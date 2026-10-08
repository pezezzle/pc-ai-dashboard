// Run against the published app with WebView2 CDP on loopback port 9321.
// Briefly displays the saver, nudges the cursor, and restores original settings.
const { chromium } = require('@playwright/test');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const pause = ms => new Promise(resolve => setTimeout(resolve,ms));
function native(move=false,key=false) {
  const result=execFileSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(__dirname,'test-screensaver-native.ps1'),...(move?['-MoveMouse']:[]),...(key?['-PressTestKey']:[])],{encoding:'utf8'});
  return move||key ? undefined : JSON.parse(result);
}
let browser,root,original;
async function saverPages() {
  const pages=[];
  for(const page of browser.contexts().flatMap(c=>c.pages())) {
    if(page.isClosed() || !page.url().startsWith('https://pc-ai-dashboard.local/')) continue;
    try { if(await page.evaluate(()=>document.body.classList.contains('screensaver'))) pages.push(page); } catch {}
  }
  return pages;
}
async function waitForSavers(count,timeout=25000) {
  const end=Date.now()+timeout;
  do {
    const pages=await saverPages();
    if(pages.length===count) return pages;
    await pause(200);
  } while(Date.now()<end);
  throw new Error(`Expected ${count} screensaver pages, found ${(await saverPages()).length}`);
}
async function checkAllMonitors(count) {
  const pages=await waitForSavers(count);
  const state=native();
  const windows=state.Windows.filter(w=>w.Visible&&w.Title.includes('Bildschirmschoner'));
  assert.equal(windows.length,count);
  for(const monitor of state.Monitors) assert(windows.some(w=>['X','Y','Width','Height'].every(k=>w[k]===monitor[k])),`No full-size window for ${monitor.Id}: ${JSON.stringify(windows)}`);
  for(const page of pages) {
    await page.waitForFunction(()=>document.querySelector('[data-metric="ramUsed"]').textContent!=='—');
    assert(await page.locator('.toolbar').isHidden());
    assert.equal(await page.evaluate(()=>window.dashboardTest.getSettings().muted),true);
    assert.equal(await page.evaluate(()=>getComputedStyle(document.querySelector('header')).display),'flex');
  }
  await checkPlayback(pages);
  return state.Monitors;
}
async function checkPlayback(pages) {
  const samples=[];
  for(const page of pages) {
    if(await page.evaluate(()=>window.dashboardTest.getSettings().backgroundMode)!=='local') continue;
    await page.waitForFunction(()=>{const v=document.querySelector('#local-video');return v.readyState>=2&&!v.paused&&!v.error&&v.videoWidth>0},{},{timeout:25000});
    const before=await page.evaluate(()=>{const v=document.querySelector('#local-video');const r=v.getBoundingClientRect();return {id:document.body.dataset.displayId,time:v.currentTime,frames:v.getVideoPlaybackQuality().totalVideoFrames,width:r.width,height:r.height,viewportWidth:innerWidth,viewportHeight:innerHeight,fit:getComputedStyle(v).objectFit}});
    assert.equal(before.width,before.viewportWidth);assert.equal(before.height,before.viewportHeight);assert.equal(before.fit,'fill');
    samples.push({page,before});
  }
  await pause(2200);
  for(const {page,before} of samples) {
    const after=await page.evaluate(()=>{const v=document.querySelector('#local-video');return {time:v.currentTime,frames:v.getVideoPlaybackQuality().totalVideoFrames}});
    assert(after.time>before.time+.5,`Video time did not advance on ${before.id}`);
    assert(after.frames>before.frames,`Video frames did not advance on ${before.id}`);
  }
  if(samples.length) console.log(`Video time and decoded frames advance on all ${samples.length} monitors, with viewport-filling stretch.`);
}
(async()=> {
  browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  root=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
  assert(root,'Dashboard missing');
  await root.waitForFunction(()=>!!window.dashboardTest);
  original=await root.evaluate(()=>window.dashboardTest.getSettings());
  await root.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:{...s,screenSaverTimerEnabled:false,screenSaverDashboardDisplayIds:null}}),original);
  await root.waitForFunction(()=>window.dashboardTest.getSettings().screenSaverTimerEnabled===false);
  const count=native().Monitors.length;
  console.log(`Testing on ${count} connected monitors.`);
  await root.locator('#screensaver-start').click();
  const monitors=await checkAllMonitors(count);
  await (await saverPages())[0].screenshot({path:'artifacts/screensaver-live.png'});
  console.log('Manual start: one full-size live dashboard per monitor; muted copies; toolbar hidden.');
  native(true);
  await waitForSavers(0);
  assert(native().Windows.some(w=>w.Visible&&!w.Title.includes('Bildschirmschoner')),'Normal dashboard did not return');
  console.log('Global mouse input dismissed every window and restored the normal dashboard.');
  // Selecting a dashboard monitor never removes the saver/background from other monitors.
  const portrait=monitors.find(m=>m.Height>m.Width)||monitors[0];
  for(const selection of [[portrait.Id],[]]) {
    await root.evaluate(({s,selection})=>window.chrome.webview.postMessage({type:'saveSettings',settings:{...s,screenSaverTimerEnabled:false,screenSaverDashboardDisplayIds:selection}}),{s:original,selection});
    await root.waitForFunction(ids=>JSON.stringify(window.dashboardTest.getSettings().screenSaverDashboardDisplayIds)===JSON.stringify(ids),selection);
    await root.locator('#screensaver-start').click();
    const pages=await waitForSavers(count);
    assert.equal(native().Windows.filter(w=>w.Visible&&w.Title.includes('Bildschirmschoner')).length,count);
    for(const page of pages) {
      const id=await page.evaluate(()=>document.body.dataset.displayId);
      assert.equal(await page.locator('#stage').isVisible(),selection.includes(id),`Dashboard visibility on ${id}`);
      assert.equal(await page.locator('#shade').isVisible(),selection.includes(id),`Background-only dimming on ${id}`);
      if(id===portrait.Id&&selection.length) await page.screenshot({path:'artifacts/screensaver-portrait-live.png'});
    }
    await checkPlayback(pages);
    native(true);await waitForSavers(0);
  }
  console.log('Single-monitor dashboard and all-video mode: all five screens keep playing their backgrounds.');
  await root.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:{...s,screenSaverTimerEnabled:false,screenSaverDashboardDisplayIds:null}}),original);
  await root.waitForFunction(()=>window.dashboardTest.getSettings().screenSaverDashboardDisplayIds===null);
  // Also check cancellation while WebView2 initialization is still underway.
  await root.locator('#screensaver-start').click();
  await root.evaluate(()=>window.chrome.webview.postMessage({type:'startScreenSaver'}));
  await pause(900);native(true);await waitForSavers(0);
  await root.locator('#screensaver-start').click();await waitForSavers(count);
  await pause(900);native(false,true);await waitForSavers(0);
  console.log('Global keyboard input dismissed every screensaver window.');
  await root.locator('#screensaver-minutes').fill('1');
  await root.locator('#screensaver-minutes').dispatchEvent('change');
  await root.locator('#screensaver-timer').click();
  await root.waitForFunction(()=>window.dashboardTest.getSettings().screenSaverTimerEnabled===true&&window.dashboardTest.getSettings().screenSaverIdleMinutes===1);
  console.log('Waiting for the real one-minute inactivity timer (input restarts the wait).');
  await waitForSavers(count,90000);
  await checkAllMonitors(count);
  native(true);await waitForSavers(0);
  console.log('Automatic inactivity start and joint dismissal passed.');
  fs.writeFileSync('artifacts/screensaver-check.local.json',JSON.stringify({monitorCount:count,monitors,manual:true,automatic:true,globalInput:true,selectedDashboard:true,allVideo:true},null,2));
})().catch(e=> { console.error(e); process.exitCode=1; }).finally(async()=> {
  if(root&&!root.isClosed()&&original) {
    try {
      if((await saverPages()).length) { native(true);await waitForSavers(0); }
      await root.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),original);
      await root.waitForFunction(s=>{const current=window.dashboardTest.getSettings();return current.screenSaverTimerEnabled===s.screenSaverTimerEnabled&&current.screenSaverIdleMinutes===s.screenSaverIdleMinutes&&JSON.stringify(current.screenSaverDashboardDisplayIds??null)===JSON.stringify(s.screenSaverDashboardDisplayIds??null)},original);
    } catch(e) { console.error('Could not restore settings:',e.message);process.exitCode=1; }
  }
  await browser?.close();
});
