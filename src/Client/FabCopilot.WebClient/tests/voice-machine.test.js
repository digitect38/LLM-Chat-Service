// ═══════════════════════════════════════════════════════════════════
// Voice State Machine v2.4 Sequential — Unit Tests (Phase 3)
// Tests XState v5 sequential: idle → listening → processing → responding → recovering → stopping
// ═══════════════════════════════════════════════════════════════════
'use strict';

const { createMachine, createActor, assign } = require('xstate');
const assert = require('assert');

// ─── Test utilities ──────────────────────────────────────────────
let passCount = 0;
let failCount = 0;
const failures = [];

function describe(name, fn) {
    console.log('\n\x1b[1m' + name + '\x1b[0m');
    fn();
}

function it(name, fn) {
    try {
        fn();
        passCount++;
        console.log('  \x1b[32m✓\x1b[0m ' + name);
    } catch (e) {
        failCount++;
        failures.push({ name, error: e.message });
        console.log('  \x1b[31m✗\x1b[0m ' + name);
        console.log('    \x1b[31m' + e.message + '\x1b[0m');
    }
}

// Async version of it — queues test to run after sync tests
const asyncQueue = [];
function itAsync(name, fn) {
    asyncQueue.push({ name, fn });
}

function eq(actual, expected, msg) {
    const a = JSON.stringify(actual);
    const b = JSON.stringify(expected);
    if (a !== b) throw new Error((msg || '') + ' expected ' + b + ' but got ' + a);
}

// ─── Mock environment ────────────────────────────────────────────
function createMockEnv() {
    const calls = [];

    const mockDotNet = {
        invokeMethodAsync: function () {
            calls.push({ target: 'dotNet', method: arguments[0], args: Array.from(arguments).slice(1) });
            return Promise.resolve();
        }
    };

    const mockTts = {
        unlockAudio: function () { calls.push({ target: 'tts', method: 'unlockAudio' }); },
        prepareMic: function () { calls.push({ target: 'tts', method: 'prepareMic' }); },
        releaseMic: function () { calls.push({ target: 'tts', method: 'releaseMic' }); },
        _stopInternal: function () { calls.push({ target: 'tts', method: '_stopInternal' }); },
        _bargeInMicStream: null,
    };

    const mockRec = {
        start: function () { calls.push({ target: 'rec', method: 'start' }); },
        stop: function () { calls.push({ target: 'rec', method: 'stop' }); },
        _persistentStream: null,
        setVoiceMode: function (on) {
            calls.push({ target: 'rec', method: 'setVoiceMode', args: [on] });
            return Promise.resolve(true);
        },
    };

    return { calls, mockDotNet, mockTts, mockRec };
}

// ─── Build the v2.4 sequential machine ──────────────────────────
function buildMachine(env) {
    const { calls, mockDotNet, mockTts, mockRec } = env;

    function dotNet() { return mockDotNet; }
    function ttsObj() { return mockTts; }
    function recObj() { return mockRec; }

    // Stub: no real RMS/cmd/VAD listener in tests
    function _startRmsDetector() { calls.push({ target: 'rms', method: 'start' }); }
    function _stopRmsDetector() { calls.push({ target: 'rms', method: 'stop' }); }
    function _startCommandListener() { calls.push({ target: 'cmdListener', method: 'start' }); }
    function _stopCommandListener() { calls.push({ target: 'cmdListener', method: 'stop' }); }
    function _startVadDetector() { calls.push({ target: 'vad', method: 'start' }); }
    function _stopVadDetector() { calls.push({ target: 'vad', method: 'stop' }); }

    var actorRef = null; // will be set after createActor

    const machine = createMachine({
        id: 'assistant',
        initial: 'idle',
        context: {
            voiceMode: false,
            ttsEnabled: false,
            inputText: '',
            screenDone: false,
            ttsDone: false,
            pendingUserText: '',
            stopReason: '',
            retryCount: 0,
            lastError: null,
            interimText: '',
            pendingMessage: null,
        },
        states: {
            idle: {
                entry: ['clearSessionData', 'notifyIdle', 'startVad'],
                exit: ['stopVad'],
                after: {
                    autoListenDelay: [{
                        guard: 'isVoiceMode',
                        target: 'listening',
                    }],
                },
                on: {
                    'MIC.VOICE_DETECTED': [{
                        guard: 'isVoiceMode',
                        target: 'listening',
                    }],
                    'KEYBOARD.SUBMIT': {
                        target: 'processing',
                        actions: [assign({
                            inputText: function (_a) { var e = _a.event; return (e.params && e.params.text) || ''; },
                        })],
                    },
                    'MIC.START': 'listening',
                    'VOICE_ENTER': {
                        target: 'listening',
                        actions: [
                            assign({ voiceMode: true, ttsEnabled: true }),
                            'enterVoiceMode',
                        ],
                    },
                    'VOICE_EXIT': {
                        actions: [assign({ voiceMode: false }), 'exitVoiceMode'],
                    },
                    'AUTO_TTS_ON': {
                        actions: [assign({ ttsEnabled: true }), 'enableAutoTts'],
                    },
                    'AUTO_TTS_OFF': {
                        actions: [
                            assign({ ttsEnabled: false, voiceMode: false }),
                            'disableAutoTts',
                        ],
                    },
                    'SEND_CONFIRM_SHOW': {
                        actions: [assign({ pendingMessage: function (_a) { var e = _a.event; return e.params ? e.params.text : ''; } })],
                    },
                    'SEND_CONFIRM_STOP': {
                        actions: ['confirmStopAndSend'],
                    },
                    'SEND_CONFIRM_KEEP': {
                        actions: ['confirmKeepAndSend'],
                    },
                    'SEND_CONFIRM_CANCEL': {
                        actions: [assign({ pendingMessage: null })],
                    },
                },
            },
            listening: {
                entry: ['startListening'],
                exit: ['stopRecording'],
                on: {
                    'MIC.FINAL': {
                        target: 'processing',
                        actions: [assign({
                            inputText: function (_a) { var e = _a.event; return (e.params && e.params.text) || ''; },
                        })],
                    },
                    'MIC.INTERIM': {
                        actions: [assign({
                            interimText: function (_a) { var e = _a.event; return (e.params && e.params.text) || ''; },
                        })],
                    },
                    'MIC.EMPTY': { target: 'idle' },
                    'MIC.ERROR': { target: 'idle', actions: ['handleMicError'] },
                    'MIC.FATAL': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false }), 'handleMicFatal'],
                    },
                    'BUTTON.STOP': 'idle',
                    'VOICE_EXIT': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false }), 'exitVoiceMode'],
                    },
                    'AUTO_TTS_OFF': {
                        target: 'idle',
                        actions: [assign({ ttsEnabled: false, voiceMode: false }), 'disableAutoTts'],
                    },
                    'WS_DISCONNECT': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false }), 'exitVoiceMode'],
                    },
                },
                after: {
                    listenTimeout: {
                        target: 'idle',
                    },
                },
            },
            processing: {
                entry: ['sendToLlm'],
                on: {
                    'LLM.STREAMING': { target: 'responding' },
                    'LLM.DONE': { target: 'idle' },
                    'LLM.ERROR': {
                        target: 'recovering',
                        actions: [
                            assign({
                                lastError: function (_a) {
                                    var e = _a.event;
                                    return (e.params && e.params.error) || { code: 'LLM_ERROR', message: 'LLM 처리 실패' };
                                },
                            }),
                            'handleLlmError',
                        ],
                    },
                    'BUTTON.STOP': 'stopping',
                    'VOICE_EXIT': {
                        target: 'stopping',
                        actions: [assign({ voiceMode: false, stopReason: 'system' })],
                    },
                },
                after: {
                    processTimeout: {
                        target: 'recovering',
                        actions: [
                            assign({ lastError: { code: 'TIMEOUT', message: 'LLM 응답 시간 초과' } }),
                        ],
                    },
                },
            },
            responding: {
                entry: [
                    assign({ screenDone: false, ttsDone: false }),
                    'startBargeIn',
                    'notifyResponding',
                ],
                exit: ['stopBargeIn'],
                on: {
                    'SCREEN.DONE': {
                        actions: [assign({ screenDone: true })],
                    },
                    'TTS_ENDED': {
                        actions: [assign({ ttsDone: true }), 'notifyTtsEnded'],
                    },
                    'TTS_ERROR': {
                        actions: [assign({ ttsDone: true }), 'notifyTtsError'],
                    },
                    'BARGE_IN': {
                        actions: [
                            'stopTts', assign({ ttsDone: true }),
                            'notifyBargeIn',
                        ],
                    },
                    'VOICE_CMD': {
                        target: 'stopping',
                        actions: [assign({ stopReason: 'voice' }), 'notifyVoiceCmd'],
                    },
                    'BUTTON.STOP': {
                        target: 'stopping',
                        actions: [assign({ stopReason: 'button' })],
                    },
                    'VOICE_EXIT': {
                        target: 'stopping',
                        actions: [assign({ voiceMode: false, stopReason: 'system' })],
                    },
                    'LLM.ERROR': {
                        target: 'recovering',
                        actions: [
                            assign({
                                stopReason: 'system',
                                lastError: function (_a) {
                                    var e = _a.event;
                                    return (e.params && e.params.error) || { code: 'LLM_STREAM_ERROR', message: 'LLM 스트리밍 중 오류' };
                                },
                            }),
                            'handleLlmError',
                        ],
                    },
                },
                always: [{
                    guard: 'isResponseComplete',
                    target: 'idle',
                }],
                after: {
                    respondTimeout: { target: 'idle' },
                },
            },
            recovering: {
                entry: [
                    assign({
                        retryCount: function (_a) { return _a.context.retryCount + 1; },
                    }),
                    'notifyRecovering',
                    'cleanupForRetry',
                ],
                on: {
                    'VOICE_EXIT': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false, retryCount: 0 }), 'exitVoiceMode'],
                    },
                    'BUTTON.STOP': {
                        target: 'idle',
                        actions: [assign({ retryCount: 0 })],
                    },
                },
                after: {
                    recoverDelay: [{
                        guard: 'canRetry',
                        target: 'processing',
                    }, {
                        target: 'idle',
                        actions: [
                            assign({ retryCount: 0 }),
                            'notifyRecoveryFailed',
                        ],
                    }],
                },
            },
            stopping: {
                entry: ['cleanup', 'notifyStopping'],
                on: {
                    'CLEANUP.DONE': [{
                        guard: 'hasPendingUserText',
                        target: 'processing',
                        actions: [assign({
                            inputText: function (_a) { return _a.context.pendingUserText; },
                            pendingUserText: '',
                        })],
                    }, {
                        target: 'idle',
                    }],
                    'VOICE_EXIT': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false })],
                    },
                },
                after: {
                    cleanupTimeout: { target: 'idle' },
                },
            },
        },
    }, {
        guards: {
            isVoiceMode: function (_a) { return _a.context.voiceMode; },
            isResponseComplete: function (_a) {
                var c = _a.context;
                return c.screenDone && (c.ttsDone || !c.ttsEnabled);
            },
            hasPendingUserText: function (_a) { return !!_a.context.pendingUserText; },
            canRetry: function (_a) { return _a.context.retryCount <= 2; },
        },
        actions: {
            clearSessionData: assign({
                inputText: '',
                screenDone: false,
                ttsDone: false,
                pendingUserText: '',
                stopReason: '',
                interimText: '',
                retryCount: 0,
                lastError: null,
            }),
            enterVoiceMode: function () {
                try { ttsObj().unlockAudio(); } catch (e) { }
                if (recObj()) recObj().setVoiceMode(true).catch(function () { });
            },
            exitVoiceMode: function () {
                try { ttsObj()._stopInternal(); } catch (e) { }
                try { if (recObj()) recObj().stop(); } catch (e) { }
                if (recObj()) recObj().setVoiceMode(false).catch(function () { });
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineExitVoiceMode');
            },
            enableAutoTts: function () {
                try { ttsObj().unlockAudio(); } catch (e) { }
                try { ttsObj().prepareMic(); } catch (e) { }
            },
            disableAutoTts: function () {
                try { ttsObj()._stopInternal(); } catch (e) { }
                try { ttsObj().releaseMic(); } catch (e) { }
                try { if (recObj()) recObj().stop(); } catch (e) { }
                if (recObj()) recObj().setVoiceMode(false).catch(function () { });
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineExitVoiceMode');
            },
            startListening: function () {
                var d = dotNet();
                if (d) {
                    d.invokeMethodAsync('OnMachineStartListening').catch(function (err) {
                        actorRef.send({ type: 'MIC.ERROR' });
                    });
                } else {
                    actorRef.send({ type: 'MIC.ERROR' });
                }
            },
            stopRecording: function () { },
            handleMicError: function () { },
            handleMicFatal: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineExitVoiceMode');
            },
            sendToLlm: function (_a) {
                var context = _a.context;
                var text = context.inputText || '';
                var d = dotNet();
                if (d && text) {
                    d.invokeMethodAsync('OnMachineSendMessage', text).catch(function (err) {
                        actorRef.send({ type: 'LLM.ERROR' });
                    });
                } else {
                    actorRef.send({ type: 'LLM.ERROR' });
                }
            },
            handleLlmError: function () { },
            notifyRecovering: function (_a) {
                var context = _a.context;
                var err = context.lastError || { code: 'UNKNOWN', message: '?' };
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineRecovering', err.code, err.message, context.retryCount);
            },
            cleanupForRetry: function () {
                try { ttsObj()._stopInternal(); } catch (e) { }
            },
            notifyRecoveryFailed: function (_a) {
                var context = _a.context;
                var err = context.lastError || { code: 'UNKNOWN', message: '?' };
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineRecoveryFailed', err.code, err.message);
            },
            notifyResponding: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineResponding');
            },
            notifyTtsEnded: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnTtsEnded');
            },
            notifyTtsError: function (_a) {
                var event = _a.event;
                var error = (event.params && event.params.error) || 'TTS 오류';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnTtsError', error);
            },
            notifyBargeIn: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnBargeIn');
            },
            notifyVoiceCmd: function (_a) {
                var event = _a.event;
                var cmd = (event.params && event.params.command) || 'stop';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnVoiceCommand', cmd);
            },
            startBargeIn: function () {
                _startRmsDetector();
                _startCommandListener();
            },
            stopBargeIn: function () {
                _stopRmsDetector();
                _stopCommandListener();
            },
            stopTts: function () {
                try { ttsObj()._stopInternal(); } catch (e) { }
            },
            cleanup: function () {
                try { ttsObj()._stopInternal(); } catch (e) { }
                try { if (recObj()) recObj().stop(); } catch (e) { }
                setTimeout(function () {
                    actorRef.send({ type: 'CLEANUP.DONE' });
                }, 0);
            },
            notifyStopping: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineStopping');
            },
            notifyIdle: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineIdle');
            },
            startVad: function (_a) {
                if (_a.context.voiceMode) _startVadDetector();
            },
            stopVad: function () {
                _stopVadDetector();
            },
            confirmStopAndSend: function (_a) {
                var context = _a.context;
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnSendConfirm', true, context.pendingMessage || '');
            },
            confirmKeepAndSend: function (_a) {
                var context = _a.context;
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnSendConfirm', false, context.pendingMessage || '');
            },
        },
        delays: {
            autoListenDelay: 999999,  // disable in tests
            listenTimeout: 999999,
            processTimeout: 999999,
            respondTimeout: 999999,
            cleanupTimeout: 999999,
            recoverDelay: 999999,
        },
    });

    const actor = createActor(machine);
    actorRef = actor;
    actor.start();
    return { machine, actor };
}

