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
// A run the sample bot holds in progress once a message is sent, so the composer shows Stop. Its events never end.
const heldRun = {
    id: '0196f1a0-0000-7000-8000-000000000009', threadId: thread.id, botId, rootRunId: '0196f1a0-0000-7000-8000-000000000009',
    status: 'running', statusReason: null, error: null, attempt: 1, lastEventSeq: 0,
    usage: { input: 0, output: 0, cached: 0, cost: 0 }, startedAt: '2026-11-18T00:00:01Z', finishedAt: null,
};
const sampleFile = {
    id: '0196f1a0-0000-7000-8000-000000000007', name: 'sample-preview.md',
    contentType: 'text/markdown', kind: 'text', size: 120,
};
const panelFile = {
    id: '0196f1a0-0000-7000-8000-000000000008', name: 'sample-panel.png',
    contentType: 'image/png', kind: 'image', size: 80,
};
const exchangedFiles = [
    {
        file: panelFile, threadId: thread.id, messageId: messages[0].id,
        inMainConversation: true, fromBot: true, sharedAt: thread.lastActivityAt,
    },
    {
        file: { id: htmlFileId, name: 'report.html', contentType: 'text/html', kind: 'other', size: 128 },
        threadId: thread.id, messageId: messages[0].id,
        inMainConversation: true, fromBot: true, sharedAt: thread.lastActivityAt,
    },
];
const samplePng = 'iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAFUlEQVR4nGNg2HCBNDSqYVTD8NUAAFV1gBC4fs/iAAAAAElFTkSuQmCC';
const sampleImageAddress = 'https://preview-image.example.test/sample.png';
if (!reference) {
    messages[0].parts.push({ kind: 'file', fileId: sampleFile.id, file: sampleFile });
}
const usage = {
    botId, botName: bot.name, model: bot.model, own: { cost: 1.23, inputTokens: 120, outputTokens: 45 }, children: [],
};
const projectId = '0196f1a0-0000-7000-8000-000000000010';
const projectFileId = '0196f1a0-0000-7000-8000-0000000000f2';
const projectOutsideThreadId = '0196f1a0-0000-7000-8000-000000000007';
const projectSummary = {
    id: projectId, name: 'Weekly', instructions: 'Ship the note.',
    createdAt: thread.lastActivityAt, updatedAt: thread.lastActivityAt,
};
const projectFile = {
    file: { id: projectFileId, name: 'brief.txt', contentType: 'text/plain', kind: 'text', size: 128 },
    projectId, uploadedAt: thread.lastActivityAt,
};
const projectOutsideThread = {
    id: projectOutsideThreadId, botId, title: 'Quarterly planning',
    isShared: false, lastActivityAt: thread.lastActivityAt, activeRunId: null, isMain: false, attention: null, projectId: null,
};
const screens = [
    ['sign-in', '/sign-in', 'Sign in to Drover', 'Sign in'],
    ['account', '/setup', 'Set up Drover', 'Create your account'],
    ['connect-model', '/setup/model', 'Connect a model'],
    ['meet-bot-empty', '/setup/bot', 'Meet your first bot'],
    ['bots', '/bots', 'Bots'],
    ['bot-editor', `/bots/${botId}`, 'Sample bot', 'Edit Sample bot'],
    ['bot-new', '/bots/new', 'New bot'],
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
const nativeProjectScreens = [
    ['projects-empty', '/projects', 'Projects', 'Projects'],
    ['projects-failed', '/projects', 'Projects', 'Projects'],
    ['projects', '/projects', 'Projects', 'Projects'],
    ['project-loading', `/projects/${projectId}`, 'Project', 'Project'],
    ['project-failed', `/projects/${projectId}`, 'Project', 'Drover could not load the project'],
    ['project-missing', `/projects/${projectId}`, 'Project', 'This project is not here'],
    ['project', `/projects/${projectId}`, 'Project', 'Weekly'],
    ['project-member', `/projects/${projectId}`, 'Project', 'Weekly'],
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
            let externalPreviewRequests = 0;
            await page.route('https://preview-image.example.test/**', async route => {
                externalPreviewRequests++;
                await route.abort();
            });
            let previewRequests = 0;
            let panelPreviewRequests = 0;
            let completePreview;
            const previewGate = new Promise(resolve => { completePreview = resolve; });
            let extensionFailures = 0;
            page.on('console', message => {
                if (message.type() !== 'error') { return; }
                if (message.location().url.startsWith(previewOrigin)) { return; }
                // These two intentional API failures exercise the loading-to-failure and retry states.
                const expectedUrl = `${origin}/api/spaces/${spaceId}/extensions/from-bot/${botId}`;
                if (current === 'conversation' && previewRequests === 5
                    && message.location().url.endsWith(`/files/${sampleFile.id}/native-preview`)
                    && message.text().includes('net::ERR_FAILED')) {
                    return;
                }
                if (message.location().url === expectedUrl && message.text() === 'Failed to load resource: the server responded with a status of 503 (Service Unavailable)') {
                    extensionFailures++;
                } else if (current === 'sign-in' && (
                    (message.location().url === `${origin}/api/session` && message.text().includes('401 (Unauthorized)'))
                    || (message.location().url === `${origin}/api/setup` && message.text().includes('404 (Not Found)'))
                )) {
                    return;
                } else if ((current === 'projects-failed' || current === 'project-loading' || current === 'project-failed')
                    && (message.location().url === `${origin}/api/spaces/${spaceId}/projects`
                        || message.location().url === `${origin}/api/spaces/${spaceId}/projects/${projectId}`)
                    && message.text().includes('503 (Service Unavailable)')) {
                    return;
                } else if (current === 'project-missing'
                    && message.location().url === `${origin}/api/spaces/${spaceId}/projects/${projectId}`
                    && message.text().includes('404 (Not Found)')) {
                    return;
                } else { errors.push(`${message.location().url}: ${message.text()}`); }
            });
            let current;
            let heldRoute;
            let botCreates = 0;
            let projectMoved = false;
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
                const method = route.request().method();
                if (address === '/api/session' && current === 'sign-in') { await route.fulfill({ status: 401 }); return; }
                if (address === '/api/session') {
                    body = current === 'project-member'
                        ? { ...person, spaces: [{ ...person.spaces[0], role: 'member' }] }
                        : person;
                }
                else if (address === '/api/antiforgery') {
                    await route.fulfill({ status: 204, headers: { 'Set-Cookie': 'XSRF-TOKEN=sample; Path=/; SameSite=Strict' } });
                    return;
                }
                else if (address === '/api/setup' && current === 'sign-in') { await route.fulfill({ status: 404 }); return; }
                else if (address === '/api/setup') { body = { requiresCode: true }; }
                else if (address.endsWith('/model-connections')) { body = current === 'meet-bot-empty' ? [] : [connection]; }
                else if (address.endsWith(`/model-connections/${connectionId}/models`)) { body = []; }
                else if (address.endsWith('/bots') && route.request().method() === 'POST') {
                    botCreates++;
                    await route.fulfill({ status: 201, json: bot });
                    return;
                }
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
                else if (address.endsWith(`/files/${sampleFile.id}/native-preview`)) {
                    const attempt = ++previewRequests;
                    if (attempt === 1) {
                        await previewGate;
                    }

                    if (attempt === 5) {
                        await route.abort();
                        return;
                    }

                    if (attempt === 1) {
                        body = { markdown: `# Sample preview heading\n\n![Sample external image](${sampleImageAddress})` };
                    } else if (attempt === 2) {
                        body = {
                            pages: [
                                { number: 1, text: 'Sample PDF page one summary.' },
                                { number: 2, text: 'Sample PDF page two details.' },
                            ],
                        };
                    } else if (attempt === 3) {
                        body = {
                            diagram: {
                                direction: 'TD',
                                nodes: [
                                    { id: 'A', label: 'Upload file' },
                                    { id: 'B', label: 'Native preview' },
                                ],
                                edges: [{ from: 'A', to: 'B', label: 'bounded' }],
                            },
                        };
                    } else {
                        body = { refusal: attempt === 4 ? 1 : attempt === 6 ? 2 : 0 };
                    }
                }
                else if (address.endsWith(`/files/${panelFile.id}/native-preview`)) {
                    body = { image: ++panelPreviewRequests === 1 ? samplePng : 'iVBORw0KGgo=' };
                }
                else if (address.endsWith(`/threads/${thread.id}/messages`) && route.request().method() === 'POST') {
                    const sent = JSON.parse(route.request().postData() ?? '{}');
                    body = {
                        message: {
                            id: '0196f1a0-0000-7000-8000-00000000000a', threadId: thread.id, position: 2, role: 'user',
                            parts: [{ kind: 'text', text: sent.text ?? '' }], createdAt: '2026-11-18T00:00:01Z',
                        },
                        run: heldRun,
                    };
                }
                else if (address.endsWith(`/runs/${heldRun.id}`)) { body = heldRun; }
                else if (address.endsWith(`/runs/${heldRun.id}/events`)) {
                    // The run has started and is not finished: one event, and the stream then closes without an end for it.
                    const started = {
                        seq: 1, runId: heldRun.id, type: 'run.started', createdAt: heldRun.startedAt,
                        data: JSON.stringify({ status: 'running', reason: null, error: null, attempt: 1, usage: null }),
                    };
                    await route.fulfill({ contentType: 'text/event-stream', body: `id: 1\ndata: ${JSON.stringify(started)}\n\n` });
                    return;
                }
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
                else if (address.endsWith('/files/rules')) { body = { maxFileBytes: 26214400 }; }
                else if (address.endsWith(`/bots/${botId}/tools`)
                    || ['/rules', '/schedules', '/tool-events'].some(suffix => address.endsWith(suffix))) { body = []; }
                else if (address.endsWith('/status')) { body = { serverRowsShown: true, rows: [{ area: 'server', name: 'Sample database', state: 'ok', summary: 'Sample: working.', checkedAt: null }, { area: 'space', name: 'Sample model', state: 'problem', summary: 'Sample: not answering.', checkedAt: null }] }; }
                else if (address.endsWith('/usage')) {
                    body = { period: current === 'usage' ? '30d' : '7d', since: thread.lastActivityAt, currency: 'usd', crew: current === 'usage' ? [usage] : [] };
                }
                else if (address.endsWith(`/usage/bots/${botId}/runs`)) { body = { botId, currency: 'usd', runs: [] }; }
                else if (address.endsWith('/tool-connections')) { body = current === 'tools-empty' || current === 'tools-add' ? [] : [{ id: connectionId, name: 'Sample tools', slug: 'sample-tools', url: 'https://tools.example.test/mcp', authKind: 'header', trust: 'untrusted', protocolMode: 'automatic', status: 'ready', negotiatedProtocol: null, lastListedAt: null, lastError: null, signInScope: 'space', signIn: null }]; }
                else if (address.endsWith(`/tool-connections/${connectionId}/tools`)) { body = current === 'tools-review' ? [{ id: botId, connectionId, name: 'sample', modelName: 'sample__sample', title: 'Sample tool', description: 'Sample changed description', sensitivity: 'read', state: 'available', lastSeenAt: '2026-11-18T00:00:00Z', needsReview: true, definitionHash: 'sample-current', reviewed: { title: 'Sample tool', description: 'Sample original description', inputSchema: '{}' } }] : []; }
                else if (address === `/api/spaces/${spaceId}/threads` && method === 'GET') {
                    body = projectMoved ? [{ ...projectOutsideThread, projectId }] : [projectOutsideThread];
                }
                else if (address === `/api/spaces/${spaceId}/threads/${projectOutsideThreadId}/project` && method === 'PUT') {
                    projectMoved = true;
                    body = { ...projectOutsideThread, projectId };
                }
                else if (address === `/api/spaces/${spaceId}/projects` && method === 'GET') {
                    if (current === 'projects-failed') {
                        await route.fulfill({ status: 503, json: { title: 'Sample: the server is unavailable.', status: 503 } });
                        return;
                    }
                    body = current === 'projects-empty' ? [] : [projectSummary];
                }
                else if (address === `/api/spaces/${spaceId}/projects/${projectId}` && method === 'GET') {
                    if (current === 'project-loading') { heldRoute = route; return; }
                    if (current === 'project-failed') {
                        await route.fulfill({ status: 503, json: { title: 'Sample: the server is unavailable.', status: 503 } });
                        return;
                    }
                    if (current === 'project-missing') {
                        await route.fulfill({ status: 404, json: { title: 'Project not found.', status: 404 } });
                        return;
                    }
                    body = projectSummary;
                }
                else if (address === `/api/spaces/${spaceId}/projects/${projectId}/threads` && method === 'GET') {
                    body = projectMoved ? [{ ...projectOutsideThread, projectId }] : [];
                }
                else if (address === `/api/spaces/${spaceId}/projects/${projectId}/files` && method === 'GET') {
                    body = [projectFile];
                }
                else if (address.endsWith('/extensions')) { body = []; }
                else if (address.endsWith(`/extensions/from-bot/${botId}`)) {
                    if (current === 'extension-editor-loading') { heldRoute = route; return; }
                    await route.fulfill({ status: 503, json: { title: 'Sample server is unavailable.', status: 503 } });
                    return;
                }
                else { await route.fulfill({ status: 404, body: '' }); return; }
                await route.fulfill({ json: body });
            });
            const activeScreens = reference ? screens : [...screens, ...nativeProjectScreens];
            for (const [name, address, title, heading = title] of activeScreens) {
                if (reference && name === 'bot-new') {
                    continue;
                }

                current = name;
                const titleBefore = await page.title();
                if (name === 'project' || name === 'project-member') { projectMoved = false; }
                const editorLoaded = name === 'bot-editor'
                    ? page.waitForResponse(response => response.ok() && new URL(response.url()).pathname.endsWith(`/bots/${botId}/sandbox`))
                    : null;
                const target = `${reference ? '' : '/native'}${address}`;
                if (name === 'account' || name === 'sign-in' || name === 'project-member' || (reference && (name.startsWith('tools') || name.startsWith('extension-editor')))) {
                    await page.goto(`${origin}${target}`);
                    await page.locator(reference ? '.dv-boot' : '.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
                } else if (reference) {
                    await page.evaluate(target => globalThis.Blazor.navigateTo(target), target);
                } else {
                    // Exercise the browser-history integration while keeping the already-started runtime.
                    await page.evaluate(target => { history.pushState(null, '', target); dispatchEvent(new PopStateEvent('popstate')); }, target);
                }
                await page.waitForFunction(title => document.title === title || (document.title.startsWith(title) && /^[\s·\-\u2014|]/.test(document.title.slice(title.length))), title, { timeout: 15000 }).catch(async error => {
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
                if (name === 'projects-empty') {
                    await page.getByRole('heading', { name: 'No project yet', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'projects-failed') {
                    await page.getByRole('heading', { name: 'Drover could not load the projects', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Try again', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'projects') {
                    await page.getByRole('link', { name: 'Weekly', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('textbox', { name: 'Project name', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'project-loading') {
                    await page.getByLabel('Loading the project.', { exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'project-failed') {
                    await page.getByRole('button', { name: 'Try again', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'project-missing') {
                    await page.getByRole('link', { name: 'Back to projects', exact: true }).first().waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'project' || name === 'project-member') {
                    await page.getByRole('textbox', { name: 'Name', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('textbox', { name: 'Instructions', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Download brief.txt', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Start with Sample bot', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                assert.equal(errors.length, 0, errors.join('\n'));
                await page.screenshot({ path: path.join(pictures, `${name}-${width}-${theme}.png`) });
                if (name === 'extension-editor') {
                    // The export is an ordinary download under CSP; verify the delivered bytes, not only the button.
                    await page.mouse.move(width / 2, 500);
                    await page.mouse.wheel(0, 3000);
                    await page.waitForTimeout(200);
                    const exportControl = page.getByRole(reference ? 'link' : 'button', { name: 'Download extension.json', exact: true });
                    const [saved] = await Promise.all([page.waitForEvent('download'), reference ? exportControl.click() : exportControl.dispatchEvent('click')]);
                    assert.equal(saved.suggestedFilename(), 'extension.json');
                    const file = JSON.parse(await fs.readFile(await saved.path(), 'utf8'));
                    assert.equal(file.version, 1);
                }
                if (name === 'projects' && width === 360) {
                    await checkPhoneNavLayout(page, width);
                }
                if (name === 'projects') {
                    await checkProjectListKeyboard(page);
                }
                if (name === 'project') {
                    const cpu = await context.newCDPSession(page);
                    await cpu.send('Emulation.setCPUThrottlingRate', { rate: 4 });
                    try {
                        await checkProjectDraftsAndKeyboard(page);
                    } finally {
                        await cpu.send('Emulation.setCPUThrottlingRate', { rate: 1 });
                        await cpu.detach();
                    }
                }
                if (name === 'project-member') {
                    await page.getByText("Owners and admins change a project's name, instructions, and files. You can read them and use the conversations.", { exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    assert.equal(await page.getByRole('button', { name: 'Save project', exact: true }).isDisabled(), true);
                }
                if (name === 'conversation' && !reference) {
                    await checkComposerBounds(page);
                    await checkStopBounds(page, width);
                }
                if (name === 'conversation' && !reference) {
                    const preview = page.getByRole('button', { name: 'Preview sample-preview.md', exact: true });
                    const closePreview = page.getByRole('button', { name: 'Close preview of sample-preview.md' });
                    const download = page.getByRole('link', { name: 'Download sample-preview.md' });
                    const previewPath = `/files/${sampleFile.id}/native-preview`;
                    const requested = page.waitForRequest(request => new URL(request.url()).pathname.endsWith(previewPath));
                    await preview.press('Enter');
                    await requested;
                    await closePreview.waitFor({ state: 'attached' });
                    await page.screenshot({ path: path.join(pictures, `preview-loading-${width}-${theme}.png`) });
                    const loaded = page.waitForResponse(reply => new URL(reply.url()).pathname.endsWith(previewPath));
                    completePreview();
                    await (await loaded).finished();
                    await page.waitForTimeout(500);
                    assert.equal(externalPreviewRequests, 0);
                    await page.screenshot({ path: path.join(pictures, `preview-markdown-${width}-${theme}.png`) });
                    await closePreview.press('Space');
                    for (const state of ['pdf', 'diagram', 'limit', 'error', 'malformed', 'unsupported']) {
                        const event = state === 'error' ? 'requestfailed' : 'requestfinished';
                        const finished = page.waitForEvent(event, request => new URL(request.url()).pathname.endsWith(previewPath));
                        await preview.press('Enter');
                        await finished;
                        await download.waitFor({ state: 'attached' });
                        await page.waitForTimeout(500);
                        if (state === 'pdf') {
                            await page.getByRole('button', { name: 'Next PDF page', exact: true }).press('Enter');
                            await page.waitForTimeout(300);
                        }
                        await page.screenshot({ path: path.join(pictures, `preview-${state}-${width}-${theme}.png`) });
                        await closePreview.press('Space');
                    }
                    await checkWorkspacePanels(page, width, theme);
                    if (width === 1280) {
                        await checkHtmlPreviewContainment(page, previewFrameRequests, revokedPreviewGrants, () => externalBeaconRequests);
                    }
                    if (width === 360) {
                        await page.getByRole('button', { name: 'More about Sample bot' }).dispatchEvent('click');
                        await page.getByRole('menuitem', { name: 'Files', exact: true }).dispatchEvent('click');
                    } else {
                        await page.getByRole('link', { name: 'Files', exact: true }).dispatchEvent('click');
                    }
                    const panelPreview = page.getByRole('button', { name: 'Preview sample-panel.png', exact: true });
                    const panelPath = `/files/${panelFile.id}/native-preview`;
                    for (const state of ['picture', 'picture-error']) {
                        const finished = page.waitForEvent('requestfinished',
                            request => new URL(request.url()).pathname.endsWith(panelPath));
                        await panelPreview.press('Enter');
                        await finished;
                        await page.waitForTimeout(500);
                        await page.screenshot({ path: path.join(pictures, `preview-${state}-${width}-${theme}.png`) });
                        await page.getByRole('button', { name: 'Close preview of sample-panel.png' }).press('Space');
                    }
                }
                if (name === 'bot-editor' && !reference) {
                    await checkLeavingWithEdits(page, address);
                }
                if (name === 'bot-new') {
                    await checkLeavingNewBot(page, titleBefore, () => botCreates);
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
                if (name === 'sign-in' || name === 'account') {
                    await checkFieldHints(page, name);
                }
                process.stdout.write(`${name} ${width} ${theme}: passed\n`);
                if (heldRoute) {
                    await heldRoute.fulfill({ status: 503, json: { title: 'Sample server is unavailable.', status: 503 } });
                    heldRoute = null;
                }
            }
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
// The accessible box of each composer button must stand where the button is drawn. The semantic tree and the canvas are
// separate, so the check asks the canvas where the button is drawn: focusing a button draws its focus ring, and the
// pixels that change between another, far away control being focused and the button being focused are the ring, found
// in a picture much larger than the box so that it is seen whole whatever the box says. Every edge of the ring must sit
// where the same edge of the box does, within 2 px (the ring of a button with an icon stands 4 px outside it). The runtime
// moves a box only when the control's own offset or size changes, so a box whose ancestor moved is stale. Opening and
// closing the options row, growing the field, and a run starting are the moves that find it.
async function composerRingCheck(page) {
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    // The ring of an icon button stands this far outside its box.
    const composerRingOutset = { 'More options': 4 };
    const margin = 48;
    const reader = await page.context().newPage();
    await reader.setContent('<canvas id="c"></canvas>');
    // Three pictures of the same place: with the field focused, with the button focused, and with a control far away
    // focused. Focus inside the composer draws things that are not the ring (the frame's edge, the field's underline), and
    // they are exactly what differs between the first and the third. The pixels that differ between the first and the
    // second, and are not among those, are the ring, whatever color it is on whatever the button is filled with. The
    // box around them is where the button's ring is drawn.
    const ringBetween = async (inField, onButton, elsewhere) => reader.evaluate(async ([one, two, three]) => {
        const load = async base64 => {
            const image = new Image();
            await new Promise(resolve => { image.onload = resolve; image.src = `data:image/png;base64,${base64}`; });
            const canvas = document.getElementById('c');
            canvas.width = image.width;
            canvas.height = image.height;
            const context = canvas.getContext('2d');
            context.drawImage(image, 0, 0);
            return context.getImageData(0, 0, image.width, image.height);
        };
        const differs = (p, q, i) => Math.abs(p.data[i] - q.data[i]) + Math.abs(p.data[i + 1] - q.data[i + 1]) + Math.abs(p.data[i + 2] - q.data[i + 2]) > 60;
        const [a, b, c] = [await load(one), await load(two), await load(three)];
        let [left, top, right, bottom, count] = [Infinity, Infinity, -1, -1, 0];
        for (let y = 0; y < a.height; y++) {
            for (let x = 0; x < a.width; x++) {
                const i = (y * a.width + x) * 4;
                if (differs(a, b, i) && !differs(a, c, i)) {
                    left = Math.min(left, x);
                    top = Math.min(top, y);
                    right = Math.max(right, x);
                    bottom = Math.max(bottom, y);
                    count++;
                }
            }
        }
        return { left, top, right, bottom, count };
    }, [inField.toString('base64'), onButton.toString('base64'), elsewhere.toString('base64')]);
    // A control far from the picture, to leave the focus on for the third picture.
    const farAway = async clip => {
        const controls = page.locator('button, a[href]');
        const total = await controls.count();
        for (let index = 0; index < total; index++) {
            const box = await controls.nth(index).boundingBox({ timeout: 500 }).catch(() => null);
            if (box && (box.x + box.width < clip.x - 24 || box.x > clip.x + clip.width + 24
                || box.y + box.height < clip.y - 24 || box.y > clip.y + clip.height + 24)) {
                return controls.nth(index);
            }
        }
        throw new Error('The page has no control far from the composer to leave the focus on.');
    };
    const misses = async label => {
        const button = page.getByRole('button', { name: label, exact: true });
        const box = await button.count() === 1 ? await button.boundingBox({ timeout: 1000 }) : null;
        if (!box) {
            return [`${label} has no accessible box`];
        }
        const viewport = page.viewportSize();
        const x0 = Math.max(0, Math.floor(box.x) - margin);
        const y0 = Math.max(0, Math.floor(box.y) - margin);
        const clip = {
            x: x0,
            y: y0,
            width: Math.min(viewport.width, Math.ceil(box.x + box.width) + margin) - x0,
            height: Math.min(viewport.height, Math.ceil(box.y + box.height) + margin) - y0,
        };
        await page.mouse.move(1, 1);
        await field.focus();
        await page.waitForTimeout(200);
        const inField = await page.screenshot({ clip });
        await button.focus();
        await page.waitForTimeout(200);
        const onButton = await page.screenshot({ clip });
        await (await farAway(clip)).focus();
        await page.waitForTimeout(200);
        const elsewhere = await page.screenshot({ clip });
        const { left, top, right, bottom, count } = await ringBetween(inField, onButton, elsewhere);
        const outset = composerRingOutset[label] ?? 0;
        const expected = {
            left: Math.floor(box.x) - outset,
            top: Math.floor(box.y) - outset,
            right: Math.ceil(box.x + box.width) - 1 + outset,
            bottom: Math.ceil(box.y + box.height) - 1 + outset,
        };
        const drawn = { left: clip.x + left, top: clip.y + top, right: clip.x + right, bottom: clip.y + bottom };
        const off = ['left', 'top', 'right', 'bottom'].filter(edge => count === 0 || Math.abs(drawn[edge] - expected[edge]) > 2);
        return off.length === 0 ? [] : [
            `${label}: ${count === 0 ? 'no focus ring is drawn' : `its focus ring is drawn from ${drawn.left},${drawn.top} to ${drawn.right},${drawn.bottom}`}, `
            + `but its accessible box says ${expected.left},${expected.top} to ${expected.right},${expected.bottom} (box ${JSON.stringify(box)}); off at: ${off.join(', ')}`,
        ];
    };
    const settled = async (state, names) => {
        let found = [];
        const until = Date.now() + 3000;
        do {
            await page.waitForTimeout(250);
            found = [];
            for (const label of names) {
                found.push(...await misses(label));
            }
        } while (found.length > 0 && Date.now() < until);
        assert.equal(found.length, 0, `${state}: the accessible boxes are not where the buttons are drawn.\n${found.join('\n')}`);
    };
    return { settled, close: () => reader.close() };
}

async function checkComposerBounds(page) {
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const buttons = ['More options', 'Attach', 'Send'];
    const { settled, close } = await composerRingCheck(page);
    const more = page.getByRole('button', { name: 'More options', exact: true });

    try {
        await more.dispatchEvent('click');
        await settled('options open', buttons.filter(label => label !== 'Send'));
        await field.focus();
        await page.keyboard.type('one');
        await page.keyboard.press('Shift+Enter');
        await page.keyboard.type('two');
        await page.keyboard.press('Shift+Enter');
        await page.keyboard.type('three');
        await settled('options open and the field three lines tall', buttons);
        await more.dispatchEvent('click');
        await settled('options closed and the field three lines tall', ['More options', 'Send']);
        await more.dispatchEvent('click');
        await settled('options open again', buttons);
        await more.dispatchEvent('click');
        await field.focus();
        await page.keyboard.press('ControlOrMeta+A');
        await page.keyboard.press('Delete');
        await page.waitForTimeout(250);
    } finally {
        await close();
    }
}

// While a run is in progress Stop stands where Send was, and the options button beside the field is still there. The
// sample bot holds a run once a message is sent, so this sends one, looks at both, and leaves the run unfinished. It runs
// before the file panels are opened, which cover the composer on a phone.
async function checkStopBounds(page, width) {
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const { settled, close } = await composerRingCheck(page);
    try {
        await field.focus();
        await page.keyboard.type('hold this run');
        // Send enables once the draft has reached the conversation, a frame after the keystrokes.
        await page.waitForFunction(() => {
            const send = document.querySelector('[aria-label="Send"]');
            return send && !send.disabled && send.getAttribute('aria-disabled') !== 'true';
        }, null, { timeout: 10000 });
        await page.getByRole('button', { name: 'Send', exact: true }).dispatchEvent('click');
        await page.getByRole('button', { name: 'Stop', exact: true }).waitFor({ state: 'attached', timeout: 10000 });
        await settled(`a run in progress at ${width} px`, ['Stop', 'More options']);
    } finally {
        await close();
    }
}

async function checkLeavingWithEdits(page, address) {
    const instructions = page.getByRole('textbox', { name: 'Instructions', exact: true });
    const leavePrompt = page.getByRole('heading', { name: 'Leave without saving', exact: true });
    const editorUrl = page.url();
    await instructions.focus();
    await page.keyboard.type('Sample edit. ');
    const deadline = Date.now() + 15000;
    let typed = '';
    while (Date.now() < deadline) {
        typed = await instructions.inputValue();
        if (typed.includes('Sample edit. ')) {
            break;
        }
        await page.waitForTimeout(100);
    }
    assert(typed.includes('Sample edit. '), `Instructions did not take the edit. The field held ${JSON.stringify(typed)}.`);
    // The save words are not in the accessibility tree. The save button enables only once the editor is dirty.
    const save = page.getByRole('button', { name: 'Save changes', exact: true });
    const enabledAt = Date.now() + 15000;
    let enabled = false;
    while (Date.now() < enabledAt) {
        if (await save.isEnabled()) {
            enabled = true;
            break;
        }
        await page.waitForTimeout(100);
    }
    assert(enabled, 'The edit did not make the bot editor dirty.');

    await page.goBack();
    await leavePrompt.waitFor({ state: 'attached', timeout: 15000 });
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

// A new bot opens as a draft: nothing is stored until it is saved, so leaving it asks first and creates no bot.
async function checkLeavingNewBot(page, titleBefore, createdBots) {
    const leavePrompt = page.getByRole('heading', { name: 'Leave without saving', exact: true });
    await page.getByRole('button', { name: 'Make bot', exact: true }).waitFor({ state: 'attached', timeout: 15000 });
    assert.equal(createdBots(), 0, 'Opening a new bot stores nothing.');

    await page.goBack();
    await leavePrompt.waitFor({ state: 'attached', timeout: 15000 });
    assert.equal(createdBots(), 0, 'Being asked about leaving stores nothing.');
    await page.getByRole('button', { name: 'Leave', exact: true }).dispatchEvent('click');
    // Leaving by a step back goes to the screen that was open before the draft.
    await page.waitForFunction(title => document.title === title, titleBefore, { timeout: 15000 });
    assert.equal(createdBots(), 0, 'Leaving an unsaved new bot creates no bot.');
}

// A password manager knows the sign-in and account fields by their autocomplete names, and a field the server limits
// stops taking letters at that limit without an error.
async function checkFieldHints(page, name) {
    const hints = name === 'sign-in'
        ? [['Email', 'username'], ['Password', 'current-password']]
        : [['Email', 'username'], ['Password', 'new-password']];
    for (const [label, autofill] of hints) {
        const field = page.getByLabel(label, { exact: true });
        await field.waitFor({ state: 'attached' });
        assert.equal(await field.getAttribute('autocomplete'), autofill, `${name}: ${label} is not named ${autofill}`);
    }
    if (name === 'account') {
        // FieldLimits.DisplayName in src/Drover.Contracts/FieldLimits.cs.
        const limit = 200;
        const field = page.getByLabel('Your name', { exact: true });
        const words = await page.evaluate(() => document.body.innerText);
        await field.focus();
        await page.keyboard.type('n'.repeat(limit + 5));
        await page.waitForFunction(limit => document.activeElement?.value?.length === limit, limit, { timeout: 5000 });
        await page.waitForTimeout(300);
        assert.equal(await page.evaluate(() => document.body.innerText), words, 'Typing past the limit changed what the page says.');
    }
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
    const frameUrl = `${previewOrigin}/preview/file?grant=sample-grant`;
    const frame = page.frames().find(candidate => candidate.url() === frameUrl)
        ?? await page.waitForEvent('framenavigated', {
            predicate: candidate => candidate.url() === frameUrl,
            timeout: 5000,
        });
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
async function checkPhoneNavLayout(page, width) {
    const nav = page.getByRole('navigation', { name: 'Drover', exact: true });
    await nav.waitFor({ state: 'visible', timeout: 5000 });
    const items = [
        nav.getByRole('link', { name: 'Chats', exact: true }),
        nav.getByRole('link', { name: 'Projects', exact: true }),
        nav.getByRole('link', { name: 'Bots', exact: true }),
        nav.getByRole('link', { name: 'Tools', exact: true }),
        nav.getByRole('button', { name: 'Account for Sample person', exact: true }),
    ];
    const boxes = [];
    for (const item of items) {
        await item.waitFor({ state: 'visible', timeout: 5000 });
        const box = await item.boundingBox();
        assert(box && box.width > 0 && box.height >= 44, `PhoneNav item ${item} has invalid bounds: ${JSON.stringify(box)}`);
        assert(box.x >= 0 && box.x + box.width <= width + 1, `PhoneNav item overflows ${width}px viewport: ${JSON.stringify(box)}`);
        boxes.push(box);
    }
    const rowY = boxes[0].y;
    for (let index = 0; index < boxes.length; index++) {
        assert(Math.abs(boxes[index].y - rowY) <= 2, `PhoneNav item ${index} wrapped to another row.`);
        if (index > 0) {
            assert(boxes[index - 1].x + boxes[index - 1].width <= boxes[index].x + 1, `PhoneNav items ${index - 1} and ${index} overlap.`);
        }
    }
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    assert.equal(overflow, false, `Horizontal overflow detected in PhoneNav at ${width}px.`);
}

async function checkProjectListKeyboard(page) {
    const field = page.getByRole('textbox', { name: 'Project name', exact: true });
    await field.focus();
    await page.keyboard.type('Draft');
    await page.waitForTimeout(150);
    assert.equal(await field.inputValue(), 'Draft');
    await page.keyboard.press('Tab');
    const activeName = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent?.trim());
    assert.equal(activeName, 'New project', `Expected Tab from Project name to land on New project, got ${activeName}`);
}

async function checkProjectDraftsAndKeyboard(page) {
    const nameField = page.getByRole('textbox', { name: 'Name', exact: true });
    const instructionsField = page.getByRole('textbox', { name: 'Instructions', exact: true });
    await nameField.focus();
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Weekly draft');
    await instructionsField.focus();
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Keep the typed draft.');
    await page.waitForTimeout(200);

    assert.equal(await nameField.inputValue(), 'Weekly draft',
        'Project name did not take the typed draft before refreshing.');
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.',
        'Project instructions did not take the typed draft before refreshing.');

    await page.getByRole('button', { name: 'Move Quarterly planning into this project', exact: true }).dispatchEvent('click');
    await page.getByRole('button', { name: 'Move Quarterly planning out of this project', exact: true }).waitFor({ state: 'attached', timeout: 5000 });

    assert.equal(await nameField.inputValue(), 'Weekly draft', 'Project name draft was lost after refreshing project details.');
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.', 'Project instructions draft was lost after refreshing project details.');

    await instructionsField.focus();
    await page.keyboard.press('Home');
    for (let index = 0; index < 5; index++) {
        await page.keyboard.press('ArrowRight');
    }
    for (let index = 0; index < 3; index++) {
        await page.keyboard.press('Shift+ArrowRight');
    }
    await page.keyboard.type('this');
    assert.equal(await instructionsField.inputValue(), 'Keep this typed draft.',
        'Forward selection in the middle of project instructions must replace only the selected word.');

    await page.keyboard.press('Home');
    for (let index = 0; index < 9; index++) {
        await page.keyboard.press('ArrowRight');
    }
    for (let index = 0; index < 4; index++) {
        await page.keyboard.press('Shift+ArrowLeft');
    }
    await page.keyboard.type('the');
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.',
        'Backward selection in the middle of project instructions must keep the remaining draft intact.');

    await selectNativeTextRange(instructionsField, 5, 8, 'forward');
    await page.keyboard.type('this');
    assert.equal(await instructionsField.inputValue(), 'Keep this typed draft.',
        'Native forward selection must reach the editor before real keyboard input replaces the selected word.');
    await selectNativeTextRange(instructionsField, 5, 9, 'backward');
    await page.keyboard.type('the');
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.',
        'Native backward selection must preserve the rest of the draft.');

    await selectNativeTextRange(instructionsField, 5, 5, 'none');
    await page.keyboard.type('point ');
    assert.equal(await instructionsField.inputValue(), 'Keep point the typed draft.',
        'A native collapsed caret must reach the editor before real keyboard input inserts text.');
    await selectNativeTextRange(instructionsField, 5, 11, 'forward');
    await page.keyboard.press('Backspace');
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.',
        'Removing text inserted at a native caret must preserve the remaining draft.');

    await instructionsField.focus();
    await page.keyboard.press('Tab');
    const activeName = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent?.trim());
    assert.equal(activeName, 'Save project', `Expected Tab from Instructions to reach Save project, got ${activeName}`);
}

async function selectNativeTextRange(field, start, end, direction) {
    await field.evaluate((element, range) => new Promise((resolve, reject) => {
        const selected = () => {
            clearTimeout(deadline);
            resolve();
        };
        const deadline = setTimeout(() => {
            element.removeEventListener('select', selected);
            reject(new Error('The native text selection event did not arrive.'));
        }, 5000);
        element.addEventListener('select', selected, { once: true });
        element.setSelectionRange(range.start, range.end, range.direction);
    }), { start, end, direction });
}

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
