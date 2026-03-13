// ═══════════════════════════════════════════════════════════════════
// Voice I/O State Machine — XState v5
// Replaces scattered boolean flags with a single source of truth.
// ═══════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    var X = window.XState;
    if (!X) { console.error('[VoiceMachine] XState not loaded'); return; }

    var createMachine = X.createMachine;
    var createActor = X.createActor;
    var assign = X.assign;
    var raise = X.raise;

    // ─── Stop command keywords ───
    var STOP_COMMANDS = ['멈춰', '그만', '중지', '스톱', 'stop', '멈춰라', '그만해', '정지', '스탑'];

    // ─── Helper: get dotNetRef ───
    function dotNet() { return window._voiceDotNetRef; }
    function tts() { return window.fabTts; }
    function rec() { return window.audioRecorder; }

    // ═══════════════════════════════════════════════════════════════
    // Machine Definition
    // ═══════════════════════════════════════════════════════════════
    var voiceMachine = createMachine({
        id: 'voice',
        type: 'parallel',

        context: {
            autoTtsEnabled: false,
            ttsPlayingIdx: -1,
            interimText: '',
            recordingSeconds: 0,
            pendingMessage: null,       // TTS send confirm
            sttEngine: 'whisper',       // 'whisper' | 'webspeech'
        },

        states: {

            // ── Mode: overall voice mode ──────────────────────
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

            // ── STT: speech-to-text ───────────────────────────
            stt: {
                initial: 'idle',
                states: {
                    idle: {
                        on: {
                            REC_START: { target: 'recording' },
                        },
                    },
                    recording: {
                        entry: ['startRecorder', 'resetRecTimer'],
                        exit: ['clearRecTimer'],
                        on: {
                            REC_INTERIM: {
                                actions: [assign({ interimText: function (_a) { var e = _a.event; return e.params ? e.params.text : ''; } })],
                            },
                            REC_STOP: { target: 'transcribing' },
                            REC_FINAL: {
                                target: 'idle',
                                actions: ['handleTranscript'],
                            },
                            REC_ERROR: {
                                target: 'idle',
                                actions: ['handleSttError'],
                            },
                            REC_ABORT: { target: 'idle' },
                            VOICE_EXIT: { target: 'idle' },
                        },
                    },
                    transcribing: {
                        entry: ['notifyTranscribing'],
                        on: {
                            TRANSCRIPT_OK: {
                                target: 'idle',
                                actions: ['handleTranscript'],
                            },
                            TRANSCRIPT_ERR: {
                                target: 'idle',
                                actions: ['handleSttError'],
                            },
                        },
                    },
                },
            },

            // ── TTS: text-to-speech ───────────────────────────
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

            // ── TTS Send Confirm ──────────────────────────────
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
            // ── Mode actions ──
            unlockAudio: function () {
                try { tts().unlockAudio(); } catch (e) { }
            },
            prepareMic: function () {
                try { tts().prepareMic(); } catch (e) { }
            },
            releaseMic: function () {
                try { tts().releaseMic(); } catch (e) { }
            },
            acquireVoiceModeMic: function () {
                if (rec()) {
                    rec().setVoiceMode(true).catch(function () { });
                }
            },
            releaseVoiceModeMic: function () {
                if (rec()) {
                    rec().setVoiceMode(false).catch(function () { });
                }
            },
            stopAllActivity: function () {
                try { tts()._stopInternal(); } catch (e) { }
                try { rec().stop(); } catch (e) { }
            },

            // ── STT actions ──
            startRecorder: function () {
                if (rec()) rec().start();
            },
            resetRecTimer: assign({ recordingSeconds: 0, interimText: '' }),
            clearRecTimer: function () { /* timer managed by audioRecorder internally */ },
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

            // ── TTS actions ──
            startBargeIn: function () {
                _startRmsDetector();
                _startCommandListener();
            },
            stopBargeIn: function () {
                _stopRmsDetector();
                _stopCommandListener();
            },
            notifyTtsState: function () {
                // State exit — Blazor will be notified via specific callbacks
            },
            feedToken: function (_a) {
                var event = _a.event;
                if (tts() && tts()._streaming) {
                    tts().feedText(event.params ? event.params.token : '');
                }
            },
            flushStream: function () {
                if (tts() && tts()._streaming) {
                    tts().flushStream();
                }
            },
            stopTtsInternal: function () {
                try { tts()._stopInternal(); } catch (e) { }
            },
            onTtsEnded: function () {
                // Blazor is notified directly by TTS callbacks (not through machine)
                // This action is for machine-internal state tracking only
                console.log('[VoiceMachine] onTtsEnded (machine internal)');
            },
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

            // ── Send Confirm actions ──
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

    // ═══════════════════════════════════════════════════════════════
    // Actor (runtime instance)
    // ═══════════════════════════════════════════════════════════════
    var actor = createActor(voiceMachine);

    // Subscribe for debug logging
    actor.subscribe(function (snapshot) {
        var s = snapshot.value;
        console.log('[VoiceMachine]', JSON.stringify(s));
    });

    actor.start();

    // ═══════════════════════════════════════════════════════════════
    // Barge-in: RMS detector (extracted from fabTts)
    // ═══════════════════════════════════════════════════════════════
    var _rms = {
        ctx: null, analyser: null, interval: null, speechStart: 0,
        THRESHOLD: 0.02, CONFIRM_MS: 300
    };

    function _startRmsDetector() {
        _stopRmsDetector();
        // Get mic stream
        var mic = (rec() && rec()._persistentStream);
        if (!mic || !mic.active) mic = tts() && tts()._bargeInMicStream;
        if (!mic || !mic.active) {
            console.log('[VoiceMachine] RMS: no mic');
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
                var ttsState = snap.value.tts;
                if (ttsState === 'idle' || !_rms.analyser) return;

                _rms.analyser.getFloatTimeDomainData(buf);
                var sum = 0;
                for (var i = 0; i < bufLen; i++) sum += buf[i] * buf[i];
                var rms = Math.sqrt(sum / bufLen);

                if (rms > _rms.THRESHOLD) {
                    if (_rms.speechStart === 0) {
                        _rms.speechStart = Date.now();
                    } else if (Date.now() - _rms.speechStart > _rms.CONFIRM_MS) {
                        console.log('[VoiceMachine] BARGE-IN: rms=' + rms.toFixed(3));
                        actor.send({ type: 'BARGE_IN' });
                    }
                } else {
                    _rms.speechStart = 0;
                }
            }, 40);
            console.log('[VoiceMachine] RMS detector started');
        } catch (e) {
            console.warn('[VoiceMachine] RMS init fail:', e.message);
        }
    }

    function _stopRmsDetector() {
        if (_rms.interval) { clearInterval(_rms.interval); _rms.interval = null; }
        if (_rms.ctx) { try { _rms.ctx.close(); } catch (e) { } _rms.ctx = null; }
        _rms.analyser = null;
        _rms.speechStart = 0;
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
                    var text = event.results[i][0].transcript.trim();
                    if (text.length === 0 || text.length > 15) continue;

                    var lower = text.toLowerCase();
                    for (var j = 0; j < STOP_COMMANDS.length; j++) {
                        if (lower.indexOf(STOP_COMMANDS[j]) >= 0) {
                            console.log('[VoiceMachine] VOICE_CMD: "' + text + '"');
                            actor.send({ type: 'VOICE_CMD', params: { command: 'stop' } });
                            return;
                        }
                    }
                }
            };

            r.onerror = function (event) {
                if (event.error !== 'aborted' && event.error !== 'no-speech') {
                    console.warn('[VoiceMachine] CmdListener error:', event.error);
                }
            };

            r.onend = function () {
                // Auto-restart while TTS is active
                var snap = actor.getSnapshot();
                if (snap.value.tts !== 'idle') {
                    try { r.start(); } catch (e) { }
                }
            };

            r.start();
            _cmdRec = r;
            console.log('[VoiceMachine] Command listener started');
        } catch (e) {
            console.warn('[VoiceMachine] CmdListener fail:', e.message);
        }
    }

    function _stopCommandListener() {
        if (_cmdRec) {
            try { _cmdRec.stop(); } catch (e) { }
            _cmdRec = null;
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
            console.log('[VoiceMachine] initialized with Blazor ref');
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
         * Get current composite state value.
         * @returns {{ mode: string, stt: string, tts: string, sendConfirm: string }}
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
         * Check if TTS is currently active (playing/streaming/flushing).
         */
        isTtsActive: function () {
            return actor.getSnapshot().value.tts !== 'idle';
        },

        /**
         * Check if currently in voice mode (full hands-free loop).
         */
        isVoiceMode: function () {
            return actor.getSnapshot().value.mode === 'voiceActive';
        },

        /**
         * Check if auto TTS is enabled.
         */
        isAutoTts: function () {
            var m = actor.getSnapshot().value.mode;
            return m === 'autoTts' || m === 'voiceActive';
        },

        /**
         * Check if currently recording.
         */
        isRecording: function () {
            return actor.getSnapshot().value.stt === 'recording';
        },

        /**
         * Update context value.
         */
        setContext: function (key, value) {
            // For autoTtsEnabled, transition mode state
            if (key === 'autoTtsEnabled') {
                actor.send({ type: value ? 'AUTO_TTS_ON' : 'AUTO_TTS_OFF' });
                return;
            }
        },

        // Expose actor for advanced usage
        _actor: actor,
    };

    console.log('[VoiceMachine] loaded (XState v5)');
})();