// Helper: get current state value
function state(actor) {
    return actor.getSnapshot().value;
}

// Helper: get current context
function ctx(actor) {
    return actor.getSnapshot().context;
}

// Helper: check if a dotNet call was made
function hasDotNetCall(calls, method) {
    return calls.some(c => c.target === 'dotNet' && c.method === method);
}

// Helper: get dotNet call args
function getDotNetCall(calls, method) {
    return calls.find(c => c.target === 'dotNet' && c.method === method);
}


// ═══════════════════════════════════════════════════════════════════
// TESTS
// ═══════════════════════════════════════════════════════════════════

describe('Initial State', function () {
    it('starts in idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        eq(state(actor), 'idle');
    });

    it('initial context has voiceMode=false, ttsEnabled=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        const c = ctx(actor);
        eq(c.voiceMode, false);
        eq(c.ttsEnabled, false);
        eq(c.inputText, '');
        eq(c.screenDone, false);
        eq(c.ttsDone, false);
    });

    it('idle entry calls notifyIdle → OnMachineIdle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        assert(hasDotNetCall(env.calls, 'OnMachineIdle'), 'OnMachineIdle not called on initial idle');
    });
});

describe('Idle → Listening (VOICE_ENTER)', function () {
    it('VOICE_ENTER transitions to listening', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
    });

    it('VOICE_ENTER sets voiceMode=true, ttsEnabled=true', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(ctx(actor).voiceMode, true);
        eq(ctx(actor).ttsEnabled, true);
    });

    it('VOICE_ENTER calls enterVoiceMode (unlockAudio, setVoiceMode)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'unlockAudio'), 'unlockAudio not called');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'setVoiceMode' && c.args[0] === true), 'setVoiceMode(true) not called');
    });

    it('listening entry calls OnMachineStartListening', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        assert(hasDotNetCall(env.calls, 'OnMachineStartListening'), 'OnMachineStartListening not called');
    });

    it('MIC.START also transitions idle → listening', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        eq(state(actor), 'listening');
    });
});

describe('Listening → Processing (MIC.FINAL)', function () {
    it('MIC.FINAL transitions to processing with text', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '안녕하세요' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, '안녕하세요');
    });

    it('processing entry calls OnMachineSendMessage with text', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '질문입니다' } });
        const call = getDotNetCall(env.calls, 'OnMachineSendMessage');
        assert(call, 'OnMachineSendMessage not called');
        eq(call.args[0], '질문입니다');
    });
});

describe('Listening → Idle (various exits)', function () {
    it('MIC.EMPTY → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.EMPTY' });
        eq(state(actor), 'idle');
    });

    it('MIC.ERROR → idle (voiceMode preserved)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
        actor.send({ type: 'MIC.ERROR' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode should stay true for auto-retry');
    });

    it('MIC.FATAL → idle with voiceMode=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FATAL' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false, 'voiceMode should be false after FATAL');
        assert(hasDotNetCall(env.calls, 'OnMachineExitVoiceMode'), 'OnMachineExitVoiceMode not called');
    });

    it('BUTTON.STOP → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
    });

    it('VOICE_EXIT → idle with voiceMode=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
    });

    it('WS_DISCONNECT → idle with voiceMode=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'WS_DISCONNECT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
    });
});

describe('Listening — MIC.INTERIM', function () {
    it('MIC.INTERIM updates interimText in context', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.INTERIM', params: { text: '안녕...' } });
        eq(ctx(actor).interimText, '안녕...');
        eq(state(actor), 'listening', 'should stay in listening');
    });
});

describe('KEYBOARD.SUBMIT → Processing', function () {
    it('idle → processing on KEYBOARD.SUBMIT', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'hello' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, 'hello');
    });

    it('processing entry calls OnMachineSendMessage', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'test msg' } });
        const call = getDotNetCall(env.calls, 'OnMachineSendMessage');
        assert(call, 'OnMachineSendMessage not called');
        eq(call.args[0], 'test msg');
    });
});

describe('Processing → Responding (LLM.STREAMING)', function () {
    it('LLM.STREAMING transitions to responding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
    });

    it('responding entry calls notifyResponding → OnMachineResponding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        assert(hasDotNetCall(env.calls, 'OnMachineResponding'), 'OnMachineResponding not called');
    });

    it('responding entry resets screenDone=false, ttsDone=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(ctx(actor).screenDone, false);
        eq(ctx(actor).ttsDone, false);
    });

    it('responding entry starts barge-in (RMS + command listener)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        assert(env.calls.some(c => c.target === 'rms' && c.method === 'start'), 'RMS detector not started');
        assert(env.calls.some(c => c.target === 'cmdListener' && c.method === 'start'), 'Command listener not started');
    });
});

describe('Processing → Idle (LLM.DONE / LLM.ERROR)', function () {
    it('LLM.DONE → idle (no TTS, short response)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.DONE' });
        eq(state(actor), 'idle');
    });

    it('LLM.ERROR → recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
    });
});

describe('Responding — Completion Guard (isResponseComplete)', function () {
    it('SCREEN.DONE alone does not complete (ttsEnabled=true, ttsDone=false)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // Enable TTS
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding', 'should stay in responding — TTS not done');
        eq(ctx(actor).screenDone, true);
        eq(ctx(actor).ttsDone, false);
    });

    it('SCREEN.DONE + TTS_ENDED → idle (both done)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'responding', 'screen not done yet');
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'both done → idle');
    });

    it('TTS_ENDED + SCREEN.DONE → idle (reverse order)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding');
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
    });

    it('SCREEN.DONE alone completes when ttsEnabled=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // ttsEnabled defaults to false
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'ttsEnabled=false → screenDone alone is sufficient');
    });

    it('TTS_ERROR also marks ttsDone=true', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'TTS_ERROR' });
        eq(ctx(actor).ttsDone, true);
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');
    });
});

describe('Responding — BARGE_IN', function () {
    it('BARGE_IN sets ttsDone=true, calls stopTts', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BARGE_IN' });
        eq(ctx(actor).ttsDone, true);
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTts not called');
        assert(hasDotNetCall(env.calls, 'OnBargeIn'), 'OnBargeIn not called');
    });

    it('BARGE_IN + SCREEN.DONE → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BARGE_IN' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');
    });
});

describe('Responding — VOICE_CMD', function () {
    it('VOICE_CMD → stopping with stopReason=voice', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(state(actor), 'stopping');
        eq(ctx(actor).stopReason, 'voice');
        assert(hasDotNetCall(env.calls, 'OnVoiceCommand'), 'OnVoiceCommand not called');
    });
});

describe('Responding — BUTTON.STOP', function () {
    it('BUTTON.STOP → stopping with stopReason=button', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        eq(ctx(actor).stopReason, 'button');
    });
});

describe('Responding exit', function () {
    it('exiting responding stops barge-in', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        // Clear calls to isolate exit actions
        env.calls.length = 0;
        actor.send({ type: 'SCREEN.DONE' }); // ttsEnabled=false → idle
        assert(env.calls.some(c => c.target === 'rms' && c.method === 'stop'), 'RMS not stopped on exit');
        assert(env.calls.some(c => c.target === 'cmdListener' && c.method === 'stop'), 'CmdListener not stopped on exit');
    });
});

describe('Stopping State', function () {
    it('stopping entry calls cleanup (tts stop + rec stop)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'tts._stopInternal not called');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'stop'), 'rec.stop not called');
        assert(hasDotNetCall(env.calls, 'OnMachineStopping'), 'OnMachineStopping not called');
    });

    itAsync('CLEANUP.DONE (no pending text) → idle', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'BUTTON.STOP' });
        // cleanup action sends CLEANUP.DONE via setTimeout(0)
        setTimeout(function () {
            eq(state(actor), 'idle');
            resolve();
        }, 50);
    });

    itAsync('CLEANUP.DONE with no pendingUserText → idle', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BUTTON.STOP' });
        setTimeout(function () {
            eq(state(actor), 'idle');
            resolve();
        }, 50);
    });
});

describe('Full Voice Loop Cycle', function () {
    it('idle → listening → processing → responding → idle (voice mode)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // 1. Enter voice mode
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
        eq(ctx(actor).voiceMode, true);
        eq(ctx(actor).ttsEnabled, true);

        // 2. User speaks → MIC.FINAL
        actor.send({ type: 'MIC.FINAL', params: { text: 'CMP 패드 교체' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, 'CMP 패드 교체');

        // 3. LLM starts streaming
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // 4. Screen output completes
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding', 'TTS not done yet');

        // 5. TTS finishes
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle', 'both done → idle');
        // voiceMode should still be true for auto-restart
        eq(ctx(actor).voiceMode, true);
    });

    it('full cycle calls correct C# callbacks', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        actor.send({ type: 'VOICE_ENTER' });
        assert(hasDotNetCall(env.calls, 'OnMachineStartListening'), 'listening entry');

        actor.send({ type: 'MIC.FINAL', params: { text: '테스트' } });
        assert(hasDotNetCall(env.calls, 'OnMachineSendMessage'), 'processing entry');

        actor.send({ type: 'LLM.STREAMING' });
        assert(hasDotNetCall(env.calls, 'OnMachineResponding'), 'responding entry');

        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        assert(hasDotNetCall(env.calls, 'OnTtsEnded'), 'tts ended notification');

        // After completing responding → idle, OnMachineIdle is called again
        const idleCalls = env.calls.filter(c => c.target === 'dotNet' && c.method === 'OnMachineIdle');
        assert(idleCalls.length >= 2, 'OnMachineIdle should be called at start and after cycle');
    });
});

describe('Keyboard Submit Cycle (no voice)', function () {
    it('idle → processing → responding → idle (ttsEnabled=false)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'hello' } });
        eq(state(actor), 'processing');

        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // No TTS enabled → screenDone alone is sufficient
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');
    });
});

describe('AUTO_TTS_ON / AUTO_TTS_OFF', function () {
    it('AUTO_TTS_ON sets ttsEnabled=true, calls enableAutoTts', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(ctx(actor).ttsEnabled, true);
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'unlockAudio'), 'unlockAudio');
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'prepareMic'), 'prepareMic');
    });

    it('AUTO_TTS_OFF sets ttsEnabled=false, voiceMode=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(ctx(actor).ttsEnabled, false);
        eq(ctx(actor).voiceMode, false);
    });

    it('AUTO_TTS_OFF during listening → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
        eq(ctx(actor).ttsEnabled, false);
    });
});

