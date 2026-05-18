using System;
using System.Collections.Concurrent;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endif
using System.Runtime.InteropServices;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    public class VoiceWebSocketClient : MonoBehaviour
    {
        [Header("Connection Settings")]
        public string serverUrl = "ws://[IP_ADDRESS]/ws";
        public float reconnectDelay = 3f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void WebGLWSConnect(string url, string gameObjectName);

    [DllImport("__Internal")]
    private static extern void WebGLWSSend(string message);

    [DllImport("__Internal")]
    private static extern void WebGLWSClose();

    [DllImport("__Internal")]
    private static extern int WebGLWSIsOpen();

    public bool IsConnected => WebGLWSIsOpen() == 1;
#else
        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
#endif
        public string SessionID { get; private set; }

        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

#if UNITY_WEBGL && !UNITY_EDITOR
    private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
    private bool _shouldReconnect = true;
    private bool _connectAttemptInProgress;
    private float _nextReconnectTime;
    private bool _wasConnectedLastFrame;
#else
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
        private bool _shouldReconnect = true;
#endif

    #if UNITY_WEBGL && !UNITY_EDITOR
        void OnEnable()
        {
            _shouldReconnect = true;
            _nextReconnectTime = Time.unscaledTime;
            TryBeginWebGLConnect();
        }
    #else
        async void OnEnable()
        {
            await ConnectAsync();
        }
    #endif

        void OnDisable()
        {
            _shouldReconnect = false;
            Disconnect();
        }

        void Update()
        {
            // Process incoming messages on main thread
            while (_incomingMessages.TryDequeue(out string msg))
            {
                OnMessageReceived?.Invoke(msg);
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            bool connectedNow = IsConnected;
            if (connectedNow && !_wasConnectedLastFrame)
            {
                _connectAttemptInProgress = false;
                Debug.Log("[VoiceWS] Connected!");
                OnConnected?.Invoke();
            }
            else if (!connectedNow && _wasConnectedLastFrame)
            {
                Debug.Log("[VoiceWS] Disconnected");
                OnDisconnected?.Invoke();
                ScheduleWebGLReconnect();
            }
            _wasConnectedLastFrame = connectedNow;

            if (_shouldReconnect && !connectedNow && !_connectAttemptInProgress && Time.unscaledTime >= _nextReconnectTime)
            {
                TryBeginWebGLConnect();
            }
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        public async Task ConnectAsync()
        {
            _shouldReconnect = true;

            while (_shouldReconnect)
            {
                try
                {
                    _cts = new CancellationTokenSource();
                    _ws = new ClientWebSocket();

                    Debug.Log($"[VoiceWS] Connecting to {serverUrl}...");
                    await _ws.ConnectAsync(new Uri(serverUrl), _cts.Token);
                    Debug.Log("[VoiceWS] Connected!");

                    OnConnected?.Invoke();

                    // Start receive loop
                    await ReceiveLoop();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VoiceWS] Connection error: {ex.Message}");
                }
                finally
                {
                    OnDisconnected?.Invoke();
                    CleanupWebSocket();
                }

                if (_shouldReconnect)
                {
                    Debug.Log($"[VoiceWS] Reconnecting in {reconnectDelay}s...");
                    await Task.Delay((int)(reconnectDelay * 1000));
                }
            }
        }
#endif

        public void Send(string jsonMessage)
        {
            if (!IsConnected) return;

#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLWSSend(jsonMessage);
#else
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
                _ = _ws.SendAsync(
                    new ArraySegment<byte>(data),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceWS] Send error: {ex.Message}");
            }
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[1024 * 64]; // 64KB per frame

            while (_ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Accumulate frames until EndOfMessage
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            _cts.Token
                        );

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Debug.Log("[VoiceWS] Server closed connection");
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);

                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(ms.ToArray());
                        _incomingMessages.Enqueue(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VoiceWS] Receive error: {ex.Message}");
                    break;
                }
            }
        }
#endif

        public void Reconnect()
        {
            Debug.Log("[VoiceWS] Manual Reconnect Triggered.");

#if UNITY_WEBGL && !UNITY_EDITOR
            if (_shouldReconnect)
            {
                WebGLWSClose();
                _connectAttemptInProgress = false;
                _nextReconnectTime = Time.unscaledTime;
            }
#else
            bool wasRunning = _shouldReconnect;
            
            _cts?.Cancel();
            CleanupWebSocket();
            
            if (!wasRunning)
            {
                _ = ConnectAsync();
            }
#endif
        }

        public void Disconnect()
        {
            _shouldReconnect = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLWSClose();
            _connectAttemptInProgress = false;
#else
            _cts?.Cancel();
            CleanupWebSocket();
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void CleanupWebSocket()
        {
            if (_ws != null)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait(1000);
                    }
                }
                catch { }
                _ws.Dispose();
                _ws = null;
            }
            _cts?.Dispose();
            _cts = null;
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private void TryBeginWebGLConnect()
        {
            if (!_shouldReconnect)
            {
                return;
            }

            _connectAttemptInProgress = true;
            Debug.Log($"[VoiceWS] Connecting to {serverUrl}...");
            WebGLWSConnect(serverUrl, gameObject.name);
        }

        private void ScheduleWebGLReconnect()
        {
            if (!_shouldReconnect)
            {
                return;
            }

            _connectAttemptInProgress = false;
            _nextReconnectTime = Time.unscaledTime + reconnectDelay;
            Debug.Log($"[VoiceWS] Reconnecting in {reconnectDelay}s...");
        }

        // Called from WebGLVoiceSocket.jslib via SendMessage
        public void OnWebGLWSOpen(string _)
        {
            _connectAttemptInProgress = false;
        }

        // Called from WebGLVoiceSocket.jslib via SendMessage
        public void OnWebGLWSMessage(string payload)
        {
            if (!string.IsNullOrEmpty(payload))
            {
                _incomingMessages.Enqueue(payload);
            }
        }

        // Called from WebGLVoiceSocket.jslib via SendMessage
        public void OnWebGLWSClose(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                Debug.Log($"[VoiceWS] Close: {reason}");
            }

            _connectAttemptInProgress = false;
        }

        // Called from WebGLVoiceSocket.jslib via SendMessage
        public void OnWebGLWSError(string error)
        {
            Debug.LogWarning($"[VoiceWS] WebGL socket error: {error}");
            _connectAttemptInProgress = false;
            ScheduleWebGLReconnect();
        }
#endif

        void OnDestroy()
        {
            Disconnect();
        }
    }
}
