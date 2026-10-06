const { chromium } = require('@playwright/test');
const fs=require('node:fs'),path=require('node:path');
let browser,encoder;
(async()=>{
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
  const videoPath=path.resolve('artifacts/local-video-test.webm'); fs.writeFileSync(videoPath,Buffer.from(bytes));
  browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  const page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
  const original=await page.evaluate(()=>window.dashboardTest.getSettings());
  page.on('response',async r=>{if(r.url().includes('pc-ai-stream.local')) console.log('Media response:',r.status(),r.headers()['content-type']);});
  try{
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),{...original,backgroundMode:'local',localVideoPath:videoPath,muted:true});
    await page.waitForFunction(()=>document.querySelector('#local-video').currentTime>.5,{},{timeout:10000});
    await page.locator('#play').click();await page.waitForFunction(()=>document.querySelector('#local-video').paused);
    await page.locator('#play').click();await page.waitForFunction(()=>!document.querySelector('#local-video').paused);
    console.log('Local WebM playback, pause and resume passed.');
  }catch(e){
    console.log('Local video diagnostic:',await page.evaluate(()=>{const v=document.querySelector('#local-video');return {source:v.currentSrc,ready:v.readyState,error:v.error?.message,time:v.currentTime,muted:v.muted}}));
    throw e;
  }finally{
    await page.evaluate(s=>window.chrome.webview.postMessage({type:'saveSettings',settings:s}),original);
    await page.waitForFunction(()=>window.dashboardTest.getSettings().backgroundMode==='gradient');
  }
})().catch(e=>{console.error(e.message);process.exitCode=1;}).finally(async()=>{await browser?.close();await encoder?.close();});