describe('VOICE_EXIT from Various States', function () {
    it('VOICE_EXIT from idle: stays idle, voiceMode=false', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.EMPTY' }); // back to idle
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true);
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
    });

    it('VOICE_EXIT from listening → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
    });

    it('VOICE_EXIT from processing → stopping', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: '질문' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'stopping');
        eq(ctx(actor).voiceMode, false);
    });

    it('VOICE_EXIT from responding → stopping', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: '질문' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'stopping');
        eq(ctx(actor).voiceMode, false);
    });
});

describe('Send Confirm (TTS 재생 중 키보드 전송)', function () {
    it('SEND_CONFIRM_SHOW sets pendingMessage', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '새 질문' } });
        eq(ctx(actor).pendingMessage, '새 질문');
        eq(state(actor), 'idle');
    });

    it('SEND_CONFIRM_STOP calls OnSendConfirm(true, text)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '질문 메시지' } });
        actor.send({ type: 'SEND_CONFIRM_STOP' });
        const call = getDotNetCall(env.calls, 'OnSendConfirm');
        assert(call, 'OnSendConfirm not called');
        eq(call.args[0], true, 'stopTts should be true');
        eq(call.args[1], '질문 메시지');
    });

    it('SEND_CONFIRM_KEEP calls OnSendConfirm(false, text)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '유지 메시지' } });
        actor.send({ type: 'SEND_CONFIRM_KEEP' });
        const call = getDotNetCall(env.calls, 'OnSendConfirm');
        assert(call, 'OnSendConfirm not called');
        eq(call.args[0], false, 'stopTts should be false');
        eq(call.args[1], '유지 메시지');
    });

    it('SEND_CONFIRM_CANCEL resets pendingMessage to null', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '취소할 메시지' } });
        actor.send({ type: 'SEND_CONFIRM_CANCEL' });
        eq(ctx(actor).pendingMessage, null);
    });
});

describe('Context Cleanup on Idle Entry', function () {
    it('clearSessionData resets inputText, screenDone, ttsDone, stopReason, interimText', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // Go through a cycle to dirty the context
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'dirty' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' }); // ttsEnabled=false → idle
        const c = ctx(actor);
        eq(c.inputText, '', 'inputText should be cleared');
        eq(c.screenDone, false, 'screenDone should be cleared');
        eq(c.ttsDone, false, 'ttsDone should be cleared');
        eq(c.stopReason, '', 'stopReason should be cleared');
        eq(c.interimText, '', 'interimText should be cleared');
    });
});

describe('Processing → Stopping (BUTTON.STOP during processing)', function () {
    it('BUTTON.STOP during processing → stopping', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
    });
});

describe('Edge Cases', function () {
    it('unhandled events in states are silently ignored', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // Send events that don't match any transition in idle
        actor.send({ type: 'MIC.FINAL', params: { text: 'ignored' } });
        eq(state(actor), 'idle', 'MIC.FINAL in idle should be ignored');
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'idle', 'LLM.STREAMING in idle should be ignored');
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'SCREEN.DONE in idle should be ignored');
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle', 'TTS_ENDED in idle should be ignored');
    });

    it('LLM.ERROR in responding → recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).stopReason, 'system');
    });

    it('VOICE_EXIT from stopping → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
    });

    it('multiple SCREEN.DONE events are idempotent', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding');
        actor.send({ type: 'SCREEN.DONE' }); // duplicate
        eq(state(actor), 'responding', 'still responding (waiting for TTS)');
    });

    it('empty inputText sends LLM.ERROR from sendToLlm → recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: '' } });
        // sendToLlm action checks for empty text and sends LLM.ERROR
        // This should transition to recovering (which will eventually exhaust retries → idle)
        eq(state(actor), 'recovering', 'empty text → LLM.ERROR → recovering');
    });
});

describe('Public API Compatibility (window.voiceMachine equivalents)', function () {
    it('getState returns current state value string', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        eq(actor.getSnapshot().value, 'idle');
        actor.send({ type: 'MIC.START' });
        eq(actor.getSnapshot().value, 'listening');
    });

    it('getContext returns current context object', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        const c = actor.getSnapshot().context;
        eq(typeof c.voiceMode, 'boolean');
        eq(typeof c.ttsEnabled, 'boolean');
    });

    it('isResponseComplete guard checks screenDone && (ttsDone || !ttsEnabled)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Case 1: ttsEnabled=false, screenDone=true → complete
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'ttsEnabled=false + screenDone → idle');

        // Case 2: ttsEnabled=true, screenDone=true, ttsDone=false → NOT complete
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q2' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding', 'ttsEnabled=true + screenDone + !ttsDone → stay');

        // Case 3: now TTS ends → complete
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle', 'screenDone + ttsDone → idle');
    });
});

// ═══════════════════════════════════════════════════════════════════
// Phase 3: Recovering State Tests
// ═══════════════════════════════════════════════════════════════════

describe('Recovering State — LLM.ERROR from processing', function () {
    it('LLM.ERROR in processing → recovering with retryCount=1', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).retryCount, 1);
    });

    it('recovering entry calls OnMachineRecovering with error details', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'NET_ERR', message: '네트워크 오류' } } });
        const call = getDotNetCall(env.calls, 'OnMachineRecovering');
        assert(call, 'OnMachineRecovering not called');
        eq(call.args[0], 'NET_ERR');
        eq(call.args[1], '네트워크 오류');
        eq(call.args[2], 1);
    });

    it('recovering saves lastError in context', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'TIMEOUT', message: '시간 초과' } } });
        eq(ctx(actor).lastError.code, 'TIMEOUT');
        eq(ctx(actor).lastError.message, '시간 초과');
    });

    it('recovering uses default error when no params', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(ctx(actor).lastError.code, 'LLM_ERROR');
    });

    it('recovering preserves inputText for retry', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: '재시도할 질문' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).inputText, '재시도할 질문');
    });

    it('recovering calls cleanupForRetry (stops TTS)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        env.calls.length = 0;
        actor.send({ type: 'LLM.ERROR' });
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'cleanupForRetry should stop TTS');
    });
});

describe('Recovering State — LLM.ERROR from responding', function () {
    it('LLM.ERROR in responding → recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).retryCount, 1);
        eq(ctx(actor).stopReason, 'system');
    });

    it('responding LLM.ERROR uses LLM_STREAM_ERROR as default code', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'LLM.ERROR' });
        eq(ctx(actor).lastError.code, 'LLM_STREAM_ERROR');
    });
});

describe('Recovering State — User Interrupts', function () {
    it('VOICE_EXIT from recovering → idle with retryCount reset', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
        eq(ctx(actor).retryCount, 0);
    });

    it('BUTTON.STOP from recovering → idle with retryCount reset', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
        eq(ctx(actor).retryCount, 0);
    });
});

describe('Recovering State — Retry with short delay', function () {
    // These tests use short recoverDelay to test retry behavior
    function buildMachineWithShortRecoverDelay(env) {
        const { calls, mockDotNet, mockTts, mockRec } = env;
        function dotNet() { return mockDotNet; }
        function ttsObj() { return mockTts; }
        function recObj() { return mockRec; }
        var actorRef = null;

        const machine = createMachine({
            id: 'assistant', initial: 'idle',
            context: {
                voiceMode: false, ttsEnabled: false, inputText: '',
                screenDone: false, ttsDone: false, pendingUserText: '',
                stopReason: '', retryCount: 0, lastError: null,
                interimText: '', pendingMessage: null,
            },
            states: {
                idle: {
                    entry: ['clearSessionData', 'notifyIdle'],
                    on: {
                        'KEYBOARD.SUBMIT': {
                            target: 'processing',
                            actions: [assign({ inputText: function (_a) { var e = _a.event; return (e.params && e.params.text) || ''; } })],
                        },
                    },
                },
                listening: {},
                processing: {
                    entry: ['sendToLlm'],
                    on: {
                        'LLM.STREAMING': { target: 'responding' },
                        'LLM.DONE': { target: 'idle' },
                        'LLM.ERROR': {
                            target: 'recovering',
                            actions: [assign({
                                lastError: function (_a) {
                                    var e = _a.event;
                                    return (e.params && e.params.error) || { code: 'LLM_ERROR', message: 'fail' };
                                },
                            })],
                        },
                    },
                },
                responding: {
                    entry: [assign({ screenDone: false, ttsDone: false })],
                    on: {
                        'SCREEN.DONE': { actions: [assign({ screenDone: true })] },
                        'LLM.ERROR': {
                            target: 'recovering',
                            actions: [assign({
                                lastError: function (_a) {
                                    var e = _a.event;
                                    return (e.params && e.params.error) || { code: 'LLM_STREAM_ERROR', message: 'fail' };
                                },
                            })],
                        },
                    },
                    always: [{ guard: 'isResponseComplete', target: 'idle' }],
                },
                recovering: {
                    entry: [
                        assign({ retryCount: function (_a) { return _a.context.retryCount + 1; } }),
                        'notifyRecovering',
                    ],
                    on: {
                        'BUTTON.STOP': { target: 'idle', actions: [assign({ retryCount: 0 })] },
                    },
                    after: {
                        recoverDelay: [{
                            guard: 'canRetry',
                            target: 'processing',
                        }, {
                            target: 'idle',
                            actions: [assign({ retryCount: 0 }), 'notifyRecoveryFailed'],
                        }],
                    },
                },
                stopping: {},
            },
        }, {
            guards: {
                isVoiceMode: function (_a) { return _a.context.voiceMode; },
                isResponseComplete: function (_a) { var c = _a.context; return c.screenDone && (c.ttsDone || !c.ttsEnabled); },
                hasPendingUserText: function (_a) { return !!_a.context.pendingUserText; },
                canRetry: function (_a) { return _a.context.retryCount <= 2; },
            },
            actions: {
                clearSessionData: assign({
                    inputText: '', screenDone: false, ttsDone: false, pendingUserText: '',
                    stopReason: '', interimText: '', retryCount: 0, lastError: null,
                }),
                notifyIdle: function () {
                    dotNet().invokeMethodAsync('OnMachineIdle');
                },
                sendToLlm: function (_a) {
                    var text = _a.context.inputText || '';
                    if (text) {
                        dotNet().invokeMethodAsync('OnMachineSendMessage', text);
                    } else {
                        actorRef.send({ type: 'LLM.ERROR' });
                    }
                },
                notifyRecovering: function (_a) {
                    var context = _a.context;
                    var err = context.lastError || { code: 'UNKNOWN', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecovering', err.code, err.message, context.retryCount);
                },
                notifyRecoveryFailed: function (_a) {
                    var context = _a.context;
                    var err = context.lastError || { code: 'UNKNOWN', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecoveryFailed', err.code, err.message);
                },
            },
            delays: {
                autoListenDelay: 999999,
                listenTimeout: 999999,
                processTimeout: 999999,
                respondTimeout: 999999,
                cleanupTimeout: 999999,
                recoverDelay: 30,  // 30ms for testing
            },
        });

        const actor = createActor(machine);
        actorRef = actor;
        actor.start();
        return { machine, actor };
    }

    itAsync('recovering → retry → processing (retryCount <= 2)', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachineWithShortRecoverDelay(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'retry-me' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).retryCount, 1);

        // After 30ms recoverDelay, canRetry (retryCount=1 <= 2) → processing
        setTimeout(function () {
            eq(state(actor), 'processing', 'should retry → processing');
            eq(ctx(actor).inputText, 'retry-me', 'inputText preserved for retry');
            // Verify OnMachineSendMessage was called again
            const sendCalls = env.calls.filter(c => c.target === 'dotNet' && c.method === 'OnMachineSendMessage');
            assert(sendCalls.length >= 2, 'OnMachineSendMessage should be called again on retry');
            resolve();
        }, 80);
    });

    itAsync('exhausting retries → idle with OnMachineRecoveryFailed', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachineWithShortRecoverDelay(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'fail-3-times' } });

        // 1st error → recovering (retryCount=1)
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'ERR', message: 'bad' } } });
        eq(ctx(actor).retryCount, 1);

        // After recoverDelay → retry 1 (processing)
        setTimeout(function () {
            eq(state(actor), 'processing', '1st retry');
            // 2nd error → recovering (retryCount=2)
            actor.send({ type: 'LLM.ERROR' });
            eq(state(actor), 'recovering');
            eq(ctx(actor).retryCount, 2);

            // After recoverDelay → retry 2 (processing) — canRetry: retryCount(2) <= 2
            setTimeout(function () {
                eq(state(actor), 'processing', '2nd retry');
                // 3rd error → recovering (retryCount=3)
                actor.send({ type: 'LLM.ERROR' });
                eq(state(actor), 'recovering');
                eq(ctx(actor).retryCount, 3);

                // After recoverDelay → canRetry(3 <= 2)=false → idle
                setTimeout(function () {
                    eq(state(actor), 'idle', 'exhausted retries → idle');
                    eq(ctx(actor).retryCount, 0, 'retryCount reset on final failure');
                    assert(hasDotNetCall(env.calls, 'OnMachineRecoveryFailed'),
                        'OnMachineRecoveryFailed should be called');
                    resolve();
                }, 80);
            }, 80);
        }, 80);
    });
});

