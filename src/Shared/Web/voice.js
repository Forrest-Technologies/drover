// The microphone and the loudspeaker of a browser, through ordinary browser APIs. No eval, no inline script, and
// the audio worklet is loaded from the app's own origin, so all of it runs under the server's content security policy.
//
// Sound is held in memory only: a recording until the app collects it, what the microphone hears until the app
// takes it, what is to be played until it has been. Nothing is written to storage and nothing is logged.

// What a live call carries both ways: 16-bit samples, one channel, this many a second.
const callRate = 24000;

// The formats a recorded message may be sent in, in the order a browser is asked for them.
const recordingTypes = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus'];

const microphone = { audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true, channelCount: 1 } };

let recording = null;
let recorded = null;
let listening = null;
let heardPieces = [];
const outputs = new Map();
let lastOutput = 0;

// Why the microphone could not be opened, as one of the words the app knows.
function refusal(error) {
    switch (error && error.name) {
        case 'NotAllowedError':
        case 'SecurityError':
            return 'refused';
        case 'NotFoundError':
        case 'OverconstrainedError':
            return 'none';
        default:
            return 'failed';
    }
}

// A browser starts a context asleep until the person has pressed something on the page. They have, to get here;
// the answer is not waited for, so a browser that still says no leaves silence and never a control that hangs.
function wake(context) {
    if (context.state === 'suspended') {
        context.resume().catch(() => { });
    }
}

async function openMicrophone() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw Object.assign(new Error('This page may not use the microphone.'), { name: 'NotFoundError' });
    }

    return await navigator.mediaDevices.getUserMedia(microphone);
}

function release(stream) {
    for (const track of stream.getTracks()) {
        track.stop();
    }
}

// Starts recording a message. Answers 'ok', or why the microphone could not be used.
export async function startRecording() {
    discardRecording();
    let stream;
    try {
        stream = await openMicrophone();
    } catch (error) {
        return refusal(error);
    }

    try {
        const type = recordingTypes.find(candidate => MediaRecorder.isTypeSupported(candidate));
        const recorder = type ? new MediaRecorder(stream, { mimeType: type }) : new MediaRecorder(stream);
        const pieces = [];
        recorder.addEventListener('dataavailable', event => pieces.push(event.data));
        recorder.start();
        recording = { stream, recorder, pieces };
        return 'ok';
    } catch {
        release(stream);
        return 'failed';
    }
}

// Ends the recording. Answers the media type of what was recorded, which the app then collects, or '' when
// nothing was being recorded.
export async function finishRecording() {
    const current = recording;
    recording = null;
    if (!current) {
        return '';
    }

    const { stream, recorder, pieces } = current;
    try {
        if (recorder.state !== 'inactive') {
            await new Promise((resolve, reject) => {
                recorder.addEventListener('stop', resolve, { once: true });
                recorder.addEventListener('error', () => reject(new Error('The recorder failed.')), { once: true });
                recorder.stop();
            });
        }
    } finally {
        release(stream);
    }

    const type = (recorder.mimeType || 'audio/webm').split(';')[0];
    recorded = new Uint8Array(await new Blob(pieces).arrayBuffer());
    return type;
}

// Hands over the finished recording, once.
export function takeRecording() {
    const bytes = recorded || new Uint8Array(0);
    recorded = null;
    return bytes;
}

// Throws away a recording under way, or one not collected.
export function discardRecording() {
    recorded = null;
    const current = recording;
    recording = null;
    if (current) {
        try {
            if (current.recorder.state !== 'inactive') {
                current.recorder.stop();
            }
        } finally {
            release(current.stream);
        }
    }
}

// Turns samples at the microphone's own rate into the sound a call carries. Each output sample is the average of
// the input samples it covers, which also keeps out what the lower rate cannot hold. What does not fill a sample
// yet waits for the next piece.
function resampler(fromRate) {
    const step = fromRate / callRate;
    let tail = new Float32Array(0);
    let offset = 0;
    return input => {
        const data = new Float32Array(tail.length + input.length);
        data.set(tail);
        data.set(input, tail.length);
        const count = Math.max(0, Math.floor((data.length - offset) / step));
        const out = new Int16Array(count);
        for (let index = 0; index < count; index++) {
            const start = offset + index * step;
            const from = Math.floor(start);
            const to = Math.min(data.length, Math.max(from + 1, Math.ceil(start + step)));
            let sum = 0;
            for (let at = from; at < to; at++) {
                sum += data[at];
            }

            const value = Math.max(-1, Math.min(1, sum / (to - from)));
            out[index] = value < 0 ? value * 0x8000 : value * 0x7fff;
        }

        const used = offset + count * step;
        const whole = Math.min(data.length, Math.floor(used));
        tail = data.slice(whole);
        offset = used - whole;
        return out;
    };
}

