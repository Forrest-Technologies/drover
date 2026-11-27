// Voice in the published browser build, under the server's exact content security policy, in Chromium with its fake
// microphone: a message is recorded and its words land in the message field, a reply is read aloud, and a scripted
// live call is held, talked over, muted and hung up. The server's side is stood in for here: the speech routes
// answer with sample words and a sample tone, and the call's socket is scripted. Every state is saved as a picture:
// <state>-<width>-<theme>.png under the folder in argv[3].
//
// Build Drover.Presentation.Tests first for its Playwright runtime, or name another copy of the same Playwright
// version in DROVER_PLAYWRIGHT.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const webRoot = path.resolve(process.argv[2] ?? 'artifacts/native/wwwroot');
const pictures = path.resolve(process.argv[3] ?? 'artifacts/native-voice');
const { chromium } = await import(process.env.DROVER_PLAYWRIGHT ?? path.join(repository, 'tests/Drover.Presentation.Tests/bin/Release/net11.0/.playwright/package/index.mjs'));
const headersSource = await fs.readFile(path.join(repository, 'src/Drover.Server/Security/SecurityHeaders.cs'), 'utf8');
const policyDefinition = headersSource.match(/private const string ContentSecurityPolicy =([\s\S]*?);\r?\n/)[1];
const policy = `${[...policyDefinition.matchAll(/"([^"\n]*)"/g)].map(match => match[1]).join('')}; frame-src 'none'`;
assert(policy.includes("script-src 'self' 'wasm-unsafe-eval'"));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.manifest': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.ttf': 'font/ttf' };
const server = http.createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://localhost').pathname).replace(/^\//, '');
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

// A step that never ends fails the check with the last step it reached.
const stallLimit = 4 * 60 * 1000;
let lastStep = 'starting the browser';
let stallTimer;
function progress(step) {
    lastStep = step;
    process.stdout.write(`${step}\n`);
    clearTimeout(stallTimer);
    stallTimer = setTimeout(() => {
        process.stderr.write(`The voice check made no progress for ${stallLimit / 60000} minutes after: ${lastStep}\n`);
        process.exit(1);
    }, stallLimit);
    stallTimer.unref();
}
progress(lastStep);
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

// The fake device gives Chromium a microphone that beeps, and the fake interface allows it without a prompt.
const browser = await chromium.launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE ? { executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE } : {}),
    args: ['--no-sandbox', '--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'],
});

const spaceId = '00000000-0000-7000-8000-0000000000b1';
const botId = '0196f1a0-0000-7000-8000-000000000004';
const connectionId = '0196f1a0-0000-7000-8000-000000000003';
const speechId = '0196f1a0-0000-7000-8000-0000000000c1';
const threadId = '0196f1a0-0000-7000-8000-000000000005';
const replyId = '0196f1a0-0000-7000-8000-000000000006';
const runId = '0196f1a0-0000-7000-8000-000000000009';
const space = `/api/spaces/${spaceId}`;
const person = role => ({ userId: '00000000-0000-7000-8000-0000000000a1', email: 'sample@example.test', displayName: 'Sample person', spaces: [{ id: spaceId, kind: 'personal', name: 'Sample space', handle: 'sample', role }] });
const connection = { id: connectionId, name: 'Sample connection', kind: 'venice', baseUrl: null, hasKey: true, status: 'ready', defaultModel: 'sample-model', balances: [], balanceReadAt: null, lastCheckedAt: null };
const bot = { id: botId, name: 'Sample bot', handle: 'sample-bot', instructions: 'Sample instructions', modelConnectionId: connectionId, model: 'sample-model', status: 'active' };
const thread = { id: threadId, botId, lastActivityAt: '2026-11-18T00:00:00Z', isMain: true };
const reply = {
    id: replyId, threadId, position: 1, role: 'assistant', runId,
    parts: [{ kind: 'text', text: 'Sample reply: the harbor office opens at nine.' }], createdAt: thread.lastActivityAt,
};
const transcript = 'Sample words from the fake microphone.';
const limits = { maxRecordingBytes: 26214400, maxReadAloudCharacters: 4000, maxCallMinutes: null };
const source = (model, onNetwork = false) => ({ connectionId: speechId, connectionName: 'Sample speech', model, isServer: false, isPrivateAddress: onNetwork });
const slot = (model, automatic, voice = null, onNetwork = false) => ({
    sources: [source(model, onNetwork)],
    selected: { connectionId: speechId, connectionName: 'Sample speech', model, voice, isServer: false, isPrivateAddress: onNetwork, automatic },
});
const nothing = { sources: [], selected: null };
const voices = [{ id: 'sample-voice', name: 'Sample voice' }, { id: 'other-voice', name: 'Other voice' }];

