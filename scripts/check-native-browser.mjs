// Smoke-test the trimmed publish under the server's exact CSP. Build Drover.Ui.Tests first for its Playwright runtime.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const published = path.resolve(process.argv[2] ?? 'artifacts/native/wwwroot');
const pictures = path.resolve(process.argv[3] ?? 'artifacts/native-smoke');
const { chromium } = await import(path.join(repository, 'tests/Drover.Ui.Tests/bin/Release/net11.0/.playwright/package/index.mjs'));
const headersSource = await fs.readFile(path.join(repository, 'src/Drover.Server/Security/SecurityHeaders.cs'), 'utf8');
const policyDefinition = headersSource.match(/private const string ContentSecurityPolicy =([\s\S]*?);\r?\n/)[1];
const policy = [...policyDefinition.matchAll(/"([^"\n]*)"/g)].map(match => match[1]).join('');
assert(policy.includes("script-src 'self' 'wasm-unsafe-eval'"));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.manifest': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf' };
const server = http.createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).replace(/^\/native\/?/, '');
    let file = path.resolve(published, relative);
    response.setHeader('Content-Security-Policy', policy);
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Cache-Control', 'no-store');
    if (!file.startsWith(`${published}${path.sep}`) && file !== published) {
        response.writeHead(404).end();
        return;
    }
    try {
        if (!path.extname(relative)) {
            file = path.join(published, 'index.html');
        }
        const bytes = await fs.readFile(file);
        response.setHeader('Content-Type', mime[path.extname(file)] ?? 'application/octet-stream');
        response.end(bytes);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const referenceOrigin = process.env.DROVER_REFERENCE_ORIGIN;
const origin = referenceOrigin ?? `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ headless: true, ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {}), args: ['--no-sandbox'] });
const spaceId = '00000000-0000-7000-8000-0000000000b1';
const botId = '0196f1a0-0000-7000-8000-000000000004';
const connectionId = '0196f1a0-0000-7000-8000-000000000003';
const person = { userId: '00000000-0000-7000-8000-0000000000a1', email: 'sample@example.test', displayName: 'Sample person', spaces: [{ id: spaceId, kind: 'personal', name: 'Sample space', handle: 'sample', role: 'owner' }] };
const connection = { id: connectionId, name: 'Sample connection', kind: 'venice', baseUrl: null, hasKey: true, status: 'ready', defaultModel: 'sample-model', balances: [], balanceReadAt: null, lastCheckedAt: null };
const bot = { id: botId, name: 'Sample bot', handle: 'sample-bot', instructions: 'Sample instructions', modelConnectionId: connectionId, model: 'sample-model', status: 'active' };
const screens = [
    ['account', '/setup', 'Set up Drover'],
    ['connect-model', '/setup/model', 'Connect a model'],
    ['meet-bot-empty', '/setup/bot', 'Meet your first bot'],
    ['bots', '/bots', 'Bots'],
    ['models', '/models', 'Model connections'],
    ['status', '/status', 'Status'],
    ['tools-empty', '/tools', 'Tool connections'],
    ['tools', '/tools', 'Tool connections'],
    ['tools-add', '/tools', 'Tool connections'],
    ['tools-review', '/tools', 'Tool connections'],
    ['usage-empty', '/usage', 'Usage'],
    ['extension-editor-loading', `/extensions/edit?bot=${botId}`, 'Make an extension'],
    ['extension-editor-failed', `/extensions/edit?bot=${botId}`, 'Make an extension'],
    ['extension-editor', '/extensions/edit', 'Make an extension'],
];
const errors = [];
try {
    await fs.mkdir(pictures, { recursive: true });
    for (const theme of ['dark', 'light']) {
        for (const width of [1280, 360]) {
            const context = await browser.newContext({ viewport: { width, height: 1000 }, reducedMotion: 'reduce', serviceWorkers: 'block' });
            await context.addInitScript(theme => localStorage.setItem('drover.theme', theme), theme);
            const page = await context.newPage();
            page.on('pageerror', error => errors.push(String(error)));
            let expectedFailures = 0;
            page.on('console', message => {
                if (message.type() !== 'error') { return; }
                // These two intentional API failures exercise the loading-to-failure and retry states.
                const expectedUrl = `${origin}/api/spaces/${spaceId}/extensions/from-bot/${botId}`;
                if (message.location().url === expectedUrl && message.text() === 'Failed to load resource: the server responded with a status of 503 (Service Unavailable)') {
                    expectedFailures++;
                } else { errors.push(message.text()); }
            });
            let current;
            let heldRoute;
            await page.route('**/api/**', async route => {
                const address = new URL(route.request().url()).pathname;
                let body;
                if (address === '/api/session') { body = person; }
                else if (address === '/api/setup') { body = { requiresCode: true }; }
                else if (address.endsWith('/model-connections')) { body = current === 'meet-bot-empty' ? [] : [connection]; }
                else if (address.endsWith('/bots')) { body = current === 'meet-bot-empty' ? [] : [bot]; }
                else if (address.endsWith('/status')) { body = { serverRowsShown: true, rows: [{ area: 'server', name: 'Sample database', state: 'ok', summary: 'Sample: working.', checkedAt: null }, { area: 'space', name: 'Sample model', state: 'problem', summary: 'Sample: not answering.', checkedAt: null }] }; }
                else if (address.endsWith('/usage')) { body = { period: '7d', since: '2026-11-18T00:00:00Z', currency: 'usd', crew: [] }; }
                else if (address.endsWith('/tool-connections')) { body = current === 'tools-empty' || current === 'tools-add' ? [] : [{ id: connectionId, name: 'Sample tools', slug: 'sample-tools', url: 'https://tools.example.test/mcp', authKind: 'header', trust: 'untrusted', protocolMode: 'automatic', status: 'ready', negotiatedProtocol: null, lastListedAt: null, lastError: null, signInScope: 'space', signIn: null }]; }
                else if (address.endsWith(`/tool-connections/${connectionId}/tools`)) { body = current === 'tools-review' ? [{ id: botId, connectionId, name: 'sample', modelName: 'sample__sample', title: 'Sample tool', description: 'Sample changed description', sensitivity: 'read', state: 'available', lastSeenAt: '2026-11-18T00:00:00Z', needsReview: true, definitionHash: 'sample-current', reviewed: { title: 'Sample tool', description: 'Sample original description', inputSchema: '{}' } }] : []; }
                else if (address.endsWith('/extensions')) { body = []; }
                else if (address.endsWith(`/extensions/from-bot/${botId}`)) {
                    if (current === 'extension-editor-loading') { heldRoute = route; return; }
                    await route.fulfill({ status: 503, json: { title: 'Sample server is unavailable.', status: 503 } });
                    return;
                }
                else { await route.fulfill({ status: 404, body: '' }); return; }
                await route.fulfill({ json: body });
            });
            for (const [name, address, title] of screens) {
                current = name;
                const target = `${referenceOrigin ? '' : '/native'}${address}`;
                if (name === 'account' || (referenceOrigin && (name.startsWith('tools') || name.startsWith('extension-editor')))) {
                    await page.goto(`${origin}${target}`);
                    await page.locator(referenceOrigin ? '.dv-boot' : '.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
                } else if (referenceOrigin) {
                    await page.evaluate(target => globalThis.Blazor.navigateTo(target), target);
                } else {
                    // Exercise the browser-history integration while keeping the already-started runtime.
                    await page.evaluate(target => { history.pushState(null, '', target); dispatchEvent(new PopStateEvent('popstate')); }, target);
                }
                await page.waitForFunction(title => document.title.startsWith(title), title, { timeout: 15000 });
                await page.waitForTimeout(600);
                await page.getByRole('heading', { name: name === 'account' ? 'Create your account' : title, exact: true })
                    .waitFor({ state: 'attached', timeout: 5000 });
                if (name === 'tools-empty') {
                    await page.getByRole('heading', { name: 'No tool server yet', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'tools' || name === 'tools-review') {
                    await page.getByRole('heading', { name: 'Sample tools', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'tools-review') {
                    await page.getByRole('button', { name: 'Accept the change to Sample tool', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'tools-add') {
                    await page.getByRole('button', { name: 'Add a tool server', exact: true }).dispatchEvent('click');
                    await page.getByRole('heading', { name: 'Add a tool server', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'extension-editor-failed') {
                    await page.getByRole('heading', { name: 'Drover could not open this bot as an extension', exact: true })
                        .waitFor({ state: 'attached', timeout: 5000 });
                }
                assert.equal(errors.length, 0, errors.join('\n'));
                await page.screenshot({ path: path.join(pictures, `${name}-${width}-${theme}.png`) });
                process.stdout.write(`${name} ${width} ${theme}: passed\n`);
                if (heldRoute) {
                    await heldRoute.fulfill({ status: 503, json: { title: 'Sample server is unavailable.', status: 503 } });
                    heldRoute = null;
                }
            }
            // The export is an ordinary download under CSP; verify the delivered bytes, not only the button.
            await page.mouse.move(width / 2, 500);
            await page.mouse.wheel(0, 3000);
            await page.waitForTimeout(200);
            const exportControl = page.getByRole(referenceOrigin ? 'link' : 'button', { name: 'Download extension.json', exact: true });
            const [saved] = await Promise.all([page.waitForEvent('download'), referenceOrigin ? exportControl.click() : exportControl.dispatchEvent('click')]);
            assert.equal(saved.suggestedFilename(), 'extension.json');
            const file = JSON.parse(await fs.readFile(await saved.path(), 'utf8'));
            assert.equal(file.version, 1);
            assert.equal(expectedFailures, 2, 'Both deliberate API failures must reach the browser.');
            await context.close();
        }
    }
    assert.equal(errors.length, 0, errors.join('\n'));
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
}
