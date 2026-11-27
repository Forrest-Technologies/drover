// Smoke-test the trimmed publish under the server's exact CSP, served at the root as Drover's server serves it. Build
// Drover.Presentation.Tests first for its Playwright runtime, or name another copy of the same Playwright version in
// DROVER_PLAYWRIGHT.
//
// With DROVER_LIVE_ORIGIN set to a running Drover server, it instead captures each scene of the JSON file
// DROVER_LIVE_SCENES against that server's real data, signed in as DROVER_LIVE_EMAIL / DROVER_LIVE_PASSWORD when given:
// <scene>-<width>-<theme>.png under the folder in argv[2]. See captureLive at the end of this file.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const published = path.resolve(process.argv[2] ?? 'artifacts/native/wwwroot');
const pictures = path.resolve(process.argv[3] ?? 'artifacts/native-smoke');
const webRoot = published;
const { chromium } = await import(process.env.DROVER_PLAYWRIGHT ?? path.join(repository, 'tests/Drover.Presentation.Tests/bin/Release/net11.0/.playwright/package/index.mjs'));
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
    const relative = pathname.replace(/^\//, '');
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
// Some Playwright calls (keyboard input, evaluate, close) have no timeout of their own. If the browser stops answering,
// this watchdog ends the check with the last step it reached instead of letting it hang until the job limit.
const stallLimit = 5 * 60 * 1000;
let lastStep = 'starting the browser';
let stallTimer;
function progress(step) {
    lastStep = step;
    clearTimeout(stallTimer);
    stallTimer = setTimeout(() => {
        process.stderr.write(`The browser check made no progress for ${stallLimit / 60000} minutes after: ${lastStep}\n`);
        process.exit(1);
    }, stallLimit);
    stallTimer.unref();
}
progress(lastStep);
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const browser = await chromium.launch({ headless: true, ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {}), args: ['--no-sandbox'] });
const spaceId = '00000000-0000-7000-8000-0000000000b1';
const previewApiRequests = [];
const unsettledRequests = new Set();
const botId = '0196f1a0-0000-7000-8000-000000000004';
const connectionId = '0196f1a0-0000-7000-8000-000000000003';
const htmlFileId = '0196f1a0-0000-7000-8000-0000000000f1';
const person = { userId: '00000000-0000-7000-8000-0000000000a1', email: 'sample@example.test', displayName: 'Sample person', spaces: [{ id: spaceId, kind: 'personal', name: 'Sample space', handle: 'sample', role: 'owner' }] };
const connection = { id: connectionId, name: 'Sample connection', kind: 'venice', baseUrl: null, hasKey: true, status: 'ready', defaultModel: 'sample-model', balances: [], balanceReadAt: null, lastCheckedAt: null };
// The kinds the server offers, in the contract's shape. The compatible kind carries two named providers made up for the
// check and marked as samples: one hosted with a key page, one on the person's own machine with no key.
const providerKinds = [
    { kind: 'venice', displayName: 'Venice', keyRule: 'required', addressRule: 'fixed', addressHint: null, keyHint: 'Made in your Venice account, under API keys.', reportsCapabilities: true, namedProviders: [], signIn: false },
    {
        kind: 'openai_compatible', displayName: 'Compatible endpoint', keyRule: 'optional', addressRule: 'required',
        addressHint: 'The address of the server, starting with http:// or https://.', keyHint: 'Leave this empty if the server needs no key.', reportsCapabilities: false,
        namedProviders: [
            { id: 'sample-hosted', displayName: 'Sample hosted provider', address: 'https://hosted.example.test/v1', keyRule: 'required', keyPage: 'https://keys.example.test/', local: false },
            { id: 'sample-local', displayName: 'Sample local server', address: 'http://localhost:9/v1', keyRule: 'none', keyPage: null, local: true },
        ],
        signIn: false,
    },
];
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
const retryPicture = {
    id: '0196f1a0-0000-7000-8000-00000000000b', name: 'sample-retry.png',
    contentType: 'image/png', kind: 'image', size: 80, width: 16, height: 16,
};
const exhaustedPicture = {
    id: '0196f1a0-0000-7000-8000-00000000000c', name: 'sample-retry-exhausted.png',
    contentType: 'image/png', kind: 'image', size: 80, width: 16, height: 16,
};
const concurrentPictures = Array.from({ length: 4 }, (_, index) => ({
    id: `0196f1a0-0000-7000-8000-${String(index + 13).padStart(12, '0')}`,
    name: `sample-concurrent-${index + 1}.png`,
    contentType: 'image/png', kind: 'image', size: 80, width: 16, height: 16,
}));
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
messages[0].parts.push({ kind: 'file', fileId: sampleFile.id, file: sampleFile });
messages[0].parts.push(...concurrentPictures.map(file => ({ kind: 'file', fileId: file.id, file })));
messages[0].parts.push({ kind: 'file', fileId: retryPicture.id, file: retryPicture });
messages[0].parts.push({ kind: 'file', fileId: exhaustedPicture.id, file: exhaustedPicture });
const searchTopMessageId = '0196f1a0-0000-7000-8000-000000000101';
const searchBottomMessageId = '0196f1a0-0000-7000-8000-000000000125';
const unusableReplyId = '0196f1a0-0000-7000-8000-000000000204';
const rawDsmlEnvelope = '<\uFF5CDSML\uFF5Ccalls>\n<\uFF5CDSML\uFF5Cinvoke name="sandbox.run">\n<\uFF5CDSML\uFF5Cparameter name="command" string="true">ls -la</\uFF5CDSML\uFF5Cparameter>\n</\uFF5CDSML\uFF5Cinvoke>\n</\uFF5CDSML\uFF5Ccalls>';
const quotedDsmlSample = 'Here is a DSML example:\n\n```xml\n<\uFF5CDSML\uFF5Ccalls>\n<\uFF5CDSML\uFF5Cinvoke name="sandbox.run">\n<\uFF5CDSML\uFF5Cparameter name="command">ls</\uFF5CDSML\uFF5Cparameter>\n</\uFF5CDSML\uFF5Cinvoke>\n</\uFF5CDSML\uFF5Ccalls>\n```';
const cutOffDsmlRequest = '<\uFF5CDSML\uFF5Ccalls>\n<\uFF5CDSML\uFF5Cinvoke name="sandbox.run">\n<\uFF5CDSML\uFF5Cparameter name="command" string="true">ls -la /handoff-sample';
const helperBotId = '0196f1a0-0000-7000-8000-000000000205';
const helperBot = { id: helperBotId, name: 'Sample helper', handle: 'sample-helper', instructions: 'Sample instructions', modelConnectionId: connectionId, model: 'sample-model', status: 'active' };
const handoffParentRunId = '0196f1a0-0000-7000-8000-000000000206';
const handoffCallId = '0196f1a0-0000-7000-8000-000000000207';
const handoffChildRunId = '0196f1a0-0000-7000-8000-000000000208';
const handoffChildThreadId = '0196f1a0-0000-7000-8000-000000000209';
const handoffTask = 'List the workspace files.';
const handoffArguments = JSON.stringify({ bot_id: helperBotId, task: handoffTask });
const handoffToolEvents = [
    { seq: 1, runId: handoffParentRunId, type: 'tool.requested', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ toolCallId: handoffCallId, toolName: 'delegate', title: 'Hand work to a bot', arguments: handoffArguments, effect: 'allow' }) },
    { seq: 2, runId: handoffParentRunId, type: 'delegation.started', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ toolCallId: handoffCallId, childRunId: handoffChildRunId, childBotId: helperBotId, childThreadId: handoffChildThreadId }) },
    { seq: 3, runId: handoffChildRunId, type: 'run.started', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ status: 'running', reason: null, error: null, attempt: 1, usage: null }) },
    { seq: 4, runId: handoffChildRunId, type: 'run.completed', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ status: 'completed', reason: null, error: null, attempt: 1, usage: null }) },
    { seq: 5, runId: handoffParentRunId, type: 'delegation.completed', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ toolCallId: handoffCallId, childRunId: handoffChildRunId, childBotId: helperBotId, outcome: 'completed', summary: cutOffDsmlRequest }) },
    { seq: 6, runId: handoffParentRunId, type: 'tool.completed', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ toolCallId: handoffCallId, outcome: 'completed', isError: false, preview: '', durationMs: 0 }) },
    { seq: 7, runId: handoffParentRunId, type: 'run.completed', createdAt: thread.lastActivityAt,
        data: JSON.stringify({ status: 'completed', reason: null, error: null, attempt: 1, usage: null }) },
];
const unusableMessages = [
    {
        id: '0196f1a0-0000-7000-8000-000000000201', threadId: thread.id, position: 1, role: 'user',
        parts: [{ kind: 'text', text: 'Show a DSML example.' }], createdAt: thread.lastActivityAt,
    },
    {
        id: '0196f1a0-0000-7000-8000-000000000202', threadId: thread.id, position: 2, role: 'assistant',
        parts: [{ kind: 'text', text: quotedDsmlSample }], createdAt: thread.lastActivityAt,
    },
    {
        id: '0196f1a0-0000-7000-8000-000000000203', threadId: thread.id, position: 3, role: 'user',
        parts: [{ kind: 'text', text: 'List the workspace files.' }], createdAt: thread.lastActivityAt,
    },
    {
        id: unusableReplyId, threadId: thread.id, position: 4, role: 'assistant',
        parts: [{ kind: 'text', text: rawDsmlEnvelope }], createdAt: thread.lastActivityAt,
    },
    {
        id: '0196f1a0-0000-7000-8000-00000000020c', threadId: thread.id, position: 5, role: 'assistant',
        parts: [{ kind: 'tool_call', toolCallId: handoffCallId, toolName: 'delegate', arguments: handoffArguments }],
        createdAt: thread.lastActivityAt,
    },
];
const searchMessages = Array.from({ length: 25 }, (_, index) => ({
    id: `0196f1a0-0000-7000-8000-${String(index + 101).padStart(12, '0')}`,
    threadId: thread.id,
    position: index + 1,
    role: index % 2 === 0 ? 'assistant' : 'user',
    parts: [{
        kind: 'text',
        text: index === 0
            ? 'The lighthouse keeper visits on Tuesdays.'
            : index === 24
                ? 'The lighthouse lamp was polished today.'
                : `Routine conversation filler message number ${index + 1} with enough words to fill vertical viewport space.`,
    }],
    createdAt: thread.lastActivityAt,
}));
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
// The Tools section's sample tools: a built-in group of one that reads and one that writes, set read only, and a
// tool server's group whose tools are set one by one.
const editorTool = (id, name, title, sensitivity, grant, server) => ({
    toolId: `0196f1a0-0000-7000-8000-0000000007${id}`, name, title, description: 'Sample description.', source: server ? 'connection' : 'builtin',
    connectionId: server ? connectionId : null, connectionName: server ? 'Sample tools' : null, sensitivity, grant, ceiling: 'allow',
    ceilingBotName: null, state: 'available', alwaysAsks: false, needsReview: false, definitionHash: `sample-${id}`, reviewed: null,
});
const editorTools = [
    editorTool('01', 'time.now', 'Sample current time', 'read', 'allow', false),
    editorTool('02', 'files.write', 'Sample file writer', 'write', 'off', false),
    editorTool('03', 'sample_lookup', 'Sample lookup', 'write', 'ask', true),
    editorTool('04', 'sample_update', 'Sample update', 'write', 'off', true),
];
const screens = [
    ['sign-in', '/sign-in', 'Sign in to Drover', 'Sign in'],
    ['account', '/setup', 'Set up Drover', 'Create your account'],
    ['connect-model', '/setup/model', 'Connect a model'],
    ['meet-bot-empty', '/setup/bot', 'Create your first bot'],
    ['bots', '/bots', 'Bots'],
    ['bot-editor', `/bots/${botId}`, 'Sample bot'],
    ['bot-editor-tools', `/bots/${botId}`, 'Sample bot'],
    ['bot-new', '/bots/new', 'New bot'],
    ['sandbox-terminal', `/bots/${botId}/sandbox?tab=terminal`, "Sample bot's sandbox"],
    ['conversation', `/talk/${botId}`, 'Sample bot'],
    ['conversation-load-failed', `/talk/${botId}`, 'Sample bot'],
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
    ['extensions', '/extensions', 'Extensions'],
];
// A sample installed extension, as the server lists one: a tool server not yet connected, a theme, and a rule.
const sampleExtension = {
    id: '0196f1a0-0000-7000-8000-0000000000e1',
    packageId: 'sample.studio',
    name: 'Sample studio',
    description: 'Sample description',
    installedAt: '2026-01-01T00:00:00Z',
    connection: {
        name: 'Sample helpdesk', url: 'https://tools.example.test/mcp', host: 'tools.example.test', auth: 'none', headerName: null,
        isLocal: false, available: true, dependsOnSettings: false, asksForHeader: false,
        connectionId: null, status: null, problem: null,
        tools: [{ name: 'lookup', why: 'Sample reason.', toolId: null }],
    },
    parts: {
        settings: [],
        themes: [{ id: 'sample-dusk', name: 'Sample dusk', intent: 'A sample look.' }],
        rules: [{
            name: 'Sample: ask before writing', enabled: true, checkpoint: 'before_tool', effect: 'ask',
            conditions: [{ kind: 'tool_kind', toolKinds: ['write'] }], position: null,
        }],
    },
    filledSettings: {},
};
const nativeProjectScreens = [
    ['projects-empty', '/projects', 'Projects', 'Projects'],
    ['projects-failed', '/projects', 'Projects', 'Projects'],
    ['projects', '/projects', 'Projects', 'Projects'],
    ['project-loading', `/projects/${projectId}`, 'Project', 'Project'],
    ['project-failed', `/projects/${projectId}`, 'Project', 'Drover could not load the project'],
    ['project-missing', `/projects/${projectId}`, 'Project', 'Project not found'],
    ['project', `/projects/${projectId}`, 'Project', 'Weekly'],
    // The Voice page, with nothing connected and with a model for each of its three rows. They stand before the
    // scene that starts the app afresh, so what they taught the app about voice goes with the runtime: no other
    // scene has a microphone or a speaker button. scripts/check-native-voice.mjs exercises voice itself.
    ['voice-nothing', '/voice', 'Voice', 'Voice'],
    ['voice', '/voice?rows=shown', 'Voice', 'Voice'],
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
            previewApiRequests.length = 0;
            unsettledRequests.clear();
            page.on('request', request => {
                previewApiRequests.push({ method: request.method(), path: new URL(request.url()).pathname });
                unsettledRequests.add(request);
            });
            page.on('requestfinished', request => unsettledRequests.delete(request));
            page.on('requestfailed', request => unsettledRequests.delete(request));
            await page.route('https://preview-image.example.test/**', async route => {
                externalPreviewRequests++;
                await route.abort();
            });
            let previewRequests = 0;
            let retryPictureRequests = 0;
            let exhaustedPictureRequests = 0;
            let concurrentPictureRequests = 0;
            let activeConcurrentPictureRequests = 0;
            let maxConcurrentPictureRequests = 0;
            const heldConcurrentPictures = [];
            // The file card in the conversation and in the Files panel reads the same preview path as the panel's
            // Preview toggle, so the answer is chosen by the step under test, not by the order of the requests.
            let panelPreviewBroken = false;
            let completePreview;
            const previewGate = new Promise(resolve => { completePreview = resolve; });
            let conversationLoadRequests = 0;
            let unusableRegenerateRequests = 0;
            page.on('console', message => {
                if (message.type() !== 'error') { return; }
                if (message.location().url.startsWith(previewOrigin)) { return; }
                // The paste check makes one file fail to read on purpose; the app logs that, as it should.
                if (current === 'conversation'
                    && message.text().includes('A file the browser handed over with a paste could not be read')) {
                    return;
                }
                if (current === 'conversation' && previewRequests === 5
                    && message.location().url.endsWith(`/files/${sampleFile.id}/native-preview`)
                    && message.text().includes('net::ERR_FAILED')) {
                    return;
                }
                if (current === 'sign-in' && (
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
                } else if (current === 'conversation-load-failed'
                    && message.location().url === `${origin}/api/spaces/${spaceId}/threads/${thread.id}/messages`
                    && message.text().includes('500 (Internal Server Error)')) {
                    // The first read of this scene fails on purpose, so the retry state can be checked.
                    return;
                } else { errors.push(`${message.location().url}: ${message.text()}`); }
            });
            // The account already keeps the look this device runs, as a second device would find it.
            let accountAppearance = { themeId: 'front-office', mode: theme, accent: 'theme' };
            let current;
            let heldRoute;
            let botCreates = 0;
            // The files the conversation uploaded, in order, by name and bytes: each pasted or dropped file adds one.
            const uploadedFiles = [];
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
                else if (address === '/api/account/appearance' && method === 'GET') { body = accountAppearance; }
                else if (address === '/api/account/appearance' && method === 'PUT') {
                    accountAppearance = route.request().postDataJSON();
                    await route.fulfill({ status: 204 });
                    return;
                }
                // Nothing can listen or speak in any scene but the Voice page's own, so no other scene has a microphone,
                // a speaker or a Call button.
                else if (address.endsWith('/voice/options')) {
                    const none = { sources: [], selected: null };
                    const filled = (model, voice = null) => ({
                        sources: [{ connectionId, connectionName: 'Sample connection', model, isServer: false, isPrivateAddress: false }],
                        selected: { connectionId, connectionName: 'Sample connection', model, voice, isServer: false, isPrivateAddress: false, automatic: true },
                    });
                    body = {
                        listen: current === 'voice' ? filled('sample-whisper') : none,
                        speak: current === 'voice' ? filled('sample-tts', 'sample-voice') : none,
                        live: current === 'voice' ? filled('sample-realtime', 'sample-voice') : none,
                        limits: { maxRecordingBytes: 26214400, maxReadAloudCharacters: 4000, maxCallMinutes: null },
                    };
                }
                else if (address.endsWith('/voice/voices') && current === 'voice') { body = [{ id: 'sample-voice', name: 'Sample voice' }]; }
                else if (address === '/api/model-provider-kinds') { body = providerKinds; }
                else if (address.endsWith('/model-connections')) { body = current === 'meet-bot-empty' ? [] : [connection]; }
                else if (address.endsWith(`/model-connections/${connectionId}/models`)) { body = []; }
                else if (address.endsWith('/bots') && route.request().method() === 'POST') {
                    botCreates++;
                    await route.fulfill({ status: 201, json: bot });
                    return;
                }
                else if (address.endsWith('/bots')) { body = current === 'meet-bot-empty' ? [] : current === 'conversation-unusable-tool-request' ? [bot, helperBot] : [bot]; }
                else if (address.endsWith(`/bots/${botId}`)) { body = bot; }
                else if (address.endsWith(`/bots/${botId}/definition`)) { body = botDetail; }
                else if (address.endsWith(`/bots/${botId}/notify`)) { body = { notifies: false }; }
                else if (address.endsWith(`/bots/${helperBotId}`)) { body = helperBot; }
                else if (address.endsWith(`/bots/${helperBotId}/definition`)) { body = { ...botDetail, id: helperBotId, name: 'Sample helper', handle: 'sample-helper' }; }
                else if (address.endsWith(`/bots/${helperBotId}/notify`)) { body = { notifies: false }; }
                else if (address.endsWith(`/bots/${helperBotId}/sandbox`)) { body = { available: false, hasSandbox: false, desktop: false, state: 'stopped' }; }
                else if (address.includes(`/bots/${helperBotId}/`)) { body = []; }
                else if (address.endsWith(`/bots/${botId}/sandbox`)) { body = { available: true, hasSandbox: true, desktop: current === 'conversation', state: 'running' }; }
                else if (address.endsWith('/sandbox/options')) { body = { available: true, maxIdleMinutes: 60 }; }
                else if (address.endsWith('/rules/options')) { body = { writtenPatterns: true }; }
                else if (address.endsWith('/limits/options')) { body = { maxSteps: 20, maxTokens: 10000, maxDelegations: 3 }; }
                else if (address.endsWith(`/bots/${botId}/threads`)) { body = [thread]; }
                else if (address.endsWith('/main-thread') || address.endsWith(`/threads/${thread.id}`)) { body = thread; }
                else if (address === `/api/spaces/${spaceId}/threads/${thread.id}/models` && route.request().method() === 'GET') {
                    body = { botModel: bot.model, botModelName: bot.model, chosen: null, models: [{ model: bot.model, name: bot.model }] };
                }
                else if (address === `/api/spaces/${spaceId}/threads/search` && route.request().method() === 'POST') {
                    const { query } = route.request().postDataJSON() ?? {};
                    const hits = query === 'lighthouse'
                        ? [
                            {
                                messageId: searchTopMessageId,
                                threadId: thread.id,
                                botId,
                                botName: bot.name,
                                threadTitle: null,
                                inMainConversation: true,
                                fromBot: true,
                                sentAt: thread.lastActivityAt,
                                snippet: 'The lighthouse keeper visits on Tuesdays.',
                                matches: [{ start: 4, length: 10 }],
                            },
                            {
                                messageId: searchBottomMessageId,
                                threadId: thread.id,
                                botId,
                                botName: bot.name,
                                threadTitle: null,
                                inMainConversation: true,
                                fromBot: false,
                                sentAt: thread.lastActivityAt,
                                snippet: 'The lighthouse lamp was polished today.',
                                matches: [{ start: 4, length: 10 }],
                            },
                        ]
                        : [];
                    body = { hits, window: 2000, older: null };
                }
                else if (address.endsWith('/summary')) { body = { text: null }; }
                else if (address.endsWith('/seen')) { await route.fulfill({ status: 204 }); return; }
                else if (address.endsWith('/stream')) {
                    if (current === 'conversation-load-failed') {
                        // A stream that ends reports a reconnection, which reads the conversation again by itself.
                        // Hold this one open so the scene checks the button, not the automatic retry.
                        return;
                    }

                    await route.fulfill({ contentType: 'text/event-stream', body: ': connected\n\n' });
                    return;
                }
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
                    body = { image: panelPreviewBroken ? 'iVBORw0KGgo=' : samplePng };
                }
                else if (address.endsWith(`/files/${retryPicture.id}/native-preview`)) {
                    retryPictureRequests++;
                    body = retryPictureRequests === 1 ? { refusal: 3 } : { image: samplePng };
                }
                else if (address.endsWith(`/files/${exhaustedPicture.id}/native-preview`)) {
                    exhaustedPictureRequests++;
                    body = { refusal: 3 };
                }
                else if (concurrentPictures.some(file => address.endsWith(`/files/${file.id}/native-preview`))) {
                    concurrentPictureRequests++;
                    activeConcurrentPictureRequests++;
                    maxConcurrentPictureRequests = Math.max(
                        maxConcurrentPictureRequests,
                        activeConcurrentPictureRequests);
                    await new Promise(resolve => heldConcurrentPictures.push(resolve));
                    activeConcurrentPictureRequests--;
                    body = { image: samplePng };
                }
                else if (address.endsWith(`/threads/${thread.id}/attachment-rules`)) {
                    body = { maxFileBytes: 26214400, maxFilesPerMessage: 10, modelReadsImages: true, hasSandbox: true };
                }
                else if (address.endsWith(`/threads/${thread.id}/files`) && route.request().method() === 'POST') {
                    const uploaded = new URL(route.request().url()).searchParams.get('name') ?? '';
                    const bytes = route.request().postDataBuffer() ?? Buffer.alloc(0);
                    uploadedFiles.push({ name: uploaded, bytes });
                    const number = String(uploadedFiles.length).padStart(3, '0');
                    await route.fulfill({
                        status: 201,
                        json: {
                            id: `0196f1a0-0000-7000-8000-000000000${number}`, name: uploaded,
                            contentType: 'image/png', kind: 'image', size: bytes.length, width: 16, height: 16,
                        },
                    });
                    return;
                }
                else if (/\/files\/[^/]+$/.test(address) && route.request().method() === 'DELETE') {
                    await route.fulfill({ status: 204 });
                    return;
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
                else if (address.endsWith(`/threads/${thread.id}/messages/${unusableReplyId}/regenerate`) && route.request().method() === 'POST') {
                    unusableRegenerateRequests++;
                    await route.fulfill({
                        status: 202,
                        json: { message: unusableMessages[2], run: heldRun },
                    });
                    return;
                }
                else if (address.endsWith('/messages') && route.request().method() === 'GET') {
                    if (current === 'conversation-load-failed' && ++conversationLoadRequests === 1) {
                        await route.fulfill({ status: 500, json: { title: 'Sample: the server is unavailable.', status: 500 } });
                        return;
                    }

                    body = current === 'conversation-links'
                        ? [{ ...messages[0], parts: [{ kind: 'text', text: 'Sample reply with a source.' }, { kind: 'source', text: 'Sample external source', url: `${origin}/sample-external` }] }]
                        : current === 'conversation-unusable-tool-request'
                            ? (unusableRegenerateRequests > 0 ? unusableMessages.slice(0, 3) : unusableMessages)
                            : current === 'conversation-search' ? searchMessages : messages;
                }
                else if (address.endsWith('/messages')) { body = current === 'conversation-search' ? searchMessages : messages; }
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
                else if (address.endsWith('/tool-events')) { body = current === 'conversation-unusable-tool-request' ? handoffToolEvents : []; }
                else if (address.endsWith(`/bots/${botId}/tools`) && current === 'bot-editor-tools') { body = editorTools; }
                else if (address.endsWith(`/bots/${botId}/tools`)
                    || ['/rules', '/schedules'].some(suffix => address.endsWith(suffix))) { body = []; }
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
                    const sent = JSON.parse(route.request().postData() ?? '{}');
                    projectMoved = sent.projectId != null;
                    body = projectMoved ? { ...projectOutsideThread, projectId } : projectOutsideThread;
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
                else if (address.endsWith('/extensions')) { body = current === 'extensions' ? [sampleExtension] : []; }
                // The themes a space's extensions offer, read when a person signs in and when the appearance choices open.
                else if (address.endsWith('/themes')) { body = []; }
                else { await route.fulfill({ status: 404, body: '' }); return; }
                await route.fulfill({ json: body });
            });
            const activeScreens = [...screens, ...nativeProjectScreens];
            for (const [name, address, title, heading = title] of activeScreens) {
                current = name;
                const titleBefore = await page.title();
                if (name === 'project' || name === 'project-member') { projectMoved = false; }
                if (name === 'conversation-load-failed') { conversationLoadRequests = 0; }
                const editorLoaded = name === 'bot-editor'
                    ? page.waitForResponse(response => response.ok()
                        && response.request().method() === 'GET'
                        && new URL(response.url()).pathname === `/api/spaces/${spaceId}/bots/${botId}/sandbox`)
                    : null;
                const target = address;
                if (name === 'account' || name === 'sign-in' || name === 'project-member' || name === 'conversation-load-failed') {
                    // A fresh runtime, so the conversation reads from the start and meets the failed answer.
                    await page.goto(`${origin}${target}`);
                    await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
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
                if (name === 'bot-editor-tools') {
                    await page.getByRole('button', { name: 'Tools, 1 allowed · 1 ask first', exact: true }).dispatchEvent('click');
                    await page.getByRole('group', { name: 'Every tool in Built in for Sample bot', exact: true }).waitFor({ state: 'attached' });
                    await page.getByRole('button', { name: 'Sample tools, 1 of 2 on', exact: true }).dispatchEvent('click');
                    const serverTools = page.getByRole('group', { name: 'Every tool in Sample tools for Sample bot', exact: true });
                    await serverTools.waitFor({ state: 'attached' });
                    await page.getByText('No tool here is known only to read.', { exact: false }).waitFor({ state: 'attached' });
                    await serverTools.scrollIntoViewIfNeeded();
                }
                if (name === 'sandbox-terminal') {
                    await page.getByRole('textbox', { name: 'Shell command', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'conversation') {
                    await page.getByRole('textbox', { name: 'Message to Sample bot', exact: true }).waitFor({ state: 'attached' });
                    await page.getByText(messages[0].parts[0].text, { exact: true }).first().waitFor({ state: 'attached' });
                    const firstPictureDeadline = Date.now() + 10000;
                    while (concurrentPictureRequests < 2 && Date.now() < firstPictureDeadline) {
                        await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    await page.evaluate(() => new Promise(resolve =>
                        requestAnimationFrame(() => requestAnimationFrame(resolve))));
                    assert.equal(concurrentPictureRequests, 2,
                        'Only two conversation pictures should read from the server together.');
                    heldConcurrentPictures.splice(0).forEach(release => release());
                    const backlogDeadline = Date.now() + 10000;
                    while (concurrentPictureRequests < concurrentPictures.length && Date.now() < backlogDeadline) {
                        await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    assert.equal(concurrentPictureRequests, concurrentPictures.length,
                        'The queued conversation pictures should start after the first pair finishes.');
                    assert.equal(activeConcurrentPictureRequests, 2,
                        'The drained picture backlog should keep both request slots busy.');
                    assert.equal(maxConcurrentPictureRequests, 2,
                        'The picture request limit should hold while the backlog drains.');
                    heldConcurrentPictures.splice(0).forEach(release => release());
                    for (const file of concurrentPictures) {
                        await page.locator(`[role="img"][aria-label="${file.name}"]`)
                            .waitFor({ state: 'attached', timeout: 10000 });
                    }
                    const retryDeadline = Date.now() + 10000;
                    while (retryPictureRequests < 2 && Date.now() < retryDeadline) {
                        await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    assert.equal(retryPictureRequests, 2, 'A busy picture preview should be retried once.');
                    await page.locator('[role="img"][aria-label="sample-retry.png"]')
                        .waitFor({ state: 'attached', timeout: 10000 });
                    const exhaustedDeadline = Date.now() + 10000;
                    while (exhaustedPictureRequests < 2 && Date.now() < exhaustedDeadline) {
                        await new Promise(resolve => setTimeout(resolve, 50));
                    }
                    assert.equal(exhaustedPictureRequests, 2, 'A busy picture preview should retry only once.');
                    await shown(page, 'This picture could not be shown.')
                        .waitFor({ state: 'attached', timeout: 10000 });
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
                    await page.getByText('Sample tools', { exact: true }).waitFor({ state: 'attached' });
                    await page.getByRole('button', { name: 'More for Sample tools', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'tools-review') {
                    await page.getByRole('button', { name: 'Accept the change to Sample tool', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'tools-add') {
                    await page.getByRole('button', { name: 'Add a tool server', exact: true }).dispatchEvent('click');
                    await page.getByRole('heading', { name: 'Add a tool server', exact: true }).waitFor({ state: 'attached' });
                }
                if (name === 'extensions') {
                    await page.getByRole('heading', { name: 'Tool server: Sample helpdesk', exact: true })
                        .waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Remove Sample studio', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
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
                    await page.getByRole('button', { name: 'New project', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    assert.equal(await page.getByRole('textbox', { name: 'Name', exact: true }).count(), 0,
                        'the projects list must not hold a field until New project is pressed');
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
                    await page.getByRole('heading', { name: 'Instructions', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('heading', { name: 'Conversations', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('heading', { name: 'Files', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Download brief.txt', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    assert.equal(await page.getByRole('textbox', { name: 'Name', exact: true }).count(), 0,
                        'a project reads before it edits, so no field is on the page');
                }
                if (name === 'conversation-load-failed') {
                    await page.getByRole('heading', { name: 'Drover could not load this conversation', exact: true })
                        .waitFor({ state: 'attached', timeout: 5000 });
                    await page.getByRole('button', { name: 'Try again', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                    assert.equal(await page.getByText('No messages yet.', { exact: false }).count(), 0,
                        'a conversation that was never read must not say nothing was said');
                    // Uno's hidden text box carries no disabled attribute, so ask the field itself: it takes no text.
                    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
                    await field.press('X');
                    assert.equal(await field.inputValue(), '', 'the field is disabled while the state is shown');
                    const waitingField = page.getByRole('textbox', {
                        name: 'Message to Sample bot', exact: true,
                        description: /^Available when the conversation has loaded\.$/,
                    });
                    await waitingField.waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'voice-nothing') {
                    await waitForShown(page, 'Voice needs a speech model.');
                    await page.getByRole('link', { name: 'Connect one', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                if (name === 'voice') {
                    for (const row of ['Listens with', 'Speaks with', 'Live calls with']) {
                        await page.getByRole('combobox', { name: row, exact: true }).waitFor({ state: 'attached', timeout: 10000 });
                    }
                    await page.getByRole('button', { name: 'Play a sample', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
                }
                assert.equal(errors.length, 0, errors.join('\n'));
                await page.screenshot({ path: path.join(pictures, `${name}-${width}-${theme}.png`) });
                if (name === 'conversation-load-failed') {
                    const reread = page.waitForResponse(reply => reply.request().method() === 'GET'
                        && new URL(reply.url()).pathname === `/api/spaces/${spaceId}/threads/${thread.id}/messages`
                        && reply.status() === 200);
                    // Enter on a wide window, Space on a phone: both keys take the one action.
                    await page.getByRole('button', { name: 'Try again', exact: true }).press(width === 360 ? 'Space' : 'Enter');
                    await reread;
                    await page.getByText(messages[0].parts[0].text, { exact: true }).first().waitFor({ state: 'attached', timeout: 10000 });
                    assert.equal(conversationLoadRequests, 2, 'the first read failed and the retry read once');
                    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
                    await field.press('X');
                    assert.equal(await field.inputValue(), 'X', 'the field takes text once the conversation has been read');
                    await field.fill('');
                    // The description leaves with the failure. Uno debounces accessibility updates, so wait for it.
                    await page.waitForFunction(
                        () => !document.querySelector('[aria-label="Message to Sample bot"]')?.getAttribute('aria-description'),
                        null, { timeout: 5000 });
                    const waitingField = page.getByRole('textbox', {
                        name: 'Message to Sample bot', exact: true,
                        description: /^Available when the conversation has loaded\.$/,
                    });
                    assert.equal(await waitingField.count(), 0, 'the field says nothing about waiting once the conversation is read');
                }
                if (name === 'projects' && width === 360) {
                    await checkPhoneNavLayout(page, width);
                }
                if (name === 'projects') {
                    await checkProjectListKeyboard(page, width, theme);
                }
                if (name === 'project') {
                    const cpu = await context.newCDPSession(page);
                    await cpu.send('Emulation.setCPUThrottlingRate', { rate: 4 });
                    try {
                        await checkProjectDraftsAndKeyboard(page, width, theme);
                    } finally {
                        await cpu.send('Emulation.setCPUThrottlingRate', { rate: 1 });
                        await cpu.detach();
                    }
                    await checkProjectMoveFlow(page, width, theme);
                }
                if (name === 'project-member') {
                    assert.equal(await page.getByRole('button', { name: 'Edit project', exact: true }).count(), 0,
                        'a member is not offered the edit action');
                    assert.equal(await page.getByRole('button', { name: 'More', exact: true }).count(), 0,
                        'a member is not offered the more menu');
                }
                if (name === 'conversation') {
                    await checkComposerBounds(page);
                    await checkPastedAndDroppedFiles(page, uploadedFiles, width, theme);
                    await checkStopBounds(page, width);
                }
                if (name === 'conversation') {
                    const preview = page.getByRole('button', { name: 'Preview sample-preview.md', exact: true });
                    const closePreview = page.getByRole('button', { name: 'Close preview of sample-preview.md' });
                    const download = page.getByRole('link', { name: 'Download sample-preview.md' });
                    const previewPath = `/files/${sampleFile.id}/native-preview`;
                    const requested = page.waitForRequest(request => request.method() === 'GET'
                        && new URL(request.url()).pathname === `/api/spaces/${spaceId}${previewPath}`);
                    await preview.press('Enter');
                    await requested;
                    await closePreview.waitFor({ state: 'attached' });
                    await page.screenshot({ path: path.join(pictures, `preview-loading-${width}-${theme}.png`) });
                    const loaded = page.waitForResponse(reply => reply.request().method() === 'GET'
                        && new URL(reply.url()).pathname === `/api/spaces/${spaceId}${previewPath}`);
                    completePreview();
                    await loaded;
                    await waitForShown(page, 'Sample preview heading');
                    await waitForPreviewLoaded(page);
                    await page.waitForTimeout(500);
                    assert.equal(externalPreviewRequests, 0);
                    await page.screenshot({ path: path.join(pictures, `preview-markdown-${width}-${theme}.png`) });
                    await closePreview.press('Space');
                    // What each state draws once its answer is applied; the request alone does not prove that.
                    const drawn = {
                        pdf: 'Sample PDF page one summary.',
                        diagram: 'Diagram node Upload file',
                        limit: 'This file exceeds the preview limits. Download the original to keep it.',
                        error: 'Could not show the preview. Download the file to keep it.',
                        malformed: 'This file contains invalid preview data. Download it to keep it.',
                        unsupported: 'This file format has no native preview. Download it to keep it.',
                    };
                    for (const state of ['pdf', 'diagram', 'limit', 'error', 'malformed', 'unsupported']) {
                        const requestPath = `/api/spaces/${spaceId}${previewPath}`;
                        const before = previewRequestCount(requestPath);
                        const settled = state === 'error'
                            ? page.waitForEvent('requestfailed', request => request.method() === 'GET'
                                && new URL(request.url()).pathname === requestPath)
                            : page.waitForResponse(reply => reply.request().method() === 'GET'
                                && new URL(reply.url()).pathname === requestPath);
                        await preview.press('Enter');
                        await closePreview.waitFor({ state: 'attached', timeout: 10000 });
                        await download.waitFor({ state: 'attached' });
                        await settled;
                        await waitForShown(page, drawn[state]);
                        await waitForPreviewLoaded(page);
                        assert.equal(previewRequestCount(requestPath), before + 1,
                            `${state}: expected exactly one preview request`);
                        await page.waitForTimeout(500);
                        assert.equal(previewRequestCount(requestPath), before + 1,
                            `${state}: no second preview request may arrive late`);
                        if (state === 'pdf') {
                            await page.getByRole('button', { name: 'Next PDF page', exact: true }).press('Enter');
                            await page.waitForTimeout(300);
                        }
                        await page.screenshot({ path: path.join(pictures, `preview-${state}-${width}-${theme}.png`) });
                        await closePreview.press('Space');
                        await preview.waitFor({ state: 'attached', timeout: 10000 });
                    }
                    await checkWorkspacePanels(page, width, theme);
                    if (width === 1280) {
                        await checkHtmlPreviewContainment(page, previewFrameRequests, revokedPreviewGrants, () => externalBeaconRequests);
                    }
                    await openBeside(page, 'Files');
                    const panelPreview = page.getByRole('button', { name: 'Preview sample-panel.png', exact: true });
                    const closePanel = page.getByRole('button', { name: 'Close preview of sample-panel.png' });
                    const requestPath = `/api/spaces/${spaceId}/files/${panelFile.id}/native-preview`;
                    // A file card can read the same preview path to draw its own picture. Let any such read settle
                    // first, so the only preview request counted below is the one the toggle sends.
                    await panelPreview.waitFor({ state: 'attached', timeout: 10000 });
                    await waitUntilSettled(requestPath);
                    for (const state of ['picture', 'picture-error']) {
                        panelPreviewBroken = state === 'picture-error';
                        const shownBefore = await page.locator('[role="img"][aria-label="sample-panel.png"]').count();
                        const before = previewRequestCount(requestPath);
                        const answer = page.waitForResponse(reply => reply.request().method() === 'GET'
                            && new URL(reply.url()).pathname === requestPath);
                        await panelPreview.press('Enter');
                        await closePanel.waitFor({ state: 'attached', timeout: 10000 });
                        await answer;
                        // The preview shows its picture, or its decode failure, only once the answer is drawn.
                        if (state === 'picture') {
                            await page.waitForFunction(
                                ({ name, count }) => document.querySelectorAll(`[role="img"][aria-label="${name}"]`).length > count,
                                { name: 'sample-panel.png', count: shownBefore }, { timeout: 10000 });
                        } else {
                            await waitForShown(page, 'Could not decode this picture. Download the file to keep it.');
                        }
                        await waitForPreviewLoaded(page);
                        assert.equal(previewRequestCount(requestPath), before + 1,
                            `${state}: expected exactly one panel preview request`);
                        await page.screenshot({ path: path.join(pictures, `preview-${state}-${width}-${theme}.png`) });
                        await closePanel.press('Space');
                        await panelPreview.waitFor({ state: 'attached', timeout: 10000 });
                    }
                    panelPreviewBroken = false;
                }
                if (name === 'bot-editor') {
                    await checkLeavingWithEdits(page, address);
                }
                if (name === 'bot-new') {
                    await checkLeavingNewBot(page, titleBefore, () => botCreates);
                }
                if (name === 'usage') {
                    await page.getByRole('link', { name: 'Sample bot', exact: true }).dispatchEvent('click');
                    await page.waitForURL(url => url.searchParams.get('bot') === botId);
                    await page.getByText("Sample bot's most expensive runs in the last 30 days", { exact: true })
                        .waitFor({ state: 'attached' });
                }
                if (name === 'sign-in' || name === 'account') {
                    await checkFieldHints(page, name);
                }
                process.stdout.write(`${name} ${width} ${theme}: passed\n`);
                progress(`${name} ${width} ${theme}`);
                if (heldRoute) {
                    await heldRoute.fulfill({ status: 503, json: { title: 'Sample server is unavailable.', status: 503 } });
                    heldRoute = null;
                }
            }
            current = 'conversation-search';
            await checkConversationSearch(page, width);
            current = 'conversation-unusable-tool-request';
            unusableRegenerateRequests = 0;
            await page.goto(`${origin}/talk/${botId}`);
            const unusableHeading = page.getByRole('heading', {
                name: "Sample bot's model returned a tool request that could not be used.",
                exact: true,
            });
            await unusableHeading.waitFor({ state: 'attached', timeout: 60000 });
            assert.equal(await unusableHeading.count(), 1, 'Only the protocol-only reply should show the unusable tool-request state, not the quoted DSML code sample.');
            const handoffCard = page.getByRole('button', { name: 'Sample bot asked Sample helper: List the workspace files.', exact: true });
            await handoffCard.waitFor({ state: 'attached', timeout: 10000 });
            await handoffCard.dispatchEvent('click');
            const handoffHeading = page.getByRole('heading', {
                name: "Sample helper's model returned a tool request that could not be used.",
                exact: true,
            });
            await handoffHeading.waitFor({ state: 'attached', timeout: 10000 });
            assert.equal(await handoffHeading.count(), 1, 'A handoff answer cut off inside a tool request shows the plain words, not the raw text.');
            const disclosures = page.getByRole('button', { name: 'Original response', exact: true });
            assert.equal(await disclosures.count(), 2, 'The reply and the handoff answer each keep their original behind a disclosure.');
            const originalDisclosure = disclosures.first();
            const handoffDisclosure = disclosures.last();
            await originalDisclosure.waitFor({ state: 'attached', timeout: 10000 });
            await page.screenshot({ path: path.join(pictures, `conversation-unusable-tool-request-${width}-${theme}.png`) });
            assert.equal(await page.getByText('ls -la /handoff-sample', { exact: false }).count(), 0, 'The handoff original stays closed until asked.');
            await handoffDisclosure.press('Enter');
            assert.equal(await page.getByText('ls -la /handoff-sample', { exact: false }).count(), 1, 'The handoff disclosure opens by keyboard.');
            await originalDisclosure.dispatchEvent('click');
            await page.getByRole('button', { name: 'Try again', exact: true }).dispatchEvent('click');
            await page.getByRole('button', { name: 'Stop', exact: true }).waitFor({ state: 'attached', timeout: 10000 });
            assert.equal(unusableRegenerateRequests, 1, 'Try again on an unusable tool-request reply must invoke the existing regenerate flow.');
            progress(`conversation-unusable-tool-request ${width} ${theme}`);
            current = 'conversation-links';
            await page.route(`${origin}/sample-external`, route => route.fulfill({
                contentType: 'text/html', body: '<!DOCTYPE html><title>Sample external destination</title>',
            }));
            await page.goto(`${origin}/talk/${botId}`);
            const externalSource = page.getByRole('link', { name: /Sample external source,/ });
            await externalSource.waitFor({ state: 'attached', timeout: 60000 });
            // A popup blocker can return a fake successful window instead of null. Links must not depend on that result.
            await page.evaluate(() => { window.open = () => ({ closed: false }); });
            await externalSource.dispatchEvent('click');
            await page.waitForURL(`${origin}/sample-external`, { timeout: 10000 });
            assert.equal(await page.title(), 'Sample external destination');
            progress(`direct external link ${width} ${theme}`);
            progress(`closing the ${width} ${theme} context`);
            await context.close();
        }
    }
    assert.equal(errors.length, 0, errors.join('\n'));
} finally {
    progress('closing the browser');
    await browser.close();
    await new Promise(resolve => server.close(resolve));
    clearTimeout(stallTimer);
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
            await new Promise((resolve, reject) => {
                const deadline = setTimeout(() => reject(new Error('A screenshot did not decode within 10 s.')), 10000);
                image.onload = () => { clearTimeout(deadline); resolve(); };
                image.onerror = () => { clearTimeout(deadline); reject(new Error('A screenshot could not be decoded.')); };
                image.src = `data:image/png;base64,${base64}`;
            });
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
        const { left, top, right, bottom, count } = await withDeadline(ringBetween(inField, onButton, elsewhere), 20000,
            'the focus ring pictures to be compared');
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

// A picture on the clipboard arrives with the page's paste event, and files dropped anywhere on the conversation
// attach to it. Each step waits for what the page did, never for a set time: a paste reports whether the page took
// it from the field, an upload is waited for by count, and the drop line is waited for as it comes and goes.
async function checkPastedAndDroppedFiles(page, uploadedFiles, width, theme) {
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const dropLine = page.getByText('Drop the files to attach them.', { exact: true });
    const pasteFailed = page.getByText('What was pasted could not be attached.');
    // A complete PNG (signature, header, pixels, end), so what is uploaded can be checked byte for byte.
    const png = [...Buffer.from(samplePng, 'base64')];
    // A file copied from storage carries the time it was last changed; a picture the browser made for the paste, now.
    const longAgo = Date.parse('2024-03-01T09:00:00Z');
    // Dispatches a paste on the element with the focus, as Ctrl+V does. True when the page's paste script took it from
    // the field. The field also handles a paste, so this is read on the body, after the script's listener on the
    // document and before any listener of the field.
    const paste = (files, text) => page.evaluate(({ files, text }) => {
        const clipboardData = new DataTransfer();
        for (const file of files) {
            clipboardData.items.add(new File([new Uint8Array(file.bytes)], file.name, {
                type: 'image/png',
                lastModified: file.lastModified ?? Date.now(),
            }));
        }
        if (text) {
            clipboardData.setData('text/plain', text);
        }
        const event = new ClipboardEvent('paste', { clipboardData, bubbles: true, cancelable: true });
        let taken = false;
        const look = seen => { taken = seen.defaultPrevented; };
        document.body.addEventListener('paste', look, { capture: true, once: true });
        document.activeElement.dispatchEvent(event);
        document.body.removeEventListener('paste', look, { capture: true });
        return taken;
    }, { files, text });
    const uploadsAre = async (count, message) => {
        const deadline = Date.now() + 10000;
        while (uploadedFiles.length < count && Date.now() < deadline) {
            await new Promise(resolve => setTimeout(resolve, 50));
        }
        assert.equal(uploadedFiles.length, count, message);
    };
    const held = () => page.evaluate(() => globalThis.droverPaste.held());
    // The platform reads a drag from the document's drag events by where the pointer is, so the events carry the point.
    const holdOver = (files, text) => page.evaluate(({ files, text }) => {
        // A drop made by script has no file-system handle behind its item; the platform then reads the file itself.
        Object.defineProperty(
            DataTransferItem.prototype, 'getAsFileSystemHandle', { value: undefined, configurable: true });
        const dataTransfer = new DataTransfer();
        for (const file of files) {
            dataTransfer.items.add(new File([new Uint8Array(file.bytes)], file.name, { type: 'image/png' }));
        }
        if (text) {
            dataTransfer.setData('text/plain', text);
        }
        const point = { clientX: Math.round(innerWidth * 0.6), clientY: Math.round(innerHeight * 0.25) };
        const target = document.elementFromPoint(point.clientX, point.clientY) ?? document.body;
        globalThis.droverCheckDrag = { dataTransfer, point, target };
    }, { files, text });
    // The platform ignores a dragleave while the pointer is still over the window, so leaving carries a point at its
    // edge, as a person's drag out of the window does.
    const dragEvent = type => page.evaluate(type => {
        const { dataTransfer, point, target } = globalThis.droverCheckDrag;
        const at = type === 'dragleave' ? { clientX: 0, clientY: 0 } : point;
        const destination = target.isConnected ? target : document.body;
        destination.dispatchEvent(new DragEvent(type, { dataTransfer, bubbles: true, cancelable: true, ...at }));
    }, type);
    // A person's drag sends dragover again and again while it is held; so does this, until the drop line is shown.
    const dragUntilShown = async message => {
        const deadline = Date.now() + 10000;
        await dragEvent('dragenter');
        while (await dropLine.count() === 0) {
            assert.ok(Date.now() < deadline, message);
            await dragEvent('dragover');
        }
    };
    const waitDropLineGone = async message => {
        try {
            await dropLine.waitFor({ state: 'detached', timeout: 5000 });
        } catch (cause) {
            throw new Error(`${message} (still shown: ${await dropLine.count()}).`, { cause });
        }
    };

    // Text and a picture together: the text is what was meant, so the page leaves the paste to the field.
    await field.focus();
    assert.equal(await paste([{ name: 'mixed.png', bytes: png, lastModified: longAgo }], 'some words'), false,
        'A picture pasted with text beside it is left to the text field.');

    // A screenshot alone: the browser names it image.png at the moment of the paste, and it is named for that moment.
    assert.equal(await paste([{ name: 'image.png', bytes: png }]), true,
        'A picture pasted alone is taken by the page.');
    await uploadsAre(1, 'A picture pasted with no text uploads once, and the mixed paste before it uploaded nothing.');
    assert.match(uploadedFiles[0].name, /^Pasted image \d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.png$/);
    assert.deepEqual([...uploadedFiles[0].bytes], png, 'The picture is uploaded byte for byte.');
    await page.getByRole('button', { name: `Remove ${uploadedFiles[0].name}`, exact: true })
        .waitFor({ state: 'attached', timeout: 10000 });
    await page.screenshot({ path: path.join(pictures, `pasted-picture-${width}-${theme}.png`) });

    // A file copied from storage keeps its own name, even image.png.
    assert.equal(await paste([{ name: 'image.png', bytes: png, lastModified: longAgo }]), true);
    await uploadsAre(2, 'A file copied from storage and pasted uploads once.');
    assert.equal(uploadedFiles[1].name, 'image.png', 'A copied file named image.png keeps its name.');

    // A file that cannot be read fails the whole paste once, and every byte held for it is let go.
    await page.evaluate(() => {
        const reading = Blob.prototype.arrayBuffer;
        let calls = 0;
        Blob.prototype.arrayBuffer = function () {
            calls++;
            return calls === 2 ? Promise.reject(new Error('Sample read failure')) : reading.call(this);
        };
        globalThis.droverCheckRead = () => { Blob.prototype.arrayBuffer = reading; };
    });
    await field.focus();
    assert.equal(await paste([
        { name: 'first.png', bytes: png, lastModified: longAgo },
        { name: 'second.png', bytes: png, lastModified: longAgo },
    ]), true);
    await pasteFailed.first().waitFor({ state: 'attached', timeout: 10000 });
    await page.evaluate(() => globalThis.droverCheckRead());
    assert.equal(await held(), 0, 'A paste that failed holds no bytes.');
    await page.getByRole('button', { name: 'Dismiss', exact: true }).first().dispatchEvent('click');
    await pasteFailed.first().waitFor({ state: 'detached', timeout: 5000 });
    assert.equal(uploadedFiles.length, 2, 'Nothing of a paste that failed is uploaded.');

    // The drop line goes away after a drop and after a drag leaves the conversation.
    // Files held over the message list: the drop line shows, and goes when the files leave the conversation.
    await holdOver([{ name: 'left.png', bytes: png }]);
    await dragUntilShown('Files held over the message list show the drop line.');
    await page.screenshot({ path: path.join(pictures, `drop-cue-${width}-${theme}.png`) });
    await dragEvent('dragleave');
    await waitDropLineGone('A drag leaving the conversation left the drop line on screen');

    // Words dragged over the conversation are refused at release: nothing is uploaded and no line is left behind.
    await holdOver([], 'Sample dragged words');
    await dragEvent('dragenter');
    await dragEvent('dragover');
    await dragEvent('drop');
    assert.equal(await dropLine.count(), 0, 'Words held over the conversation never show the drop line.');

    // Files dropped on the message list attach to the conversation, and the line goes with the drop.
    await holdOver([{ name: 'dropped.png', bytes: png }]);
    await dragUntilShown('Files held over the message list after a refused drag show the drop line.');
    await dragEvent('drop');
    await waitDropLineGone('A file dropped on the conversation left the drop line on screen');
    await uploadsAre(3, 'A file dropped on the message list uploads to the conversation; the dragged words did not.');
    assert.equal(uploadedFiles[2].name, 'dropped.png');
    assert.deepEqual([...uploadedFiles[2].bytes], png);

    // A drag cancelled without a delivered dragleave or drop (such as Escape inside the viewport): normal pointer
    // input clears the drop line and restores hit-testing on both the message list and the composer.
    await holdOver([{ name: 'cancelled.png', bytes: png }]);
    await dragUntilShown('Files held over the message list before a cancelled drag show the drop line.');
    await page.evaluate(() => {
        const { dataTransfer, point, target } = globalThis.droverCheckDrag;
        const destination = target.isConnected ? target : document.body;
        destination.dispatchEvent(new DragEvent('dragleave', { dataTransfer, bubbles: true, cancelable: true, ...point }));
    });
    const dragPoint = await page.evaluate(() => globalThis.droverCheckDrag.point);
    await page.mouse.move(dragPoint.clientX, dragPoint.clientY);
    await page.mouse.move(dragPoint.clientX + 8, dragPoint.clientY + 8);
    await waitDropLineGone('A cancelled drag left the drop line on screen');
    assert.equal(uploadedFiles.length, 3, 'A cancelled drag uploads nothing.');
    await page.evaluate(() => new Promise(resolve =>
        requestAnimationFrame(() => requestAnimationFrame(resolve))));

    // One real mouse click opening the preview is the smallest proof that message list hit-testing came back.
    const previewRetry = page.getByRole('button', { name: 'Preview sample-retry.png', exact: true });
    const closeRetry = page.getByRole('button', { name: 'Close preview of sample-retry.png', exact: true });
    const previewBox = await previewRetry.boundingBox({ timeout: 5000 });
    assert.ok(previewBox, 'Preview sample-retry.png has an accessible box in the message list.');
    await page.mouse.click(previewBox.x + previewBox.width / 2, previewBox.y + previewBox.height / 2);
    await closeRetry.waitFor({ state: 'attached', timeout: 5000 }).catch(cause => {
        throw new Error('The message list did not open a file preview by mouse click after a cancelled drag.', { cause });
    });
    // Close by keyboard, as the other preview checks do, so a canvas click on the expanded card is not needed.
    await closeRetry.press('Space');
    await closeRetry.waitFor({ state: 'detached', timeout: 5000 });
    await previewRetry.waitFor({ state: 'attached', timeout: 5000 });

    const more = page.getByRole('button', { name: 'More options', exact: true });
    const moreBox = await more.boundingBox({ timeout: 5000 });
    assert.ok(moreBox, 'More options has an accessible box in the composer.');
    await page.mouse.click(moreBox.x + moreBox.width / 2, moreBox.y + moreBox.height / 2);
    const attach = page.getByRole('button', { name: 'Attach', exact: true });
    await attach.waitFor({ state: 'attached', timeout: 5000 });
    const closeBox = await more.boundingBox({ timeout: 5000 });
    assert.ok(closeBox, 'More options still has an accessible box while options are open.');
    await page.mouse.click(closeBox.x + closeBox.width / 2, closeBox.y + closeBox.height / 2);
    await attach.waitFor({ state: 'detached', timeout: 5000 });

    const fieldBox = await field.boundingBox({ timeout: 5000 });
    assert.ok(fieldBox, 'The message field has an accessible box in the composer.');
    const fieldBefore = await field.inputValue();
    await page.mouse.click(fieldBox.x + fieldBox.width / 2, fieldBox.y + fieldBox.height / 2);
    // The click puts focus on the field a frame or more later. A person cannot type before that, so the check waits
    // until focus has stayed on the field for 300 ms. Letters typed sooner land elsewhere and are lost.
    await page.waitForFunction(() => {
        const now = performance.now();
        if (document.activeElement?.getAttribute('aria-label') !== 'Message to Sample bot') {
            window.droverFieldFocusSince = undefined;
            return false;
        }

        window.droverFieldFocusSince ??= now;
        return now - window.droverFieldFocusSince >= 300;
    }, null, { polling: 50, timeout: 5000 }).catch(async cause => {
        const focused = await page.evaluate(() =>
            document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName);
        throw new Error(`A mouse click on the composer field did not keep focus on it after a cancelled drag (focus: ${JSON.stringify(focused)}).`, { cause });
    });
    await page.keyboard.type('after cancel');
    await page.waitForFunction(
        before => {
            const value = document.querySelector('[aria-label="Message to Sample bot"]')?.value ?? '';
            return value.includes('after cancel') && value.length === before.length + 'after cancel'.length;
        },
        fieldBefore,
        { timeout: 5000 }).catch(async cause => {
        throw new Error(`The composer field did not accept typed text after a cancelled drag (value: ${JSON.stringify(await field.inputValue())}).`, { cause });
    });
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.press('Delete');
    await page.waitForFunction(
        () => document.querySelector('[aria-label="Message to Sample bot"]')?.value === '',
        null,
        { timeout: 5000 });

    // More than a hundred files in one paste are one paste: the first hundred are taken and the person is told.
    await field.focus();
    assert.equal(await paste(Array.from({ length: 150 }, (_, index) => ({
        name: `many ${index + 1}.png`, bytes: png, lastModified: longAgo,
    }))), true);
    await page.getByText('Only the first 100 files were taken.').first().waitFor({ state: 'attached', timeout: 10000 });
    const remove = page.getByRole('button', { name: /^Remove / });
    assert.equal(await remove.count(), 103, 'The hundred files taken stand beside the three attached before.');
    assert.equal(await held(), 0, 'Bytes the conversation did not collect are let go once the paste is reported.');
    while (await remove.count() > 0) {
        await remove.first().dispatchEvent('click');
    }
    // A warning stays until it is dismissed, and it would stand over the buttons the next check measures.
    await page.getByRole('button', { name: 'Dismiss', exact: true }).first().dispatchEvent('click');
    await page.getByText('Only the first 100 files were taken.').first().waitFor({ state: 'detached', timeout: 5000 });

    // The paste belongs to the field it was made in: the cursor leaves while the file is read, and it still arrives.
    // It comes last, since the cursor is not brought back to the field after it.
    await page.evaluate(() => {
        const reading = Blob.prototype.arrayBuffer;
        let open;
        const gate = new Promise(resolve => { open = resolve; });
        Blob.prototype.arrayBuffer = async function () {
            await gate;
            return reading.call(this);
        };
        globalThis.droverCheckRead = () => {
            Blob.prototype.arrayBuffer = reading;
            open();
        };
    });
    const before = uploadedFiles.length;
    await field.focus();
    assert.equal(await paste([{ name: 'held.png', bytes: png, lastModified: longAgo }]), true);
    await page.keyboard.press('Tab');
    await page.evaluate(() => globalThis.droverCheckRead());
    await uploadsAre(before + 1, 'A paste read after the cursor left its field still goes to that conversation.');
    assert.equal(uploadedFiles[before].name, 'held.png');
    const heldChip = page.getByRole('button', { name: 'Remove held.png', exact: true });
    await heldChip.waitFor({ state: 'attached', timeout: 10000 });
    await heldChip.dispatchEvent('click');
    await heldChip.waitFor({ state: 'detached', timeout: 5000 });
}

// While a run is in progress Stop stands where Send was, and the options button beside the field is still there. The
// sample bot holds a run once a message is sent, so this sends one, looks at both, and leaves the run unfinished. It runs
// before the file panels are opened, which cover the composer on a phone.
async function checkStopBounds(page, width) {
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const { settled, close } = await composerRingCheck(page);
    try {
        await focusSettled(page);
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

// What opens beside the conversation is opened from the one menu in its header, at every width.
// A screen that has just opened puts focus on its first control a frame or two later. A person cannot act before
// that happens, so a check waits for it too: focus must stay on one element for 300 ms.
async function focusSettled(page) {
    await page.waitForFunction(() => {
        const now = performance.now();
        if (window.droverFocus?.element !== document.activeElement) {
            window.droverFocus = { element: document.activeElement, since: now };
        }

        return now - window.droverFocus.since >= 300;
    }, null, { polling: 50 });
}

async function openBeside(page, name) {
    await focusSettled(page);
    await page.getByRole('button', { name: 'More about Sample bot' }).dispatchEvent('click');
    await page.getByRole('menuitem', { name, exact: true }).dispatchEvent('click');
}

async function checkWorkspacePanels(page, width, theme) {
    const crew = page.getByLabel('Resize the crew sidebar', { exact: true });
    const right = page.getByLabel('Resize the right panel', { exact: true });
    const close = page.getByRole('link', { name: 'Close the activity', exact: true });
    const composer = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const emptyActivity = page.getByRole('heading', { name: 'Nothing has run in this conversation yet', exact: true });

    if (width === 360) {
        assert.equal(await crew.count(), 0);
        assert.equal(await right.count(), 0);
        await openBeside(page, 'Activity');
        await emptyActivity.waitFor({ state: 'attached' });
        assert.equal(await composer.count(), 0);
        await page.screenshot({ path: path.join(pictures, `conversation-activity-${width}-${theme}.png`) });
        await close.dispatchEvent('click');
        await composer.waitFor({ state: 'attached' });
        return;
    }

    await openBeside(page, 'Activity');
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

    await page.screenshot({ path: path.join(pictures, `conversation-activity-${width}-${theme}.png`) });
    await close.dispatchEvent('click');
    await page.waitForURL(url => url.pathname.endsWith(`/talk/${botId}`));
    await right.waitFor({ state: 'detached' });
    await openBeside(page, 'Activity');
    await page.waitForURL('**/activity');
    await page.waitForFunction(x => {
        const box = document.querySelector('[aria-label="Resize the right panel"]')?.getBoundingClientRect();
        return box && box.width > 0 && Math.abs(box.x - x) <= 1;
    }, edge);
}

async function checkHtmlPreviewContainment(page, previewFrameRequests, revokedPreviewGrants, getBeaconCount) {
    previewFrameRequests.length = 0;
    revokedPreviewGrants.length = 0;
    await openBeside(page, 'Files');
    const previewButton = page.getByRole('button', { name: 'Preview report.html', exact: true });
    await previewButton.waitFor({ state: 'attached', timeout: 5000 });
    const frameUrl = `${previewOrigin}/preview/file?grant=sample-grant`;
    const navigated = page.waitForEvent('framenavigated', {
        predicate: candidate => candidate.url() === frameUrl,
        timeout: 5000,
    });
    await previewButton.dispatchEvent('click');
    const frame = await navigated;

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

    const revoked = page.waitForResponse(response => response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/api/spaces/${spaceId}/files/${htmlFileId}/preview/revoke`);
    await page.getByRole('button', { name: 'Close', exact: true }).dispatchEvent('click');
    await iframe.waitFor({ state: 'detached', timeout: 5000 });
    await revoked;
    assert.deepEqual(revokedPreviewGrants, ['sample-grant']);
}

function previewRequestCount(requestPath) {
    return previewApiRequests.filter(request => request.method === 'GET' && request.path === requestPath).length;
}

// Playwright waits that have no timeout of their own, such as a download's file, fail here instead of hanging the check.
async function withDeadline(promise, milliseconds, what) {
    let timer;
    const late = new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(`Timed out after ${milliseconds} ms waiting for ${what}.`)), milliseconds);
    });
    try {
        return await Promise.race([promise, late]);
    } finally {
        clearTimeout(timer);
    }
}

// Drawn words belong to a semantic element: its accessible name, or its own content when it is standalone text.
// The polite announcer repeats words in a separate div, so it is never a match.
function shown(page, words) {
    const named = `[id^="uno-semantics-"][aria-label="${words}"]`;
    const drawn = `[id^="uno-semantics-"][data-uno-standalone-text="true"]:text-is("${words}")`;
    return page.locator(`${named}, ${drawn}`);
}

async function waitForShown(page, words) {
    await shown(page, words).waitFor({ state: 'attached', timeout: 10000 });
}

async function waitForPreviewLoaded(page) {
    await shown(page, 'Loading preview.').waitFor({ state: 'detached', timeout: 10000 });
}

// Waits until no request to this path is still open, so a later count starts from a quiet page.
async function waitUntilSettled(requestPath) {
    const deadline = Date.now() + 10000;
    while ([...unsettledRequests].some(request => new URL(request.url()).pathname === requestPath)) {
        assert(Date.now() < deadline, `A request to ${requestPath} is still open after 10 s.`);
        await new Promise(resolve => setTimeout(resolve, 50));
    }
}

// The exact search request, registered before the typing that triggers it. The hybrid text field updates its value
// from real key events, so a request carrying the words is the proof that the query reached the app.
function searchResponseFor(page, words) {
    return page.waitForResponse(response =>
        response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/api/spaces/${spaceId}/threads/search`
        && (response.request().postDataJSON()?.query ?? null) === words,
    { timeout: 15000 });
}

async function typeSearch(page, box, words) {
    const response = searchResponseFor(page, words);
    await box.focus();
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.type(words);
    await page.keyboard.press('Enter');
    return await response;
}

// Exercises conversation search (empty state, clear, hit list, ArrowDown/ArrowUp) and verifies opening an older hit
// scrolls its message into the viewport rather than remaining stuck at the conversation foot.
async function checkConversationSearch(page, width) {
    const target = width === 360 ? '/' : `/talk/${botId}`;
    await page.evaluate(url => { history.pushState(null, '', url); dispatchEvent(new PopStateEvent('popstate')); }, target);

    const searchBox = page.getByRole('textbox', { name: 'Search conversations', exact: true });
    await searchBox.waitFor({ state: 'attached', timeout: 15000 });

    const topMessage = page.getByText('The lighthouse keeper visits on Tuesdays.', { exact: true }).first();
    if (width === 1280) {
        await page.getByText('The lighthouse lamp was polished today.', { exact: true }).first().waitFor({ state: 'attached', timeout: 15000 });
        await topMessage.waitFor({ state: 'attached', timeout: 15000 });
        await page.waitForFunction(() => {
            const group = [...document.querySelectorAll('[role="group"]')].find(node =>
                node.textContent?.includes('The lighthouse keeper visits on Tuesdays.'));
            if (!group) { return false; }
            const box = group.getBoundingClientRect();
            return box.bottom <= 120;
        }, null, { timeout: 10000 });
    }

    // The exact request and the heading the app builds from the words it searched for prove the query landed.
    await typeSearch(page, searchBox, 'zebra');
    const nothing = page.getByRole('heading', { name: 'Nothing matches “zebra”', exact: true });
    await nothing.waitFor({ state: 'attached', timeout: 10000 });
    await page.getByRole('button', { name: 'Clear the search', exact: true }).last().dispatchEvent('click');
    await nothing.waitFor({ state: 'detached', timeout: 10000 });

    await typeSearch(page, searchBox, 'lighthouse');
    await page.locator('[aria-label="2 messages found."]')
        .or(page.getByText('2 messages found.', { exact: true }))
        // The count is both announced and drawn, so either or both can be present at once.
        .first()
        .waitFor({ state: 'attached', timeout: 10000 });

    const firstHit = page.getByRole('link', { name: /The lighthouse keeper visits on Tuesdays\./ });
    const secondHit = page.getByRole('link', { name: /The lighthouse lamp was polished today\./ });
    await firstHit.waitFor({ state: 'attached', timeout: 10000 });
    await secondHit.waitFor({ state: 'attached', timeout: 10000 });

    await firstHit.focus();
    await page.keyboard.press('ArrowDown');
    await page.waitForFunction(() =>
        (document.activeElement?.getAttribute('aria-label') ?? '').includes('The lighthouse lamp was polished today.'),
    null, { timeout: 5000 });
    await page.keyboard.press('ArrowUp');
    await page.waitForFunction(() =>
        (document.activeElement?.getAttribute('aria-label') ?? '').includes('The lighthouse keeper visits on Tuesdays.'),
    null, { timeout: 5000 });

    await firstHit.dispatchEvent('click');
    await page.waitForURL(url => url.pathname.endsWith(`/talk/${botId}`) && url.hash === `#message-${searchTopMessageId}`, { timeout: 15000 });
    await topMessage.waitFor({ state: 'attached', timeout: 15000 });
    await page.waitForFunction(() => {
        const group = [...document.querySelectorAll('[role="group"]')].find(node =>
            node.textContent?.includes('The lighthouse keeper visits on Tuesdays.'));
        if (!group) { return false; }
        const box = group.getBoundingClientRect();
        return box.height > 0 && box.top >= 80 && box.bottom <= 900;
    }, null, { timeout: 10000 });
    process.stdout.write(`conversation-search ${width}: passed\n`);
    progress(`conversation-search ${width}`);
}

/**
 * Captures each scene against a running server. A scene is
 * { name, path, title?, signedOut?, height?, hold?, fail?, steps?, nativeWait?, settle? }, where
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

async function checkProjectListKeyboard(page, width, theme) {
    const add = page.getByRole('button', { name: 'New project', exact: true });
    await add.focus();
    await page.keyboard.press('Enter');
    const field = page.getByRole('textbox', { name: 'Name', exact: true });
    await field.waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-new-dialog-${width}-${theme}.png`) });
    await page.getByRole('button', { name: 'Create project', exact: true }).dispatchEvent('click');
    await waitForShown(page, 'Enter a name for the project.');
    await page.screenshot({ path: path.join(pictures, `project-new-refusal-${width}-${theme}.png`) });
    await field.focus();
    await page.keyboard.type('Draft');
    await page.waitForTimeout(150);
    assert.equal(await field.inputValue(), 'Draft');
    await page.keyboard.press('Tab');
    const activeName = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent?.trim());
    assert.ok(['Cancel', 'Create project'].includes(activeName),
        `Expected Tab from Name to stay in the dialog, got ${activeName}`);
    await page.getByRole('button', { name: 'Cancel', exact: true }).dispatchEvent('click');
    await field.waitFor({ state: 'detached', timeout: 5000 });
    await add.dispatchEvent('click');
    await field.waitFor({ state: 'attached', timeout: 5000 });
    assert.equal(await field.inputValue(), '', 'Reopening New project must start with an empty Name field');
    assert.equal(await shown(page, 'Enter a name for the project.').count(), 0,
        'Reopening New project must clear the previous refusal');
    await page.keyboard.press('Escape');
    await field.waitFor({ state: 'detached', timeout: 5000 });
}

async function checkProjectDraftsAndKeyboard(page, width, theme) {
    await page.getByRole('button', { name: 'Edit project', exact: true }).dispatchEvent('click');
    const nameField = page.getByRole('textbox', { name: 'Name', exact: true });
    const instructionsField = page.getByRole('textbox', { name: 'Instructions', exact: true });
    await nameField.waitFor({ state: 'attached', timeout: 5000 });
    await instructionsField.waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-edit-sheet-${width}-${theme}.png`) });
    await nameField.focus();
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Weekly draft');
    await instructionsField.focus();
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.type('Keep the typed draft.');
    await page.waitForTimeout(200);

    assert.equal(await nameField.inputValue(), 'Weekly draft',
        "The sheet's Name did not take the typed draft.");
    assert.equal(await instructionsField.inputValue(), 'Keep the typed draft.',
        'Project instructions did not take the typed draft before refreshing.');

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
    assert.ok(['Cancel', 'Save changes'].includes(activeName),
        `Expected Tab from Instructions to stay in the sheet, got ${activeName}`);
    await page.keyboard.press('Escape');
    await page.getByRole('heading', { name: 'Discard changes?', exact: true }).waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-discard-dialog-${width}-${theme}.png`) });
    await page.getByRole('button', { name: 'Keep editing', exact: true }).dispatchEvent('click');
    await page.getByRole('button', { name: 'Cancel', exact: true }).dispatchEvent('click');
    await page.getByRole('button', { name: 'Discard', exact: true }).dispatchEvent('click');
    await nameField.waitFor({ state: 'detached', timeout: 5000 });
}

async function checkProjectMoveFlow(page, width, theme) {
    const address = `/api/spaces/${spaceId}/threads/${projectOutsideThreadId}/project`;
    const write = () => page.waitForResponse(reply => reply.request().method() === 'PUT'
        && new URL(reply.url()).pathname === address && reply.ok());

    await focusSettled(page);
    await page.getByRole('button', { name: 'New conversation', exact: true }).dispatchEvent('click');
    const moveHere = page.getByRole('menuitem', { name: 'Move a conversation here', exact: true });
    await moveHere.waitFor({ state: 'attached', timeout: 5000 });
    await moveHere.dispatchEvent('click');
    const move = page.getByRole('button', { name: 'Move Quarterly planning to this project', exact: true });
    await move.waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-move-dialog-${width}-${theme}.png`) });
    const movedIn = write();
    await move.dispatchEvent('click');
    const inReply = await movedIn;
    assert.equal(JSON.parse(inReply.request().postData() ?? '{}').projectId, projectId,
        'Move in must send this project, not the conversation or its bot');
    await page.getByText('You have no other conversations to move here.', { exact: true })
        .waitFor({ state: 'attached', timeout: 10000 });
    await page.screenshot({ path: path.join(pictures, `project-move-empty-${width}-${theme}.png`) });
    await page.getByRole('button', { name: 'Done', exact: true }).dispatchEvent('click');
    const conversation = page.getByRole('link', { name: 'Quarterly planning', exact: true });
    await conversation.waitFor({ state: 'attached', timeout: 5000 });

    const more = page.getByRole('button', { name: 'More for Quarterly planning', exact: true });
    await more.waitFor({ state: 'attached', timeout: 5000 });
    await focusSettled(page);
    await more.dispatchEvent('click');
    const moveOut = page.getByRole('menuitem', { name: 'Move out of this project', exact: true });
    await moveOut.waitFor({ state: 'attached', timeout: 5000 });
    const movedOut = write();
    await moveOut.dispatchEvent('click');
    const outReply = await movedOut;
    assert.equal(JSON.parse(outReply.request().postData() ?? '{}').projectId, null,
        'Move out must send no project');
    await conversation.waitFor({ state: 'detached', timeout: 10000 });

    await focusSettled(page);
    await page.getByRole('button', { name: 'More', exact: true }).dispatchEvent('click');
    const removeProject = page.getByRole('menuitem', { name: 'Remove project', exact: true });
    await removeProject.waitFor({ state: 'attached', timeout: 5000 });
    await removeProject.dispatchEvent('click');
    const keepProject = page.getByRole('button', { name: 'Keep project', exact: true });
    await keepProject.waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-remove-dialog-${width}-${theme}.png`) });
    await keepProject.dispatchEvent('click');
    await keepProject.waitFor({ state: 'detached', timeout: 5000 });

    const removeFile = page.getByRole('button', { name: 'Remove brief.txt', exact: true });
    await removeFile.waitFor({ state: 'attached', timeout: 5000 });
    await removeFile.dispatchEvent('click');
    const keepFile = page.getByRole('button', { name: 'Keep file', exact: true });
    await keepFile.waitFor({ state: 'attached', timeout: 5000 });
    await page.screenshot({ path: path.join(pictures, `project-remove-file-dialog-${width}-${theme}.png`) });
    await keepFile.dispatchEvent('click');
    await keepFile.waitFor({ state: 'detached', timeout: 5000 });
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
                        await page.goto(`${origin}${scene.path}`);
                        await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
                        if (scene.title) {
                            await page.waitForFunction(title => document.title.startsWith(title), scene.title, { timeout: 15000 });
                        }
                        // The app starts and reads its data before it can be acted on.
                        await page.waitForTimeout(scene.nativeWait ?? Number(process.env.DROVER_LIVE_NATIVE_WAIT ?? 1500));
                        for (const step of scene.nativeSteps ?? scene.steps ?? []) {
                            await perform(page, step);
                        }
                        await page.waitForTimeout(scene.settle ?? 600);
                        await fs.mkdir(folder, { recursive: true });
                        await page.screenshot({ path: path.join(folder, `${name}.png`) });
                        record[name] = { url: page.url(), title: await page.title(), errors };
                        process.stdout.write(`${name}: captured\n`);
                    } catch (failure) {
                        record[name] = { url: page.url(), failed: String(failure).split('\n')[0], errors };
                        process.stdout.write(`${name}: FAILED ${String(failure).split('\n')[0]}\n`);
                    }
                    await context.close();
                }
            }
        }
    } finally {
        await fs.writeFile(path.join(folder, 'captures.json'), `${JSON.stringify(record, null, 2)}\n`);
        await browser.close();
    }
}

async function perform(page, step) {
    const find = target => page.getByRole(target.role, { name: target.name, exact: target.exact ?? true }).first();
    if (step.click) {
        await find(step.click).dispatchEvent('click');
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
