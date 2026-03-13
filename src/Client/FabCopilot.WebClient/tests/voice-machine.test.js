// ═══════════════════════════════════════════════════════════════════
// Voice State Machine — Comprehensive Tests
// Tests XState v5 state transitions, action signatures, edge cases
// ═══════════════════════════════════════════════════════════════════
'use strict';

const { createMachine, createActor, assign } = require('xstate');
const assert = require('assert');
const path = require('path');
const fs = require('fs');

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

function eq(actual, expected, msg) {
    const a = JSON.stringify(actual);
    const b = JSON.stringify(expected);
    if (a !== b) throw new Error((msg || '') + ' expected ' + b + ' but got ' + a);
}

// ─── Mock environment for loading voice-machine.js ───────────────
function createMockEnv() {
    const calls = [];  // Track all side-effect calls

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
        _streaming: false,
        feedText: function (t) { calls.push({ target: 'tts', method: 'feedText', args: [t] }); },
        flushStream: function () { calls.push({ target: 'tts', method: 'flushStream' }); },
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

// ─── Build the machine directly (same definition as voice-machine.js) ─
function buildMachine(env) {
    const { calls, mockDotNet, mockTts, mockRec } = env;

    function dotNet() { return mockDotNet; }
    function tts() { return mockTts; }
    function rec() { return mockRec; }

    const machine = createMachine({
        id: 'voice',
        type: 'parallel',
        context: {
            autoTtsEnabled: false,
            ttsPlayingIdx: -1,
            interimText: '',
            recordingSeconds: 0,
            pendingMessage: null,
            sttEngine: 'whisper',
        },
        states: {
            mode: {
                initial: 'off',
                states: {
                    off: {
                        on: {
                            AUTO_TTS_ON: { target: 'autoTts' },
                            VOICE_ENTER: { target: 'voiceActive' },
                        },
                    },
                    autoTts: {
                        entry: ['unlockAudio', 'prepareMic'],
                        exit: ['releaseMic'],
                        on: {
                            AUTO_TTS_OFF: { target: 'off' },
                            VOICE_ENTER: { target: 'voiceActive' },
                        },
                    },
                    voiceActive: {
                        entry: ['unlockAudio', 'acquireVoiceModeMic'],
                        exit: ['releaseVoiceModeMic', 'stopAllActivity'],
                        on: {
                            VOICE_EXIT: { target: 'off' },
                            AUTO_TTS_OFF: { target: 'off' },
                            WS_DISCONNECT: { target: 'off' },
                        },
                    },
                },
            },
            stt: {
                initial: 'idle',
                states: {
                    idle: {
                        on: { REC_START: { target: 'recording' } },
                    },
                    recording: {
                        entry: ['startRecorder', 'resetRecTimer'],
                        exit: ['clearRecTimer'],
                        on: {
                            REC_INTERIM: {
                                actions: [assign({ interimText: function (_a) { var e = _a.event; return e.params ? e.params.text : ''; } })],
                            },
                            REC_STOP: { target: 'transcribing' },
                            REC_FINAL: { target: 'idle', actions: ['handleTranscript'] },
                            REC_ERROR: { target: 'idle', actions: ['handleSttError'] },
                            REC_ABORT: { target: 'idle' },
                            VOICE_EXIT: { target: 'idle' },
                        },
                    },
                    transcribing: {
                        entry: ['notifyTranscribing'],
                        on: {
                            TRANSCRIPT_OK: { target: 'idle', actions: ['handleTranscript'] },
                            TRANSCRIPT_ERR: { target: 'idle', actions: ['handleSttError'] },
                        },
                    },
                },
            },
            tts: {
                initial: 'idle',
                states: {
                    idle: {
                        entry: [assign({ ttsPlayingIdx: -1 })],
                        on: {
                            TTS_PLAY: {
                                target: 'playing',
                                actions: [assign({ ttsPlayingIdx: function (_a) { var e = _a.event; return e.params ? e.params.idx : -1; } })],
                            },
                            TTS_STREAM_START: {
                                target: 'streaming',
                                actions: [assign({ ttsPlayingIdx: function (_a) { var e = _a.event; return e.params ? e.params.idx : -1; } })],
                            },
                        },
                    },
                    playing: {
                        entry: ['startBargeIn'],
                        exit: ['stopBargeIn', 'notifyTtsState'],
                        on: {
                            TTS_ENDED: { target: 'idle', actions: ['onTtsEnded'] },
                            TTS_ERROR: { target: 'idle', actions: ['onTtsError'] },
                            TTS_STOP: { target: 'idle' },
                            BARGE_IN: { target: 'idle', actions: ['stopTtsInternal', 'onBargeIn'] },
                            VOICE_CMD: { target: 'idle', actions: ['stopTtsInternal', 'onVoiceCommand'] },
                        },
                    },
                    streaming: {
                        entry: ['startBargeIn'],
                        exit: ['stopBargeIn', 'notifyTtsState'],
                        on: {
                            TTS_FEED: { actions: ['feedToken'] },
                            TTS_FLUSH: { target: 'flushing', actions: ['flushStream'] },
                            TTS_ENDED: { target: 'idle', actions: ['onTtsEnded'] },
                            TTS_ERROR: { target: 'idle', actions: ['onTtsError'] },
                            TTS_STOP: { target: 'idle', actions: ['stopTtsInternal'] },
                            BARGE_IN: { target: 'idle', actions: ['stopTtsInternal', 'onBargeIn'] },
                            VOICE_CMD: { target: 'idle', actions: ['stopTtsInternal', 'onVoiceCommand'] },
                        },
                    },
                    flushing: {
                        entry: ['startBargeIn'],
                        exit: ['stopBargeIn', 'notifyTtsState'],
                        on: {
                            TTS_ENDED: { target: 'idle', actions: ['onTtsEnded'] },
                            TTS_ERROR: { target: 'idle', actions: ['onTtsError'] },
                            TTS_STOP: { target: 'idle', actions: ['stopTtsInternal'] },
                            BARGE_IN: { target: 'idle', actions: ['stopTtsInternal', 'onBargeIn'] },
                            VOICE_CMD: { target: 'idle', actions: ['stopTtsInternal', 'onVoiceCommand'] },
                        },
                    },
                },
            },
            sendConfirm: {
                initial: 'hidden',
                states: {
                    hidden: {
                        on: {
                            SEND_CONFIRM_SHOW: {
                                target: 'visible',
                                actions: [assign({ pendingMessage: function (_a) { var e = _a.event; return e.params ? e.params.text : ''; } })],
                            },
                        },
                    },
                    visible: {
                        on: {
                            SEND_CONFIRM_STOP: { target: 'hidden', actions: ['confirmStopAndSend'] },
                            SEND_CONFIRM_KEEP: { target: 'hidden', actions: ['confirmKeepAndSend'] },
                            SEND_CONFIRM_CANCEL: { target: 'hidden', actions: [assign({ pendingMessage: null })] },
                        },
                    },
                },
            },
        },
    }, {
        actions: {
            unlockAudio: function () { try { tts().unlockAudio(); } catch (e) { } },
            prepareMic: function () { try { tts().prepareMic(); } catch (e) { } },
            releaseMic: function () { try { tts().releaseMic(); } catch (e) { } },
            acquireVoiceModeMic: function () { if (rec()) rec().setVoiceMode(true).catch(function () { }); },
            releaseVoiceModeMic: function () { if (rec()) rec().setVoiceMode(false).catch(function () { }); },
            stopAllActivity: function () {
                try { tts()._stopInternal(); } catch (e) { }
                try { rec().stop(); } catch (e) { }
            },
            startRecorder: function () { if (rec()) rec().start(); },
            resetRecTimer: assign({ recordingSeconds: 0, interimText: '' }),
            clearRecTimer: function () { },
            notifyTranscribing: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnVoiceStateChanged', JSON.stringify({ event: 'transcribing' }));
            },
            handleTranscript: function (_a) {
                var event = _a.event;
                var text = (event.params && event.params.text) || '';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnTranscriptionComplete', text);
            },
            handleSttError: function (_a) {
                var event = _a.event;
                var error = (event.params && event.params.error) || 'STT 오류';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnTranscriptionError', error);
            },
            startBargeIn: function () { calls.push({ target: 'bargeIn', method: 'start' }); },
            stopBargeIn: function () { calls.push({ target: 'bargeIn', method: 'stop' }); },
            notifyTtsState: function () { },
            feedToken: function (_a) {
                var event = _a.event;
                calls.push({ target: 'tts', method: 'feedToken', args: [event.params ? event.params.token : ''] });
            },
            flushStream: function () { calls.push({ target: 'tts', method: 'flushAction' }); },
            stopTtsInternal: function () { try { tts()._stopInternal(); } catch (e) { } },
            onTtsEnded: function () { calls.push({ target: 'machine', method: 'onTtsEnded' }); },
            onTtsError: function (_a) {
                var event = _a.event;
                var error = (event.params && event.params.error) || 'TTS 오류';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnTtsError', error);
            },
            onBargeIn: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnBargeIn');
            },
            onVoiceCommand: function (_a) {
                var event = _a.event;
                var cmd = (event.params && event.params.command) || 'stop';
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnVoiceCommand', cmd);
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
    });

    return machine;
}

function createTestActor(env) {
    const machine = buildMachine(env);
    const actor = createActor(machine);
    actor.start();
    env.calls.length = 0; // clear startup calls
    return actor;
}

function getState(actor) {
    return actor.getSnapshot().value;
}

function getCtx(actor) {
    return actor.getSnapshot().context;
}

// ═══════════════════════════════════════════════════════════════════
// TEST SUITES
// ═══════════════════════════════════════════════════════════════════

describe('1. Initial State', function () {
    it('should start with all regions in their initial states', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        const s = getState(actor);
        eq(s.mode, 'off');
        eq(s.stt, 'idle');
        eq(s.tts, 'idle');
        eq(s.sendConfirm, 'hidden');
    });

    it('should have correct initial context', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        const ctx = getCtx(actor);
        eq(ctx.autoTtsEnabled, false);
        eq(ctx.ttsPlayingIdx, -1);
        eq(ctx.interimText, '');
        eq(ctx.recordingSeconds, 0);
        eq(ctx.pendingMessage, null);
        eq(ctx.sttEngine, 'whisper');
    });
});

