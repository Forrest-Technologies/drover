import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const adapter = await fs.readFile(
    path.join(repository, 'src/Drover.Native/Platforms/WebAssembly/WasmScripts/Accessibility.js'),
    'utf8');

function makeEnvironment(bridgeReady = true, semanticReady = true) {
    class Element {
        constructor(id = '') {
            this.id = id;
        }

        matches() {
            return false;
        }

        querySelectorAll() {
            return [];
        }
    }

    class HTMLInputElement extends Element {
        constructor(id, value = '', selectionStart = 0, selectionEnd = selectionStart, selectionDirection = 'none') {
            super(id);
            this.value = value;
            this.selectionStart = selectionStart;
            this.selectionEnd = selectionEnd;
            this.selectionDirection = selectionDirection;
        }

        setSelectionRange(start, end, direction = 'none') {
            this.selectionStart = start;
            this.selectionEnd = end;
            this.selectionDirection = direction;
        }
    }

    class HTMLTextAreaElement extends HTMLInputElement {
    }

    class MutationObserver {
        disconnect() {
        }

        observe() {
        }
    }

    const forwarded = [];
    const originalCalls = [];
    const logged = [];
    const listeners = new Map();
    const elements = new Map();
    const semanticCalls = [];
    const bridge = {
        waitingAsyncOnSelectionChange: true,
        isInSelectionChange: false,
        nextSelectionStart: 12,
        nextSelectionEnd: 12,
        nextSelectionDirection: 'none',
        _exports: bridgeReady
            ? { OnSelectionChanged: (start, length) => forwarded.push([start, length]) }
            : undefined,
    };
    const document = {
        activeElement: null,
        addEventListener(type, listener, options) {
            const registered = listeners.get(type) ?? [];
            registered.push({ listener, options });
            listeners.set(type, registered);
        },
        getElementById(id) {
            return elements.get(id) ?? null;
        },
        onselectionchange(event) {
            originalCalls.push({ receiver: this, event });
            if (bridge.waitingAsyncOnSelectionChange && document.activeElement) {
                document.activeElement.setSelectionRange(
                    bridge.nextSelectionStart,
                    bridge.nextSelectionEnd,
                    bridge.nextSelectionDirection);
                bridge.waitingAsyncOnSelectionChange = false;
            }
        },
    };
    const semanticElements = {
        updateResult: { status: 'updated' },
        updateTextBoxValue(handle, value, start, end, backward) {
            semanticCalls.push({
                receiver: this,
                arguments: [handle, value, start, end, backward],
            });
            const input = elements.get(`uno-semantics-${handle}`);
            if (input && start >= 0 && end >= 0) {
                input.value = value;
                input.setSelectionRange(start, end, backward ? 'backward' : 'forward');
            }
            return this.updateResult;
        },
    };
    const runtime = {
        Accessibility: {
            managedDisableAccessibility() {
            },
            managedEnableAccessibility() {
            },
        },
        BrowserInvisibleTextBoxViewExtension: bridge,
        SemanticElements: semanticReady ? semanticElements : undefined,
    };
    const sandbox = {
        console: {
            debug: (...values) => logged.push(values),
            error: (...values) => logged.push(values),
            info: (...values) => logged.push(values),
            log: (...values) => logged.push(values),
            warn: (...values) => logged.push(values),
        },
        document,
        Element,
        HTMLInputElement,
        HTMLTextAreaElement,
        MutationObserver,
        Uno: {
            UI: {
                Runtime: runtime,
            },
        },
    };
    vm.runInContext(adapter, vm.createContext(sandbox), { filename: 'Accessibility.js' });

    return {
        bridge,
        document,
        focus(element) {
            elements.set(element.id, element);
            document.activeElement = element;
            for (const { listener } of listeners.get('focusin') ?? []) {
                listener.call(document, { target: element });
            }
        },
        forwarded,
        HTMLInputElement,
        HTMLTextAreaElement,
        logged,
        listeners,
        originalCalls,
        runtime,
        semanticCalls,
        semanticElements,
    };
}

function semanticTextarea(environment, start, end = start, direction = 'none') {
    return new environment.HTMLTextAreaElement(
        'uno-semantics-7001',
        'private text that must not be logged',
        start,
        end,
        direction);
}

