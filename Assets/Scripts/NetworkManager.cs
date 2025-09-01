using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using NativeWebSocket;
#else
using WebSocketSharp;
#endif
using System.Collections.Concurrent;
using System;
using System.Collections;
using System.Threading.Tasks;

public class NetworkManager : MonoBehaviour
{
    [Header("Server Configuration")]
    [SerializeField] private bool useLocalServer = false;
    [SerializeField] private string localServerUrl = "ws://localhost:9000/ws";
    [SerializeField] private string renderServerUrl = "wss://cheap-bingo-go-server.onrender.com/ws";
    [SerializeField] private string localHttpUrl = "http://localhost:9000/";
    [SerializeField] private string renderHttpUrl = "https://cheap-bingo-go-server.onrender.com/";

    [Header("Connection Settings")]
    [SerializeField] private float connectionTimeout = 15f;
    [SerializeField] private float reconnectCooldown = 5f;
    [SerializeField] private int maxReconnectAttempts = 3;

    // Events
    public event Action<string, string> OnRoomCreated;
    public event Action<string, string> OnGameReady;
    public event Action<int> OnGameMove;
    public event Action<string> OnWinClaim;
    public event Action<string> OnRetry;
    public event Action OnExitRoom;
    public event Action<string> OnError;
    public event Action OnConnected;
    public event Action<string> OnDisconnected;

    // Private fields
#if UNITY_WEBGL && !UNITY_EDITOR
	private NativeWebSocket.WebSocket ws;
#else
    private WebSocketSharp.WebSocket ws;
#endif
    private bool isReconnecting = false;
    private float lastReconnectAttempt = 0f;
    private int reconnectAttempts = 0;
    private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

    // Properties
#if UNITY_WEBGL && !UNITY_EDITOR
	public bool IsConnected => ws != null && ws.State == WebSocketState.Open;
#else
    public bool IsConnected => ws != null && ws.IsAlive;
#endif
    public bool IsReconnecting => isReconnecting;
    public string CurrentServerUrl => useLocalServer ? localServerUrl : renderServerUrl;
    public string CurrentServerType => useLocalServer ? "Local" : "Render";

    // Singleton pattern
    private static NetworkManager instance;
    public static NetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<NetworkManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject("NetworkManager");
                    instance = go.AddComponent<NetworkManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // Check server status and establish connection
        StartCoroutine(CheckServerStatus());
    }

    private void Update()
    {
        // Process queued actions on main thread
        while (_actions.Count > 0)
        {
            if (_actions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }

		// WebGL message dispatching is handled automatically by the browser
		// No manual dispatch needed for WebGL builds

        // Monitor connection status and handle reconnection
#if UNITY_WEBGL && !UNITY_EDITOR
		if (ws != null && ws.State != WebSocketState.Open && !isReconnecting)
#else
        if (ws != null && !ws.IsAlive && !isReconnecting)
#endif
        {
            float currentTime = Time.time;
            if (currentTime - lastReconnectAttempt > reconnectCooldown && reconnectAttempts < maxReconnectAttempts)
            {
                Debug.LogWarning($"WebSocket connection lost, attempting to reconnect... (Attempt {reconnectAttempts + 1}/{maxReconnectAttempts})");
                isReconnecting = true;
                lastReconnectAttempt = currentTime;
                reconnectAttempts++;

                StartCoroutine(AttemptReconnection());
            }
            else if (reconnectAttempts >= maxReconnectAttempts)
            {
                Debug.LogError("Maximum reconnection attempts reached. Stopping reconnection loop.");
                OnError?.Invoke($"Connection failed after {maxReconnectAttempts} attempts. {CurrentServerType} server may be down.");
            }
        }
    }
    


    #region Connection Management

    public void Connect()
    {
        if (IsConnected)
        {
            Debug.Log("Already connected to WebSocket server");
            return;
        }

        StartCoroutine(CreateWebSocketConnection());
    }

    public void Disconnect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
		if (ws != null && ws.State == WebSocketState.Open)
		{
			ws.Close();
		}
#else
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
        }