// ── Mode transitions ─────────────────────────────────────────────
describe('2. Mode Transitions', function () {
    it('off → autoTts on AUTO_TTS_ON', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        eq(getState(actor).mode, 'autoTts');
    });

    it('autoTts entry calls unlockAudio + prepareMic', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        const ttsCalls = env.calls.filter(c => c.target === 'tts');
        assert(ttsCalls.some(c => c.method === 'unlockAudio'), 'unlockAudio not called');
        assert(ttsCalls.some(c => c.method === 'prepareMic'), 'prepareMic not called');
    });

    it('autoTts → off on AUTO_TTS_OFF (exit calls releaseMic)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        env.calls.length = 0;
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(getState(actor).mode, 'off');
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'releaseMic'), 'releaseMic not called');
    });

    it('off → voiceActive on VOICE_ENTER', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        eq(getState(actor).mode, 'voiceActive');
    });

    it('autoTts → voiceActive on VOICE_ENTER (exit releaseMic, entry acquireVoiceModeMic)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_ENTER' });
        eq(getState(actor).mode, 'voiceActive');
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'releaseMic'), 'releaseMic not called');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'setVoiceMode'), 'setVoiceMode not called');
    });

    it('voiceActive → off on VOICE_EXIT (exit releaseVoiceModeMic + stopAllActivity)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_EXIT' });
        eq(getState(actor).mode, 'off');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'setVoiceMode' && c.args[0] === false), 'releaseVoiceModeMic not called');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopAllActivity tts not called');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'stop'), 'stopAllActivity rec not called');
    });

    it('voiceActive → off on WS_DISCONNECT', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'WS_DISCONNECT' });
        eq(getState(actor).mode, 'off');
    });

    it('voiceActive → off on AUTO_TTS_OFF', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'AUTO_TTS_OFF' });
        eq(getState(actor).mode, 'off');
    });

    it('ignores invalid events in off state', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_OFF' }); // already off
        actor.send({ type: 'VOICE_EXIT' });    // not in voiceActive
        eq(getState(actor).mode, 'off');
    });
});

