// Use an explicitly started local CDP test instance; no settings are changed.
const { chromium } = require('@playwright/test');
const assert = require('node:assert/strict');
let browser;
(async () => {
  browser = await chromium.connectOverCDP('http://127.0.0.1:9321');
  const page = browser.contexts().flatMap(c => c.pages()).find(p => p.url().startsWith('https://pc-ai-dashboard.local/'));
  assert(page, 'Dashboard page missing');
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.waitForFunction(() => latest?.ai.some(a => a.name === 'Codex' && a.status === 'Live' && a.credits?.balance != null), {}, { timeout: 30000 });
  const inspect = () => page.evaluate(() => ({
    ai: latest.ai.map(a => ({name:a.name,status:a.status,updatedAt:a.updatedAt,balanceAvailable:a.credits?.balance != null,unit:a.credits?.unit,manualResets:a.manualResets})),
    manualResetBanner: {visible:!document.querySelector('#codex-card .manual-reset').hidden,text:document.querySelector('#codex-card .manual-reset').textContent},
    rings: [...document.querySelectorAll('.fan-meter')].map(e => ({id:e.dataset.duty,value:latest.metrics.find(m=>m.id===e.dataset.duty)?.value,dash:e.querySelector('.fan-meter-fill').style.strokeDasharray})),
    drives: [...document.querySelectorAll('.drive-title strong')].map(e=>e.textContent),
    clipped: [...document.querySelectorAll('.ai-card,.disks-card,.fan-card')].filter(e=>e.scrollHeight>e.clientHeight||e.scrollWidth>e.clientWidth).map(e=>e.id||e.className),
    temperature: [...document.querySelectorAll('[data-temp]')].map(e=>({value:latest.metrics.find(m=>m.id===e.dataset.temp)?.value,height:e.getBoundingClientRect().height,total:e.parentElement.getBoundingClientRect().height})),
    video: {time:video.currentTime,paused:video.paused,error:video.error?.code ?? null,loop:video.loop},
    settings: {accent:settings.accent,textColor:settings.textColor,muted:settings.muted,volume:settings.volume,temperatureScaleMax:settings.temperatureScaleMax}
  }));
  const first = await inspect();
  assert.equal(first.rings.length, 5);
  for (const ring of first.rings) {
    assert(ring.value != null && ring.value >= 0 && ring.value <= 100, 'Missing live PWM: '+ring.id);
    assert(Math.abs(parseFloat(ring.dash)-ring.value)<.001, 'PWM graphic differs from telemetry');
  }
  assert(first.drives.length>0); for(const drive of first.drives) assert.match(drive,/^\d+,\d{2} %$/);
  assert.deepEqual(first.clipped, []);
  const resets=first.ai.find(a=>a.name==='Codex').manualResets;
  if(resets?.availableCount>0) {
    assert.equal(first.manualResetBanner.visible,true);
    assert(first.manualResetBanner.text.includes(String(resets.availableCount)) && first.manualResetBanner.text.includes('verfügbar'), 'Live reset count not displayed');
    assert(first.manualResetBanner.text.includes('In Codex'), 'Manual reset instructions missing');
  } else assert.equal(first.manualResetBanner.visible,false);
  for(const t of first.temperature) assert(Math.abs(t.height/t.total-t.value/first.settings.temperatureScaleMax)<.03, 'Temperature bar scale mismatch');
  assert.equal(first.video.paused,false); assert.equal(first.video.error,null); assert.equal(first.video.loop,true);
  console.log('Live PWM, storage decimals, provider credit availability, layout, and looping large video:', JSON.stringify(first));
  await page.waitForFunction(t => latest.ai.find(a=>a.name==='Codex')?.updatedAt !== t, first.ai.find(a=>a.name==='Codex').updatedAt, {timeout:85000});
  const next=await inspect();
  assert(next.video.time>first.video.time, 'Video did not continue during credit refresh');
  assert.equal(next.video.error,null); assert.deepEqual(next.clipped,[]); assert.deepEqual(errors,[]);
  await page.screenshot({path:'artifacts/credits-layout-live.png'});
  console.log('Credit poll refreshed; video continued without layout clipping or JavaScript errors.');
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(()=>browser?.close());
