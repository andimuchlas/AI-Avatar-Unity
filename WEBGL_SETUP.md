# WebGL Voice Pipeline Runtime Start Overlay

## What Changed

WebGL now shows a runtime start overlay automatically from script.
No extra Canvas object or Button setup is required in the scene.

## Behavior

1. App loads and connects to server.
2. Overlay appears with animated "Mulai Listening" button.
3. User taps once (required by browser policy).
4. Gesture is saved.
5. When session is ready, microphone pipeline starts automatically.

## Notes

- This is WebGL-only behavior.
- Desktop behavior remains unchanged.
- If user taps during loading, a second tap is not required.

## Logs You Should See

- "[Pipeline] WebSocket connected, waiting for session..."
- "[Pipeline] Session started: ..."
- "[Pipeline] User gesture already captured during loading. Starting now." (if tapped early)
- "[Pipeline] Started — listening for voice input"

## WebSocket Notes (WebGL)

- WebGL now uses browser-native `WebSocket` bridge (`WebGLVoiceSocket.jslib`).
- On successful connect, browser Network tab should show a `ws://.../ws` or `wss://.../ws` request.
- Expected logs include:
	- "[VoiceWS] Connecting to ..."
	- "[WebGLWS] Open: ..."
	- "[VoiceWS] Connected!"

## Troubleshooting

### Repeated AudioContext warning before tapping

This is expected in browsers:
"The AudioContext was not allowed to start..."

Tap the overlay button once to unlock audio context and microphone.

### No overlay appears

- Ensure build target is WebGL (not Editor/Desktop).
- Ensure `autoStartOnConnect` is enabled on VoicePipelineManager.

### Still not listening after tap

- Check WebSocket is connected.
- Confirm server sends `session_started`.
- Check console for `[Pipeline] Cannot start: ...` warnings.