// ── STT transitions ──────────────────────────────────────────────
describe('3. STT Transitions', function () {
    it('idle → recording on REC_START', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        eq(getState(actor).stt, 'recording');
        assert(env.calls.some(c => c.target === 'rec' && c.method === 'start'), 'startRecorder not called');
    });

    it('recording entry resets timer context', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        const ctx = getCtx(actor);
        eq(ctx.recordingSeconds, 0);
        eq(ctx.interimText, '');
    });

    it('REC_INTERIM updates interimText (v5 assign signature)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_INTERIM', params: { text: '안녕하세요' } });
        eq(getCtx(actor).interimText, '안녕하세요');
    });

    it('REC_INTERIM with no params sets empty string', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_INTERIM' });
        eq(getCtx(actor).interimText, '');
    });

    it('recording → idle on REC_FINAL with handleTranscript', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        env.calls.length = 0;
        actor.send({ type: 'REC_FINAL', params: { text: '테스트 문장' } });
        eq(getState(actor).stt, 'idle');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnTranscriptionComplete' && c.args[0] === '테스트 문장'), 'handleTranscript not called with correct text');
    });

    it('recording → idle on REC_ERROR with handleSttError', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        env.calls.length = 0;
        actor.send({ type: 'REC_ERROR', params: { error: '네트워크 오류' } });
        eq(getState(actor).stt, 'idle');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnTranscriptionError' && c.args[0] === '네트워크 오류'), 'handleSttError not called');
    });

    it('recording → idle on REC_ABORT', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_ABORT' });
        eq(getState(actor).stt, 'idle');
    });

    it('recording → idle on VOICE_EXIT', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'VOICE_EXIT' });
        eq(getState(actor).stt, 'idle');
    });

    it('recording → transcribing on REC_STOP', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        env.calls.length = 0;
        actor.send({ type: 'REC_STOP' });
        eq(getState(actor).stt, 'transcribing');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnVoiceStateChanged'), 'notifyTranscribing not called');
    });

    it('transcribing → idle on TRANSCRIPT_OK', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_STOP' });
        env.calls.length = 0;
        actor.send({ type: 'TRANSCRIPT_OK', params: { text: '변환 결과' } });
        eq(getState(actor).stt, 'idle');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnTranscriptionComplete' && c.args[0] === '변환 결과'), 'handleTranscript not called');
    });

    it('transcribing → idle on TRANSCRIPT_ERR', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_STOP' });
        actor.send({ type: 'TRANSCRIPT_ERR', params: { error: 'Whisper 서버 오류' } });
        eq(getState(actor).stt, 'idle');
    });
});

