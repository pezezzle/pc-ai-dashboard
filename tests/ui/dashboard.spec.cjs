const { test, expect } = require('@playwright/test');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const settings = { displayId:'test', fullscreen:true, alwaysOnTop:true, autostart:false, startAquasuite:true, backgroundMode:'gradient',youtubeUrl:'',localVideoPath:'',muted:true,volume:25,dim:.5,cardOpacity:.76,accent:'#66e7c8',aquasuiteSharedMemory:'PC-AI-Dashboard',aquasuiteXmlPath:'',pumpChannel:4,topChannel:3,sideChannel:0,bottomChannel:1,backChannel:2,pumpTempChannel:0,radiatorTempChannel:2,caseTempChannel:3,codexSessionId:'',claudeSessionId:'' };
test.beforeEach(async ({ page }) => {
  await page.goto(pathToFileURL(path.resolve('src/PcAiDashboard/Web/index.html')).href);
  await page.evaluate(s => window.dashboardTest.applyConfiguration({settings:s,displays:[{id:'test',label:'Test · 1024 × 600'}]}),settings);
});
test('all requested measurements fit the small display; unavailable values stay explicit',async ({page}) => {
  const ids=['cpuTemp','cpuLoad','gpuTemp','gpuLoad','ramUsed','ramTotal','ramLoad','coolantPump','coolantRadiator','caseTemp','pumpRpm','topRpm','sideRpm','bottomRpm','backRpm'];
  await page.evaluate(ids=>window.dashboardTest.render({time:new Date().toISOString(),metrics:ids.map(id=>({id,label:id,value:id==='cpuTemp'?null:id.endsWith('Rpm')?950:37.5,unit:id.endsWith('Rpm')?'RPM':id.includes('Temp')||id.includes('coolant')?'°C':'%',source:'Test',updatedAt:new Date().toISOString()})),drives:[{name:'C:\\',label:'System',usedGb:700,totalGb:1000,usedPercent:70}],ai:[{name:'Codex',status:'Live',quotas:[{label:'7 Tage',usedPercent:72,resetsAt:Math.floor(Date.now()/1000)+86400}],sessions:[]},{name:'Claude',status:'Live',quotas:[],sessions:[]}],hardwareStatus:'Test live'}),ids);
  await expect(page.locator('[data-metric="cpuTemp"]')).toHaveText('—');
  await expect(page.locator('#codex-card .quota-title')).toContainText('72%');
  await expect(page.locator('#claude-card .quotas')).toContainText('Noch keine');
  const fit=await page.locator('#stage').boundingBox(); expect(fit.width).toBe(1024); expect(fit.height).toBe(600);
  const overflow=await page.evaluate(()=>[...document.querySelectorAll('.compute,.cooling,.fans,.bottom-grid')].some(e=>e.scrollWidth>e.clientWidth)); expect(overflow).toBe(false);
  await page.screenshot({path:'artifacts/dashboard-preview.png'});
});
test('both standard Claude windows fit without hiding the weekly reset',async ({page})=>{
  await page.evaluate(()=>window.dashboardTest.render({time:new Date().toISOString(),metrics:[],drives:[],ai:[{name:'Claude',status:'Live',updatedAt:new Date().toISOString(),quotas:[{label:'5 Stunden',usedPercent:10,resetsAt:Date.now()/1000+3000},{label:'7 Tage',usedPercent:30,resetsAt:Date.now()/1000+90000}],sessions:[]}],hardwareStatus:'Test'}));
  expect(await page.locator('#claude-card .quotas').evaluate(e=>e.scrollHeight<=e.clientHeight)).toBe(true);
});
test('settings save, invalid video rejection, audio and cinema controls',async ({page})=> {
  await page.locator('#settings-button').click(); await expect(page.locator('#settings-dialog')).toBeVisible();
  await page.locator('[name="backgroundMode"]').selectOption('youtube'); await page.locator('[name="youtubeUrl"]').fill('invalid'); await page.getByRole('button',{name:'Speichern',exact:true}).click();
  await expect(page.locator('#notice')).toContainText('gültigen YouTube-Link'); await expect(page.locator('#settings-dialog')).toBeVisible();
  await page.locator('[name="backgroundMode"]').selectOption('gradient'); await page.locator('[name="accent"]').fill('#44aaff'); await page.getByRole('button',{name:'Speichern',exact:true}).click();
  await expect(page.locator('#settings-dialog')).not.toBeVisible();
  expect(await page.evaluate(()=>window.dashboardTest.getSettings().accent)).toBe('#44aaff');
  await page.locator('#mute').click(); expect(await page.evaluate(()=>window.dashboardTest.getSettings().muted)).toBe(false);
  await page.locator('#cinema').click(); await expect(page.locator('#stage')).not.toBeVisible(); await page.locator('#leave-cinema').click(); await expect(page.locator('#stage')).toBeVisible();
});
test('chat choice persists across live updates and strings render as text',async ({page})=> {
  const usage={name:'Codex',status:'Live',quotas:[],updatedAt:new Date().toISOString(),sessions:[{id:'first',label:'First',usedPercent:10,tokens:10,capacity:100,updatedAt:new Date().toISOString()},{id:'second',label:'<img src=x onerror=alert(1)>',usedPercent:70,tokens:70,capacity:100,updatedAt:new Date().toISOString()}]};
  const snapshot={time:new Date().toISOString(),metrics:[],drives:[],ai:[usage],hardwareStatus:'Test'};
  await page.evaluate(s=>window.dashboardTest.render(s),snapshot); await page.locator('#codex-card select').selectOption('second');
  await page.evaluate(s=>window.dashboardTest.render(s),snapshot); await expect(page.locator('#codex-card select')).toHaveValue('second'); await expect(page.locator('#codex-card .context-value strong')).toHaveText('70%'); await expect(page.locator('#codex-card img')).toHaveCount(0);
});