#endif
    }

    private IEnumerator CreateWebSocketConnection()
    {
        string serverUrl = CurrentServerUrl;
        Debug.Log($"Creating WebSocket connection to: {serverUrl}");

        bool connectionCreated = false;
        Exception connectionException = null;
#if UNITY_WEBGL && !UNITY_EDITOR
		System.Threading.Tasks.Task connectTask = null;
#endif

        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
			ws = new NativeWebSocket.WebSocket(serverUrl);
			AttachWebSocketHandlers();
			connectTask = ws.Connect();
			connectionCreated = true;
#else
            ws = new WebSocketSharp.WebSocket(serverUrl);
            ws.WaitTime = TimeSpan.FromSeconds(connectionTimeout);
            ws.Compression = CompressionMethod.None;
            if (!useLocalServer)
            {
                ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                                          System.Security.Authentication.SslProtocols.Tls11 |
                                                          System.Security.Authentication.SslProtocols.Tls;
                ws.SslConfiguration.CheckCertificateRevocation = false;
                ws.SslConfiguration.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            AttachWebSocketHandlers();
            ws.ConnectAsync();
            connectionCreated = true;
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create WebSocket connection: {e.Message}");
            connectionException = e;
        }

        if (!connectionCreated)
        {
            if (connectionException != null)
            {
                OnError?.Invoke($"WebSocket creation failed: {connectionException.Message}");
            }
            else
            {
                OnError?.Invoke("WebSocket creation failed");
            }
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
		// Wait for connection completion outside try/catch
		float connectWait = 0f;
		while (connectTask != null && !connectTask.IsCompleted && connectWait < connectionTimeout)
		{
			connectWait += Time.deltaTime;
			yield return null;
		}
		if (ws.State == WebSocketState.Open)
		{
			Debug.Log("WebSocket connection established successfully!");
			OnConnected?.Invoke();
		}
		else
		{
			Debug.LogWarning("WebSocket connection failed to establish");
			OnError?.Invoke("WebSocket connection failed. Check server status.");
		}
#else
        float elapsed = 0f;
        while (!ws.IsAlive && elapsed < connectionTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (ws.IsAlive)
        {
            Debug.Log("WebSocket connection established successfully!");
            OnConnected?.Invoke();
        }
        else
        {
            Debug.LogWarning("WebSocket connection failed to establish");
            OnError?.Invoke("WebSocket connection failed. Check server status.");
        }
#endif
    }

    private IEnumerator AttemptReconnection()
    {
        Debug.Log("Attempting to reconnect to WebSocket server...");

        bool reconnectionStarted = false;
        Exception reconnectionException = null;
#if UNITY_WEBGL && !UNITY_EDITOR
		System.Threading.Tasks.Task reconnectTask = null;
#endif

        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
			reconnectTask = ws.Connect();
#else
            ws.ConnectAsync();
#endif
            reconnectionStarted = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Reconnection attempt failed: {e.Message}");
            reconnectionException = e;
        }

        if (!reconnectionStarted)
        {
            OnError?.Invoke($"Reconnection failed: {reconnectionException?.Message ?? "Unknown error"}");
            isReconnecting = false;
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
		// Wait for reconnection completion outside try/catch
		float reconnectWait = 0f;
		while (reconnectTask != null && !reconnectTask.IsCompleted && reconnectWait < connectionTimeout)
		{
			reconnectWait += Time.deltaTime;
			yield return null;
		}
		if (ws.State == WebSocketState.Open)
		{
			Debug.Log("Reconnection successful!");
			reconnectAttempts = 0;
			OnConnected?.Invoke();
		}
		else
		{
			Debug.LogWarning("Reconnection attempt failed");
			OnError?.Invoke($"Reconnection failed. Will retry in {reconnectCooldown}s.");
		}
#else
        float elapsed = 0f;
        while (!ws.IsAlive && elapsed < connectionTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        if (ws.IsAlive)
        {
            Debug.Log("Reconnection successful!");
            reconnectAttempts = 0;
            OnConnected?.Invoke();
        }
        else
        {
            Debug.LogWarning("Reconnection attempt failed");
            OnError?.Invoke($"Reconnection failed. Will retry in {reconnectCooldown}s.");
        }
#endif

        isReconnecting = false;
    }

    #endregion

    #region Server Management

    public void SwitchToLocalServer()
    {
        if (!useLocalServer)
        {
            useLocalServer = true;
            Debug.Log("Switched to local server");
            RestartConnection();
        }
    }

    public void SwitchToRenderServer()
    {
        if (useLocalServer)
        {
            useLocalServer = false;
            Debug.Log("Switched to Render server");
            RestartConnection();
        }
    }

    public void ToggleServer()
    {
        useLocalServer = !useLocalServer;
        string serverType = CurrentServerType;
        Debug.Log($"Switched to {serverType} server");

        RestartConnection();
    }

    private void RestartConnection()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
		if (ws != null && ws.State == WebSocketState.Open)
		{
			ws.Close();
			ws = null;
		}
#else
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
            ws = null;
        }
#endif

        // Reset reconnection state
        isReconnecting = false;
        reconnectAttempts = 0;
        lastReconnectAttempt = 0f;

        // Create new connection
        StartCoroutine(CreateWebSocketConnection());
    }

    private IEnumerator CheckServerStatus()
    {
        string httpUrl = useLocalServer ? localHttpUrl : renderHttpUrl;
        Debug.Log($"Checking if {CurrentServerType} server is reachable...");

        using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(httpUrl))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"{CurrentServerType} HTTP check successful: {request.responseCode}");
                Connect();
            }
            else
            {
                Debug.LogError($"{CurrentServerType} HTTP check failed: {request.error}");
                OnError?.Invoke($"{CurrentServerType} server unreachable: {request.error}");
            }
        }
    }

    #endregion

    #region Message Handling

    public void SendMessage(string channel, string data, string roomCode = "", bool isCreator = false, int dimension = 5, int move = 0)
    {
        if (!IsConnected)
        {
            OnError?.Invoke("Not connected to server");
            return;
        }

        var message = new NetworkMessage
        {
            channel = channel,
            res = data,
            roomCode = roomCode,
            dimension = dimension,
            isCreator = isCreator,
            move = move,
            appVersion = Application.version
        };

        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
			ws.SendText(JsonUtility.ToJson(message));
#else
            ws.Send(JsonUtility.ToJson(message));
#endif
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to send message: {e.Message}");
            OnError?.Invoke($"Failed to send message: {e.Message}");
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private void HandleMessage(WebSocketSharp.MessageEventArgs e)
    {
        try
        {
            var data = JsonUtility.FromJson<NetworkMessage>(e.Data);

            switch (data.channel)
            {
                case "create-room":
                    OnRoomCreated?.Invoke(data.roomCode, data.res);
                    break;
                case "game-ready":
                    OnGameReady?.Invoke(data.res, data.roomCode);
                    break;
                case "game-on":
                    OnGameMove?.Invoke(data.move);
                    break;
                case "win-claim":
                    OnWinClaim?.Invoke(data.res);
                    break;
                case "retry":
                    OnRetry?.Invoke(data.res);
                    break;
                case "exit-room":
                    OnExitRoom?.Invoke();
                    break;
                case "error":
                    OnError?.Invoke(data.res);
                    break;
                default:
                    Debug.Log($"Unhandled channel: {data.channel}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to parse message: {ex.Message}");
            OnError?.Invoke($"Failed to parse message: {ex.Message}");
        }
    }
#endif

    #endregion

    #region WebSocket Event Handlers

    private void AttachWebSocketHandlers()
    {
        if (ws == null) return;

#if UNITY_WEBGL && !UNITY_EDITOR
		ws.OnOpen += () =>
		{
			_actions.Enqueue(() =>
			{
				Debug.Log("WebSocket connection opened successfully!");
				OnConnected?.Invoke();
			});
		};
		ws.OnMessage += (bytes) =>
		{
			_actions.Enqueue(() =>
			{
				var text = System.Text.Encoding.UTF8.GetString(bytes);
				try
				{
					var data = JsonUtility.FromJson<NetworkMessage>(text);
					switch (data.channel)
					{
						case "create-room": OnRoomCreated?.Invoke(data.roomCode, data.res); break;
						case "game-ready": OnGameReady?.Invoke(data.res, data.roomCode); break;
						case "game-on": OnGameMove?.Invoke(data.move); break;
						case "win-claim": OnWinClaim?.Invoke(data.res); break;
						case "retry": OnRetry?.Invoke(data.res); break;
						case "exit-room": OnExitRoom?.Invoke(); break;
						case "error": OnError?.Invoke(data.res); break;
						default: Debug.Log($"Unhandled channel: {data.channel}"); break;
					}
				}
				catch (Exception ex)
				{
					Debug.LogError($"Failed to parse message: {ex.Message}");
					OnError?.Invoke($"Failed to parse message: {ex.Message}");
				}
			});
		};
		ws.OnError += (err) =>
		{
			_actions.Enqueue(() =>
			{
				Debug.LogError($"WebSocket error: {err}");
				OnError?.Invoke($"Connection error: {err}");
				isReconnecting = false;
			});
		};
		ws.OnClose += (code) =>
		{
			_actions.Enqueue(() =>
			{
				Debug.Log($"WebSocket closed: Code {code}");
				OnDisconnected?.Invoke(code.ToString());
				isReconnecting = false;
			});
		};
#else
        // WebSocketSharp event handlers
        ws.OnOpen += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.Log("WebSocket connection opened successfully!");
                OnConnected?.Invoke();
            });
        };
        ws.OnMessage += (_, e) => { _actions.Enqueue(() => HandleMessage(e)); };
        ws.OnError += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.LogError($"WebSocket error: {e.Message}");
                OnError?.Invoke($"Connection error: {e.Message}");
                isReconnecting = false;
            });
        };
        ws.OnClose += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.Log($"WebSocket closed: Code {e.Code}, Reason: {e.Reason}");
                OnDisconnected?.Invoke(e.Reason);
                isReconnecting = false;
            });
        };
