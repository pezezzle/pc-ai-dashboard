const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({ testDir: 'tests/ui', workers: 1, reporter: 'list', use: { channel: 'msedge', viewport: { width: 1024, height: 600 }, headless: true }, outputDir: 'artifacts/ui-results' });