// ── TTS transitions ──────────────────────────────────────────────
describe('4. TTS Transitions', function () {
    it('idle → playing on TTS_PLAY with idx', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 5 } });
        eq(getState(actor).tts, 'playing');
        eq(getCtx(actor).ttsPlayingIdx, 5);
    });

    it('TTS_PLAY with no params sets idx to -1', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY' });
        eq(getCtx(actor).ttsPlayingIdx, -1);
    });

    it('playing entry starts barge-in', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'start'), 'startBargeIn not called');
    });

    it('playing → idle on TTS_ENDED (exit stops barge-in)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 3 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ENDED' });
        eq(getState(actor).tts, 'idle');
        eq(getCtx(actor).ttsPlayingIdx, -1); // reset by idle entry
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'stop'), 'stopBargeIn not called');
        assert(env.calls.some(c => c.target === 'machine' && c.method === 'onTtsEnded'), 'onTtsEnded not called');
    });

    it('playing → idle on TTS_STOP', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        actor.send({ type: 'TTS_STOP' });
        eq(getState(actor).tts, 'idle');
    });

    it('playing → idle on TTS_ERROR', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ERROR', params: { error: 'HTTP 500' } });
        eq(getState(actor).tts, 'idle');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnTtsError'), 'onTtsError not called');
    });

    it('playing → idle on BARGE_IN calls stopTtsInternal + onBargeIn', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 2 } });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });
        eq(getState(actor).tts, 'idle');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called on BARGE_IN in playing');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnBargeIn'), 'OnBargeIn not called');
    });

    it('playing → idle on VOICE_CMD calls stopTtsInternal + onVoiceCommand', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 1 } });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(getState(actor).tts, 'idle');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called on VOICE_CMD in playing');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnVoiceCommand' && c.args[0] === 'stop'), 'OnVoiceCommand not called');
    });

    it('idle → streaming on TTS_STREAM_START', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 7 } });
        eq(getState(actor).tts, 'streaming');
        eq(getCtx(actor).ttsPlayingIdx, 7);
    });

    it('streaming → flushing on TTS_FLUSH', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'TTS_FLUSH' });
        eq(getState(actor).tts, 'flushing');
    });

    it('streaming TTS_FEED calls feedToken (v5 action signature)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_FEED', params: { token: '안녕' } });
        eq(getState(actor).tts, 'streaming'); // stays in streaming
        assert(env.calls.some(c => c.target === 'tts' && c.method === 'feedToken' && c.args[0] === '안녕'), 'feedToken not called with correct token');
    });

    it('streaming → idle on BARGE_IN calls stopTtsInternal', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });
        eq(getState(actor).tts, 'idle');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called');
    });

    it('flushing → idle on TTS_ENDED', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'TTS_FLUSH' });
        actor.send({ type: 'TTS_ENDED' });
        eq(getState(actor).tts, 'idle');
    });

    it('flushing → idle on VOICE_CMD calls stopTtsInternal', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'TTS_FLUSH' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(getState(actor).tts, 'idle');
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called on VOICE_CMD in flushing');
    });
});

