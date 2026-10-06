const { chromium } = require('@playwright/test');
(async()=>{
  const browser=await chromium.connectOverCDP('http://127.0.0.1:9321');
  try {
    const page=browser.contexts().flatMap(c=>c.pages()).find(p=>p.url().startsWith('https://pc-ai-dashboard.local/'));
    console.log(await page.evaluate(()=>{const v=document.querySelector('#local-video');return {url:v.currentSrc,ready:v.readyState,time:v.currentTime,duration:v.duration,error:v.error?.message,notice:document.querySelector('#notice').textContent,mode:window.dashboardTest.getSettings().backgroundMode}}));
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