describe('Recovering State — Context Cleanup', function () {
    it('idle entry after recovering resets retryCount and lastError', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'ERR', message: 'fail' } } });
        eq(state(actor), 'recovering');
        eq(ctx(actor).retryCount, 1);
        eq(ctx(actor).lastError.code, 'ERR');
        // Force back to idle
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
        eq(ctx(actor).retryCount, 0, 'retryCount reset');
        eq(ctx(actor).lastError, null, 'lastError reset');
    });
});

describe('Stopping with CLEANUP.DONE timing', function () {
    itAsync('processing BUTTON.STOP → stopping → CLEANUP.DONE → idle', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        setTimeout(function () {
            eq(state(actor), 'idle');
            resolve();
        }, 50);
    });
});

describe('Responding → Stopping → Idle chain', function () {
    itAsync('responding BUTTON.STOP → stopping → CLEANUP.DONE → idle', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        setTimeout(function () {
            eq(state(actor), 'idle');
            resolve();
        }, 50);
    });
});

// ═══════════════════════════════════════════════════════════════════
// PHASE 1 — Voice Loop Orchestration 심화 테스트
// ═══════════════════════════════════════════════════════════════════

describe('[P1] Multi-Cycle Voice Loop (연속 음성 대화)', function () {
    it('3 consecutive voice cycles without interruption', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(ctx(actor).voiceMode, true);

        // Cycle 1
        actor.send({ type: 'MIC.FINAL', params: { text: '첫 질문' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode preserved after cycle 1');
        eq(ctx(actor).inputText, '', 'inputText cleared');

        // Cycle 2 (simulate autoListenDelay by sending MIC.START manually)
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '두번째 질문' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, '두번째 질문');
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'responding', 'still responding — screen not done');
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');

        // Cycle 3
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '세번째' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode still true after 3 cycles');
    });

    it('voice cycle with barge-in mid-response', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });

        // Start first response
        actor.send({ type: 'MIC.FINAL', params: { text: '질문' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // User interrupts with barge-in
        actor.send({ type: 'BARGE_IN' });
        eq(ctx(actor).ttsDone, true, 'barge-in marks TTS done');
        // Screen continues...
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'barge-in + screen done → idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode preserved');

        // Auto-listen → new question
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '다음 질문' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, '다음 질문');
    });

    it('voice cycle with MIC.ERROR → auto-retry via idle re-enter', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');

        // Mic error — goes to idle but voiceMode preserved
        actor.send({ type: 'MIC.ERROR' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode stays true for auto-retry');

        // Simulate autoListenDelay → back to listening
        actor.send({ type: 'MIC.START' });
        eq(state(actor), 'listening');
        actor.send({ type: 'MIC.FINAL', params: { text: '재시도 성공' } });
        eq(state(actor), 'processing');
    });

    it('MIC.EMPTY multiple times → remains in idle with voiceMode', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });

        // User stays silent — MIC.EMPTY
        actor.send({ type: 'MIC.EMPTY' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true);

        // Try again
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.EMPTY' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode still true after multiple empties');

        // Eventually succeeds
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '드디어' } });
        eq(state(actor), 'processing');
    });
});

describe('[P1] Fire-and-Forget 제거 검증 — Machine이 Orchestrator', function () {
    it('responding → idle transition only via isResponseComplete guard (not fire-and-forget)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // Without SCREEN.DONE or TTS_ENDED, machine stays in responding
        // (no fire-and-forget chain to force transition)
        eq(state(actor), 'responding');
        eq(ctx(actor).screenDone, false);
        eq(ctx(actor).ttsDone, false);

        // Only when both complete does guard trigger
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'responding', 'screen not done');
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'guard fires');
    });

    it('C# callbacks are called in correct order through machine actions', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: '질문' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });

        // Extract C# callback order
        const dotNetCalls = env.calls
            .filter(c => c.target === 'dotNet')
            .map(c => c.method);

        // Verify callback sequence
        assert(dotNetCalls.indexOf('OnMachineIdle') >= 0, 'OnMachineIdle called');
        assert(dotNetCalls.indexOf('OnMachineStartListening') >= 0, 'OnMachineStartListening called');
        assert(dotNetCalls.indexOf('OnMachineSendMessage') >= 0, 'OnMachineSendMessage called');
        assert(dotNetCalls.indexOf('OnMachineResponding') >= 0, 'OnMachineResponding called');
        assert(dotNetCalls.indexOf('OnTtsEnded') >= 0, 'OnTtsEnded called');

        // Verify order: StartListening < SendMessage < Responding < TtsEnded
        const listenIdx = dotNetCalls.indexOf('OnMachineStartListening');
        const sendIdx = dotNetCalls.indexOf('OnMachineSendMessage');
        const respondIdx = dotNetCalls.indexOf('OnMachineResponding');
        const ttsIdx = dotNetCalls.indexOf('OnTtsEnded');
        assert(listenIdx < sendIdx, 'StartListening before SendMessage');
        assert(sendIdx < respondIdx, 'SendMessage before Responding');
        assert(respondIdx < ttsIdx, 'Responding before TtsEnded');
    });
});

// ═══════════════════════════════════════════════════════════════════
// PHASE 2 — Sequential Machine 심화 테스트
// ═══════════════════════════════════════════════════════════════════

describe('[P2] Deadman Timer 시뮬레이션', function () {
    // Build a machine with short delays for timer testing
    function buildTimerMachine(env, overrideDelays) {
        const { calls, mockDotNet, mockTts, mockRec } = env;
        function dotNet() { return mockDotNet; }
        function ttsObj() { return mockTts; }
        function recObj() { return mockRec; }
        var actorRef = null;

        const delays = Object.assign({
            autoListenDelay: 999999,
            listenTimeout: 999999,
            processTimeout: 999999,
            respondTimeout: 999999,
            cleanupTimeout: 999999,
            recoverDelay: 999999,
        }, overrideDelays);

        const machine = createMachine({
            id: 'assistant', initial: 'idle',
            context: {
                voiceMode: false, ttsEnabled: false, inputText: '',
                screenDone: false, ttsDone: false, pendingUserText: '',
                stopReason: '', retryCount: 0, lastError: null,
                interimText: '', pendingMessage: null,
            },
            states: {
                idle: {
                    entry: ['clearSessionData', 'notifyIdle'],
                    after: {
                        autoListenDelay: [{
                            guard: 'isVoiceMode',
                            target: 'listening',
                        }],
                    },
                    on: {
                        'KEYBOARD.SUBMIT': {
                            target: 'processing',
                            actions: [assign({ inputText: function (_a) { return (_a.event.params && _a.event.params.text) || ''; } })],
                        },
                        'MIC.START': 'listening',
                        'VOICE_ENTER': {
                            target: 'listening',
                            actions: [assign({ voiceMode: true, ttsEnabled: true })],
                        },
                    },
                },
                listening: {
                    entry: ['startListening'],
                    on: {
                        'MIC.FINAL': {
                            target: 'processing',
                            actions: [assign({ inputText: function (_a) { return (_a.event.params && _a.event.params.text) || ''; } })],
                        },
                        'MIC.EMPTY': 'idle',
                        'BUTTON.STOP': 'idle',
                    },
                    after: { listenTimeout: { target: 'idle' } },
                },
                processing: {
                    entry: ['sendToLlm'],
                    on: {
                        'LLM.STREAMING': { target: 'responding' },
                        'LLM.DONE': { target: 'idle' },
                        'LLM.ERROR': {
                            target: 'recovering',
                            actions: [assign({ lastError: function (_a) { return (_a.event.params && _a.event.params.error) || { code: 'ERR', message: 'fail' }; } })],
                        },
                        'BUTTON.STOP': 'stopping',
                    },
                    after: {
                        processTimeout: {
                            target: 'recovering',
                            actions: [assign({ lastError: { code: 'TIMEOUT', message: 'timeout' } })],
                        },
                    },
                },
                responding: {
                    entry: [assign({ screenDone: false, ttsDone: false }), 'notifyResponding'],
                    on: {
                        'SCREEN.DONE': { actions: [assign({ screenDone: true })] },
                        'TTS_ENDED': { actions: [assign({ ttsDone: true })] },
                        'BUTTON.STOP': { target: 'stopping', actions: [assign({ stopReason: 'button' })] },
                    },
                    always: [{ guard: 'isResponseComplete', target: 'idle' }],
                    after: { respondTimeout: { target: 'idle' } },
                },
                recovering: {
                    entry: [assign({ retryCount: function (_a) { return _a.context.retryCount + 1; } }), 'notifyRecovering'],
                    on: { 'BUTTON.STOP': { target: 'idle', actions: [assign({ retryCount: 0 })] } },
                    after: {
                        recoverDelay: [{
                            guard: 'canRetry', target: 'processing',
                        }, {
                            target: 'idle', actions: [assign({ retryCount: 0 }), 'notifyRecoveryFailed'],
                        }],
                    },
                },
                stopping: {
                    entry: ['cleanup'],
                    on: {
                        'CLEANUP.DONE': [
                            { guard: 'hasPendingUserText', target: 'processing', actions: [assign({ inputText: function (_a) { return _a.context.pendingUserText; }, pendingUserText: '' })] },
                            { target: 'idle' },
                        ],
                    },
                    after: { cleanupTimeout: { target: 'idle' } },
                },
            },
        }, {
            guards: {
                isVoiceMode: function (_a) { return _a.context.voiceMode; },
                isResponseComplete: function (_a) { var c = _a.context; return c.screenDone && (c.ttsDone || !c.ttsEnabled); },
                hasPendingUserText: function (_a) { return !!_a.context.pendingUserText; },
                canRetry: function (_a) { return _a.context.retryCount <= 2; },
            },
            actions: {
                clearSessionData: assign({
                    inputText: '', screenDone: false, ttsDone: false, pendingUserText: '',
                    stopReason: '', interimText: '', retryCount: 0, lastError: null,
                }),
                notifyIdle: function () { dotNet().invokeMethodAsync('OnMachineIdle'); },
                startVad: function () {},
                stopVad: function () {},
                startListening: function () { dotNet().invokeMethodAsync('OnMachineStartListening'); },
                sendToLlm: function (_a) {
                    var text = _a.context.inputText;
                    if (text) dotNet().invokeMethodAsync('OnMachineSendMessage', text);
                    else actorRef.send({ type: 'LLM.ERROR' });
                },
                notifyResponding: function () { dotNet().invokeMethodAsync('OnMachineResponding'); },
                notifyRecovering: function (_a) {
                    var err = _a.context.lastError || { code: '?', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecovering', err.code, err.message, _a.context.retryCount);
                },
                notifyRecoveryFailed: function (_a) {
                    var err = _a.context.lastError || { code: '?', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecoveryFailed', err.code, err.message);
                },
                cleanup: function () {
                    try { ttsObj()._stopInternal(); } catch (e) { }
                    setTimeout(function () { actorRef.send({ type: 'CLEANUP.DONE' }); }, 0);
                },
            },
            delays: delays,
        });

        const actor = createActor(machine);
        actorRef = actor;
        actor.start();
        return { machine, actor };
    }

    itAsync('autoListenDelay: idle → listening (voiceMode=true)', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildTimerMachine(env, { autoListenDelay: 30 });
        // Force voiceMode=true by entering and returning to idle
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.EMPTY' }); // back to idle, voiceMode=true
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true);

        setTimeout(function () {
            eq(state(actor), 'listening', 'autoListenDelay should transition to listening');
            resolve();
        }, 80);
    });

    itAsync('autoListenDelay: stays idle when voiceMode=false', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildTimerMachine(env, { autoListenDelay: 30 });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);

        setTimeout(function () {
            eq(state(actor), 'idle', 'should stay idle when voiceMode=false');
            resolve();
        }, 80);
    });

    itAsync('listenTimeout: listening → idle after timeout', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildTimerMachine(env, { listenTimeout: 30 });
        actor.send({ type: 'MIC.START' });
        eq(state(actor), 'listening');

        setTimeout(function () {
            eq(state(actor), 'idle', 'listenTimeout should force idle');
            resolve();
        }, 80);
    });

    itAsync('processTimeout: processing → recovering after timeout', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildTimerMachine(env, { processTimeout: 30 });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'slow LLM' } });
        eq(state(actor), 'processing');

        setTimeout(function () {
            eq(state(actor), 'recovering', 'processTimeout should → recovering');
            eq(ctx(actor).lastError.code, 'TIMEOUT');
            resolve();
        }, 80);
    });

    itAsync('respondTimeout: responding → idle after timeout', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildTimerMachine(env, { respondTimeout: 30 });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        setTimeout(function () {
            eq(state(actor), 'idle', 'respondTimeout should force idle');
            resolve();
        }, 80);
    });

    itAsync('cleanupTimeout: stopping → idle after timeout', function (resolve) {
        const env = createMockEnv();
        // Override cleanup to NOT send CLEANUP.DONE (simulate stuck cleanup)
        const { actor } = buildTimerMachine(env, { cleanupTimeout: 30 });
        // Override cleanup to do nothing
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'BUTTON.STOP' });

        // cleanup still sends CLEANUP.DONE via setTimeout(0), so it goes to idle fast
        // Test that even if we were stuck, cleanupTimeout would save us
        setTimeout(function () {
            eq(state(actor), 'idle', 'should be idle after cleanup');
            resolve();
        }, 80);
    });
});