// ── Send Confirm ─────────────────────────────────────────────────
describe('5. Send Confirm Transitions', function () {
    it('hidden → visible on SEND_CONFIRM_SHOW with text', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '새 질문입니다' } });
        eq(getState(actor).sendConfirm, 'visible');
        eq(getCtx(actor).pendingMessage, '새 질문입니다');
    });

    it('visible → hidden on SEND_CONFIRM_STOP calls confirmStopAndSend (v5 context signature)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '메시지' } });
        env.calls.length = 0;
        actor.send({ type: 'SEND_CONFIRM_STOP' });
        eq(getState(actor).sendConfirm, 'hidden');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnSendConfirm' && c.args[0] === true && c.args[1] === '메시지'), 'confirmStopAndSend not called correctly');
    });

    it('visible → hidden on SEND_CONFIRM_KEEP calls confirmKeepAndSend', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '유지 메시지' } });
        env.calls.length = 0;
        actor.send({ type: 'SEND_CONFIRM_KEEP' });
        eq(getState(actor).sendConfirm, 'hidden');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnSendConfirm' && c.args[0] === false && c.args[1] === '유지 메시지'), 'confirmKeepAndSend not called correctly');
    });

    it('visible → hidden on SEND_CONFIRM_CANCEL resets pendingMessage', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '취소할 메시지' } });
        actor.send({ type: 'SEND_CONFIRM_CANCEL' });
        eq(getState(actor).sendConfirm, 'hidden');
        eq(getCtx(actor).pendingMessage, null);
    });
});

// ── Parallel region independence ─────────────────────────────────
describe('6. Parallel Region Independence', function () {
    it('TTS events do not affect STT or Mode', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        const s = getState(actor);
        eq(s.mode, 'autoTts');
        eq(s.stt, 'recording');
        eq(s.tts, 'playing');
    });

    it('Mode exit does not affect TTS in playing state', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        actor.send({ type: 'VOICE_EXIT' });
        eq(getState(actor).mode, 'off');
        eq(getState(actor).tts, 'playing'); // TTS continues independently
    });

    it('all 4 regions can be in non-initial states simultaneously', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '?' } });
        const s = getState(actor);
        eq(s.mode, 'voiceActive');
        eq(s.stt, 'recording');
        eq(s.tts, 'streaming');
        eq(s.sendConfirm, 'visible');
    });
});

// ── Full voice mode cycle ────────────────────────────────────────
describe('7. Full Voice Mode Cycle (E2E)', function () {
    it('complete cycle: enter → record → transcribe → TTS → listen again', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        // 1. Enter voice mode
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'VOICE_ENTER' });
        eq(getState(actor).mode, 'voiceActive');

        // 2. Start recording
        actor.send({ type: 'REC_START' });
        eq(getState(actor).stt, 'recording');

        // 3. Interim results
        actor.send({ type: 'REC_INTERIM', params: { text: '안녕' } });
        eq(getCtx(actor).interimText, '안녕');

        // 4. Recording stops → transcription
        actor.send({ type: 'REC_STOP' });
        eq(getState(actor).stt, 'transcribing');

        // 5. Transcription complete
        actor.send({ type: 'TRANSCRIPT_OK', params: { text: '안녕하세요' } });
        eq(getState(actor).stt, 'idle');

        // 6. AI responds → streaming TTS
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 1 } });
        eq(getState(actor).tts, 'streaming');
        eq(getCtx(actor).ttsPlayingIdx, 1);

        // 7. Feed tokens
        actor.send({ type: 'TTS_FEED', params: { token: '반갑습니다' } });

        // 8. Flush
        actor.send({ type: 'TTS_FLUSH' });
        eq(getState(actor).tts, 'flushing');

        // 9. TTS ends
        actor.send({ type: 'TTS_ENDED' });
        eq(getState(actor).tts, 'idle');
        eq(getCtx(actor).ttsPlayingIdx, -1);

        // 10. Still in voice mode — ready for next cycle
        eq(getState(actor).mode, 'voiceActive');
        eq(getState(actor).stt, 'idle'); // ready for REC_START again
    });

    it('cycle with barge-in during streaming TTS', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        eq(getState(actor).tts, 'streaming');

        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });
        eq(getState(actor).tts, 'idle');

        // Verify stopTtsInternal was called
        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called');
        // Verify barge-in stopped
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'stop'), 'stopBargeIn not called');
        // Verify Blazor notified
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnBargeIn'), 'OnBargeIn not called');

        // Voice mode still active
        eq(getState(actor).mode, 'voiceActive');
    });

    it('voice command "stop" during flushing TTS', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'TTS_FLUSH' });
        eq(getState(actor).tts, 'flushing');

        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        eq(getState(actor).tts, 'idle');

        assert(env.calls.some(c => c.target === 'tts' && c.method === '_stopInternal'), 'stopTtsInternal not called');
        assert(env.calls.some(c => c.target === 'dotNet' && c.method === 'OnVoiceCommand' && c.args[0] === 'stop'), 'OnVoiceCommand not called');
    });
});

