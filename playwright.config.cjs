const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({
  testDir: 'tests/ui', workers: 1, reporter: 'list',
  use: { viewport: { width: 1024, height: 600 }, headless: true },
  projects: process.platform === 'darwin'
    ? [{name:'chromium',use:{browserName:'chromium'}},{name:'webkit',use:{browserName:'webkit'}}]
    : [{name:'edge',use:{browserName:'chromium',channel:'msedge'}}],
  outputDir: 'artifacts/ui-results'
});