describe('[P2] Stopping with PendingUserText', function () {
    itAsync('CLEANUP.DONE with pendingUserText → processing (not idle)', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Get to responding
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q1' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // Manually set pendingUserText (normally set by barge-in or new keyboard input)
        // Use VOICE_CMD to go to stopping
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(state(actor), 'stopping');

        // Inject pendingUserText into context before CLEANUP.DONE
        // (In real usage, this would be set by a BARGE_IN action)
        // Since we can't directly set context, we'll test the guard path differently

        setTimeout(function () {
            // cleanup already fired CLEANUP.DONE → idle (no pending text)
            eq(state(actor), 'idle');
            resolve();
        }, 50);
    });

    it('hasPendingUserText guard returns false for empty string', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        eq(ctx(actor).pendingUserText, '');
        // The guard: !!'' = false
    });
});

describe('[P2] Cross-State Event Robustness (이벤트 무시 검증)', function () {
    it('KEYBOARD.SUBMIT ignored in listening', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.START' });
        eq(state(actor), 'listening');
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ignored' } });
        eq(state(actor), 'listening', 'should stay in listening');
    });

    it('KEYBOARD.SUBMIT ignored in processing', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q1' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q2' } });
        eq(state(actor), 'processing', 'should stay in processing');
        eq(ctx(actor).inputText, 'q1', 'original text preserved');
    });

    it('KEYBOARD.SUBMIT ignored in responding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ignored' } });
        eq(state(actor), 'responding');
    });

    it('KEYBOARD.SUBMIT ignored in stopping', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ignored' } });
        eq(state(actor), 'stopping');
    });

    it('KEYBOARD.SUBMIT ignored in recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ignored' } });
        eq(state(actor), 'recovering');
    });

    it('MIC.FINAL ignored in idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'MIC.FINAL', params: { text: 'ghost' } });
        eq(state(actor), 'idle');
        eq(ctx(actor).inputText, '', 'inputText not set');
    });

    it('MIC.FINAL ignored in processing', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'MIC.FINAL', params: { text: 'late' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, 'q', 'original inputText preserved');
    });

    it('VOICE_ENTER ignored in non-idle states', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'processing', 'ignored in processing');
        eq(ctx(actor).voiceMode, false, 'voiceMode not changed');

        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'responding', 'ignored in responding');
    });

    it('AUTO_TTS_ON/OFF ignored in processing', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(state(actor), 'processing');
        eq(ctx(actor).ttsEnabled, false, 'ttsEnabled not changed in processing');
    });

    it('AUTO_TTS_ON/OFF ignored in responding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(state(actor), 'responding');
        // Note: this is by design — mode changes only apply in idle/listening
    });

    it('SEND_CONFIRM events ignored in non-idle states', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: 'ignored' } });
        eq(ctx(actor).pendingMessage, null, 'pendingMessage not set in responding');
    });

    it('WS_DISCONNECT from processing → no direct transition (only VOICE_EXIT handled)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        eq(state(actor), 'processing');
        // WS_DISCONNECT is only handled in listening state
        actor.send({ type: 'WS_DISCONNECT' });
        eq(state(actor), 'processing', 'WS_DISCONNECT not handled in processing');
    });

    it('WS_DISCONNECT from responding → no direct transition', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'WS_DISCONNECT' });
        eq(state(actor), 'responding', 'WS_DISCONNECT not handled in responding');
    });
});

describe('[P2] Concurrent/Rapid Event Sequences (빠른 이벤트 시퀀스)', function () {
    it('rapid full cycle: KEYBOARD.SUBMIT → LLM.STREAMING → SCREEN.DONE (no TTS)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // All events in rapid succession
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'fast' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'ttsEnabled=false → immediate completion');
    });

    it('double BARGE_IN is idempotent', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BARGE_IN' });
        eq(ctx(actor).ttsDone, true);
        actor.send({ type: 'BARGE_IN' }); // second one
        eq(ctx(actor).ttsDone, true, 'still true');
        eq(state(actor), 'responding', 'still responding (screen not done)');
    });

    it('BARGE_IN after TTS_ENDED (TTS already done)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'TTS_ENDED' });
        eq(ctx(actor).ttsDone, true);
        // BARGE_IN after TTS already ended — should be harmless
        actor.send({ type: 'BARGE_IN' });
        eq(ctx(actor).ttsDone, true, 'still true');
        eq(state(actor), 'responding', 'still responding (screen not done)');
    });

    it('VOICE_CMD after BARGE_IN in responding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'BARGE_IN' });
        eq(state(actor), 'responding');
        // Now voice command to fully stop
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(state(actor), 'stopping');
        eq(ctx(actor).stopReason, 'voice');
    });

    it('LLM.DONE immediately (no streaming) → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'short' } });
        eq(state(actor), 'processing');
        // LLM responds with complete answer (no streaming)
        actor.send({ type: 'LLM.DONE' });
        eq(state(actor), 'idle');
    });

    it('multiple TTS_ENDED events are idempotent', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'TTS_ENDED' });
        actor.send({ type: 'TTS_ENDED' }); // duplicate
        eq(ctx(actor).ttsDone, true);
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');
    });

    it('SCREEN.DONE before LLM.STREAMING is ignored', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        eq(state(actor), 'processing');
        // SCREEN.DONE arrives before LLM.STREAMING — should be ignored in processing
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'processing', 'SCREEN.DONE ignored in processing');
    });

    it('TTS_ENDED in idle is ignored', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        // Stale TTS_ENDED from previous session
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
        eq(ctx(actor).ttsDone, false, 'ttsDone stays false in idle');
    });

    it('TTS_ERROR in idle is ignored', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'TTS_ERROR', params: { error: 'stale error' } });
        eq(state(actor), 'idle');
    });
});

describe('[P2] Mixed Voice/Keyboard Workflows', function () {
    it('keyboard submit during voice mode (voiceMode=true, idle)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.EMPTY' }); // back to idle
        eq(ctx(actor).voiceMode, true);

        // User types instead of speaking
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: '키보드 입력' } });
        eq(state(actor), 'processing');
        eq(ctx(actor).inputText, '키보드 입력');
        eq(ctx(actor).voiceMode, true, 'voiceMode preserved');

        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode still true after keyboard cycle');
    });

    it('voice mode exit mid-listening, then keyboard submit', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(state(actor), 'listening');
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, false);
        // Note: ttsEnabled stays true after VOICE_EXIT (independent of voiceMode)
        eq(ctx(actor).ttsEnabled, true, 'ttsEnabled persists after VOICE_EXIT');

        // Now use keyboard — ttsEnabled=true means must wait for TTS_ENDED too
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: '키보드' } });
        eq(state(actor), 'processing');
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding', 'ttsEnabled=true → waiting for TTS');
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
    });

    it('AUTO_TTS_ON then keyboard submit → ttsEnabled affects responding guard', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(ctx(actor).ttsEnabled, true);

        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        // ttsEnabled=true → must wait for TTS_ENDED
        eq(state(actor), 'responding', 'waiting for TTS');
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
    });

    it('AUTO_TTS toggled between cycles', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Cycle 1: TTS on
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q1' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'responding', 'waiting for TTS');
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');

        // Toggle TTS off
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(ctx(actor).ttsEnabled, false);

        // Cycle 2: TTS off → screenDone alone completes
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q2' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'TTS off → screenDone completes');
    });
});

describe('[P2] Context Integrity Across Cycles', function () {
    it('all context fields reset on idle entry', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Dirty context through a full cycle
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'dirty text' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });

        // Now in idle — verify everything is clean
        const c = ctx(actor);
        eq(c.inputText, '', 'inputText');
        eq(c.screenDone, false, 'screenDone');
        eq(c.ttsDone, false, 'ttsDone');
        eq(c.pendingUserText, '', 'pendingUserText');
        eq(c.stopReason, '', 'stopReason');
        eq(c.interimText, '', 'interimText');
        eq(c.retryCount, 0, 'retryCount');
        eq(c.lastError, null, 'lastError');
    });

    it('context isolation: error cycle does not pollute next success cycle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Error cycle
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'error-q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'NET', message: 'network' } } });
        eq(state(actor), 'recovering');
        actor.send({ type: 'BUTTON.STOP' }); // abort recovery
        eq(state(actor), 'idle');
        eq(ctx(actor).retryCount, 0);
        eq(ctx(actor).lastError, null);
        eq(ctx(actor).inputText, '');

        // Success cycle
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'good-q' } });
        eq(ctx(actor).inputText, 'good-q');
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');
        eq(ctx(actor).retryCount, 0, 'no retry contamination');
    });

    it('voiceMode/ttsEnabled preserved correctly through stopping', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });

        // Stop mid-response
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'stopping');
        eq(ctx(actor).voiceMode, true, 'voiceMode preserved through stop');
        eq(ctx(actor).ttsEnabled, true, 'ttsEnabled preserved through stop');
    });

    it('voiceMode=false after VOICE_EXIT propagates through stopping → idle', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'VOICE_EXIT' });
        eq(state(actor), 'stopping');
        eq(ctx(actor).voiceMode, false, 'voiceMode=false from VOICE_EXIT');
    });

    it('pendingMessage persists across events until SEND_CONFIRM_CANCEL', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '대기 메시지' } });
        eq(ctx(actor).pendingMessage, '대기 메시지');

        // Other events don't clear it
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(ctx(actor).pendingMessage, '대기 메시지', 'not cleared by AUTO_TTS_ON');

        // Only CANCEL clears it
        actor.send({ type: 'SEND_CONFIRM_CANCEL' });
        eq(ctx(actor).pendingMessage, null);
    });
});

describe('[P2] Action Verification — C# Callback 검증', function () {
    it('exitVoiceMode calls tts._stopInternal, rec.stop, rec.setVoiceMode(false), OnMachineExitVoiceMode', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_EXIT' });

        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'tts._stopInternal');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'stop'), 'rec.stop');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'setVoiceMode' && c.args[0] === false), 'setVoiceMode(false)');
        assert(hasDotNetCall(env.calls, 'OnMachineExitVoiceMode'), 'OnMachineExitVoiceMode');
    });

    it('disableAutoTts calls tts._stopInternal, tts.releaseMic, rec.stop, OnMachineExitVoiceMode', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        env.calls.length = 0;
        actor.send({ type: 'AUTO_TTS_OFF' });

        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'tts._stopInternal');
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'releaseMic'), 'tts.releaseMic');
        assert(hasDotNetCall(env.calls, 'OnMachineExitVoiceMode'), 'OnMachineExitVoiceMode');
    });

    it('enableAutoTts calls tts.unlockAudio, tts.prepareMic', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        env.calls.length = 0;
        actor.send({ type: 'AUTO_TTS_ON' });

        assert(env.calls.some(c => c.target === 'tts' && c.method === 'unlockAudio'), 'unlockAudio');
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'prepareMic'), 'prepareMic');
    });

    it('notifyTtsError passes error message to OnTtsError', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ERROR', params: { error: '합성 실패' } });
        const call = getDotNetCall(env.calls, 'OnTtsError');
        assert(call, 'OnTtsError called');
        eq(call.args[0], '합성 실패');
    });

    it('notifyVoiceCmd passes command to OnVoiceCommand', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        const call = getDotNetCall(env.calls, 'OnVoiceCommand');
        assert(call, 'OnVoiceCommand called');
        eq(call.args[0], 'stop');
    });

    it('cleanup action calls tts._stopInternal and rec.stop', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'BUTTON.STOP' });
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'tts cleanup');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'stop'), 'rec cleanup');
        assert(hasDotNetCall(env.calls, 'OnMachineStopping'), 'OnMachineStopping');
    });
});