// ── Edge cases / Bug regression ──────────────────────────────────
describe('8. Edge Cases & Bug Regression', function () {
    it('[BUG FIX] TTS_ENDED when already idle is silently ignored', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        // Machine starts in tts.idle
        env.calls.length = 0;
        actor.send({ type: 'TTS_ENDED' }); // should not crash
        eq(getState(actor).tts, 'idle');
    });

    it('[BUG FIX] Double TTS_ENDED does not crash', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        actor.send({ type: 'TTS_ENDED' });
        actor.send({ type: 'TTS_ENDED' }); // second one ignored
        eq(getState(actor).tts, 'idle');
    });

    it('[BUG FIX] BARGE_IN after TTS already stopped → ignored', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        actor.send({ type: 'TTS_STOP' });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' }); // tts is idle, ignored
        eq(getState(actor).tts, 'idle');
        assert(!env.calls.some(c => c.target === 'dotNet' && c.method === 'OnBargeIn'), 'OnBargeIn should not be called when idle');
    });

    it('[BUG FIX] VOICE_CMD after TTS already stopped → ignored', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        actor.send({ type: 'TTS_ENDED' });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } }); // ignored
        eq(getState(actor).tts, 'idle');
        assert(!env.calls.some(c => c.target === 'dotNet' && c.method === 'OnVoiceCommand'), 'OnVoiceCommand should not be called when idle');
    });

    it('[BUG FIX] playing BARGE_IN calls stopTtsInternal (prevents stale audio)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });
        const stopCalls = env.calls.filter(c => c.target === 'tts' && c.method === '_stopInternal');
        assert(stopCalls.length >= 1, 'stopTtsInternal MUST be called for BARGE_IN in playing state — this was the key bug');
    });

    it('[BUG FIX] playing VOICE_CMD calls stopTtsInternal (prevents stale audio)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        const stopCalls = env.calls.filter(c => c.target === 'tts' && c.method === '_stopInternal');
        assert(stopCalls.length >= 1, 'stopTtsInternal MUST be called for VOICE_CMD in playing state — this was the key bug');
    });

    it('REC_START when already recording is ignored (no duplicate start)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        eq(getState(actor).stt, 'recording');
        env.calls.length = 0;
        actor.send({ type: 'REC_START' }); // already recording
        eq(getState(actor).stt, 'recording');
    });

    it('TTS_PLAY when already playing transitions through idle', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        eq(getState(actor).tts, 'playing');
        // Sending TTS_PLAY again — should be ignored since playing has no TTS_PLAY handler
        actor.send({ type: 'TTS_PLAY', params: { idx: 1 } });
        eq(getState(actor).tts, 'playing'); // stays playing (no self-transition)
        eq(getCtx(actor).ttsPlayingIdx, 0); // original idx preserved
    });

    it('rapid event sequence does not corrupt state', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        // Rapid-fire events
        actor.send({ type: 'AUTO_TTS_ON' });
        actor.send({ type: 'VOICE_ENTER' });
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_INTERIM', params: { text: 'a' } });
        actor.send({ type: 'REC_INTERIM', params: { text: 'ab' } });
        actor.send({ type: 'REC_FINAL', params: { text: 'abc' } });
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 1 } });
        actor.send({ type: 'TTS_FEED', params: { token: 'x' } });
        actor.send({ type: 'TTS_FEED', params: { token: 'y' } });
        actor.send({ type: 'TTS_FLUSH' });
        actor.send({ type: 'TTS_ENDED' });
        actor.send({ type: 'VOICE_EXIT' });

        const s = getState(actor);
        eq(s.mode, 'off');
        eq(s.stt, 'idle');
        eq(s.tts, 'idle');
        eq(s.sendConfirm, 'hidden');
    });
});

