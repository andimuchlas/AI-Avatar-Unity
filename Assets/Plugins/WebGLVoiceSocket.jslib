mergeInto(LibraryManager.library, {
    WebGLWSConnect: function(urlPtr, gameObjectNamePtr) {
        var url = UTF8ToString(urlPtr);
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var state = globalThis.__WebGLVoiceSocketState;
        if (!state) {
            state = {
                socket: null,
                gameObjectName: ""
            };
            globalThis.__WebGLVoiceSocketState = state;
        }

        state.gameObjectName = gameObjectName;

        if (!url || url.length === 0) {
            console.error("[WebGLWS] Empty URL");
            if (state.gameObjectName) {
                SendMessage(state.gameObjectName, "OnWebGLWSError", "Empty URL");
            }
            return;
        }

        if (state.socket) {
            try {
                state.socket.close();
            } catch (e) {
                // ignore
            }
            state.socket = null;
        }

        try {
            var socket = new WebSocket(url);
            state.socket = socket;

            socket.onopen = function() {
                console.log("[WebGLWS] Open:", url);
                if (state.gameObjectName) {
                    SendMessage(state.gameObjectName, "OnWebGLWSOpen", "open");
                }
            };

            socket.onmessage = function(evt) {
                var payload = "";
                if (typeof evt.data === "string") {
                    payload = evt.data;
                }

                if (state.gameObjectName) {
                    SendMessage(state.gameObjectName, "OnWebGLWSMessage", payload);
                }
            };

            socket.onerror = function() {
                console.error("[WebGLWS] Error");
                if (state.gameObjectName) {
                    SendMessage(state.gameObjectName, "OnWebGLWSError", "WebSocket error");
                }
            };

            socket.onclose = function(evt) {
                var reason = "code=" + evt.code;
                console.log("[WebGLWS] Close:", reason);
                if (state.gameObjectName) {
                    SendMessage(state.gameObjectName, "OnWebGLWSClose", reason);
                }
            };
        } catch (e) {
            console.error("[WebGLWS] Connect exception:", e);
            if (state.gameObjectName) {
                SendMessage(state.gameObjectName, "OnWebGLWSError", String(e));
            }
        }
    },

    WebGLWSSend: function(messagePtr) {
        var message = UTF8ToString(messagePtr);
        var state = globalThis.__WebGLVoiceSocketState;
        if (!state) {
            return;
        }

        if (!state.socket || state.socket.readyState !== WebSocket.OPEN) {
            return;
        }

        state.socket.send(message);
    },

    WebGLWSClose: function() {
        var state = globalThis.__WebGLVoiceSocketState;
        if (!state) {
            return;
        }

        if (state.socket) {
            try {
                state.socket.close();
            } catch (e) {
                // ignore
            }
            state.socket = null;
        }
    },

    WebGLWSIsOpen: function() {
        var state = globalThis.__WebGLVoiceSocketState;
        if (!state) {
            return 0;
        }

        return state.socket && state.socket.readyState === WebSocket.OPEN ? 1 : 0;
    }
});
