const {test,expect}=require('@playwright/test');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const settings={profile:'notebook',fullscreen:false,backgroundMode:'gradient',youtubeUrl:'',localVideoPath:'',muted:true,volume:25,dim:.15,cardOpacity:.8,accent:'#66e7c8',textColor:'#ecf3f6',temperatureScaleMax:100,codexSessionId:'',claudeSessionId:'',screenSaverDashboardDisplayIds:null};

test.beforeEach(async({page})=>{
  await page.addInitScript(()=>{
    window.sentMessages=[]; window.receivers=[];
    window.dashboardBridge={postMessage:value=>window.sentMessages.push(value),addEventListener:(type,callback)=>{if(type==='message') window.receivers.push(callback);}};
  });
  await page.goto(pathToFileURL(path.resolve('ui/web/index.html')).href);
  await page.evaluate(settings=>window.dashboardTest.applyConfiguration({settings,displays:[],platform:'macos'}),settings);
});
test('native bridge works and Mac notebook hides Windows-only controls',async({page})=>{
  expect(await page.evaluate(()=>window.sentMessages.some(value=>value.type==='ready'))).toBe(true);
  await page.locator('#settings-button').click();
  await expect(page.locator('input[name=autostart]')).not.toBeVisible();
  await expect(page.locator('input[name=fullscreen]')).not.toBeVisible();
  await expect(page.locator('input[name=startAquasuite]')).not.toBeVisible();
  await expect(page.locator('#display-select')).not.toBeVisible();
  await expect(page.locator('select[name=profile]')).toBeVisible();
});
test('notebook fits a narrow window and exposes read-only reset information',async({page})=>{
  await page.setViewportSize({width:440,height:700});
  await page.evaluate(()=>window.dashboardTest.render({time:new Date().toISOString(),metrics:[{id:'batteryLoad',label:'Akku',value:80,unit:'%'},{id:'memoryPressure',label:'Speicherdruck',value:1,unit:'Status'}],drives:[],ai:[{name:'Codex',status:'Live',updatedAt:new Date().toISOString(),quotas:[{label:'5 Stunden',usedPercent:34,resetsAt:Date.now()/1000+3600}],sessions:[],manualResets:{availableCount:1,nextExpiresAt:null}},{name:'Claude',status:'Live',updatedAt:new Date().toISOString(),quotas:[],sessions:[]}],hardwareStatus:'Test',capabilities:{platform:'macos',cooling:false,fans:false,screenSaver:false}}));
  await expect(page.locator('#codex-card .manual-reset')).toContainText('1 manueller Reset');
  await expect(page.locator('#codex-card .manual-reset button')).toHaveCount(0);
  await expect(page.locator('#mac-system')).toContainText('80 %');
  await expect(page.locator('#mac-system')).toContainText('Normal');
  await expect(page.locator('.cooling')).not.toBeVisible();
  await expect(page.locator('.fans')).not.toBeVisible();
  const size=await page.evaluate(()=>({scroll:document.documentElement.scrollWidth,width:innerWidth}));
  expect(size.scroll).toBeLessThanOrEqual(size.width);
  const codex=await page.locator('#codex-card').boundingBox();const cpu=await page.locator('#cpu-card').boundingBox();
  expect(codex.y).toBeLessThan(cpu.y);
  expect(await page.evaluate(()=>window.sentMessages.some(value=>/consume|redeem|reset/i.test(value.type)))).toBe(false);
});
test('stationary profile can be restored without the notebook transform',async({page})=>{
  await page.evaluate(settings=>window.dashboardTest.applyConfiguration({settings:{...settings,profile:'desktop'},displays:[]}),settings);
  await expect(page.locator('body')).not.toHaveClass(/notebook/);
  const stage=await page.locator('#stage').boundingBox();expect(stage.width).toBe(1024);expect(stage.height).toBe(600);
});

test('Mac fullscreen button delegates every toggle to the native window',async({page})=>{
  await page.locator('#fullscreen').click();
  await page.locator('#fullscreen').click();
  expect(await page.evaluate(()=>window.sentMessages.filter(value=>value.type==='toggleFullscreen').length)).toBe(2);
  expect(await page.evaluate(()=>window.sentMessages.some(value=>value.type==='fullscreen'||value.type==='windowed'))).toBe(false);
});

test('native Mac video controls delegate playback and seek without a web video download',async({page})=>{
  await page.evaluate(settings=>window.dashboardTest.applyConfiguration({settings:{...settings,backgroundMode:'local',localVideoPath:'/fixture.mp4'},displays:[],platform:'macos',nativeVideo:true}),settings);
  await expect(page.locator('body')).toHaveClass(/native-video/);
  expect(await page.locator('#local-video').getAttribute('src')).toBeNull();
  await page.locator('#play').click();
  expect(await page.evaluate(()=>window.sentMessages.some(value=>value.type==='videoToggle'))).toBe(true);
  await page.locator('#cinema').click();
  await page.evaluate(()=>window.receivers.forEach(callback=>callback({data:{type:'playback',native:true,paused:false,time:4,duration:12,error:0}})));
  await expect(page.locator('#native-video-play')).toHaveText('Pause');
  await expect(page.locator('#native-video-time')).toHaveText('0:00:04 / 0:00:12');
  await expect(page.locator('#native-player-controls')).toBeVisible();
  await page.locator('#native-video-position').fill('8');
  await page.locator('#native-video-position').dispatchEvent('change');
  expect(await page.evaluate(()=>window.sentMessages.some(value=>value.type==='videoSeek'&&value.seconds===8))).toBe(true);
  await page.locator('#leave-cinema').click();
  await expect(page.locator('#native-player-controls')).not.toBeVisible();
});

test('Mac background retention defaults off and saves immediately',async({page})=>{
  await page.locator('#settings-button').click();
  const keep=page.locator('#keep-dashboard-background');
  await expect(keep).toBeVisible(); await expect(keep).not.toBeChecked();
  await keep.check();
  expect(await page.evaluate(()=>window.sentMessages.filter(value=>value.type==='saveSettings').at(-1).settings.keepDashboardInBackground)).toBe(true);
  await page.evaluate(settings=>window.dashboardTest.applyConfiguration({settings:{...settings,keepDashboardInBackground:true},displays:[],platform:'macos'}),settings);
  await expect(keep).toBeChecked();
  await keep.uncheck();
  expect(await page.evaluate(()=>window.sentMessages.filter(value=>value.type==='saveSettings').at(-1).settings.keepDashboardInBackground)).toBe(false);
});

test('audio and playback buttons use accessible monochrome vector icons',async({page})=>{
  const mute=page.locator('#mute');
  await expect(mute.locator('svg path')).toHaveCount(2);
  await expect(mute).toHaveAttribute('data-icon','volume-off');
  await expect(mute).toHaveAttribute('aria-label','Ton einschalten');
  await expect(mute).toHaveAttribute('aria-pressed','true');
  await mute.click();
  await expect(mute).toHaveAttribute('data-icon','volume-on');
  await expect(mute).toHaveAttribute('aria-label','Ton ausschalten');
  await expect(mute).toHaveAttribute('aria-pressed','false');
  await mute.click();
  await expect(mute).toHaveAttribute('data-icon','volume-off');
  await expect(page.locator('#play svg')).toHaveCount(1);
  await expect(mute.locator('svg')).toHaveAttribute('stroke','currentColor');
});