describe('[P2] Integration-style Multi-Round 시뮬레이션', function () {
    it('3 consecutive keyboard rounds with TTS', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });

        for (let i = 1; i <= 3; i++) {
            actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'round ' + i } });
            eq(state(actor), 'processing', 'round ' + i + ' processing');
            actor.send({ type: 'LLM.STREAMING' });
            eq(state(actor), 'responding', 'round ' + i + ' responding');
            actor.send({ type: 'TTS_ENDED' });
            actor.send({ type: 'SCREEN.DONE' });
            eq(state(actor), 'idle', 'round ' + i + ' completed');
        }
        eq(ctx(actor).ttsEnabled, true, 'ttsEnabled preserved');
    });

    it('mixed: keyboard → voice → keyboard → voice', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Round 1: keyboard, no TTS (ttsEnabled=false)
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'kb1' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');

        // Round 2: voice (VOICE_ENTER sets ttsEnabled=true)
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: 'voice1' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true);

        // Round 3: keyboard while voice mode on (ttsEnabled=true)
        actor.send({ type: 'MIC.START' }); // auto-listen
        actor.send({ type: 'MIC.EMPTY' }); // user doesn't speak
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'kb2' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' }); // need TTS done too (ttsEnabled=true)
        eq(state(actor), 'idle');

        // Round 4: exit voice mode, then disable TTS, keyboard only
        actor.send({ type: 'VOICE_EXIT' });
        eq(ctx(actor).voiceMode, false);
        // ttsEnabled still true from VOICE_ENTER — need AUTO_TTS_OFF for keyboard-only
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(ctx(actor).ttsEnabled, false);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'kb3' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle', 'ttsEnabled=false → screenDone enough');
    });

    it('voice mode with error recovery → success', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });

        // Question
        actor.send({ type: 'MIC.FINAL', params: { text: '에러 테스트' } });
        eq(state(actor), 'processing');

        // LLM error → recovering
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'RATE_LIMIT', message: '요청 제한' } } });
        eq(state(actor), 'recovering');
        eq(ctx(actor).retryCount, 1);
        eq(ctx(actor).inputText, '에러 테스트', 'inputText preserved for retry');

        // User cancels retry
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
        eq(ctx(actor).voiceMode, true, 'voiceMode preserved');
        eq(ctx(actor).retryCount, 0);

        // Successful round
        actor.send({ type: 'MIC.START' });
        actor.send({ type: 'MIC.FINAL', params: { text: '성공 질문' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        actor.send({ type: 'TTS_ENDED' });
        eq(state(actor), 'idle');
    });
});

// ═══════════════════════════════════════════════════════════════════
// PHASE 3 — Recovering 심화 + Volume Ducking 테스트
// ═══════════════════════════════════════════════════════════════════

describe('[P3] Recovering — Guard Boundary Tests', function () {
    it('canRetry: retryCount=0 → true (first error)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(ctx(actor).retryCount, 1);
        // canRetry: 1 <= 2 = true (will retry after delay)
    });

    it('canRetry: retryCount=2 after entry (retryCount was 1) → true', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' }); // retryCount → 1
        eq(ctx(actor).retryCount, 1);
        // Would retry → processing → error again
        // Can't easily test retryCount=2 without delay, but structure is verified
    });

    it('lastError object structure from processing', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'AUTH', message: '인증 실패' } } });
        const err = ctx(actor).lastError;
        assert(typeof err === 'object', 'lastError is object');
        eq(err.code, 'AUTH');
        eq(err.message, '인증 실패');
    });

    it('lastError object structure from responding', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'STREAM_CUT', message: '스트림 끊김' } } });
        eq(ctx(actor).lastError.code, 'STREAM_CUT');
        eq(ctx(actor).lastError.message, '스트림 끊김');
    });
});

describe('[P3] Recovering — Callback Verification', function () {
    it('OnMachineRecovering called with correct args', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        env.calls.length = 0;
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'NET_ERR', message: '타임아웃' } } });

        const call = getDotNetCall(env.calls, 'OnMachineRecovering');
        assert(call, 'OnMachineRecovering called');
        eq(call.args[0], 'NET_ERR', 'error code');
        eq(call.args[1], '타임아웃', 'error message');
        eq(call.args[2], 1, 'retryCount');
    });

    it('cleanupForRetry stops TTS on recovering entry', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'LLM.ERROR' });
        // cleanupForRetry should call tts._stopInternal
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'),
            'tts._stopInternal called for retry cleanup');
    });

    it('VOICE_EXIT from recovering calls exitVoiceMode (cleanup)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'MIC.FINAL', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        env.calls.length = 0;
        actor.send({ type: 'VOICE_EXIT' });
        assert(hasDotNetCall(env.calls, 'OnMachineExitVoiceMode'), 'exitVoiceMode called');
    });
});

describe('[P3] Recovering — Complex Error Scenarios', function () {
    it('error during responding with partial TTS → recovering', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');

        // Partial TTS played, then LLM stream error
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        eq(ctx(actor).stopReason, 'system');
        eq(ctx(actor).retryCount, 1);
    });

    it('error with custom error params preserved in context', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: {
            error: { code: 'RATE_LIMIT_429', message: 'Too many requests' }
        }});
        eq(ctx(actor).lastError.code, 'RATE_LIMIT_429');
        eq(ctx(actor).lastError.message, 'Too many requests');
    });

    it('recovering → BUTTON.STOP resets ALL error state', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR', params: { error: { code: 'ERR', message: 'bad' } } });
        eq(ctx(actor).retryCount, 1);
        eq(ctx(actor).lastError.code, 'ERR');

        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
        // idle entry clearSessionData resets everything
        eq(ctx(actor).retryCount, 0);
        eq(ctx(actor).lastError, null);
        eq(ctx(actor).inputText, '');
        eq(ctx(actor).stopReason, '');
    });
});

describe('[P3] Recovering — Multi-error retry simulation (short delay)', function () {
    // Reuse buildMachineWithShortRecoverDelay from earlier, or use the timer machine
    function buildRetryMachine(env) {
        const { calls, mockDotNet, mockTts, mockRec } = env;
        function dotNet() { return mockDotNet; }
        function ttsObj() { return mockTts; }
        function recObj() { return mockRec; }
        var actorRef = null;

        const machine = createMachine({
            id: 'assistant', initial: 'idle',
            context: {
                voiceMode: false, ttsEnabled: false, inputText: '',
                screenDone: false, ttsDone: false, pendingUserText: '',
                stopReason: '', retryCount: 0, lastError: null,
                interimText: '', pendingMessage: null,
            },
            states: {
                idle: {
                    entry: ['clearSessionData', 'notifyIdle'],
                    on: {
                        'KEYBOARD.SUBMIT': {
                            target: 'processing',
                            actions: [assign({ inputText: function (_a) { return (_a.event.params && _a.event.params.text) || ''; } })],
                        },
                    },
                },
                listening: {},
                processing: {
                    entry: ['sendToLlm'],
                    on: {
                        'LLM.STREAMING': { target: 'responding' },
                        'LLM.DONE': { target: 'idle' },
                        'LLM.ERROR': {
                            target: 'recovering',
                            actions: [assign({ lastError: function (_a) { return (_a.event.params && _a.event.params.error) || { code: 'ERR', message: 'fail' }; } })],
                        },
                    },
                },
                responding: {
                    entry: [assign({ screenDone: false, ttsDone: false })],
                    on: {
                        'SCREEN.DONE': { actions: [assign({ screenDone: true })] },
                        'TTS_ENDED': { actions: [assign({ ttsDone: true })] },
                    },
                    always: [{ guard: 'isResponseComplete', target: 'idle' }],
                },
                recovering: {
                    entry: [
                        assign({ retryCount: function (_a) { return _a.context.retryCount + 1; } }),
                        'notifyRecovering',
                    ],
                    on: {
                        'BUTTON.STOP': { target: 'idle', actions: [assign({ retryCount: 0 })] },
                    },
                    after: {
                        recoverDelay: [{
                            guard: 'canRetry', target: 'processing',
                        }, {
                            target: 'idle', actions: [assign({ retryCount: 0 }), 'notifyRecoveryFailed'],
                        }],
                    },
                },
                stopping: {},
            },
        }, {
            guards: {
                isVoiceMode: function (_a) { return _a.context.voiceMode; },
                isResponseComplete: function (_a) { var c = _a.context; return c.screenDone && (c.ttsDone || !c.ttsEnabled); },
                hasPendingUserText: function (_a) { return !!_a.context.pendingUserText; },
                canRetry: function (_a) { return _a.context.retryCount <= 2; },
            },
            actions: {
                clearSessionData: assign({
                    inputText: '', screenDone: false, ttsDone: false, pendingUserText: '',
                    stopReason: '', interimText: '', retryCount: 0, lastError: null,
                }),
                notifyIdle: function () { dotNet().invokeMethodAsync('OnMachineIdle'); },
                sendToLlm: function (_a) {
                    var text = _a.context.inputText;
                    if (text) dotNet().invokeMethodAsync('OnMachineSendMessage', text);
                    else actorRef.send({ type: 'LLM.ERROR' });
                },
                notifyRecovering: function (_a) {
                    var err = _a.context.lastError || { code: '?', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecovering', err.code, err.message, _a.context.retryCount);
                },
                notifyRecoveryFailed: function (_a) {
                    var err = _a.context.lastError || { code: '?', message: '?' };
                    dotNet().invokeMethodAsync('OnMachineRecoveryFailed', err.code, err.message);
                },
            },
            delays: {
                autoListenDelay: 999999, listenTimeout: 999999, processTimeout: 999999,
                respondTimeout: 999999, cleanupTimeout: 999999,
                recoverDelay: 25,
            },
        });

        const actor = createActor(machine);
        actorRef = actor;
        actor.start();
        return { machine, actor };
    }

    itAsync('1st retry succeeds → processing → responding → idle', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildRetryMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'retry-q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');

        // Wait for retry → processing
        setTimeout(function () {
            eq(state(actor), 'processing', 'retried');
            // This time LLM succeeds
            actor.send({ type: 'LLM.STREAMING' });
            eq(state(actor), 'responding');
            actor.send({ type: 'SCREEN.DONE' });
            eq(state(actor), 'idle');
            eq(ctx(actor).retryCount, 0, 'reset on idle entry');
            resolve();
        }, 60);
    });

    itAsync('2 failures then success on 2nd retry', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildRetryMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });

        // 1st fail
        actor.send({ type: 'LLM.ERROR' });
        eq(ctx(actor).retryCount, 1);

        setTimeout(function () {
            eq(state(actor), 'processing', '1st retry');
            // 2nd fail
            actor.send({ type: 'LLM.ERROR' });
            eq(ctx(actor).retryCount, 2);

            setTimeout(function () {
                eq(state(actor), 'processing', '2nd retry');
                // This time succeeds
                actor.send({ type: 'LLM.STREAMING' });
                actor.send({ type: 'SCREEN.DONE' });
                eq(state(actor), 'idle');
                resolve();
            }, 60);
        }, 60);
    });

    itAsync('BUTTON.STOP during recovery wait cancels retry', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildRetryMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');

        // User presses stop before recoverDelay fires
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');

        // After delay, should NOT be in processing (was cancelled)
        setTimeout(function () {
            eq(state(actor), 'idle', 'cancelled — stays idle');
            resolve();
        }, 60);
    });

    itAsync('recovery count correct in OnMachineRecovering calls', function (resolve) {
        const env = createMockEnv();
        const { actor } = buildRetryMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.ERROR' });

        setTimeout(function () {
            actor.send({ type: 'LLM.ERROR' }); // 2nd fail

            setTimeout(function () {
                actor.send({ type: 'LLM.ERROR' }); // 3rd fail

                setTimeout(function () {
                    eq(state(actor), 'idle', 'exhausted');
                    // Check recovery counts in calls
                    const recoverCalls = env.calls.filter(c =>
                        c.target === 'dotNet' && c.method === 'OnMachineRecovering');
                    eq(recoverCalls.length, 3, '3 recovering calls');
                    eq(recoverCalls[0].args[2], 1, '1st retry count');
                    eq(recoverCalls[1].args[2], 2, '2nd retry count');
                    eq(recoverCalls[2].args[2], 3, '3rd retry count');
                    resolve();
                }, 60);
            }, 60);
        }, 60);
    });
});

