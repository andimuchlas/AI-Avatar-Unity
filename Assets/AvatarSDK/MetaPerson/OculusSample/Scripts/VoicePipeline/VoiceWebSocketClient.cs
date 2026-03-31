using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AvatarSDK.MetaPerson.VoicePipeline
{
    public class VoiceWebSocketClient : MonoBehaviour
    {
        [Header("Connection Settings")]
        public string serverUrl = "ws://[IP_ADDRESS]/ws";
        public float reconnectDelay = 3f;

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        public string SessionID { get; private set; }

        public event Action<string> OnMessageReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<string> _incomingMessages = new ConcurrentQueue<string>();
        private bool _shouldReconnect = true;

        async void OnEnable()
        {
            await ConnectAsync();
        }

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
        }

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

        public async void Send(string jsonMessage)
        {
            if (!IsConnected) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(jsonMessage);
                await _ws.SendAsync(
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
        }

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

        public void Reconnect()
        {
            Debug.Log("[VoiceWS] Manual Reconnect Triggered.");
            bool wasRunning = _shouldReconnect;
            
            _cts?.Cancel();
            CleanupWebSocket();
            
            if (!wasRunning)
            {
                _ = ConnectAsync();
            }
        }

        public void Disconnect()
        {
            _shouldReconnect = false;
            _cts?.Cancel();
            CleanupWebSocket();
        }

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

        void OnDestroy()
        {
            Disconnect();
        }
    }
}