test('text and accent colors apply immediately and save without submitting the dialog',async ({page})=> {
  await page.locator('#settings-button').click();
  await page.locator('[name="textColor"]').fill('#eecc88');
  await page.locator('[name="textColor"]').dispatchEvent('change');
  await expect(page.locator('#clock')).toHaveCSS('color','rgb(238, 204, 136)');
  await expect(page.locator('[data-metric="cpuTemp"]')).toHaveCSS('color','rgb(238, 204, 136)');
  await expect(page.locator('#settings-status')).toContainText('automatisch gespeichert');
  await page.locator('[name="accent"]').fill('#ff00ff');
  await page.locator('[name="accent"]').dispatchEvent('change');
  const saved=await page.evaluate(()=>window.dashboardTest.getSettings());
  await page.reload();
  await page.evaluate(s=>window.dashboardTest.applyConfiguration({settings:s,displays:[]}),saved);
  await expect(page.locator('#clock')).toHaveCSS('color','rgb(238, 204, 136)');
  await expect(page.locator('.brand-mark')).toHaveCSS('color','rgb(255, 0, 255)');
});

test('credits, PWM rings and readable lower cards fit with two quota windows',async ({page})=> {
  await page.evaluate(()=>window.dashboardTest.render({time:new Date().toISOString(),metrics:[{id:'coolantPump',value:40,unit:'°C'},{id:'topDuty',value:45.96,unit:'%'},{id:'topRpm',value:950,unit:'RPM'}],drives:[{name:'C:\\',label:'System',usedGb:723.4,totalGb:1000,usedPercent:72.34}],ai:[{name:'Codex',status:'Live',updatedAt:new Date().toISOString(),quotas:[{label:'7 Tage',usedPercent:100,resetsAt:Date.now()/1000+90000}],sessions:[{id:'1',label:'Test',tokens:57027,capacity:380000,usedPercent:15,updatedAt:new Date().toISOString()}],credits:{status:'Live',balance:44651.02745925,unit:'Credits',unlimited:false,spent:null,limit:null,enabled:true,detail:null}},{name:'Claude',status:'Live',updatedAt:new Date().toISOString(),quotas:[{label:'5 Stunden',usedPercent:10,resetsAt:Date.now()/1000+3000},{label:'7 Tage',usedPercent:30,resetsAt:Date.now()/1000+90000}],sessions:[],credits:{status:'Live',balance:100,unit:'USD',unlimited:false,spent:12.67,limit:40,enabled:true,detail:null}}],hardwareStatus:'Test'}));
  await expect(page.locator('.drive-title strong')).toHaveText('72,34 %');
  await expect(page.locator('#codex-card .credit-line strong')).toHaveText('44.651,03 Credits');
  await expect(page.locator('#claude-card .credit-line strong')).toHaveText('100,00 USD');
  await expect(page.locator('#claude-card .credit-detail')).toContainText('12,67 / 40,00 USD');
  expect(await page.locator('[data-temp="coolantPump"]').evaluate(e=>e.style.height)).toBe('40%');
  expect(await page.locator('[data-duty="topDuty"] .fan-meter-fill').evaluate(e=>e.style.strokeDasharray)).toBe('45.96, 100');
  await expect(page.locator('[data-metric="topDuty"]')).toHaveText('46');
  const clipped=await page.evaluate(()=>[...document.querySelectorAll('.ai-card,.disks-card,.fan-card')].filter(e=>e.scrollHeight>e.clientHeight||e.scrollWidth>e.clientWidth).map(e=>e.id||e.className));
  expect(clipped).toEqual([]);
  await page.screenshot({path:'artifacts/dashboard-credits-preview.png'});
});

test('missing balance remains explicit and degree scale changes preserve measurements',async ({page})=> {
  await page.evaluate(()=>window.dashboardTest.render({time:new Date().toISOString(),metrics:[{id:'coolantPump',value:40,unit:'°C'}],drives:[],ai:[{name:'Claude',status:'Live',updatedAt:new Date().toISOString(),quotas:[],sessions:[],credits:{status:'Live',balance:null,unit:'USD',unlimited:false,spent:0,limit:135,enabled:false,detail:'Restguthaben nicht vom Anbieter bereitgestellt'}}],hardwareStatus:'Test'}));
  await expect(page.locator('#claude-card .credit-line strong')).toHaveText('Nicht abrufbar');
  await expect(page.locator('#claude-card .credit-detail')).toContainText('0,00 / 135,00 USD');
  await page.locator('#settings-button').click();await page.locator('[name="temperatureScaleMax"]').fill('80');await page.getByRole('button',{name:'Speichern',exact:true}).click();
  expect(await page.locator('[data-temp="coolantPump"]').evaluate(e=>e.style.height)).toBe('50%');
  await expect(page.locator('[data-temp-max]').first()).toHaveText('80 °C');
  await expect(page.locator('[data-metric="coolantPump"]')).toHaveText('40,0');
});