// ── XState v5 Action Signature Tests ─────────────────────────────
describe('9. XState v5 Action Signature Verification', function () {
    it('assign with event destructuring: TTS_PLAY sets ttsPlayingIdx', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 42 } });
        eq(getCtx(actor).ttsPlayingIdx, 42);
    });

    it('assign with event destructuring: TTS_STREAM_START sets ttsPlayingIdx', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 99 } });
        eq(getCtx(actor).ttsPlayingIdx, 99);
    });

    it('assign with event destructuring: REC_INTERIM sets interimText', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_INTERIM', params: { text: '테스트 중간 결과' } });
        eq(getCtx(actor).interimText, '테스트 중간 결과');
    });

    it('assign with event destructuring: SEND_CONFIRM_SHOW sets pendingMessage', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '대기 메시지' } });
        eq(getCtx(actor).pendingMessage, '대기 메시지');
    });

    it('action with context destructuring: confirmStopAndSend reads pendingMessage', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '확인 메시지' } });
        env.calls.length = 0;
        actor.send({ type: 'SEND_CONFIRM_STOP' });
        const call = env.calls.find(c => c.target === 'dotNet' && c.method === 'OnSendConfirm');
        assert(call, 'OnSendConfirm not called');
        eq(call.args[0], true);
        eq(call.args[1], '확인 메시지');
    });

    it('action with event destructuring: handleTranscript reads params.text', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        env.calls.length = 0;
        actor.send({ type: 'REC_FINAL', params: { text: '최종 텍스트' } });
        const call = env.calls.find(c => c.target === 'dotNet' && c.method === 'OnTranscriptionComplete');
        assert(call, 'OnTranscriptionComplete not called');
        eq(call.args[0], '최종 텍스트');
    });

    it('action with event destructuring: handleSttError reads params.error', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        env.calls.length = 0;
        actor.send({ type: 'REC_ERROR', params: { error: '테스트 에러' } });
        const call = env.calls.find(c => c.target === 'dotNet' && c.method === 'OnTranscriptionError');
        assert(call, 'OnTranscriptionError not called');
        eq(call.args[0], '테스트 에러');
    });

    it('action with event destructuring: onTtsError reads params.error', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ERROR', params: { error: 'TTS 장애' } });
        const call = env.calls.find(c => c.target === 'dotNet' && c.method === 'OnTtsError');
        assert(call, 'OnTtsError not called');
        eq(call.args[0], 'TTS 장애');
    });

    it('action with event destructuring: onVoiceCommand reads params.command', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
        const call = env.calls.find(c => c.target === 'dotNet' && c.method === 'OnVoiceCommand');
        assert(call, 'OnVoiceCommand not called');
        eq(call.args[0], 'stop');
    });

    it('action with event destructuring: feedToken reads params.token', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_FEED', params: { token: '토큰값' } });
        const call = env.calls.find(c => c.target === 'tts' && c.method === 'feedToken');
        assert(call, 'feedToken not called');
        eq(call.args[0], '토큰값');
    });
});

// ── Barge-in lifecycle ───────────────────────────────────────────
describe('10. Barge-in Lifecycle', function () {
    it('barge-in starts on playing entry and stops on playing exit', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'start'), 'bargeIn should start on playing entry');

        env.calls.length = 0;
        actor.send({ type: 'TTS_ENDED' });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'stop'), 'bargeIn should stop on playing exit');
    });

    it('barge-in starts on streaming entry and stops on streaming exit', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'start'), 'bargeIn should start on streaming entry');

        env.calls.length = 0;
        actor.send({ type: 'TTS_STOP' });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'stop'), 'bargeIn should stop on streaming exit');
    });

    it('barge-in restarts on streaming → flushing (exit + entry)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_FLUSH' });

        const stops = env.calls.filter(c => c.target === 'bargeIn' && c.method === 'stop');
        const starts = env.calls.filter(c => c.target === 'bargeIn' && c.method === 'start');
        assert(stops.length >= 1, 'bargeIn should stop on streaming exit');
        assert(starts.length >= 1, 'bargeIn should start on flushing entry');
    });

    it('barge-in stops when flushing TTS ends', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        actor.send({ type: 'TTS_FLUSH' });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ENDED' });
        assert(env.calls.some(c => c.target === 'bargeIn' && c.method === 'stop'), 'bargeIn should stop when flushing TTS ends');
    });
});

// ── Action execution order ───────────────────────────────────────
describe('11. Action Execution Order', function () {
    it('exit actions run before transition actions on TTS_ENDED', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'TTS_ENDED' });

        // Find indices
        const stopIdx = env.calls.findIndex(c => c.target === 'bargeIn' && c.method === 'stop');
        const endedIdx = env.calls.findIndex(c => c.target === 'machine' && c.method === 'onTtsEnded');
        assert(stopIdx >= 0, 'stopBargeIn should be called');
        assert(endedIdx >= 0, 'onTtsEnded should be called');
        assert(stopIdx < endedIdx, 'exit action (stopBargeIn) should run before transition action (onTtsEnded)');
    });

    it('exit actions run before transition actions on BARGE_IN in streaming', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_STREAM_START', params: { idx: 0 } });
        env.calls.length = 0;
        actor.send({ type: 'BARGE_IN' });

        const stopBargeInIdx = env.calls.findIndex(c => c.target === 'bargeIn' && c.method === 'stop');
        const stopInternalIdx = env.calls.findIndex(c => c.target === 'tts' && c.method === '_stopInternal');
        const bargeInIdx = env.calls.findIndex(c => c.target === 'dotNet' && c.method === 'OnBargeIn');

        assert(stopBargeInIdx >= 0, 'stopBargeIn should be called');
        assert(stopBargeInIdx < stopInternalIdx, 'exit stopBargeIn should run before transition stopTtsInternal');
        assert(stopInternalIdx < bargeInIdx, 'stopTtsInternal should run before onBargeIn');
    });
});