#endif
    }

    #endregion

    #region Public API Methods

    public void CreateRoom(string playerName)
    {
        SendMessage("create-room", playerName, "", true);
    }

    public void JoinRoom(string roomCode, string playerName)
    {
        SendMessage("join-room", playerName, roomCode, false);
    }

    public void SendGameMove(string roomCode, int move, bool isCreator)
    {
        SendMessage("game-on", "", roomCode, isCreator, 5, move);
    }

    public void SendWinClaim(string roomCode, bool isCreator)
    {
        SendMessage("win-claim", "", roomCode, isCreator);
    }

    public void SendRetry(string roomCode, bool isCreator)
    {
        SendMessage("retry", "", roomCode, isCreator);
    }

    public void SendExitRoom(string roomCode, bool isCreator)
    {
        SendMessage("exit-room", "", roomCode, isCreator);
    }

    public void ResetReconnectionState()
    {
        isReconnecting = false;
        reconnectAttempts = 0;
        lastReconnectAttempt = 0f;
        Debug.Log("Reconnection state reset");
    	}
	
	#endregion
	
	// Public method to handle scene changes
    public void OnSceneChanged()
    {
        Debug.Log("Scene changed, NetworkManager remains active");
        // Reset any game-specific state but keep the connection
        // The GameManager will handle unsubscribing from events
    }

    // Method to disconnect from current room but keep server connection
    public void DisconnectFromRoom()
    {
        Debug.Log("Disconnecting from current room but keeping server connection");
        // This can be called when switching scenes to clean up room state
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
		if (ws != null && ws.State == WebSocketState.Open)
		{
			ws.Close();
		}
#else
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
        }
#endif
    }
}

[System.Serializable]
public class NetworkMessage
{
    public string channel;
    public string res;
    public string roomCode;
    public int dimension;
    public bool isCreator;
    public int move;
    public string appVersion;
}

[System.Serializable]
public class GameMessage
{
    public string channel;
    public string data;
    public string roomCode;
    public int dimension;
    public bool isCreator;
    public int move;
    public string appVersion;
}

[System.Serializable]
public class ServerStatus
{
    public bool isOnline;
    public string serverType;
    public string message;
}
