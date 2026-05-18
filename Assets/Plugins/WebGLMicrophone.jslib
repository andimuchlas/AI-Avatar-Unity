mergeInto(LibraryManager.library, {
    // Initialize WebGL microphone with browser getUserMedia
    WebGLMicrophoneStart: function() {
        var state = globalThis.__WebGLMicrophoneState;
        if (!state) {
            state = {
                audioContext: null,
                mediaStream: null,
                sourceNode: null,
                scriptProcessor: null,
                muteGainNode: null,
                ringBuffer: null,
                ringCapacity: 0,
                readIndex: 0,
                writeIndex: 0,
                availableSamples: 0,
                isRecording: false,
                targetSampleRate: 16000,
                inputSampleRate: 16000,
                audioContextResumed: false,
                inputGain: 2.2,
            };
            globalThis.__WebGLMicrophoneState = state;
        }
        
        if (state.isRecording) {
            console.log("[WebGLMic] Already recording");
            return 1; // Already recording
        }

        // Request microphone access
        navigator.mediaDevices.getUserMedia({ 
            audio: {
                sampleRate: { ideal: 16000 },
                channelCount: { ideal: 1 },
                echoCancellation: false,
                noiseSuppression: false,
                autoGainControl: true
            } 
        }).then(function(stream) {
            console.log("[WebGLMic] Microphone permission granted");

            // Create audio context if needed
            if (!state.audioContext) {
                state.audioContext = new (window.AudioContext || window.webkitAudioContext)();
            }

            // Attempt to resume AudioContext only once per session
            if (!state.audioContextResumed && state.audioContext.state === "suspended") {
                state.audioContextResumed = true;
                state.audioContext.resume().then(function() {
                    console.log("[WebGLMic] AudioContext resumed");
                }).catch(function(err) {
                    console.log("[WebGLMic] AudioContext will resume after next user gesture:", err.message);
                });
            }

            state.mediaStream = stream;

            // Create audio source from stream
            state.sourceNode = state.audioContext.createMediaStreamSource(stream);

            // Create ScriptProcessorNode for real-time audio processing
            var bufferSize = 4096;
            state.scriptProcessor = state.audioContext.createScriptProcessor(bufferSize, 1, 1);

            // Keep 5 seconds of 16kHz mono audio to absorb jitter
            state.ringCapacity = state.targetSampleRate * 5;
            state.ringBuffer = new Float32Array(state.ringCapacity);
            state.readIndex = 0;
            state.writeIndex = 0;
            state.availableSamples = 0;

            state.inputSampleRate = state.audioContext.sampleRate || state.targetSampleRate;
            console.log("[WebGLMic] Input sample rate:", state.inputSampleRate, "Target:", state.targetSampleRate);

            // Collect audio data
            state.scriptProcessor.onaudioprocess = function(event) {
                var inputData = event.inputBuffer.getChannelData(0);
                if (!inputData || inputData.length === 0) {
                    return;
                }

                // Resample browser input to 16kHz so backend format matches desktop.
                var ratio = state.inputSampleRate / state.targetSampleRate;
                var outputLength = Math.floor(inputData.length / ratio);

                if (outputLength <= 0 || !state.ringBuffer || state.ringCapacity <= 0) {
                    return;
                }

                for (var i = 0; i < outputLength; i++) {
                    var sourcePos = i * ratio;
                    var left = Math.floor(sourcePos);
                    var right = Math.min(left + 1, inputData.length - 1);
                    var frac = sourcePos - left;
                    var sample = inputData[left] + (inputData[right] - inputData[left]) * frac;
                    sample *= state.inputGain;
                    if (sample > 1.0) sample = 1.0;
                    if (sample < -1.0) sample = -1.0;

                    if (state.availableSamples >= state.ringCapacity) {
                        state.readIndex = (state.readIndex + 1) % state.ringCapacity;
                        state.availableSamples = state.ringCapacity - 1;
                    }

                    state.ringBuffer[state.writeIndex] = sample;
                    state.writeIndex = (state.writeIndex + 1) % state.ringCapacity;
                    state.availableSamples++;
                }
            };

            // Connect nodes. ScriptProcessor must be in graph to execute; mute output.
            state.muteGainNode = state.audioContext.createGain();
            state.muteGainNode.gain.value = 0.0;
            state.sourceNode.connect(state.scriptProcessor);
            state.scriptProcessor.connect(state.muteGainNode);
            state.muteGainNode.connect(state.audioContext.destination);

            state.isRecording = true;
            console.log("[WebGLMic] Recording started");

        }).catch(function(error) {
            console.error("[WebGLMic] Microphone access denied:", error);
        });

        return 0; // Success
    },

    // Get current position in audio buffer (for compatibility with Microphone.GetPosition)
    WebGLMicrophoneGetPosition: function() {
        var state = globalThis.__WebGLMicrophoneState;
        return state ? state.availableSamples : 0;
    },

    // Get a chunk of audio data and reset the buffer
    // Returns number of samples retrieved
    WebGLMicrophoneGetChunk: function(outputBuffer, maxSamples) {
        var state = globalThis.__WebGLMicrophoneState;
        if (!state) {
            return 0;
        }

        var samplesToRead = Math.min(maxSamples, state.availableSamples);

        if (samplesToRead > 0) {
            // Copy contiguous samples from ring buffer to WASM output.
            var outputHeap = new Float32Array(HEAPF32.buffer, outputBuffer, samplesToRead);
            for (var i = 0; i < samplesToRead; i++) {
                outputHeap[i] = state.ringBuffer[state.readIndex];
                state.readIndex = (state.readIndex + 1) % state.ringCapacity;
            }
            state.availableSamples -= samplesToRead;
        }

        return samplesToRead;
    },

    // Check if recording
    WebGLMicrophoneIsRecording: function() {
        var state = globalThis.__WebGLMicrophoneState;
        return state && state.isRecording ? 1 : 0;
    },

    // Stop recording
    WebGLMicrophoneStop: function() {
        var state = globalThis.__WebGLMicrophoneState;
        if (!state) {
            return 0;
        }

        if (state.isRecording) {
            // Stop all tracks
            if (state.mediaStream) {
                state.mediaStream.getTracks().forEach(function(track) {
                    track.stop();
                });
                state.mediaStream = null;
            }

            if (state.sourceNode) {
                state.sourceNode.disconnect();
                state.sourceNode = null;
            }

            // Disconnect nodes
            if (state.scriptProcessor) {
                state.scriptProcessor.disconnect();
                state.scriptProcessor = null;
            }

            if (state.muteGainNode) {
                state.muteGainNode.disconnect();
                state.muteGainNode = null;
            }

            state.isRecording = false;
            state.ringBuffer = null;
            state.ringCapacity = 0;
            state.readIndex = 0;
            state.writeIndex = 0;
            state.availableSamples = 0;
            console.log("[WebGLMic] Recording stopped");
        }

        return 0;
    },

    // Get sample rate
    WebGLMicrophoneGetSampleRate: function() {
        var state = globalThis.__WebGLMicrophoneState;
        return state ? state.targetSampleRate : 16000;
    }
});