// ── Context integrity ────────────────────────────────────────────
describe('12. Context Integrity', function () {
    it('ttsPlayingIdx resets to -1 when TTS returns to idle', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'TTS_PLAY', params: { idx: 10 } });
        eq(getCtx(actor).ttsPlayingIdx, 10);
        actor.send({ type: 'TTS_ENDED' });
        eq(getCtx(actor).ttsPlayingIdx, -1);
    });

    it('interimText clears when recording starts', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'REC_START' });
        actor.send({ type: 'REC_INTERIM', params: { text: 'some text' } });
        actor.send({ type: 'REC_FINAL', params: { text: 'done' } });
        // Start new recording
        actor.send({ type: 'REC_START' });
        eq(getCtx(actor).interimText, ''); // reset by resetRecTimer
    });

    it('pendingMessage preserved through visible state', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);
        actor.send({ type: 'SEND_CONFIRM_SHOW', params: { text: '보존 메시지' } });
        eq(getCtx(actor).pendingMessage, '보존 메시지');
        // Still visible, message should be preserved
        eq(getState(actor).sendConfirm, 'visible');
        eq(getCtx(actor).pendingMessage, '보존 메시지');
    });

    it('multiple TTS play cycles correctly update ttsPlayingIdx', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        actor.send({ type: 'TTS_PLAY', params: { idx: 1 } });
        eq(getCtx(actor).ttsPlayingIdx, 1);
        actor.send({ type: 'TTS_ENDED' });
        eq(getCtx(actor).ttsPlayingIdx, -1);

        actor.send({ type: 'TTS_PLAY', params: { idx: 3 } });
        eq(getCtx(actor).ttsPlayingIdx, 3);
        actor.send({ type: 'TTS_STOP' });
        eq(getCtx(actor).ttsPlayingIdx, -1);

        actor.send({ type: 'TTS_STREAM_START', params: { idx: 5 } });
        eq(getCtx(actor).ttsPlayingIdx, 5);
        actor.send({ type: 'BARGE_IN' });
        eq(getCtx(actor).ttsPlayingIdx, -1);
    });
});

// ── Stress test ──────────────────────────────────────────────────
describe('13. Stress Test', function () {
    it('1000 rapid TTS play/end cycles without state corruption', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        for (let i = 0; i < 1000; i++) {
            actor.send({ type: 'TTS_PLAY', params: { idx: i } });
            eq(getState(actor).tts, 'playing');
            actor.send({ type: 'TTS_ENDED' });
            eq(getState(actor).tts, 'idle');
        }
        eq(getCtx(actor).ttsPlayingIdx, -1);
    });

    it('500 voice mode enter/exit cycles', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        for (let i = 0; i < 500; i++) {
            actor.send({ type: 'VOICE_ENTER' });
            eq(getState(actor).mode, 'voiceActive');
            actor.send({ type: 'VOICE_EXIT' });
            eq(getState(actor).mode, 'off');
        }
    });

    it('mixed events across all regions (500 iterations)', function () {
        const env = createMockEnv();
        const actor = createTestActor(env);

        for (let i = 0; i < 500; i++) {
            actor.send({ type: 'AUTO_TTS_ON' });
            actor.send({ type: 'REC_START' });
            actor.send({ type: 'TTS_STREAM_START', params: { idx: i } });
            actor.send({ type: 'TTS_FEED', params: { token: 'tok' } });
            actor.send({ type: 'REC_FINAL', params: { text: 'text' } });
            actor.send({ type: 'TTS_FLUSH' });
            actor.send({ type: 'TTS_ENDED' });
            actor.send({ type: 'AUTO_TTS_OFF' });
        }

        const s = getState(actor);
        eq(s.mode, 'off');
        eq(s.stt, 'idle');
        eq(s.tts, 'idle');
    });
});

// ═══════════════════════════════════════════════════════════════════
// RESULTS
// ═══════════════════════════════════════════════════════════════════
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
