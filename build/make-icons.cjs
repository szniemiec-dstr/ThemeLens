// SPDX-License-Identifier: GPL-3.0-or-later
// Draws assets/icons/*.svg at the sizes Windows asks for, as PNG files in a folder of your choice.
// build/make-icons.py then packs them into the .ico files the app carries. Needs Node with Playwright.
//   node build/make-icons.cjs <output folder> && python3 build/make-icons.py <output folder>
const { chromium } = require('playwright');
const { readFileSync, mkdirSync } = require('node:fs');
const { join } = require('node:path');

(async () => {
const out = process.argv[2];
mkdirSync(out, { recursive: true });
const light = '#c9c4b8';   // drawn on a dark taskbar
const dark = '#222222';    // drawn on a light taskbar
const jobs = [
  { svg: 'app', name: 'app', sizes: [16, 20, 24, 32, 40, 48, 64, 256] },
  ...['on', 'off', 'hold'].flatMap(state => [
    { svg: `tray-${state}`, name: `tray-${state}-dark`, sizes: [16, 20, 24, 32], colour: light, ink: '#000000' },
    { svg: `tray-${state}`, name: `tray-${state}-light`, sizes: [16, 20, 24, 32], colour: dark, ink: '#f4f2ec' },
  ]),
];
const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
for (const job of jobs) {
  const svg = readFileSync(join('assets', 'icons', job.svg + '.svg'), 'utf8').replace(/<!--.*?-->\s*/s, '');
  for (const size of job.sizes) {
    await page.setViewportSize({ width: size, height: size });
    await page.setContent(
      `<style>html,body{margin:0;background:transparent}svg{display:block;width:${size}px;height:${size}px;color:${job.colour ?? '#000'};--tray-ink:${job.ink ?? '#000'}}</style>${svg}`);
    await page.screenshot({ path: join(out, `${job.name}-${size}.png`), omitBackground: true });
  }
}
await browser.close();
})();