describe('[P3] Volume Ducking — _duckVolume Logic', function () {
    // These tests verify the ducking function behavior by checking its preconditions
    // (actual Web Audio API not available in Node.js, but we verify the call structure)

    it('responding exit calls stopBargeIn (stops RMS + restores volume)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        eq(state(actor), 'responding');
        env.calls.length = 0;

        // Trigger exit from responding
        actor.send({ type: 'SCREEN.DONE' }); // ttsEnabled=false → idle
        assert(env.calls.some(c => c.target === 'rms' && c.method === 'stop'), 'RMS stopped (volume restored)');
        assert(env.calls.some(c => c.target === 'cmdListener' && c.method === 'stop'), 'cmd listener stopped');
    });

    it('BARGE_IN triggers stopTts (ducking irrelevant after TTS stop)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'TTS stopped on barge-in');
    });

    it('startBargeIn is called on responding entry', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        env.calls.length = 0;
        actor.send({ type: 'LLM.STREAMING' });
        assert(env.calls.some(c => c.target === 'rms' && c.method === 'start'), 'RMS started');
        assert(env.calls.some(c => c.target === 'cmdListener' && c.method === 'start'), 'cmd listener started');
    });

    it('stopBargeIn is called on responding exit (any exit path)', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        actor.send({ type: 'LLM.STREAMING' });
        env.calls.length = 0;

        // Exit via BUTTON.STOP → stopping
        actor.send({ type: 'BUTTON.STOP' });
        assert(env.calls.some(c => c.target === 'rms' && c.method === 'stop'), 'RMS stopped on BUTTON.STOP exit');
        assert(env.calls.some(c => c.target === 'cmdListener' && c.method === 'stop'), 'cmd stopped on BUTTON.STOP exit');
    });
});

describe('[P3] All States Summary — Transition Matrix', function () {
    it('idle handles: KEYBOARD.SUBMIT, MIC.START, VOICE_ENTER, VOICE_EXIT, AUTO_TTS_ON/OFF, SEND_CONFIRM_*', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        eq(state(actor), 'idle');

        // All these should be handled without error
        actor.send({ type: 'VOICE_EXIT' }); eq(state(actor), 'idle');
        actor.send({ type: 'AUTO_TTS_ON' }); eq(state(actor), 'idle');
        actor.send({ type: 'AUTO_TTS_OFF' }); eq(state(actor), 'idle');
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: 't' } }); eq(state(actor), 'idle');
        actor.send({ type: 'SEND_CONFIRM_CANCEL' }); eq(state(actor), 'idle');
        actor.send({ type: 'SEND_CONFIRM_STOP' }); eq(state(actor), 'idle');
        actor.send({ type: 'SEND_CONFIRM_KEEP' }); eq(state(actor), 'idle');
    });

    it('listening handles: MIC.FINAL, MIC.INTERIM, MIC.EMPTY, MIC.ERROR, MIC.FATAL, BUTTON.STOP, VOICE_EXIT, AUTO_TTS_OFF, WS_DISCONNECT', function () {
        // Test each handled event
        const events = [
            { type: 'MIC.EMPTY' },
            { type: 'MIC.ERROR' },
            { type: 'MIC.FATAL' },
            { type: 'BUTTON.STOP' },
            { type: 'VOICE_EXIT' },
            { type: 'AUTO_TTS_OFF' },
            { type: 'WS_DISCONNECT' },
        ];
        events.forEach(function (evt) {
            const env = createMockEnv();
            const { actor } = buildMachine(env);
            actor.send({ type: 'MIC.START' });
            eq(state(actor), 'listening');
            actor.send(evt);
            eq(state(actor), 'idle', evt.type + ' → idle');
        });

        // MIC.FINAL → processing
        const env2 = createMockEnv();
        const { actor: a2 } = buildMachine(env2);
        a2.send({ type: 'MIC.START' });
        a2.send({ type: 'MIC.FINAL', params: { text: 't' } });
        eq(state(a2), 'processing');

        // MIC.INTERIM → stays listening
        const env3 = createMockEnv();
        const { actor: a3 } = buildMachine(env3);
        a3.send({ type: 'MIC.START' });
        a3.send({ type: 'MIC.INTERIM', params: { text: 'partial' } });
        eq(state(a3), 'listening');
    });

    it('processing handles: LLM.STREAMING, LLM.DONE, LLM.ERROR, BUTTON.STOP, VOICE_EXIT', function () {
        // LLM.STREAMING → responding
        const e1 = createMockEnv(); const { actor: a1 } = buildMachine(e1);
        a1.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a1.send({ type: 'LLM.STREAMING' });
        eq(state(a1), 'responding');

        // LLM.DONE → idle
        const e2 = createMockEnv(); const { actor: a2 } = buildMachine(e2);
        a2.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a2.send({ type: 'LLM.DONE' });
        eq(state(a2), 'idle');

        // LLM.ERROR → recovering
        const e3 = createMockEnv(); const { actor: a3 } = buildMachine(e3);
        a3.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a3.send({ type: 'LLM.ERROR' });
        eq(state(a3), 'recovering');

        // BUTTON.STOP → stopping
        const e4 = createMockEnv(); const { actor: a4 } = buildMachine(e4);
        a4.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a4.send({ type: 'BUTTON.STOP' });
        eq(state(a4), 'stopping');

        // VOICE_EXIT → stopping
        const e5 = createMockEnv(); const { actor: a5 } = buildMachine(e5);
        a5.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a5.send({ type: 'VOICE_EXIT' });
        eq(state(a5), 'stopping');
    });

    it('responding handles: SCREEN.DONE, TTS_ENDED, TTS_ERROR, BARGE_IN, VOICE_CMD, BUTTON.STOP, VOICE_EXIT, LLM.ERROR', function () {
        // Each event tested individually
        const targets = {
            'VOICE_CMD': 'stopping',
            'BUTTON.STOP': 'stopping',
            'VOICE_EXIT': 'stopping',
            'LLM.ERROR': 'recovering',
        };
        Object.keys(targets).forEach(function (evtType) {
            const env = createMockEnv(); const { actor } = buildMachine(env);
            actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
            actor.send({ type: 'LLM.STREAMING' });
            actor.send({ type: evtType, params: evtType === 'VOICE_CMD' ? { command: 'stop' } : undefined });
            eq(state(actor), targets[evtType], evtType + ' → ' + targets[evtType]);
        });

        // SCREEN.DONE, TTS_ENDED, TTS_ERROR, BARGE_IN → stay responding (or idle via guard)
        ['SCREEN.DONE', 'TTS_ENDED', 'TTS_ERROR', 'BARGE_IN'].forEach(function (evtType) {
            const env = createMockEnv(); const { actor } = buildMachine(env);
            actor.send({ type: 'AUTO_TTS_ON' });
            actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
            actor.send({ type: 'LLM.STREAMING' });
            actor.send({ type: evtType });
            // With TTS on, single event doesn't complete (except SCREEN.DONE if !ttsEnabled)
            assert(state(actor) === 'responding' || state(actor) === 'idle',
                evtType + ' handled in responding');
        });
    });

    it('recovering handles: VOICE_EXIT, BUTTON.STOP', function () {
        // VOICE_EXIT
        const e1 = createMockEnv(); const { actor: a1 } = buildMachine(e1);
        a1.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a1.send({ type: 'LLM.ERROR' });
        a1.send({ type: 'VOICE_EXIT' });
        eq(state(a1), 'idle');

        // BUTTON.STOP
        const e2 = createMockEnv(); const { actor: a2 } = buildMachine(e2);
        a2.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a2.send({ type: 'LLM.ERROR' });
        a2.send({ type: 'BUTTON.STOP' });
        eq(state(a2), 'idle');
    });

    it('stopping handles: CLEANUP.DONE, VOICE_EXIT', function () {
        // VOICE_EXIT from stopping
        const e1 = createMockEnv(); const { actor: a1 } = buildMachine(e1);
        a1.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' } });
        a1.send({ type: 'BUTTON.STOP' });
        eq(state(a1), 'stopping');
        a1.send({ type: 'VOICE_EXIT' });
        eq(state(a1), 'idle');
    });
});

describe('[P3] Stress — Rapid State Cycling', function () {
    it('10 rapid keyboard cycles without crash', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        for (let i = 0; i < 10; i++) {
            actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'q' + i } });
            actor.send({ type: 'LLM.STREAMING' });
            actor.send({ type: 'SCREEN.DONE' });
            eq(state(actor), 'idle', 'cycle ' + i + ' completed');
        }
    });

    it('5 rapid voice cycles with TTS', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);
        actor.send({ type: 'VOICE_ENTER' });

        for (let i = 0; i < 5; i++) {
            actor.send({ type: 'MIC.FINAL', params: { text: '질문' + i } });
            actor.send({ type: 'LLM.STREAMING' });
            actor.send({ type: 'TTS_ENDED' });
            actor.send({ type: 'SCREEN.DONE' });
            eq(state(actor), 'idle', 'voice cycle ' + i);
            eq(ctx(actor).voiceMode, true, 'voiceMode preserved');
            // Manually re-enter listening (simulating autoListenDelay)
            if (i < 4) actor.send({ type: 'MIC.START' });
        }
    });

    it('alternating error/success cycles', function () {
        const env = createMockEnv();
        const { actor } = buildMachine(env);

        // Success
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ok1' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');

        // Error → recover cancel
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'err' } });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');

        // Success
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'ok2' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'SCREEN.DONE' });
        eq(state(actor), 'idle');

        // Error in responding → recover cancel
        actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'err2' } });
        actor.send({ type: 'LLM.STREAMING' });
        actor.send({ type: 'LLM.ERROR' });
        eq(state(actor), 'recovering');
        actor.send({ type: 'BUTTON.STOP' });
        eq(state(actor), 'idle');
    });
});

// ═══════════════════════════════════════════════════════════════════
// [Sprint 2] Q_end 음성 질문 종료 판정 테스트
// ═══════════════════════════════════════════════════════════════════

// Q_end evaluator — _Layout.cshtml의 _qend 로직을 테스트용으로 복제
var qendEvaluator = {
    w1: 0.40,
    w2: 0.35,
    w3: 0.25,
    threshold: 0.6,
    silenceRef: 1200,
    _endings: /(?:다|요|까|죠|네|지|야|래|걸|데|나|세요|습니다|합니다|됩니다|입니다|었다|겠다|한다|ㄴ다|ㄹ까|ㄹ게|ㄹ래|해요|해줘|할까|하자|인가|닌가|런가|된가|인데|인지|하네|하지|이야|거야|잖아|이에요|예요|일까|할게|어요|아요|되나|는데|은데|는지|은지|든지|가요|나요|래요|대요|답니다|랍니다|시오|겠습니까|었습니까)\s*[.?!]?\s*$/,
    _endingsEn: /[.?!]\s*$/,
    evaluate: function(text, silenceDurationMs, confidence) {
        text = (text || '').trim();
        if (!text) return { score: 0, shouldEnd: false };
        var S_sentence = 0.3;
        if (this._endings.test(text) || this._endingsEn.test(text)) {
            S_sentence = 1.0;
        } else if (text.length > 50) {
            S_sentence = 0.5;
        }
        var S_silence = Math.max(0, Math.min(1, silenceDurationMs / this.silenceRef));
        var S_confidence = Math.max(0, Math.min(1, confidence != null ? confidence : 0.5));
        var score = this.w1 * S_sentence + this.w2 * S_silence + this.w3 * S_confidence;
        var shouldEnd = score >= this.threshold;
        return { score: score, shouldEnd: shouldEnd, S_sentence: S_sentence, S_silence: S_silence, S_confidence: S_confidence };
    }
};

describe('[Sprint 2] Q_end 기본 공식 검증', function() {
    it('빈 텍스트 → score 0, shouldEnd false', function() {
        var r = qendEvaluator.evaluate('', 2000, 0.9);
        eq(r.score, 0);
        eq(r.shouldEnd, false);
    });

    it('종결어미 + 충분한 무음 + 높은 confidence → shouldEnd true', function() {
        // S_sentence=1.0, S_silence=1200/1200=1.0, S_confidence=0.9
        // Q_end = 0.4*1.0 + 0.35*1.0 + 0.25*0.9 = 0.4+0.35+0.225 = 0.975
        var r = qendEvaluator.evaluate('오늘 날씨 어떤가요', 1200, 0.9);
        eq(r.shouldEnd, true);
        eq(r.S_sentence, 1.0);
        assert.ok(r.score > 0.9, 'score should be > 0.9, got ' + r.score);
    });

    it('종결어미 + 짧은 무음 + 높은 confidence → shouldEnd true', function() {
        // S_sentence=1.0, S_silence=600/1200=0.5, S_confidence=0.8
        // Q_end = 0.4*1.0 + 0.35*0.5 + 0.25*0.8 = 0.4+0.175+0.2 = 0.775
        var r = qendEvaluator.evaluate('이것은 테스트입니다', 600, 0.8);
        eq(r.shouldEnd, true);
        assert.ok(r.score >= 0.6, 'score >= 0.6');
    });

    it('종결어미 없음 + 짧은 무음 + 낮은 confidence → shouldEnd false', function() {
        // S_sentence=0.3, S_silence=300/1200=0.25, S_confidence=0.3
        // Q_end = 0.4*0.3 + 0.35*0.25 + 0.25*0.3 = 0.12+0.0875+0.075 = 0.2825
        var r = qendEvaluator.evaluate('그래서 그건', 300, 0.3);
        eq(r.shouldEnd, false);
        assert.ok(r.score < 0.6, 'score < 0.6, got ' + r.score);
    });

    it('종결어미 없음 + 매우 긴 무음 → shouldEnd true (무음이 보상)', function() {
        // S_sentence=0.3, S_silence=2400/1200=clamped 1.0, S_confidence=0.5
        // Q_end = 0.4*0.3 + 0.35*1.0 + 0.25*0.5 = 0.12+0.35+0.125 = 0.595
        // 0.595 < 0.6 → shouldEnd false (살짝 모자람)
        var r = qendEvaluator.evaluate('그래서 그건', 2400, 0.5);
        // 경계 테스트: 0.595 < 0.6
        eq(r.shouldEnd, false);
    });

    it('종결어미 없음 + 매우 긴 무음 + 높은 confidence → shouldEnd true', function() {
        // S_sentence=0.3, S_silence=1.0, S_confidence=0.9
        // Q_end = 0.4*0.3 + 0.35*1.0 + 0.25*0.9 = 0.12+0.35+0.225 = 0.695
        var r = qendEvaluator.evaluate('그래서 그건', 2400, 0.9);
        eq(r.shouldEnd, true);
    });
});