function runSelectionChange(environment, receiver = environment.document, event = { type: 'selectionchange' }) {
    return environment.document.onselectionchange.call(receiver, event);
}

function runSelect(environment, target) {
    const event = { type: 'select', target };
    for (const { listener } of environment.listeners.get('select') ?? []) {
        listener.call(environment.document, event);
    }
}

const tests = [
    ['document selection ignores a stale shared-input restore for a semantic field', () => {
        const environment = makeEnvironment();
        const input = new environment.HTMLTextAreaElement(
            'uno-semantics-7001',
            'Keep the typed draft.',
            21);
        environment.focus(input);

        runSelectionChange(environment);

        assert.deepEqual(environment.forwarded, []);
        assert.equal(input.selectionStart, 21);
        assert.equal(input.selectionEnd, 21);
        assert.equal(environment.bridge.waitingAsyncOnSelectionChange, false);
        assert.equal(environment.bridge.isInSelectionChange, false);
        assert.equal(environment.originalCalls.length, 0);
    }],
    ['a select event forwards the focused semantic field selection', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 21);
        environment.focus(input);

        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, [[21, 0]]);
        assert.equal(environment.bridge.isInSelectionChange, false);
    }],
    ['a stale select event from another semantic field is ignored', () => {
        const environment = makeEnvironment();
        const stale = semanticTextarea(environment, 12);
        const active = new environment.HTMLTextAreaElement('uno-semantics-7002', 'active field', 3);
        environment.focus(active);

        runSelect(environment, stale);

        assert.deepEqual(environment.forwarded, []);
    }],
    ['the semantic update wrapper preserves receiver arguments and return value', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 0);
        const receiver = { updateResult: { status: 'custom result' } };
        environment.focus(input);

        const result = environment.runtime.SemanticElements.updateTextBoxValue.call(
            receiver,
            7001,
            'changed value',
            2,
            5,
            true);

        assert.equal(result, receiver.updateResult);
        assert.equal(environment.semanticCalls[0].receiver, receiver);
        assert.deepEqual(
            environment.semanticCalls[0].arguments,
            [7001, 'changed value', 2, 5, true]);
    }],
    ['a managed semantic selection echo is ignored', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 0);
        environment.focus(input);

        environment.runtime.SemanticElements.updateTextBoxValue(7001, input.value, 3, 7, false);
        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, []);
    }],
    ['a genuine semantic selection after a managed update is forwarded', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 0);
        environment.focus(input);
        environment.runtime.SemanticElements.updateTextBoxValue(7001, input.value, 3, 7, false);

        input.setSelectionRange(4, 9, 'forward');
        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, [[4, 5]]);
    }],
    ['managed semantic selections remain independent per field', () => {
        const environment = makeEnvironment();
        const first = semanticTextarea(environment, 0);
        const second = new environment.HTMLTextAreaElement('uno-semantics-7002', 'second field', 0);
        environment.focus(first);
        environment.runtime.SemanticElements.updateTextBoxValue(7001, first.value, 2, 4, false);
        environment.focus(second);
        environment.runtime.SemanticElements.updateTextBoxValue(7002, second.value, 5, 8, true);

        runSelect(environment, second);
        second.setSelectionRange(3, 7, 'backward');
        runSelect(environment, second);
        environment.focus(first);
        runSelect(environment, first);

        assert.deepEqual(environment.forwarded, [[7, -4]]);
    }],
    ['a negative managed selection sentinel releases earlier metadata', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 0);
        environment.focus(input);
        environment.runtime.SemanticElements.updateTextBoxValue(7001, input.value, 2, 6, false);

        environment.runtime.SemanticElements.updateTextBoxValue(7001, input.value, -1, -1, false);
        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, [[2, 4]]);
    }],
    ['semantic update tracking installs on focus when the runtime initializes late', () => {
        const environment = makeEnvironment(true, false);
        const input = semanticTextarea(environment, 0);
        environment.runtime.SemanticElements = environment.semanticElements;
        environment.focus(input);

        environment.runtime.SemanticElements.updateTextBoxValue(7001, input.value, 1, 5, false);
        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, []);
    }],
    ['backward semantic selection keeps its direction', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 5, 11, 'backward');
        environment.focus(input);

        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, [[11, -6]]);
        assert.equal(input.selectionStart, 5);
        assert.equal(input.selectionEnd, 11);
    }],
    ['a semantic input uses the same field-scoped bridge', () => {
        const environment = makeEnvironment();
        const input = new environment.HTMLInputElement('uno-semantics-7002', 'password value', 3, 8);
        environment.focus(input);

        runSelect(environment, input);

        assert.deepEqual(environment.forwarded, [[3, 5]]);
        assert.equal(environment.originalCalls.length, 0);
    }],
    ['the shared hidden input keeps the platform handler', () => {
        const environment = makeEnvironment();
        const input = new environment.HTMLTextAreaElement('uno-input', 'shared input', 21);
        const receiver = { kind: 'document receiver' };
        const event = { type: 'selectionchange', source: 'shared' };
        environment.focus(input);

        runSelectionChange(environment, receiver, event);

        assert.equal(environment.originalCalls.length, 1);
        assert.equal(environment.originalCalls[0].receiver, receiver);
        assert.equal(environment.originalCalls[0].event, event);
        assert.equal(input.selectionStart, 12);
        assert.equal(input.selectionEnd, 12);
        assert.deepEqual(environment.forwarded, []);
    }],
    ['an unrelated input keeps the platform handler', () => {
        const environment = makeEnvironment();
        const input = new environment.HTMLInputElement('search', 'search', 6);
        environment.focus(input);

        runSelectionChange(environment);

        assert.equal(environment.originalCalls.length, 1);
        assert.deepEqual(environment.forwarded, []);
    }],
    ['repeated focus does not stack wrappers', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 8);
        const managedUpdate = environment.runtime.SemanticElements.updateTextBoxValue;
        environment.focus(input);
        const installed = environment.document.onselectionchange;
        const selectListeners = environment.listeners.get('select');
        environment.focus(input);
        environment.focus(input);

        assert.equal(environment.document.onselectionchange, installed);
        assert.equal(environment.runtime.SemanticElements.updateTextBoxValue, managedUpdate);
        assert.equal(environment.listeners.get('select'), selectListeners);
        assert.equal(selectListeners.length, 1);
        assert.equal(selectListeners[0].options, true);
        runSelect(environment, input);
        assert.deepEqual(environment.forwarded, [[8, 0]]);
    }],
    ['a bridge initialized after startup installs on a later focus', () => {
        const environment = makeEnvironment(false);
        const input = semanticTextarea(environment, 9);
        const original = environment.document.onselectionchange;
        environment.focus(input);
        assert.equal(environment.document.onselectionchange, original);

        environment.bridge._exports = {
            OnSelectionChanged: (start, length) => environment.forwarded.push([start, length]),
        };
        environment.focus(input);

        assert.notEqual(environment.document.onselectionchange, original);
        runSelect(environment, input);
        assert.deepEqual(environment.forwarded, [[9, 0]]);
    }],
    ['a bridge absent on the first focus installs when it appears', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 10);
        const original = environment.document.onselectionchange;
        delete environment.runtime.BrowserInvisibleTextBoxViewExtension;
        environment.focus(input);
        assert.equal(environment.document.onselectionchange, original);

        environment.runtime.BrowserInvisibleTextBoxViewExtension = environment.bridge;
        environment.focus(input);

        assert.notEqual(environment.document.onselectionchange, original);
        runSelect(environment, input);
        assert.deepEqual(environment.forwarded, [[10, 0]]);
    }],
    ['a failed managed export always releases the selection guard', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 7);
        environment.bridge._exports.OnSelectionChanged = () => {
            throw new Error('managed selection failed');
        };
        environment.focus(input);

        assert.throws(() => runSelect(environment, input), /managed selection failed/);
        assert.equal(environment.bridge.isInSelectionChange, false);
    }],
    ['selection forwarding does not log text', () => {
        const environment = makeEnvironment();
        const input = semanticTextarea(environment, 4);
        environment.focus(input);

        runSelect(environment, input);

        assert.deepEqual(environment.logged, []);
    }],
];

for (const [name, behavior] of tests) {
    test(name, behavior);
}
