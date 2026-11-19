// Opens the native browser build served by the screen-check harness and waits until the in-app checks report.
// The harness sets DROVER_UI_CHECK_* and serves the published wwwroot at <server>/native/.
import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const origin = process.env.DROVER_UI_CHECK_SERVER;
assert(origin, 'DROVER_UI_CHECK_SERVER is required');
const names = [
    'DROVER_UI_CHECK_SERVER',
    'DROVER_UI_CHECK_EMAIL',
    'DROVER_UI_CHECK_PASSWORD',
    'DROVER_UI_CHECK_CERTIFICATE',
    'DROVER_UI_CHECK_REPLY',
];
const stored = {};
for (const name of names) {
    assert(process.env[name], `${name} is required`);
    stored[name] = process.env[name];
}

const playwright = process.env.DROVER_PLAYWRIGHT
    ?? path.join(repository, 'tests/Drover.Ui.Tests/bin/Release/net11.0/.playwright/package/index.mjs');
const { chromium } = await import(playwright);
const executable = process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE;
const browser = await chromium.launch({
    headless: true,
    ...(executable ? { executablePath: executable } : {}),
    args: ['--no-sandbox', '--ignore-certificate-errors'],
});
const page = await browser.newPage({ ignoreHTTPSErrors: true, viewport: { width: 420, height: 860 } });
await page.addInitScript(values => {
    const recorded = [];
    globalThis.droverUiCheckLines = recorded;
    // A function taken off localStorage throws when the app calls it. The app calls this wrapper instead.
    globalThis.droverUiCheckRead = name => localStorage.getItem(name);
    globalThis.droverUiCheckWrite = line => {
        const text = String(line);
        recorded.push(text);
        console.log(text);
    };
    for (const [name, value] of Object.entries(values)) {
        localStorage.setItem(name, value);
    }
}, stored);

const lines = [];
function keep(line) {
    if (!lines.includes(line)) {
        lines.push(line);
        console.log(line);
    }
}
page.on('console', message => {
    const text = message.text();
    if (message.type() === 'error') {
        console.log(`console error: ${text}`);
    }
    const marker = 'ui-check ';
    const at = text.indexOf(marker);
    if (at >= 0) {
        keep(text.slice(at));
    }
});
page.on('pageerror', error => {
    keep(`ui-check page error: ${error}`);
});
page.on('requestfailed', request => {
    const failure = request.failure();
    console.log(`request failed: ${request.url()} ${failure ? failure.errorText : ''}`);
});
page.on('response', response => {
    if (response.status() >= 400) {
        console.log(`response ${response.status()} ${response.url()}`);
    }
});

const url = new URL('native/sign-in', origin);
await page.goto(url.href, { waitUntil: 'domcontentloaded', timeout: 120000 });
const deadline = Date.now() + 180000;
let result = null;
while (Date.now() < deadline) {
    try {
        const recorded = await page.evaluate(() => globalThis.droverUiCheckLines ?? []);
        for (const line of recorded) {
            keep(line);
        }
    } catch (error) {
        console.log(`page not readable: ${error}`);
    }
    result = lines.find(line => line.startsWith('ui-check result:'));
    if (result) {
        break;
    }
    await new Promise(resolve => setTimeout(resolve, 250));
}

let snapshot = '';
if (!result) {
    try {
        snapshot = JSON.stringify(await page.evaluate(() => ({
            href: location.href,
            title: document.title,
            text: (document.body?.innerText ?? '').slice(0, 500),
            lines: globalThis.droverUiCheckLines ?? [],
        })));
    } catch (error) {
        snapshot = String(error);
    }
}
await browser.close();
if (!result) {
    console.error(`The browser screen checks did not report a result. ${snapshot}`);
    process.exit(1);
}
process.exit(result === 'ui-check result: passed' ? 0 : 1);