// Starts listening for a call. Each time more was heard, `heard` is told how many bytes wait, and the app takes
// them. Answers 'ok', or why the microphone could not be used.
export async function startListening(heard) {
    await stopListening();
    let stream;
    try {
        stream = await openMicrophone();
    } catch (error) {
        return refusal(error);
    }

    let context;
    try {
        context = new AudioContext();
        wake(context);
        const source = context.createMediaStreamSource(stream);
        const convert = resampler(context.sampleRate);
        const take = samples => {
            const converted = convert(samples);
            if (converted.length > 0) {
                heardPieces.push(new Uint8Array(converted.buffer));
                heard(heardPieces.reduce((total, piece) => total + piece.length, 0));
            }
        };

        // Nothing is played: the microphone only has to be pulled through the graph.
        const silence = context.createGain();
        silence.gain.value = 0;
        silence.connect(context.destination);

        let node;
        if (context.audioWorklet) {
            await context.audioWorklet.addModule(new URL('voice-worklet.js', import.meta.url));
            node = new AudioWorkletNode(context, 'drover-capture', { numberOfInputs: 1, numberOfOutputs: 1, channelCount: 1 });
            node.port.onmessage = event => take(event.data);
        } else {
            node = context.createScriptProcessor(2048, 1, 1);
            node.onaudioprocess = event => take(event.inputBuffer.getChannelData(0));
        }

        source.connect(node);
        node.connect(silence);
        heardPieces = [];
        listening = { stream, context, node };
        return 'ok';
    } catch {
        release(stream);
        if (context) {
            await context.close();
        }

        return 'failed';
    }
}

// Hands over everything heard since the last time, as one run of samples.
export function takeHeard() {
    const pieces = heardPieces;
    heardPieces = [];
    const all = new Uint8Array(pieces.reduce((total, piece) => total + piece.length, 0));
    let at = 0;
    for (const piece of pieces) {
        all.set(piece, at);
        at += piece.length;
    }

    return all;
}

export async function stopListening() {
    const current = listening;
    listening = null;
    heardPieces = [];
    if (current) {
        if (current.node.port) {
            current.node.port.onmessage = null;
        } else {
            current.node.onaudioprocess = null;
        }

        current.node.disconnect();
        release(current.stream);
        await current.context.close();
    }
}

// Opens an output that plays 16-bit samples of one channel at `rate`, one piece after another with no gap.
// `drained` is told the output's id each time everything queued has been played. Answers the id, or 0 when the
// browser cannot play sound.
export async function openOutput(rate, drained) {
    let context;
    try {
        try {
            context = new AudioContext({ sampleRate: rate });
        } catch {
            // A browser that cannot run at this rate plays each piece at its own rate inside its usual one.
            context = new AudioContext();
        }

        wake(context);
    } catch {
        return 0;
    }

    const id = ++lastOutput;
    outputs.set(id, { context, rate, drained, sources: new Set(), next: 0, scheduled: 0 });
    return id;
}

export function queue(id, bytes) {
    const output = outputs.get(id);
    if (!output || bytes.length < 2) {
        return;
    }

    const samples = new Int16Array(bytes.buffer, bytes.byteOffset, bytes.length >> 1);
    const buffer = output.context.createBuffer(1, samples.length, output.rate);
    const channel = buffer.getChannelData(0);
    for (let index = 0; index < samples.length; index++) {
        channel[index] = samples[index] / 0x8000;
    }

    const source = output.context.createBufferSource();
    source.buffer = buffer;
    source.connect(output.context.destination);
    const at = Math.max(output.context.currentTime + 0.03, output.next);
    source.start(at);
    output.next = at + buffer.duration;
    output.scheduled += buffer.duration;
    output.sources.add(source);
    source.onended = () => {
        if (output.sources.delete(source) && output.sources.size === 0) {
            output.drained(id);
        }
    };
}

function waiting(output) {
    return Math.max(0, output.next - output.context.currentTime);
}

// Stops at once and drops what was queued. What was not played is taken off the count of what was.
export function flush(id) {
    const output = outputs.get(id);
    if (!output) {
        return;
    }

    output.scheduled -= Math.min(output.scheduled, waiting(output));
    output.next = 0;
    const stopped = [...output.sources];
    output.sources.clear();
    for (const source of stopped) {
        source.onended = null;
        source.stop();
    }
}

// How much has been played since the output was opened, in milliseconds.
export function played(id) {
    const output = outputs.get(id);
    return output ? Math.max(0, Math.round((output.scheduled - waiting(output)) * 1000)) : 0;
}

export function playing(id) {
    const output = outputs.get(id);
    return !!output && output.sources.size > 0;
}

export async function closeOutput(id) {
    const output = outputs.get(id);
    if (output) {
        flush(id);
        outputs.delete(id);
        await output.context.close();
    }
}