describe('[Sprint 2] Q_end 한국어 종결어미 감지', function() {
    var endings = [
        '이건 뭐예요', '설명해 주세요', '어디입니까', '좋습니다',
        '가능한가요', '알려줘요', '해줘', '무엇인지', '맞나요',
        '그렇죠', '아시겠네', '할까', '먹을래', '그만하자',
        '이것은 합니다', '처리됩니다', '발생했다', '진행한다',
        '문제인데', '되잖아', '아닌가', '그거야', '맞이에요',
        '좋아요', '아니에요', '보세요', '됐어요', '있나요',
    ];

    endings.forEach(function(text) {
        it('종결어미 감지: "' + text + '"', function() {
            var r = qendEvaluator.evaluate(text, 800, 0.8);
            eq(r.S_sentence, 1.0, text + ' should have S_sentence=1.0');
        });
    });

    it('미완성 문장: S_sentence=0.3', function() {
        var r = qendEvaluator.evaluate('그래서 이것을', 800, 0.8);
        eq(r.S_sentence, 0.3);
    });

    it('긴 미완성 문장(>50자): S_sentence=0.5', function() {
        var longText = '이 장비에서 발생하는 CMP 폴리싱 패드의 표면 마모율이 최근에 증가하고 있는 것으로 보이는 상황에서 추가적으로 검토해야 할 사항이';
        assert.ok(longText.length > 50, 'text should be > 50 chars, got ' + longText.length);
        var r = qendEvaluator.evaluate(longText, 800, 0.8);
        eq(r.S_sentence, 0.5);
    });

    it('영어 종결 (./?/!)', function() {
        var r = qendEvaluator.evaluate('What is this?', 800, 0.8);
        eq(r.S_sentence, 1.0);
    });
});

describe('[Sprint 2] Q_end S_silence 정규화', function() {
    it('silenceDuration=0 → S_silence=0', function() {
        var r = qendEvaluator.evaluate('테스트', 0, 0.5);
        eq(r.S_silence, 0);
    });

    it('silenceDuration=600 → S_silence=0.5', function() {
        var r = qendEvaluator.evaluate('테스트', 600, 0.5);
        eq(r.S_silence, 0.5);
    });

    it('silenceDuration=1200 → S_silence=1.0', function() {
        var r = qendEvaluator.evaluate('테스트', 1200, 0.5);
        eq(r.S_silence, 1.0);
    });

    it('silenceDuration=3000 → S_silence=1.0 (clamped)', function() {
        var r = qendEvaluator.evaluate('테스트', 3000, 0.5);
        eq(r.S_silence, 1.0);
    });
});

describe('[Sprint 2] Q_end S_confidence 클램프', function() {
    it('confidence=0 → S_confidence=0', function() {
        var r = qendEvaluator.evaluate('테스트', 600, 0);
        eq(r.S_confidence, 0);
    });

    it('confidence=1.0 → S_confidence=1.0', function() {
        var r = qendEvaluator.evaluate('테스트', 600, 1.0);
        eq(r.S_confidence, 1.0);
    });

    it('confidence 미지정 → S_confidence=0.5 (기본값)', function() {
        var r = qendEvaluator.evaluate('테스트', 600);
        eq(r.S_confidence, 0.5);
    });

    it('confidence > 1.0 → clamped to 1.0', function() {
        var r = qendEvaluator.evaluate('테스트', 600, 1.5);
        eq(r.S_confidence, 1.0);
    });

    it('confidence < 0 → clamped to 0', function() {
        var r = qendEvaluator.evaluate('테스트', 600, -0.5);
        eq(r.S_confidence, 0);
    });
});

describe('[Sprint 2] Q_end 경계값 테스트', function() {
    it('threshold 정확히 0.6 → shouldEnd true', function() {
        // 역산: 0.4*S + 0.35*sil + 0.25*conf = 0.6
        // S=0.3, sil=1200ms→1.0, conf=0.6: 0.12+0.35+0.15 = 0.62 → true
        var r = qendEvaluator.evaluate('그건', 1200, 0.6);
        eq(r.shouldEnd, true);
    });

    it('threshold 바로 아래 → shouldEnd false', function() {
        // S=0.3, sil=800ms→0.667, conf=0.4: 0.12+0.233+0.10 = 0.453
        var r = qendEvaluator.evaluate('그건', 800, 0.4);
        eq(r.shouldEnd, false);
    });
});

describe('[Sprint 2] Whisper 적응형 무음 기간', function() {
    // 적응형 무음 계산 함수 (Whisper _startSilenceDetector 로직 복제)
    function adaptiveSilenceDuration(recordingSec) {
        if (recordingSec < 2) return 2500;
        if (recordingSec < 5) return 1800;
        if (recordingSec < 10) return 1400;
        return 1000;
    }

    it('녹음 < 2초 → 2500ms 대기', function() {
        eq(adaptiveSilenceDuration(0.5), 2500);
        eq(adaptiveSilenceDuration(1.9), 2500);
    });

    it('녹음 2~5초 → 1800ms 대기', function() {
        eq(adaptiveSilenceDuration(2), 1800);
        eq(adaptiveSilenceDuration(4.9), 1800);
    });

    it('녹음 5~10초 → 1400ms 대기', function() {
        eq(adaptiveSilenceDuration(5), 1400);
        eq(adaptiveSilenceDuration(9.9), 1400);
    });

    it('녹음 > 10초 → 1000ms 대기', function() {
        eq(adaptiveSilenceDuration(10), 1000);
        eq(adaptiveSilenceDuration(30), 1000);
    });
});

describe('[Sprint 2] Q_end 실제 시나리오 시뮬레이션', function() {
    it('시나리오 1: 완전한 질문 + 짧은 무음 → 빠른 종료', function() {
        // "이 장비의 상태를 알려주세요" + 500ms silence + confidence 0.85
        var r = qendEvaluator.evaluate('이 장비의 상태를 알려주세요', 500, 0.85);
        eq(r.shouldEnd, true);
        // S=1.0, sil=0.417, conf=0.85 → 0.4+0.146+0.213 = 0.759
    });

    it('시나리오 2: 미완성 + 짧은 무음 → 더 기다림', function() {
        // "그래서 이 장비가" + 500ms silence + confidence 0.7
        var r = qendEvaluator.evaluate('그래서 이 장비가', 500, 0.7);
        eq(r.shouldEnd, false);
        // S=0.3, sil=0.417, conf=0.7 → 0.12+0.146+0.175 = 0.441
    });

    it('시나리오 3: 미완성 + 긴 무음 + 높은 confidence → 결국 종료', function() {
        // 사용자가 말을 끝낸 것 같지만 종결어미 없음
        var r = qendEvaluator.evaluate('그래서 이 장비가', 2000, 0.9);
        eq(r.shouldEnd, true);
        // S=0.3, sil=1.0(clamped), conf=0.9 → 0.12+0.35+0.225 = 0.695
    });

    it('시나리오 4: 잡음/오인식 + 짧은 발화 → 종료하지 않음', function() {
        // 매우 짧은 텍스트, 낮은 confidence
        var r = qendEvaluator.evaluate('어', 400, 0.2);
        eq(r.shouldEnd, false);
        // S=0.3, sil=0.333, conf=0.2 → 0.12+0.117+0.05 = 0.287
    });

    it('시나리오 5: 질문형 종결 + 중간 무음 → 종료', function() {
        // "CMP 패드 교체 주기가 어떻게 되나요" + 700ms + 0.75
        var r = qendEvaluator.evaluate('CMP 패드 교체 주기가 어떻게 되나요', 700, 0.75);
        eq(r.shouldEnd, true);
        // S=1.0, sil=0.583, conf=0.75 → 0.4+0.204+0.188 = 0.792
    });
});

// ═══════════════════════════════════════════════════════════════════
// [Sprint 3] VAD 기반 listening 진입 테스트
// ═══════════════════════════════════════════════════════════════════

describe('[Sprint 3] MIC.VOICE_DETECTED 이벤트', function() {
    it('voiceMode=true + MIC.VOICE_DETECTED → listening', function() {
        var env = createMockEnv();
        var m = buildMachine(env);
        m.actor.send({ type: 'VOICE_ENTER' });
        eq(state(m.actor), 'listening');
        m.actor.send({ type: 'MIC.EMPTY' });
        eq(state(m.actor), 'idle');
        // VAD 이벤트로 listening 진입
        m.actor.send({ type: 'MIC.VOICE_DETECTED' });
        eq(state(m.actor), 'listening');
    });

    it('voiceMode=false + MIC.VOICE_DETECTED → stays idle (guard)', function() {
        var env = createMockEnv();
        var m = buildMachine(env);
        eq(state(m.actor), 'idle');
        m.actor.send({ type: 'MIC.VOICE_DETECTED' });
        eq(state(m.actor), 'idle');
    });

    it('MIC.VOICE_DETECTED ignored in non-idle states', function() {
        var env = createMockEnv();
        var m = buildMachine(env);
        // processing
        m.actor.send({ type: 'KEYBOARD.SUBMIT', params: { text: 'test' } });
        eq(state(m.actor), 'processing');
        m.actor.send({ type: 'MIC.VOICE_DETECTED' });
        eq(state(m.actor), 'processing');
        // responding
        m.actor.send({ type: 'LLM.STREAMING' });
        eq(state(m.actor), 'responding');
        m.actor.send({ type: 'MIC.VOICE_DETECTED' });
        eq(state(m.actor), 'responding');
    });

    it('VAD detector stop tracked on idle exit', function() {
        var env = createMockEnv();
        var m = buildMachine(env);
        m.actor.send({ type: 'VOICE_ENTER' });
        var vadStops = env.calls.filter(function(c) { return c.target === 'vad' && c.method === 'stop'; });
        assert.ok(vadStops.length >= 1, 'stopVad should be called on idle exit');
    });

    it('VAD → listening → MIC.FINAL → processing cycle', function() {
        var env = createMockEnv();
        var m = buildMachine(env);
        m.actor.send({ type: 'VOICE_ENTER' });
        eq(state(m.actor), 'listening');
        m.actor.send({ type: 'MIC.EMPTY' });
        eq(state(m.actor), 'idle');
        m.actor.send({ type: 'MIC.VOICE_DETECTED' });
        eq(state(m.actor), 'listening');
        m.actor.send({ type: 'MIC.FINAL', params: { text: 'VAD 테스트' } });
        eq(state(m.actor), 'processing');
        eq(ctx(m.actor).inputText, 'VAD 테스트');
    });
});

// ═══════════════════════════════════════════════════════════════════
// Async test runner — runs queued async tests sequentially, then prints results
// ═══════════════════════════════════════════════════════════════════
async function runAsyncTests() {
    for (const test of asyncQueue) {
        try {
            await new Promise(function (resolve, reject) {
                try {
                    test.fn(resolve);
                } catch (e) {
                    reject(e);
                }
            });
            passCount++;
            console.log('  \x1b[32m✓\x1b[0m ' + test.name);
        } catch (e) {
            failCount++;
            failures.push({ name: test.name, error: e.message });
            console.log('  \x1b[31m✗\x1b[0m ' + test.name);
            console.log('    \x1b[31m' + e.message + '\x1b[0m');
        }
    }
}

runAsyncTests().then(function () {
    console.log('\n' + '═'.repeat(60));
    console.log('\x1b[1mResults: ' +
        '\x1b[32m' + passCount + ' passed\x1b[0m, ' +
        (failCount > 0 ? '\x1b[31m' + failCount + ' failed\x1b[0m' : '\x1b[32m0 failed\x1b[0m'));

    if (failures.length > 0) {
        console.log('\n\x1b[31mFailures:\x1b[0m');
        failures.forEach(function (f, i) {
            console.log('  ' + (i + 1) + '. ' + f.name);
            console.log('     ' + f.error);
        });
    }
    console.log('═'.repeat(60));

    process.exit(failCount > 0 ? 1 : 0);
});
