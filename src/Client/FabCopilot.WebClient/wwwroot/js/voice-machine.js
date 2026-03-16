// ═══════════════════════════════════════════════════════════════════
// Multimodal Assistant State Machine — XState v5
// v2.4 Sequential Design: idle → listening → processing → responding → idle
// JS machine is the single orchestrator; C# is the executor.
// ═══════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    var X = window.XState;
    if (!X) { console.error('[Machine] XState not loaded'); return; }

    var createMachine = X.createMachine;
    var createActor = X.createActor;
    var assign = X.assign;
    var raise = X.raise;
    var enqueueActions = X.enqueueActions;

    // ─── Stop command keywords ───
    var STOP_COMMANDS = ['멈춰', '그만', '중지', '스톱', 'stop', '멈춰라', '그만해', '정지', '스탑'];

    // ─── Timer durations (ms) ───
    var AUTO_LISTEN_DELAY = 500;   // idle → listening 자동 전이 대기
    var LISTEN_TIMEOUT = 30000;    // 30s: listening deadman
    var PROCESS_TIMEOUT = 60000;   // 60s: processing deadman
    var RESPOND_TIMEOUT = 180000;  // 3min: responding deadman
    var CLEANUP_TIMEOUT = 3000;    // 3s: stopping cleanup deadline
    var VOICE_CMD_DELAY = 1000;    // 1s: 음성 명령 후 대기
    var RECOVER_DELAY = 1500;      // 1.5s: recovering 재시도 대기
    var MAX_RETRIES = 2;           // 최대 재시도 횟수
    var FOLLOWUP_TIMEOUT = 2000;   // 2s: followupIntentActor timeout

    // ─── Followup Intent Patterns (v2.4 §9.3) ───
    var FOLLOWUP_QUESTION_ENDINGS = [
        '?', '？',
        '뭐', '뭘', '뭔', '무엇',
        '왜', '어디', '언제', '어떻게', '어떤', '얼마',
        '누가', '누구',
        '인가요', '인가', '인지', '나요', '가요', '할까',
        '할까요', '줄래', '줄까', '있어', '있나',
        'what', 'why', 'where', 'when', 'how', 'who', 'which'
    ];
    var FOLLOWUP_MIN_LENGTH = 2;
    var FOLLOWUP_MAX_LENGTH = 100;

    // ─── Error Announcement Messages (v2.4 §6.3 / M-2) ───
    var ERROR_ANNOUNCEMENTS = {
        NETWORK_TIMEOUT:    { message: '연결이 잠시 불안정했어요. 다시 시도할게요.', retryable: true },
        RATE_LIMIT:         { message: '잠시 후 다시 시도할게요.', retryable: true },
        LLM_UNAVAILABLE:    { message: '서비스가 잠시 응답하지 않아요. 다시 시도할게요.', retryable: true },
        STREAM_INTERRUPTED: { message: '응답이 끊겼어요. 다시 시작할게요.', retryable: true },
        LLM_ERROR:          { message: '응답 처리 중 오류가 발생했어요. 다시 시도할게요.', retryable: true },
        LLM_STREAM_ERROR:   { message: '응답 스트리밍이 중단되었어요. 다시 시도할게요.', retryable: true },
        TIMEOUT:            { message: '응답 시간이 초과되었어요. 다시 시도할게요.', retryable: true },
        INTERNAL_ERROR:     { message: '죄송해요, 문제가 발생했어요. 다시 말씀해 주세요.', retryable: false },
        UNKNOWN:            { message: '알 수 없는 오류가 발생했어요.', retryable: true }
    };
    var FATAL_ANNOUNCEMENT = '여러 번 시도했지만 실패했어요. 다시 말씀해 주세요.';

    // ─── Volume ducking constants ───
    var DUCK_GAIN = 0.3;           // 음성 감지 시 TTS 볼륨 (30%)
    var DUCK_RAMP_MS = 0.15;      // gain 전이 시간 (150ms)

    // ─── Helper: get references ───
    function dotNet() { return window._voiceDotNetRef; }
    function ttsObj() { return window.fabTts; }
    function recObj() { return window.audioRecorder; }

    // ═══════════════════════════════════════════════════════════════
    // Machine Definition — v2.4 Sequential
    // ═══════════════════════════════════════════════════════════════
    var machine = createMachine({
        id: 'assistant',
        initial: 'idle',

        context: {
            // ── 모드 ──
            voiceMode: false,
            ttsEnabled: false,       // autoTts or voiceMode

            // ── 입력 ──
            inputText: '',

            // ── 응답 완료 추적 ──
            screenDone: false,
            ttsDone: false,

            // ── 인터럽트 / 라우팅 ──
            pendingUserText: '',
            stopReason: '',          // 'voice' | 'button' | 'system' | ''

            // ── 에러 복구 ──
            retryCount: 0,
            lastError: null,         // { code: string, message: string }

            // ── 기존 호환 ──
            interimText: '',
            pendingMessage: null,    // sendConfirm
        },

        states: {
            // ═══ IDLE ═══════════════════════════════════════════════
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
                    // ── VAD 음성 감지 (v2.4 Sprint 3) ──
                    'MIC.VOICE_DETECTED': [{
                        guard: 'isVoiceMode',
                        target: 'listening',
                    }],
                    // ── 키보드 입력 ──
                    'KEYBOARD.SUBMIT': {
                        target: 'processing',
                        actions: [assign({
                            inputText: function (_a) { var e = _a.event; return (e.params && e.params.text) || ''; },
                        })],
                    },
                    // ── 마이크 시작 (수동 or 음성모드) ──
                    'MIC.START': 'listening',
                    // ── 음성 모드 진입 ──
                    'VOICE_ENTER': {
                        target: 'listening',
                        actions: [
                            assign({ voiceMode: true, ttsEnabled: true }),
                            'enterVoiceMode',
                        ],
                    },
                    // ── 음성 모드 해제 ──
                    'VOICE_EXIT': {
                        actions: [assign({ voiceMode: false }), 'exitVoiceMode'],
                    },
                    // ── Auto TTS 토글 ──
                    'AUTO_TTS_ON': {
                        actions: [assign({ ttsEnabled: true }), 'enableAutoTts'],
                    },
                    'AUTO_TTS_OFF': {
                        actions: [
                            assign({ ttsEnabled: false, voiceMode: false }),
                            'disableAutoTts',
                        ],
                    },
                    // ── Send Confirm (TTS 재생 중 키보드 전송 시) ──
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

            // ═══ LISTENING ══════════════════════════════════════════
            listening: {
                entry: ['startListening'],
                exit: ['stopRecording'],
                on: {
                    // ── ASR 결과 ──
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
                    'MIC.EMPTY': {
                        target: 'idle',
                    },
                    'MIC.ERROR': {
                        target: 'idle',
                        actions: ['handleMicError'],
                    },
                    'MIC.FATAL': {
                        target: 'idle',
                        actions: [assign({ voiceMode: false }), 'handleMicFatal'],
                    },
                    // ── 수동 중지 ──
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
                        actions: [function () { console.warn('[Machine] DEADMAN: listening timeout'); }],
                    },
                },
            },

            // ═══ PROCESSING ═════════════════════════════════════════
            processing: {
                entry: ['sendToLlm'],
                on: {
                    // ── LLM 응답 시작 ──
                    'LLM.STREAMING': {
                        target: 'responding',
                    },
                    // ── LLM 완료 (TTS 없이 바로 완료) ──
                    'LLM.DONE': {
                        target: 'idle',
                    },
                    // ── LLM 에러 → recovering (재시도 가능) ──
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
                    // ── 수동 중지 ──
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
                            function () { console.warn('[Machine] DEADMAN: processing timeout'); },
                        ],
                    },
                },
            },

            // ═══ RESPONDING ═════════════════════════════════════════
            // 화면 출력 + TTS 재생이 동시 진행. 둘 다 완료되면 idle로 복귀.
            responding: {
                entry: [
                    assign({ screenDone: false, ttsDone: false }),
                    'startBargeIn',
                    'notifyResponding',
                ],
                exit: ['stopBargeIn', 'cancelFollowup'],
                on: {
                    // ── 화면 출력 완료 ──
                    'SCREEN.DONE': {
                        actions: [assign({ screenDone: true })],
                    },
                    // ── TTS 완료 ──
                    'TTS_ENDED': {
                        actions: [assign({ ttsDone: true }), 'notifyTtsEnded'],
                    },
                    'TTS_ERROR': {
                        actions: [assign({ ttsDone: true }), 'notifyTtsError'],
                    },
                    // ── Barge-in: TTS만 중지, 화면은 계속 ──
                    'BARGE_IN': {
                        actions: [
                            'stopTts', assign({ ttsDone: true }),
                            'notifyBargeIn',
                        ],
                    },
                    // ── 음성 명령 (멈춰): 전체 중지 ──
                    'VOICE_CMD': {
                        target: 'stopping',
                        actions: [assign({ stopReason: 'voice' }), 'notifyVoiceCmd'],
                    },
                    // ── Followup Intent (v2.4 §9.3) ──
                    'CMD.UNCLASSIFIED': {
                        actions: ['forwardToFollowupIntent'],
                    },
                    'CMD.UNRESOLVED': {
                        actions: ['restoreVolume'],
                    },
                    'USER.FOLLOWUP_TEXT': {
                        target: 'stopping',
                        actions: [
                            assign({
                                pendingUserText: function (_a) { return _a.event.params.text; },
                                stopReason: 'voice',
                            }),
                            'savePendingFollowup',
                            'stopTts',
                        ],
                    },
                    // ── 버튼 중지 ──
                    'BUTTON.STOP': {
                        target: 'stopping',
                        actions: [assign({ stopReason: 'button' })],
                    },
                    // ── 음성 모드 해제 ──
                    'VOICE_EXIT': {
                        target: 'stopping',
                        actions: [assign({ voiceMode: false, stopReason: 'system' })],
                    },
                    // ── LLM 에러 (스트리밍 중) → recovering ──
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
                // ── 완료 조건: screenDone && (ttsDone || !ttsEnabled) → idle ──
                always: [{
                    guard: 'isResponseComplete',
                    target: 'idle',
                }],
                after: {
                    respondTimeout: {
                        target: 'idle',
                        actions: [function () { console.warn('[Machine] DEADMAN: responding timeout'); }],
                    },
                },
            },

            // ═══ RECOVERING ═══════════════════════════════════════════
            // 에러 발생 시 재시도 또는 복구. retryCount <= MAX_RETRIES면 재시도.
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
                        actions: [function () { console.log('[Machine] recovering → retry'); }],
                    }, {
                        target: 'idle',
                        actions: [
                            assign({ retryCount: 0 }),
                            'notifyRecoveryFailed',
                        ],
                    }],
                },
            },

            // ═══ STOPPING ═══════════════════════════════════════════
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
        // ═══ Guards ═══
        guards: {
            isVoiceMode: function (_a) { return _a.context.voiceMode; },
            isResponseComplete: function (_a) {
                var c = _a.context;
                return c.screenDone && (c.ttsDone || !c.ttsEnabled);
            },
            hasPendingUserText: function (_a) { return !!_a.context.pendingUserText; },
            canRetry: function (_a) { return _a.context.retryCount <= MAX_RETRIES; },
        },

        // ═══ Actions ═══
        actions: {
            // ── 세션 초기화 ──
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

            // ── 모드 진입/해제 ──
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

            // ── Listening ──
            startListening: function () {
                var d = dotNet();
                if (d) {
                    console.log('[Machine] → listening: calling OnMachineStartListening');
                    d.invokeMethodAsync('OnMachineStartListening').catch(function (err) {
                        console.error('[Machine] startListening failed:', err);
                        actor.send({ type: 'MIC.ERROR' });
                    });
                } else {
                    actor.send({ type: 'MIC.ERROR' });
                }
            },
            stopRecording: function () {
                // listening exit: audioRecorder는 C# 콜백 통해 자연 정지
                // 강제 정지 필요 시 여기서 처리
            },
            handleMicError: function () {
                console.warn('[Machine] MIC.ERROR → idle (will auto-retry if voiceMode)');
            },
            handleMicFatal: function () {
                console.error('[Machine] MIC.FATAL → exit voice mode');
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineExitVoiceMode');
            },

            // ── Processing ──
            sendToLlm: function (_a) {
                var context = _a.context;
                var text = context.inputText || '';
                var d = dotNet();
                if (d && text) {
                    console.log('[Machine] → processing: sending "' + text.substring(0, 40) + '"');
                    d.invokeMethodAsync('OnMachineSendMessage', text).catch(function (err) {
                        console.error('[Machine] sendToLlm failed:', err);
                        actor.send({ type: 'LLM.ERROR' });
                    });
                } else {
                    console.warn('[Machine] sendToLlm: no text or dotNetRef');
                    actor.send({ type: 'LLM.ERROR' });
                }
            },
            handleLlmError: function () {
                console.warn('[Machine] LLM.ERROR');
            },

            // ── Responding ──
            notifyResponding: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineResponding');
            },
            notifyTtsEnded: function () {
                console.log('[Machine] TTS_ENDED in responding');
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

            // ── Barge-in ──
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

            // ── Stopping ──
            cleanup: function () {
                console.log('[Machine] → stopping: cleanup');
                try { ttsObj()._stopInternal(); } catch (e) { }
                try { if (recObj()) recObj().stop(); } catch (e) { }
                // 짧은 지연 후 CLEANUP.DONE 발행
                setTimeout(function () {
                    actor.send({ type: 'CLEANUP.DONE' });
                }, 100);
            },
            notifyStopping: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineStopping');
            },

            // ── Idle ──
            notifyIdle: function () {
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineIdle');
            },

            // ── VAD (Voice Activity Detection) ──
            startVad: function (_a) {
                var context = _a.context;
                if (context.voiceMode) {
                    _startVadDetector();
                }
            },
            stopVad: function () {
                _stopVadDetector();
            },

            // ── Recovering ──
            notifyRecovering: function (_a) {
                var context = _a.context;
                var err = context.lastError || { code: 'UNKNOWN', message: '알 수 없는 오류' };
                console.warn('[Machine] recovering: retry ' + context.retryCount + '/' + MAX_RETRIES + ' — ' + err.code);
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineRecovering', err.code, err.message, context.retryCount);
                // Voice UX: announce error via TTS in voice mode
                if (context.voiceMode) {
                    _announceError(err.code);
                }
            },
            cleanupForRetry: function () {
                // 스트리밍 TTS 중이었으면 정지
                try { ttsObj()._stopInternal(); } catch (e) { }
            },
            notifyRecoveryFailed: function (_a) {
                var context = _a.context;
                var err = context.lastError || { code: 'UNKNOWN', message: '알 수 없는 오류' };
                console.error('[Machine] recovery FAILED after ' + MAX_RETRIES + ' retries: ' + err.code);
                var d = dotNet();
                if (d) d.invokeMethodAsync('OnMachineRecoveryFailed', err.code, err.message);
                // Voice UX: announce fatal error via TTS in voice mode
                if (context.voiceMode) {
                    _announceFatalError();
                }
            },

            // ── Send Confirm ──
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

            // ── Followup Intent (v2.4 §9.3) ──
            forwardToFollowupIntent: function (_a) {
                var event = _a.event;
                if (event.type === 'CMD.UNCLASSIFIED' && event.params) {
                    _handleFollowupUnclassified(event.params.text, event.params.confidence);
                }
            },
            restoreVolume: function () {
                _duckVolume(false);
                console.log('[Machine] CMD.UNRESOLVED → volume restored');
            },
            savePendingFollowup: function (_a) {
                var event = _a.event;
                if (event.type === 'USER.FOLLOWUP_TEXT' && event.params) {
                    console.log('[Machine] USER.FOLLOWUP_TEXT: "' + event.params.text.substring(0, 30) + '"');
                }
            },
            cancelFollowup: function () {
                _cancelFollowupPending();
            },
        },

        // ═══ Delays ═══
        delays: {
            autoListenDelay: AUTO_LISTEN_DELAY,
            listenTimeout: LISTEN_TIMEOUT,
            processTimeout: PROCESS_TIMEOUT,
            respondTimeout: RESPOND_TIMEOUT,
            cleanupTimeout: CLEANUP_TIMEOUT,
            recoverDelay: RECOVER_DELAY,
        },
    });

    // ═══════════════════════════════════════════════════════════════
    // Actor (runtime instance)
    // ═══════════════════════════════════════════════════════════════
    var actor = createActor(machine);

    // Subscribe for debug logging
    actor.subscribe(function (snapshot) {
        var s = snapshot.value;
        console.log('[Machine]', typeof s === 'string' ? s : JSON.stringify(s));
    });

    actor.start();

    // ═══════════════════════════════════════════════════════════════
    // Barge-in: RMS detector
    // ═══════════════════════════════════════════════════════════════
    var _rms = {
        ctx: null, analyser: null, interval: null, speechStart: 0,
        THRESHOLD: 0.02, CONFIRM_MS: 300
    };

    function _startRmsDetector() {
        _stopRmsDetector();
        var mic = (recObj() && recObj()._persistentStream);
        if (!mic || !mic.active) mic = ttsObj() && ttsObj()._bargeInMicStream;
        if (!mic || !mic.active) {
            console.log('[Machine] RMS: no mic');
            return;
        }
        try {
            _rms.ctx = new (window.AudioContext || window.webkitAudioContext)();
            var src = _rms.ctx.createMediaStreamSource(mic);
            _rms.analyser = _rms.ctx.createAnalyser();
            _rms.analyser.fftSize = 512;
            src.connect(_rms.analyser);

            var bufLen = _rms.analyser.fftSize;
            var buf = new Float32Array(bufLen);
            _rms.speechStart = 0;

            _rms.interval = setInterval(function () {
                var snap = actor.getSnapshot();
                var state = snap.value;
                // RMS 감지는 responding 상태에서만 활성
                if (state !== 'responding' || !_rms.analyser) return;

                _rms.analyser.getFloatTimeDomainData(buf);
                var sum = 0;
                for (var i = 0; i < bufLen; i++) sum += buf[i] * buf[i];
                var rms = Math.sqrt(sum / bufLen);

                if (rms > _rms.THRESHOLD) {
                    if (_rms.speechStart === 0) {
                        _rms.speechStart = Date.now();
                        // Volume ducking: 음성 감지 즉시 TTS 볼륨 낮춤
                        _duckVolume(true);
                    } else if (Date.now() - _rms.speechStart > _rms.CONFIRM_MS) {
                        console.log('[Machine] BARGE-IN: rms=' + rms.toFixed(3));
                        _duckVolume(false); // barge-in 시 ducking 해제 (TTS 정지될 것)
                        actor.send({ type: 'BARGE_IN' });
                    }
                } else {
                    if (_rms.speechStart !== 0) {
                        // 음성 끊김 → 볼륨 복원
                        _duckVolume(false);
                    }
                    _rms.speechStart = 0;
                }
            }, 40);
            console.log('[Machine] RMS detector started');
        } catch (e) {
            console.warn('[Machine] RMS init fail:', e.message);
        }
    }

    function _stopRmsDetector() {
        if (_rms.interval) { clearInterval(_rms.interval); _rms.interval = null; }
        if (_rms.ctx) { try { _rms.ctx.close(); } catch (e) { } _rms.ctx = null; }
        _rms.analyser = null;
        _rms.speechStart = 0;
        _duckVolume(false); // 볼륨 복원
    }

    // ─── Volume Ducking ───
    function _duckVolume(duck) {
        try {
            var tts = ttsObj();
            if (!tts || !tts._gainNode) return;
            var gain = tts._gainNode.gain;
            var now = tts._gainNode.context.currentTime;
            gain.cancelScheduledValues(now);
            gain.setValueAtTime(gain.value, now);
            gain.linearRampToValueAtTime(duck ? DUCK_GAIN : 1.0, now + DUCK_RAMP_MS);
        } catch (e) { /* AudioContext may be closed */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // VAD (Voice Activity Detection) — idle에서 음성 감지 시 listening 전이
    // ═══════════════════════════════════════════════════════════════
    var _vad = {
        ctx: null, analyser: null, interval: null,
        speechStart: 0,
        THRESHOLD: 0.018,   // RMS above this = voice activity
        CONFIRM_MS: 200     // 200ms 이상 지속 시 확정
    };

    function _startVadDetector() {
        _stopVadDetector();
        var mic = (recObj() && recObj()._persistentStream);
        if (!mic || !mic.active) {
            console.log('[Machine] VAD: no persistent mic stream');
            return;
        }
        try {
            _vad.ctx = new (window.AudioContext || window.webkitAudioContext)();
            var src = _vad.ctx.createMediaStreamSource(mic);
            _vad.analyser = _vad.ctx.createAnalyser();
            _vad.analyser.fftSize = 512;
            src.connect(_vad.analyser);

            var bufLen = _vad.analyser.fftSize;
            var buf = new Float32Array(bufLen);
            _vad.speechStart = 0;

            _vad.interval = setInterval(function () {
                var snap = actor.getSnapshot();
                // idle 상태에서만 VAD 활성
                if (snap.value !== 'idle' || !snap.context.voiceMode || !_vad.analyser) return;

                _vad.analyser.getFloatTimeDomainData(buf);
                var sum = 0;
                for (var i = 0; i < bufLen; i++) sum += buf[i] * buf[i];
                var rms = Math.sqrt(sum / bufLen);

                if (rms > _vad.THRESHOLD) {
                    if (_vad.speechStart === 0) {
                        _vad.speechStart = Date.now();
                    } else if (Date.now() - _vad.speechStart > _vad.CONFIRM_MS) {
                        console.log('[Machine] VAD: voice detected (rms=' + rms.toFixed(3) + ')');
                        _vad.speechStart = 0;
                        actor.send({ type: 'MIC.VOICE_DETECTED' });
                    }
                } else {
                    _vad.speechStart = 0;
                }
            }, 50);
            console.log('[Machine] VAD detector started');
        } catch (e) {
            console.warn('[Machine] VAD init fail:', e.message);
        }
    }

    function _stopVadDetector() {
        if (_vad.interval) { clearInterval(_vad.interval); _vad.interval = null; }
        if (_vad.ctx) { try { _vad.ctx.close(); } catch (e) { } _vad.ctx = null; }
        _vad.analyser = null;
        _vad.speechStart = 0;
    }

    // ═══════════════════════════════════════════════════════════════
    // Barge-in: Voice command listener (WebSpeech)
    // ═══════════════════════════════════════════════════════════════
    var _cmdRec = null;

    function _startCommandListener() {
        var SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRecognition) return;
        _stopCommandListener();

        try {
            var r = new SpeechRecognition();
            r.lang = 'ko-KR';
            r.interimResults = true;
            r.continuous = true;
            r.maxAlternatives = 1;

            r.onresult = function (event) {
                for (var i = event.resultIndex; i < event.results.length; i++) {
                    var result = event.results[i];
                    var text = result[0].transcript.trim();
                    var confidence = result[0].confidence || 0.5;
                    if (text.length === 0) continue;

                    var lower = text.toLowerCase();
                    // Short text — check stop commands first
                    if (text.length <= 15) {
                        for (var j = 0; j < STOP_COMMANDS.length; j++) {
                            if (lower.indexOf(STOP_COMMANDS[j]) >= 0) {
                                console.log('[Machine] VOICE_CMD: "' + text + '"');
                                actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
                                return;
                            }
                        }
                        // No stop command → unclassified (final results only)
                        if (result.isFinal) {
                            console.log('[Machine] CMD.UNCLASSIFIED: "' + text + '" (conf=' + confidence.toFixed(2) + ')');
                            actor.send({ type: 'CMD.UNCLASSIFIED', params: { text: text, confidence: confidence } });
                        }
                    } else if (result.isFinal) {
                        // Longer final result → unclassified for followup analysis
                        console.log('[Machine] CMD.UNCLASSIFIED (long): "' + text.substring(0, 30) + '..."');
                        actor.send({ type: 'CMD.UNCLASSIFIED', params: { text: text, confidence: confidence } });
                    }
                }
            };

            r.onerror = function (event) {
                if (event.error !== 'aborted' && event.error !== 'no-speech') {
                    console.warn('[Machine] CmdListener error:', event.error);
                }
            };

            r.onend = function () {
                // responding 상태에서만 자동 재시작
                var snap = actor.getSnapshot();
                if (snap.value === 'responding') {
                    try { r.start(); } catch (e) { }
                }
            };

            r.start();
            _cmdRec = r;
            console.log('[Machine] Command listener started');
        } catch (e) {
            console.warn('[Machine] CmdListener fail:', e.message);
        }
    }

    function _stopCommandListener() {
        if (_cmdRec) {
            try { _cmdRec.stop(); } catch (e) { }
            _cmdRec = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Followup Intent Actor (v2.4 §9.3) — rule-based classifier
    // ═══════════════════════════════════════════════════════════════
    var _followupTimer = null;

    function _classifyFollowupUtterance(text, confidence) {
        var trimmed = text.trim();
        if (trimmed.length < FOLLOWUP_MIN_LENGTH) return { type: 'CMD.UNRESOLVED', reason: 'too_short' };
        if (trimmed.length > FOLLOWUP_MAX_LENGTH) return { type: 'CMD.UNRESOLVED', reason: 'too_long' };
        if (confidence < 0.3) return { type: 'CMD.UNRESOLVED', reason: 'low_confidence' };

        var lower = trimmed.toLowerCase();
        for (var i = 0; i < FOLLOWUP_QUESTION_ENDINGS.length; i++) {
            var pattern = FOLLOWUP_QUESTION_ENDINGS[i];
            if (lower.indexOf(pattern) >= 0) {
                return { type: 'USER.FOLLOWUP_TEXT', text: trimmed, reason: 'question_pattern:' + pattern };
            }
        }
        if (trimmed.length >= 10 && confidence >= 0.7) {
            return { type: 'USER.FOLLOWUP_TEXT', text: trimmed, reason: 'long_confident_speech' };
        }
        return { type: 'CMD.UNRESOLVED', reason: 'no_match' };
    }

    function _handleFollowupUnclassified(text, confidence) {
        _cancelFollowupPending();
        console.log('[FollowupIntent] classifying: "' + text.substring(0, 30) + '" (conf=' + (confidence || 0).toFixed(2) + ')');

        _followupTimer = setTimeout(function () {
            console.log('[FollowupIntent] timeout → CMD.UNRESOLVED');
            actor.send({ type: 'CMD.UNRESOLVED' });
            _followupTimer = null;
        }, FOLLOWUP_TIMEOUT);

        try {
            var result = _classifyFollowupUtterance(text, confidence);
            _cancelFollowupPending();
            console.log('[FollowupIntent] result: ' + result.type + ' (' + result.reason + ')');

            if (result.type === 'USER.FOLLOWUP_TEXT' && result.text) {
                actor.send({ type: 'USER.FOLLOWUP_TEXT', params: { text: result.text } });
            } else {
                actor.send({ type: 'CMD.UNRESOLVED' });
            }
        } catch (err) {
            console.error('[FollowupIntent] classify error:', err);
            _cancelFollowupPending();
            actor.send({ type: 'CMD.UNRESOLVED' });
        }
    }

    function _cancelFollowupPending() {
        if (_followupTimer) {
            clearTimeout(_followupTimer);
            _followupTimer = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Error TTS Announcements (v2.4 §6.3 / M-2)
    // ═══════════════════════════════════════════════════════════════

    function _announceError(errorCode) {
        var entry = ERROR_ANNOUNCEMENTS[errorCode] || ERROR_ANNOUNCEMENTS['UNKNOWN'];
        if (!entry) return;
        _speakAnnouncement(entry.message);
        console.log('[Machine] announceError: "' + entry.message + '" (' + errorCode + ')');
    }

    function _announceFatalError() {
        _speakAnnouncement(FATAL_ANNOUNCEMENT);
        console.log('[Machine] announceFatalError: "' + FATAL_ANNOUNCEMENT + '"');
    }

    function _speakAnnouncement(text) {
        if (!window.speechSynthesis) {
            console.warn('[Machine] SpeechSynthesis not available');
            return;
        }
        try {
            var utt = new SpeechSynthesisUtterance(text);
            utt.lang = 'ko-KR';
            utt.rate = 1.1;
            utt.volume = 1.0;
            utt.pitch = 1.0;
            window.speechSynthesis.speak(utt);
        } catch (e) {
            console.warn('[Machine] speakAnnouncement failed:', e.message);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Public API: window.voiceMachine
    // ═══════════════════════════════════════════════════════════════
    window.voiceMachine = {
        /**
         * Initialize with Blazor DotNetObjectReference.
         */
        init: function (dotNetRef) {
            window._voiceDotNetRef = dotNetRef;
            console.log('[Machine] initialized with Blazor ref');
        },

        /**
         * Send an event to the state machine.
         * @param {string|object} event - event type string or { type, params }
         */
        send: function (event) {
            if (typeof event === 'string') event = { type: event };
            actor.send(event);
        },

        /**
         * Get current state value (string).
         * @returns {string} 'idle'|'listening'|'processing'|'responding'|'stopping'
         */
        getState: function () {
            return actor.getSnapshot().value;
        },

        /**
         * Get current context (data).
         */
        getContext: function () {
            return actor.getSnapshot().context;
        },

        /**
         * Check if TTS is active (in responding and tts not done).
         */
        isTtsActive: function () {
            var snap = actor.getSnapshot();
            return snap.value === 'responding' && !snap.context.ttsDone;
        },

        /**
         * Check if in voice mode.
         */
        isVoiceMode: function () {
            return actor.getSnapshot().context.voiceMode;
        },

        /**
         * Check if auto TTS is enabled.
         */
        isAutoTts: function () {
            return actor.getSnapshot().context.ttsEnabled;
        },

        /**
         * Check if currently recording (listening state).
         */
        isRecording: function () {
            return actor.getSnapshot().value === 'listening';
        },

        /**
         * Get current phase for UI.
         * @returns {string} 'idle'|'listening'|'processing'|'responding'|'stopping'
         */
        getPhase: function () {
            return actor.getSnapshot().value;
        },

        /**
         * Update context value (backward compatibility).
         */
        setContext: function (key, value) {
            if (key === 'autoTtsEnabled') {
                actor.send({ type: value ? 'AUTO_TTS_ON' : 'AUTO_TTS_OFF' });
                return;
            }
        },

        // Expose actor for advanced usage
        _actor: actor,
    };

    console.log('[Machine] loaded (v2.4 Sequential)');
})();