// What the space's options answer, by the name a scene sets.
const options = {
    all: { listen: slot('sample-whisper', true, null, true), speak: slot('sample-tts', true, 'sample-voice'), live: slot('sample-realtime', true, 'sample-voice'), limits },
    chosen: { listen: slot('sample-whisper', false, null, true), speak: slot('sample-tts', false, 'other-voice'), live: slot('sample-realtime', false, 'sample-voice'), limits },
    noLive: { listen: slot('sample-whisper', true), speak: slot('sample-tts', true, 'sample-voice'), live: nothing, limits },
    nothing: { listen: nothing, speak: nothing, live: nothing, limits },
};

// A WAV file of a steady tone: what the speech route answers, and long enough to be seen playing.
function tone(seconds, rate = 24000) {
    const samples = Buffer.alloc(Math.round(seconds * rate) * 2);
    for (let index = 0; index < samples.length / 2; index++) {
        samples.writeInt16LE(Math.round(Math.sin(index * 2 * Math.PI * 440 / rate) * 6000), index * 2);
    }
    return samples;
}
function wav(samples, rate = 24000) {
    const header = Buffer.alloc(44);
    header.write('RIFF', 0);
    header.writeUInt32LE(0xffffffff, 4);
    header.write('WAVEfmt ', 8);
    header.writeUInt32LE(16, 16);
    header.writeUInt16LE(1, 20);
    header.writeUInt16LE(1, 22);
    header.writeUInt32LE(rate, 24);
    header.writeUInt32LE(rate * 2, 28);
    header.writeUInt16LE(2, 32);
    header.writeUInt16LE(16, 34);
    header.write('data', 36);
    header.writeUInt32LE(0xffffffff, 40);
    return Buffer.concat([header, samples]);
}

const combinations = [['dark', 1280], ['light', 1280], ['dark', 360]];
const errors = [];
try {
    await fs.mkdir(pictures, { recursive: true });
    for (const [theme, width] of combinations) {
        await checkVoice(theme, width);
    }
    assert.equal(errors.length, 0, errors.join('\n'));
    progress('voice check passed');
} finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
    clearTimeout(stallTimer);
}

