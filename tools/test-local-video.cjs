const { chromium } = require('@playwright/test');
const fs=require('node:fs'),path=require('node:path');
let browser,encoder;
(async()=>{
  let videoPath=process.argv[2];
  if(!videoPath) {
  encoder=await chromium.launch({channel:'msedge',headless:true});
  const fixture=await encoder.newPage();
  const bytes=await fixture.evaluate(async()=>{
    const canvas=document.createElement('canvas'); canvas.width=320; canvas.height=180;
    const ctx=canvas.getContext('2d'); const stream=canvas.captureStream(12);
    const recorder=new MediaRecorder(stream,{mimeType:'video/webm;codecs=vp8'}); const chunks=[];
    const result=new Promise(resolve=>{recorder.ondataavailable=e=>chunks.push(e.data); recorder.onstop=async()=>resolve(Array.from(new Uint8Array(await new Blob(chunks).arrayBuffer())));});
    let i=0; const timer=setInterval(()=>{ctx.fillStyle=`hsl(${i++*4},60%,25%)`;ctx.fillRect(0,0,320,180);},80);
    recorder.start(); setTimeout(()=>{recorder.stop();clearInterval(timer);stream.getTracks().forEach(t=>t.stop());},1800);
    return result;
  });
  await encoder.close();encoder=null;
  videoPath=path.resolve('artifacts/local-video-test.webm'); fs.writeFileSync(videoPath,Buffer.from(bytes));
  }
  browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  const page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
  const original=await page.evaluate(()=>window.dashboardTest.getSettings());
  page.on('response',async r=>{if(r.url().startsWith('http://127.0.0.1:')) console.log('Media response:',r.status(),r.headers()['content-type']);});
  try{
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),{...original,backgroundMode:'local',localVideoPath:videoPath,muted:true});
    await page.waitForFunction(()=>document.querySelector('#local-video').currentTime>.5,{},{timeout:10000});
    const duration=await page.locator('#local-video').evaluate(v=>v.duration);
    if(duration>11070) {
      await page.locator('#local-video').evaluate(v=>{v.currentTime=11067;});
      await page.waitForFunction(()=>{const v=document.querySelector('#local-video');return !v.seeking && v.currentTime>11068},{},{timeout:15000});
      console.log('Large MP4 seek to 3:04:27 passed, duration:',duration);
    }
    await page.locator('#play').click();await page.waitForFunction(()=>document.querySelector('#local-video').paused);
    await page.locator('#play').click();await page.waitForFunction(()=>!document.querySelector('#local-video').paused);
    await page.locator('#volume').evaluate(e=>{e.value='0';e.dispatchEvent(new Event('input'));e.dispatchEvent(new Event('change'));});
    await page.waitForFunction(()=>document.querySelector('#local-video').volume===0);
    await page.locator('#mute').click();await page.waitForFunction(()=>!document.querySelector('#local-video').muted);
    await page.locator('#mute').click();await page.waitForFunction(()=>document.querySelector('#local-video').muted);
    console.log('Audio volume and mute/unmute passed.');
    if(duration>30) {
      const before=await page.locator('#local-video').evaluate(v=>v.currentTime);
      await page.waitForFunction(t=>document.querySelector('#local-video').currentTime>t+30,before,{timeout:45000});
      const size=fs.statSync(videoPath).size;
      if(size>4294967328) {
        const source=await page.locator('#local-video').evaluate(v=>v.currentSrc);
        const offset=Math.min(6000000000,size-32);
        const response=await page.request.get(source,{headers:{Range:`bytes=${offset}-${offset+31}`}});
        if(response.status()!==206 || (await response.body()).length!==32) throw new Error('Byte range beyond 4GB failed');
        console.log('Byte range beyond 4GB passed.');
      }
      console.log('30 seconds uninterrupted playback passed.');
      await page.screenshot({path:'artifacts/large-video-live.png'});
    }
    console.log('Local video playback, pause and resume passed.');
  }catch(e){
    console.log('Local video diagnostic:',await page.evaluate(()=>{const v=document.querySelector('#local-video');return {source:v.currentSrc,ready:v.readyState,error:v.error?.message,time:v.currentTime,muted:v.muted}}));
    throw e;
  }finally{
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),original);
    await page.waitForFunction(mode=>window.dashboardTest.getSettings().backgroundMode===mode,original.backgroundMode);
  }
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(async()=>{await browser?.close();await encoder?.close();});