async function checkVoice(theme, width) {
    const scene = { options: 'all', role: 'owner', transcribe: 'ok', callRefusal: null };
    const requests = { transcribe: [], speak: [], sent: 0 };
    let releaseTranscript = null;
    const call = { line: null, frames: [], audioBytes: 0, loudest: 0, odd: 0 };

    const context = await browser.newContext({ viewport: { width, height: 800 }, reducedMotion: 'reduce', serviceWorkers: 'block' });
    await context.addInitScript(theme => {
        if (window === window.top) {
            localStorage.setItem('drover.theme', theme);
        }
    }, theme);
    const page = await context.newPage();
    page.on('pageerror', error => {
        if (!String(error).includes("Failed to read the 'serviceWorker' property from 'Navigator'")) {
            errors.push(`${theme} ${width}: ${error}`);
        }
    });
    page.on('console', message => {
        if (message.type() === 'error' && /Content Security Policy|Refused to/.test(message.text())) {
            errors.push(`${theme} ${width}: ${message.text()}`);
        }
        if (process.env.DROVER_VOICE_LOG) {
            process.stdout.write(`  console ${message.type()}: ${message.text()}\n`);
        }
    });

    // The call's socket, scripted: what the app sends is kept, and the test sends the server's frames.
    await page.routeWebSocket(url => url.pathname.endsWith('/call'), socket => {
        call.line = socket;
        socket.onMessage(message => {
            if (typeof message === 'string') {
                const frame = JSON.parse(message);
                call.frames.push(frame);
                if (frame.type === 'hangup') {
                    socket.send(JSON.stringify({ type: 'ended', reason: 'hung_up' }));
                }
                return;
            }
            call.audioBytes += message.length;
            call.odd += message.length % 2;
            for (let at = 0; at + 1 < message.length; at += 2) {
                call.loudest = Math.max(call.loudest, Math.abs(message.readInt16LE(at)));
            }
        });
    });

    await page.route('**/api/**', async route => {
        const request = route.request();
        const address = new URL(request.url()).pathname;
        const method = request.method();
        const problem = (status, title) => route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify({ title, status }) });
        if (address === '/api/session') { await route.fulfill({ json: person(scene.role) }); return; }
        if (address === '/api/antiforgery') {
            await route.fulfill({ status: 204, headers: { 'Set-Cookie': 'XSRF-TOKEN=sample; Path=/; SameSite=Strict' } });
            return;
        }
        if (address === `${space}/voice/options`) { await route.fulfill({ json: options[scene.options] }); return; }
        if (address === `${space}/voice/voices`) { await route.fulfill({ json: voices }); return; }
        if (address === `${space}/voice/settings` && method === 'PUT') { await route.fulfill({ json: options.chosen }); return; }
        if (address === `${space}/voice/transcribe` && method === 'POST') {
            requests.transcribe.push({ type: request.headers()['content-type'], bytes: request.postDataBuffer()?.length ?? 0 });
            if (scene.transcribe === 'held') {
                await new Promise(resolve => { releaseTranscript = resolve; });
            }
            if (scene.transcribe === 'failed') {
                await problem(502, 'Sample: the speech provider did not answer. Try again.');
                return;
            }
            await route.fulfill({ json: { text: transcript } });
            return;
        }
        if (address === `${space}/voice/speak` && method === 'POST') {
            requests.speak.push(request.postDataJSON());
            await route.fulfill({ contentType: 'audio/wav', body: wav(tone(1.5)) });
            return;
        }
        if (address === `${space}/threads/${threadId}/call`) {
            if (scene.callRefusal) {
                await problem(409, scene.callRefusal);
                return;
            }
            await route.fulfill({ status: 426 });
            return;
        }
        if (address.endsWith('/messages') && method === 'POST') { requests.sent++; await route.fulfill({ status: 500 }); return; }
        let body;
        if (address === '/api/model-provider-kinds') {
            body = [
                { kind: 'venice', displayName: 'Venice', keyRule: 'required', addressRule: 'fixed', addressHint: null, keyHint: 'Made in your Venice account, under API keys.', reportsCapabilities: true, namedProviders: [], signIn: false },
                { kind: 'openai_compatible', displayName: 'Compatible endpoint', keyRule: 'optional', addressRule: 'required', addressHint: 'Any server that speaks the chat completions API.', keyHint: 'Leave this empty if the server needs no key.', reportsCapabilities: false, namedProviders: [], signIn: false },
            ];
        }
        else if (address.endsWith('/model-connections')) { body = [connection]; }
        else if (address.endsWith(`/model-connections/${connectionId}/models`)) { body = []; }
        else if (address.endsWith('/bots')) { body = [bot]; }
        else if (address.endsWith(`/bots/${botId}`)) { body = bot; }
        else if (address.endsWith(`/bots/${botId}/notify`)) { body = { notifies: false }; }
        else if (address.endsWith(`/bots/${botId}/sandbox`)) { body = { available: false, hasSandbox: false, desktop: false, state: 'none' }; }
        else if (address.endsWith(`/bots/${botId}/threads`)) { body = [thread]; }
        else if (address.endsWith('/main-thread') || address.endsWith(`/threads/${threadId}`)) { body = thread; }
        else if (address === `${space}/threads/${threadId}/models`) { body = { botModel: bot.model, botModelName: bot.model, chosen: null, models: [{ model: bot.model, name: bot.model }] }; }
        else if (address.endsWith('/summary')) { body = { text: null }; }
        else if (address.endsWith('/seen')) { await route.fulfill({ status: 204 }); return; }
        else if (address.endsWith('/stream')) { await route.fulfill({ contentType: 'text/event-stream', body: ': connected\n\n' }); return; }
        else if (address.endsWith('/attachment-rules')) { body = { maxFileBytes: 26214400, maxFilesPerMessage: 10, modelReadsImages: true, hasSandbox: false }; }
        else if (address.endsWith('/messages')) { body = [reply]; }
        else if (address.endsWith('/tool-events')) { body = []; }
        else { await route.fulfill({ status: 404, body: '' }); return; }
        await route.fulfill({ json: body });
    });

    const button = name => page.getByRole('button', { name, exact: true });
    // The app is drawn on a canvas under its accessible elements, so a press is a real pointer press where the
    // button's accessible box stands: the browser counts it as the person's own, which sound needs.
    const press = async name => {
        const target = button(name);
        await target.waitFor({ state: 'attached', timeout: 15000 });
        // The box is read once it has stopped moving: a part just laid out again is placed a moment later.
        let box = await target.boundingBox();
        for (let tries = 0; tries < 20; tries++) {
            await page.waitForTimeout(120);
            const next = await target.boundingBox();
            const settled = box && next && box.x === next.x && box.y === next.y;
            box = next;
            if (settled) {
                break;
            }
        }
        assert(box, `${theme} ${width}: "${name}" has no place on the page`);
        if (process.env.DROVER_VOICE_LOG) {
            process.stdout.write(`  press "${name}" at ${Math.round(box.x + (box.width / 2))}, ${Math.round(box.y + (box.height / 2))}\n`);
        }
        await page.mouse.click(box.x + (box.width / 2), box.y + (box.height / 2));
    };
    const field = page.getByRole('textbox', { name: 'Message to Sample bot', exact: true });
    const snap = async name => {
        await page.waitForTimeout(350);
        await page.screenshot({ path: path.join(pictures, `${name}-${width}-${theme}.png`) });
    };
    const open = async address => {
        await page.goto(`${origin}${address}`);
        await page.locator('.uno-loader').waitFor({ state: 'hidden', timeout: 60000 });
    };
    const said = words => shown(page, words).first().waitFor({ state: 'attached', timeout: 15000 });
    // A toast is one element that reads its kind and then its sentence. It is dismissed before the check goes on,
    // as a person would, since it stands over the corner the composer's buttons are in.
    const toast = words => page.getByText(words, { exact: false }).first();
    const told = async words => {
        await toast(words).waitFor({ state: 'attached', timeout: 15000 });
    };
    const dismiss = async words => {
        await button('Dismiss').first().dispatchEvent('click');
        await toast(words).waitFor({ state: 'detached', timeout: 5000 });
    };
    const until = async (condition, what) => {
        const deadline = Date.now() + 15000;
        while (!(await condition())) {
            assert(Date.now() < deadline, `${theme} ${width}: timed out waiting for ${what}`);
            await page.waitForTimeout(50);
        }
    };

    try {
        await steps();
    } catch (failure) {
        // What the screen showed where the check stopped, beside the pictures of the states it reached.
        await page.screenshot({ path: path.join(pictures, `stopped-${width}-${theme}.png`) }).catch(() => { });
        throw failure;
    }

    progress(`closing the ${width} ${theme} context`);
    await context.close();

    async function steps() {
    // Recording a message.
    progress(`composer ${width} ${theme}`);
    await open(`/talk/${botId}`);
    await field.waitFor({ state: 'attached', timeout: 60000 });
    await button('Record a message').waitFor({ state: 'attached', timeout: 15000 });
    await button('Call').waitFor({ state: 'attached' });
    await button('Read aloud').waitFor({ state: 'attached' });
    await snap('composer-idle');

    await press('Record a message');
    await button('Stop recording').waitFor({ state: 'attached', timeout: 15000 });
    await button('Discard recording').waitFor({ state: 'attached' });
    await said('0:01');
    await snap('composer-recording');

    scene.transcribe = 'held';
    await press('Stop recording');
    await said('Writing it down');
    await until(() => releaseTranscript !== null, 'the recording to reach the server');
    await snap('composer-writing');
    releaseTranscript();
    scene.transcribe = 'ok';
    await until(async () => await field.inputValue() === transcript, 'the words in the message field');
    assert.equal(requests.transcribe.length, 1);
    assert.match(requests.transcribe[0].type, /^audio\/(webm|mp4|ogg)$/, 'The recording names its audio format.');
    assert(requests.transcribe[0].bytes > 200, `The recording holds sound (${requests.transcribe[0].bytes} bytes).`);
    assert.equal(requests.sent, 0, 'The words of a recording are never sent by themselves.');
    await button('Record a message').waitFor({ state: 'attached' });
    await snap('composer-words');

    // The words are the person's to change.
    await field.focus();
    await page.keyboard.press('End');
    await page.keyboard.type(' Edited.');
    await until(async () => await field.inputValue() === `${transcript} Edited.`, 'the edited words');

    // Discarding sends nothing.
    await press('Record a message');
    await button('Discard recording').waitFor({ state: 'attached', timeout: 15000 });
    await press('Discard recording');
    await button('Record a message').waitFor({ state: 'attached' });
    assert.equal(await button('Discard recording').count(), 0);
    assert.equal(requests.transcribe.length, 1, 'A discarded recording is not sent.');

    // The provider fails: its sentence is shown, and the field keeps what it had.
    scene.transcribe = 'failed';
    await press('Record a message');
    await button('Stop recording').waitFor({ state: 'attached', timeout: 15000 });
    await page.waitForTimeout(400);
    await press('Stop recording');
    await told('Sample: the speech provider did not answer. Try again.');
    assert.equal(await field.inputValue(), `${transcript} Edited.`);
    await snap('composer-failure');
    await dismiss('Sample: the speech provider did not answer. Try again.');
    scene.transcribe = 'ok';

    // Reading a reply aloud.
    progress(`read aloud ${width} ${theme}`);
    await snap('reply-speaker');
    await press('Read aloud');
    await button('Stop reading').waitFor({ state: 'attached', timeout: 15000 });
    await until(() => requests.speak.length > 0, 'the reply to be asked for');
    await snap('reply-reading');
    assert.deepEqual(requests.speak[0], { messageId: replyId, text: null, voice: null, format: 'wav' });
    // It goes back to the speaker by itself only once the sound has been played to its end.
    await button('Read aloud').waitFor({ state: 'attached', timeout: 15000 });
    await press('Read aloud');
    await button('Stop reading').waitFor({ state: 'attached', timeout: 15000 });
    await press('Stop reading');
    await button('Read aloud').waitFor({ state: 'attached', timeout: 5000 });

    // A live call, scripted.
    progress(`call ${width} ${theme}`);
    const state = word => page.getByRole('heading', { name: word, exact: true }).waitFor({ state: 'attached', timeout: 15000 });
    const both = async name => {
        await snap(`call-${name}`);
        await press('Shrink the call');
        await button('Expand the call').waitFor({ state: 'attached' });
        await snap(`call-${name}-bar`);
        await press('Expand the call');
        await button('Shrink the call').waitFor({ state: 'attached' });
    };
    const tell = frame => call.line.send(JSON.stringify(frame));

    await press('Call');
    await state('Connecting');
    await until(() => call.line !== null, 'the call socket');
    await both('connecting');
    tell({ type: 'ready', model: 'sample-realtime', voice: 'sample-voice' });
    await state('Listening');
    for (const name of ['Mute', 'Show words', 'Hang up', 'Shrink the call']) {
        await button(name).waitFor({ state: 'attached' });
    }
    await until(() => call.audioBytes > 24000, 'half a second of the microphone to reach the call');
    assert.equal(call.odd, 0, 'Every piece of the microphone is whole 16-bit samples.');
    assert(call.loudest > 500, `The fake microphone's beep is in what was sent (loudest sample ${call.loudest}).`);
    await both('listening');

    tell({ type: 'person_started' });
    tell({ type: 'person_said', text: 'Sample question about the harbor office.' });
    await state('Thinking');
    await both('thinking');

    tell({ type: 'bot_saying', text: 'Sample answer: it opens at nine' });
    call.line.send(tone(6));
    await state('Speaking');
    await both('speaking');
    await press('Show words');
    await said('You said: Sample question about the harbor office.');
    await said('Sample bot said: Sample answer: it opens at nine');
    await snap('call-words');
    await press('Hide words');

    // Talking over the bot: its voice stops and the server is told how much of the six seconds was heard.
    const before = call.frames.length;
    tell({ type: 'person_started' });
    await until(() => call.frames.length > before, 'the interruption');
    const interrupt = call.frames[before];
    assert.equal(interrupt.type, 'interrupt');
    assert(interrupt.heardMs > 300 && interrupt.heardMs < 5900, `The interruption says how much was heard (${interrupt.heardMs} ms).`);
    await state('Listening');
    tell({ type: 'bot_said', text: 'Sample answer: it opens' });

    // Muted, nothing of the microphone is sent.
    await press('Mute');
    await state('Muted');
    await page.waitForTimeout(300);
    const whileMuted = call.audioBytes;
    await page.waitForTimeout(500);
    assert.equal(call.audioBytes, whileMuted, 'A muted microphone sends nothing.');
    await both('muted');
    await press('Unmute');
    await state('Listening');
    await until(() => call.audioBytes > whileMuted, 'the microphone to be sent again');

    await press('Hang up');
    await state('Call ended');
    assert.equal(call.frames.at(-1).type, 'hangup');
    await snap('call-ended');
    await press('Close');
    await until(async () => await page.getByRole('heading', { name: 'Call ended', exact: true }).count() === 0, 'the call surface to close');

    // Without a model that can hold a call, the press says what is missing and opens nothing.
    progress(`call without a model ${width} ${theme}`);
    scene.options = 'noLive';
    scene.callRefusal = 'Calls need a live voice model or a speech model.';
    call.line = null;
    await open(`/talk/${botId}`);
    await button('Call').waitFor({ state: 'attached', timeout: 60000 });
    await press('Call');
    await said('Calls need a live voice model or a speech model.');
    await page.getByRole('link', { name: 'Connect one', exact: true }).waitFor({ state: 'attached' });
    assert.equal(call.line, null, 'A call the server would not take opens no socket.');
    await snap('call-needs-model');
    scene.callRefusal = null;

    // A microphone the browser refuses: the sentence says where to allow it.
    progress(`refused microphone ${width} ${theme}`);
    await page.evaluate(() => {
        navigator.mediaDevices.getUserMedia = () => Promise.reject(Object.assign(new Error('Sample refusal'), { name: 'NotAllowedError' }));
    });
    await press('Record a message');
    await told("Drover is not allowed to use the microphone. Allow it for this site in the browser's site settings, then try again.");
    await snap('composer-refused');

    // The Voice page.
    progress(`voice page ${width} ${theme}`);
    scene.options = 'all';
    await open('/voice');
    await page.getByRole('heading', { name: 'Voice', exact: true }).waitFor({ state: 'attached', timeout: 60000 });
    for (const name of ['Listens with', 'Speaks with', 'Live calls with']) {
        await page.getByRole('combobox', { name, exact: true }).waitFor({ state: 'attached', timeout: 15000 });
    }
    await said('Automatic: sample-whisper, Sample speech (On your network)');
    await snap('voice-page-automatic');
    const spoken = requests.speak.length;
    await press('Play a sample');
    await button('Stop the sample').waitFor({ state: 'attached', timeout: 15000 });
    await until(() => requests.speak.length > spoken, 'the sample to be asked for');
    assert.deepEqual(requests.speak.at(-1), { messageId: null, text: 'This is a sample of this voice.', voice: 'sample-voice', format: 'wav' });
    await button('Play a sample').waitFor({ state: 'attached', timeout: 15000 });

    scene.options = 'chosen';
    await open('/voice');
    await said('sample-tts, Sample speech');
    await snap('voice-page-chosen');

    scene.role = 'member';
    await open('/voice');
    await said('An owner or admin of the space can change these.');
    await snap('voice-page-member');
    scene.role = 'owner';

    scene.options = 'nothing';
    await open('/voice');
    await said('Voice needs a speech model.');
    await page.getByRole('link', { name: 'Connect one', exact: true }).waitFor({ state: 'attached' });
    await snap('voice-page-nothing');

    // With nothing connected, no control of voice exists.
    await open(`/talk/${botId}`);
    await field.waitFor({ state: 'attached', timeout: 60000 });
    await page.waitForTimeout(1000);
    for (const name of ['Record a message', 'Call', 'Read aloud']) {
        assert.equal(await button(name).count(), 0, `With nothing connected there is no "${name}" button.`);
    }

    }
}

// Drawn words belong to a semantic element: its accessible name, or its own content when it is standalone text.
function shown(page, words) {
    const named = `[id^="uno-semantics-"][aria-label="${words}"]`;
    const drawn = `[id^="uno-semantics-"][data-uno-standalone-text="true"]:text-is("${words}")`;
    return page.locator(`${named}, ${drawn}`);
}
