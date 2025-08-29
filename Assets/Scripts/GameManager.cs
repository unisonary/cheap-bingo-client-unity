using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using WebSocketSharp;
using System.Collections.Concurrent;
using System;

public class GameManager : MonoBehaviour
{
    [Header("Server Configuration")]
    [SerializeField] private bool useLocalServer = false;
    [SerializeField] private string localServerUrl = "ws://localhost:9000/ws";
    [SerializeField] private string renderServerUrl = "wss://cheap-bingo-go-server.onrender.com/ws";
    [SerializeField] private string localHttpUrl = "http://localhost:9000/";
    [SerializeField] private string renderHttpUrl = "https://cheap-bingo-go-server.onrender.com/";
    
    [Header("UI Elements")]
    [SerializeField] private TMP_InputField nameInput, roomCodeInput;
    [SerializeField] private TMP_Text alert;
    private TMP_Text gameStatus, players;
    private Button retryButton;
    private readonly Color32 markedColor = new Color32(78, 242, 245, 255);
    private readonly Color32 myLastColor = new Color32(227, 145, 186, 255);
    private readonly Color32 oppLastColor = new Color32(158, 232, 159, 255);
    private Color32 retryColor = new Color32(241, 237, 203, 255);

    private string roomCode, creatorName, joinerName, winner, lastMove;
    private bool amICreator, roomReady = false, isMyMove = false;
    private CheckWinner checkWinner;
    private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();
    private int winners = 0;

    private static GameObject mainManagerInstance;
    private void Awake()
    {
        if (mainManagerInstance != null)
            Destroy(mainManagerInstance);

        mainManagerInstance = gameObject;
        DontDestroyOnLoad(this);
    }

    private struct RoomResponse
    {
        public string channel;
        public string res;
        public string roomCode;
        public int dimension;
        public bool isCreator;
        public int move;
        public string appVersion;
    }

    private WebSocket ws;
    private bool isReconnecting = false;
    private float lastReconnectAttempt = 0f;
    private const float RECONNECT_COOLDOWN = 5f;
    private int reconnectAttempts = 0;
    private const int MAX_RECONNECT_ATTEMPTS = 3;

    private void Start()
    {
        checkWinner = new CheckWinner();

        // Check server status based on configuration
        StartCoroutine(CheckServerStatus());

        if (ws == null)
        {
            string serverUrl = useLocalServer ? localServerUrl : renderServerUrl;
            Debug.Log($"Creating WebSocket connection to: {serverUrl}");
            
            try
            {
                ws = new WebSocket(serverUrl);
                Debug.Log("WebSocket object created successfully");
                
                // Set additional WebSocket options for better connection handling
                ws.WaitTime = System.TimeSpan.FromSeconds(10);
                ws.Compression = CompressionMethod.None; // Disable compression for better compatibility
                
                // Add SSL/TLS configuration for secure connections
                if (!useLocalServer)
                {
                    // For Render.com server, configure SSL/TLS settings
                    ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11 | System.Security.Authentication.SslProtocols.Tls;
                    ws.SslConfiguration.CheckCertificateRevocation = false;
                    ws.SslConfiguration.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true; // Accept all certificates for testing
                }
                
                // Attach handlers BEFORE connecting so events are captured
                AttachWebSocketHandlers();
                
                // Use non-blocking connect to avoid freezing main thread
                ws.ConnectAsync();
                Debug.Log($"ConnectAsync() called for {(useLocalServer ? "local" : "Render")} server");
                
                // Start connection timeout monitoring
                StartCoroutine(MonitorConnectionTimeout());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to create or connect WebSocket: {e.Message}");
                Debug.LogError($"Stack trace: {e.StackTrace}");
                
                if (alert != null)
                {
                    string serverType = useLocalServer ? "local" : "Render";
                    alert.text = $"Failed to create WebSocket connection to {serverType} server: {e.Message}";
                    alert.alpha = 1f;
                }
            }
        }
    }

    private void SetMessage(object _, MessageEventArgs e)
    {
        _actions.Enqueue(() =>
        {
            RoomResponse data = JsonUtility.FromJson<RoomResponse>(e.Data);
            switch (data.channel)
            {
                case "create-room":
                    SceneManager.LoadSceneAsync(1).completed += delegate
                    {
                        roomCode = data.roomCode;

                        Debug.Log($"Room code = {roomCode}");
                        SetupGameScene();
                        gameStatus.text = "Share room code with someone to join";
                    };
                    break;
                case "game-ready":
                    roomReady = true;
                    isMyMove = amICreator;

                    if (amICreator)
                    {
                        joinerName = data.res;
                        if (joinerName == creatorName)
                        {
                            creatorName = $"{creatorName} (1)";
                            joinerName = $"{joinerName} (2)";
                        }
                        players.text = $"Players:\n{creatorName} (You)\n{joinerName} (Joiner)";
                        gameStatus.text = "Game is ready\nYou move first";
                    }
                    else
                    {
                        creatorName = data.res;
                        if (joinerName == creatorName)
                        {
                            creatorName = $"{creatorName} (1)";
                            joinerName = $"{joinerName} (2)";
                        }
                        SceneManager.LoadSceneAsync(1).completed += delegate
                        {
                            SetupGameScene();
                            players.text += "\n" + joinerName + " (You)";
                            gameStatus.text = $"Game is ready\n{creatorName} moves first";
                        };
                    }
                    break;
                case "game-on":
                    gameStatus.text = $"Current move:\n{data.move}\nYour turn now";

                    if (!lastMove.IsNullOrEmpty())
                        GameObject.Find(lastMove).GetComponent<Button>().image.color = markedColor;

                    //searching the incoming move in two dimensional array using
                    //linear search algorithm
                    int[] ndx = checkWinner.GetIndex(data.move, GridPopulator.arrBoard);
                    GridPopulator.arrBoard[ndx[0], ndx[1]] = 0;
                    Button btn = GameObject.Find($"{ndx[0]}{ndx[1]}").GetComponent<Button>();
                    btn.GetComponentInChildren<TMP_Text>().text = "x";
                    btn.image.color = oppLastColor;
                    isMyMove = true;
                    lastMove = $"{ndx[0]}{ndx[1]}";

                    SetBingoStatus();
                    break;
                case "win-claim":
                    winners++;
                    winner = amICreator ? joinerName : creatorName;
                    gameStatus.text = $"Yayy, {winner} is the winner\nYou lost";
                    Debug.Log($"Winner is {winner}");

                    // checking for draw
                    if (winners > 1)
                        gameStatus.text = "Oh wait! It's a draw\nGame over";

                    // showing retry button
                    retryColor.a = 255;
                    retryButton.image.color = retryColor;
                    break;
                case "retry":
                    ResetGame(false, amICreator ? joinerName : creatorName);
                    break;
                case "error":
                    // display some error
                    // UnityEditor.EditorUtility.DisplayDialog("Error", data.res, "Ok");
                    alert.text = data.res;
                    alert.alpha = 1f;
                    break;
                case "exit-room":
                    SceneManager.LoadScene(0);
                    break;

                default:
                    Debug.Log("Channel not implemented");
                    break;
            }
        });
    }

    public void CreateRoomClick()
    {
        if (!ws.IsAlive)
        {
            try
            {
                ws.ConnectAsync();
                // Wait a bit for connection
                StartCoroutine(WaitForConnection());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Connection failed: {e.Message}");
                alert.text = "Connection failed. Please check your network.";
                alert.alpha = 1f;
            }
        }
        else
        {
            ProceedWithCreateRoom();
        }
    }

    private System.Collections.IEnumerator WaitForConnection()
    {
        float timeout = 5f;
        float elapsed = 0f;
        
        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (ws.IsAlive)
        {
            ProceedWithCreateRoom();
        }
        else
        {
            alert.text = "Connection timeout. Please try again.";
            alert.alpha = 1f;
        }
    }

    private void ProceedWithCreateRoom()
    {
        creatorName = nameInput.text.Trim();
        if (creatorName.IsNullOrEmpty())
        {
            alert.text = "Please enter your name";
            alert.alpha = 1f;
        }
        else
        {
            amICreator = true;
            RoomResponse data = default;
            data.channel = "create-room";
            data.res = creatorName;
            data.dimension = 5;
            data.appVersion = Application.version;
            ws.Send(JsonUtility.ToJson(data));
        }
    }

    public void JoinRoomClick()
    {
        roomCode = roomCodeInput.text.Trim();
        joinerName = nameInput.text.Trim();
        if (roomCode.IsNullOrEmpty() || joinerName.IsNullOrEmpty())
        {
            alert.text = "Please enter all required details";
            alert.alpha = 1f;
            return;
        }

        if (!ws.IsAlive)
        {
            try
            {
                ws.ConnectAsync();
                StartCoroutine(WaitForJoinConnection());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Join connect failed: {e.Message}");
                alert.text = "Connection failed. Please check your network.";
                alert.alpha = 1f;
            }
            return;
        }

        ProceedWithJoinRoom();
    }

    private void ProceedWithJoinRoom()
    {
        amICreator = false;
        RoomResponse data = default;
        data.channel = "join-room";
        data.res = joinerName;
        data.roomCode = roomCode;
        data.appVersion = Application.version;
        ws.Send(JsonUtility.ToJson(data));
    }

    private System.Collections.IEnumerator WaitForJoinConnection()
    {
        float timeout = 5f;
        float elapsed = 0f;

        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (ws.IsAlive)
        {
            ProceedWithJoinRoom();
        }
        else
        {
            alert.text = "Connection timeout. Please try again.";
            alert.alpha = 1f;
        }
    }

    private void SetupGameScene()
    {
        gameStatus = GameObject.Find("GameStatus").GetComponent<TMP_Text>();
        GameObject.Find("RoomCode").GetComponent<TMP_Text>().text = $"Room code: {roomCode}";
        players = GameObject.Find("Players").GetComponent<TMP_Text>();
        players.text += "\n" + creatorName + (amICreator ? " (You)" : " (Creator)");

        retryButton = GameObject.Find("RetryBtn").GetComponent<Button>();
        retryButton.onClick.AddListener(delegate
        {
            ResetGame(true, amICreator ? creatorName : joinerName);

            RoomResponse data = default;
            data.channel = "retry";
            data.roomCode = roomCode;
            data.isCreator = !amICreator;
            ws.Send(JsonUtility.ToJson(data));
        });

        for (int i = 0; i < 5; i++)
        {
            for (int j = 0; j < 5; j++)
            {
                Button btn = GameObject.Find($"{i}{j}").GetComponent<Button>();
                btn.onClick.AddListener(delegate
                {
                    BingoBoardBtnClick(btn);
                });
            }
        }
    }

    public void BingoBoardBtnClick(Button button)
    {
        int indices = int.Parse(button.name);
        int x = indices / 10;
        int y = indices % 10;

        if (isMyMove && winner.IsNullOrEmpty() && roomReady && GridPopulator.arrBoard[x, y] != 0)
        {
            if (!lastMove.IsNullOrEmpty())
                GameObject.Find(lastMove).GetComponent<Button>().image.color = markedColor;

            lastMove = button.name;
            button.GetComponentInChildren<TMP_Text>().text = "x";
            button.image.color = myLastColor;

            RoomResponse data = default;
            data.channel = "game-on";
            data.roomCode = roomCode;
            data.move = GridPopulator.arrBoard[x, y];
            data.isCreator = !amICreator;
            ws.Send(JsonUtility.ToJson(data));

            string turn = amICreator ? joinerName : creatorName;
            gameStatus.text = $"Current move:\n{data.move}\n{turn}'s turn now";

            GridPopulator.arrBoard[x, y] = 0;
            isMyMove = false;

            SetBingoStatus();
        }
    }

    private void SetBingoStatus()
    {
        int connections = checkWinner.GetConnections(GridPopulator.arrBoard);
        for (int i = 0; i < connections && i < 5; i++)
            GameObject.Find($"MarkT{i}").GetComponent<TMP_Text>().alpha = 1f;

        if (connections > 4)
        {
            // stop game and declare winner
            winners++;
            winner = amICreator ? creatorName : joinerName;
            gameStatus.text = "Yayy, you won!";

            RoomResponse winClaim = default;
            winClaim.channel = "win-claim";
            winClaim.roomCode = roomCode;
            winClaim.isCreator = !amICreator;
            ws.Send(JsonUtility.ToJson(winClaim));

            if (winners > 1)
                gameStatus.text = "Oh wait! It's a draw\nGame over";

            // showing retry button
            retryColor.a = 255;
            retryButton.image.color = retryColor;
        }
    }

    private void ResetGame(bool whoseTurn, string player)
    {
        if (!winner.IsNullOrEmpty())
        {
            GridPopulator.SetBingoGrid();

            isMyMove = whoseTurn;
            winner = "";
            lastMove = "";
            winners = 0;
            gameStatus.text = $"Game is ready\n{(whoseTurn ? "You move" : player + " moves")} first";

            for (int i = 0; i < 5; i++)
                GameObject.Find($"MarkT{i}").GetComponent<TMP_Text>().alpha = 0f;

            // hide retry button
            retryColor.a = 0;
            retryButton.image.color = retryColor;
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (SceneManager.GetActiveScene().buildIndex == 1)
            {
                RoomResponse data = default;
                data.channel = "exit-room";
                data.roomCode = roomCode;
                data.isCreator = !amICreator;
                try { ws.Send(JsonUtility.ToJson(data)); } catch { }
                SceneManager.LoadScene(0);
            }
            else
                Application.Quit();
        }

        // Monitor connection status with proper reconnection logic
        if (ws != null && !ws.IsAlive && roomReady && !isReconnecting)
        {
            float currentTime = Time.time;
            if (currentTime - lastReconnectAttempt > RECONNECT_COOLDOWN && reconnectAttempts < MAX_RECONNECT_ATTEMPTS)
            {
                Debug.LogWarning($"WebSocket connection lost, attempting to reconnect... (Attempt {reconnectAttempts + 1}/{MAX_RECONNECT_ATTEMPTS})");
                isReconnecting = true;
                lastReconnectAttempt = currentTime;
                reconnectAttempts++;
                
                StartCoroutine(AttemptReconnection());
            }
            else if (reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                Debug.LogError("Maximum reconnection attempts reached. Stopping reconnection loop.");
                if (alert != null)
                {
                    string serverType = useLocalServer ? "local" : "Render";
                    alert.text = $"Connection failed after {MAX_RECONNECT_ATTEMPTS} attempts. {serverType} server may be down.";
                    alert.alpha = 1f;
                }
            }
        }

        // Debug connection status
        if (Input.GetKeyDown(KeyCode.C))
        {
            TestConnection();
        }
        
        // Reset reconnection state (for debugging)
        if (Input.GetKeyDown(KeyCode.X))
        {
            ResetReconnectionState();
        }
        
        // Test server connectivity (for debugging)
        if (Input.GetKeyDown(KeyCode.T))
        {
            TestServerConnectivity();
        }
        
        // Test WebSocket connection specifically (for debugging)
        if (Input.GetKeyDown(KeyCode.W))
        {
            TestWebSocketConnection();
        }
        
        // Server switching shortcuts
        if (Input.GetKeyDown(KeyCode.L))
        {
            SwitchToLocalServer();
        }
        
        if (Input.GetKeyDown(KeyCode.R))
        {
            SwitchToRenderServer();
        }

        while (_actions.Count > 0)
        {
            if (_actions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }

    private System.Collections.IEnumerator CheckServerStatus()
    {
        string serverType = useLocalServer ? "local" : "Render";
        string httpUrl = useLocalServer ? localHttpUrl : renderHttpUrl;
        
        Debug.Log($"Checking if {serverType} server is reachable...");
        
        using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequest.Get(httpUrl))
        {
            // Set timeout for HTTP request
            request.timeout = 10; // Increased timeout for better reliability
            yield return request.SendWebRequest();
            
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"{serverType} HTTP check successful: {request.responseCode}");
                Debug.Log($"Response: {request.downloadHandler.text}");
                
                if (alert != null)
                {
                    alert.text = $"{serverType} server is reachable. Proceeding with WebSocket connection...";
                    alert.alpha = 1f;
                }
            }
            else if (request.result == UnityEngine.Networking.UnityWebRequest.Result.ConnectionError)
            {
                Debug.LogError($"{serverType} HTTP connection failed: {request.error}");
                if (alert != null)
                {
                    string errorMsg = useLocalServer 
                        ? $"Local server connection failed after 10s timeout. Make sure Go server is running on localhost:9000"
                        : $"Render server connection failed after 10s timeout. Server may be down.";
                    alert.text = errorMsg;
                    alert.alpha = 1f;
                }
            }
            else
            {
                Debug.LogError($"{serverType} HTTP check failed: {request.error}");
                if (alert != null)
                {
                    string errorMsg = useLocalServer 
                        ? $"Local server unreachable: {request.error}. Make sure Go server is running on localhost:9000"
                        : $"Render server unreachable: {request.error}. Server may be down.";
                    alert.text = errorMsg;
                    alert.alpha = 1f;
                }
            }
        }
    }

    private System.Collections.IEnumerator MonitorConnectionTimeout()
    {
        float timeout = 5f;
        float elapsed = 0f;
        string serverType = useLocalServer ? "local" : "Render";
        
        Debug.Log($"Starting {serverType} server connection timeout monitoring (5s)...");

        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (!ws.IsAlive)
        {
            Debug.LogWarning($"⚠️ {serverType} server connection timeout after {timeout}s! Server may be down or not responding.");
            if (alert != null)
            {
                string timeoutMsg = useLocalServer 
                    ? $"Local server connection timeout after {timeout}s. Make sure Go server is running on localhost:9000"
                    : $"Render server connection timeout after {timeout}s. Server may be down or overloaded.";
                alert.text = timeoutMsg;
                alert.alpha = 1f;
            }
        }
        else
        {
            Debug.Log($"✅ {serverType} server connection established within {elapsed:F1}s");
        }
    }

    private System.Collections.IEnumerator MonitorConnection()
    {
        float timeout = 10f;
        float elapsed = 0f;
        
        Debug.Log("Starting connection monitoring...");
        
        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            Debug.Log($"Connection attempt: {elapsed:F1}s elapsed, Status: {ws.ReadyState}");
            yield return new WaitForSeconds(0.5f);
        }
        
        if (ws.IsAlive)
        {
            Debug.Log("WebSocket connection established successfully!");
        }
        else
        {
            Debug.LogError($"Connection failed after {timeout}s. ReadyState: {ws.ReadyState}");
            if (alert != null)
            {
                alert.text = "Connection to Render server failed. Server may be down.";
                alert.alpha = 1f;
            }
        }
    }

    private void TestConnection()
    {
        if (ws != null)
        {
            Debug.Log($"WebSocket Status: IsAlive={ws.IsAlive}, ReadyState={ws.ReadyState}");
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                string status = ws.IsAlive ? $"Connected to {serverType} server" : $"Disconnected from {serverType} server";
                alert.text = status;
                alert.alpha = 1f;
            }
        }
        else
        {
            Debug.Log("WebSocket is null");
            if (alert != null)
            {
                alert.text = "WebSocket not initialized";
                alert.alpha = 1f;
            }
        }
    }

    // Public method to reset reconnection state and force a fresh connection attempt
    public void ResetReconnectionState()
    {
        isReconnecting = false;
        reconnectAttempts = 0;
        lastReconnectAttempt = 0f;
        
        if (alert != null)
        {
            alert.text = "Reconnection state reset. Will attempt to reconnect if needed.";
            alert.alpha = 1f;
        }
        
        Debug.Log("Reconnection state reset");
    }

    // Public method to test server connectivity
    public void TestServerConnectivity()
    {
        StartCoroutine(CheckServerStatus());
        
        if (alert != null)
        {
            string serverType = useLocalServer ? "local" : "Render";
            alert.text = $"Testing {serverType} server connectivity...";
            alert.alpha = 1f;
        }
    }

    // Test WebSocket connection specifically
    public void TestWebSocketConnection()
    {
        if (ws == null)
        {
            if (alert != null)
            {
                alert.text = "WebSocket not initialized. Creating new connection...";
                alert.alpha = 1f;
            }
            
            // Force recreate WebSocket
            if (ws != null && ws.IsAlive)
            {
                ws.Close();
                ws = null;
            }
            
            StartCoroutine(CreateWebSocketConnection());
            return;
        }

        if (ws.IsAlive)
        {
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                alert.text = $"WebSocket is alive and connected to {serverType} server";
                alert.alpha = 1f;
            }
        }
        else
        {
            if (alert != null)
            {
                alert.text = "WebSocket is not alive. Attempting to reconnect...";
                alert.alpha = 1f;
            }
            
            StartCoroutine(CreateWebSocketConnection());
        }
    }

    private System.Collections.IEnumerator CreateWebSocketConnection()
    {
        string serverUrl = useLocalServer ? localServerUrl : renderServerUrl;
        Debug.Log($"Creating new WebSocket connection to: {serverUrl}");
        
        bool connectionCreated = false;
        System.Exception connectionException = null;
        
        try
        {
            ws = new WebSocket(serverUrl);
            Debug.Log("WebSocket object created successfully");
            
            // Set additional WebSocket options for better connection handling
            ws.WaitTime = System.TimeSpan.FromSeconds(15); // Increased timeout
            ws.Compression = CompressionMethod.None;
            
            // Add SSL/TLS configuration for secure connections
            if (!useLocalServer)
            {
                // For Render.com server, configure SSL/TLS settings
                ws.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls11 | System.Security.Authentication.SslProtocols.Tls;
                ws.SslConfiguration.CheckCertificateRevocation = false;
                ws.SslConfiguration.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            
            // Attach handlers BEFORE connecting
            AttachWebSocketHandlers();
            
            // Use non-blocking connect
            ws.ConnectAsync();
            Debug.Log($"ConnectAsync() called for {(useLocalServer ? "local" : "Render")} server");
            
            connectionCreated = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create WebSocket connection: {e.Message}");
            connectionException = e;
        }
        
        // If connection creation failed, handle error and exit
        if (!connectionCreated)
        {
            if (alert != null)
            {
                string errorMsg = connectionException != null 
                    ? $"WebSocket creation failed: {connectionException.Message}"
                    : "WebSocket creation failed";
                alert.text = errorMsg;
                alert.alpha = 1f;
            }
            yield break;
        }
        
        // Wait for connection with longer timeout (outside try/catch to satisfy CS1626)
        float timeout = 15f;
        float elapsed = 0f;
        
        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (ws.IsAlive)
        {
            Debug.Log("WebSocket connection established successfully!");
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                alert.text = $"Successfully connected to {serverType} server!";
                alert.alpha = 1f;
            }
        }
        else
        {
            Debug.LogWarning("WebSocket connection failed to establish");
            if (alert != null)
            {
                alert.text = "WebSocket connection failed. Check server status.";
                alert.alpha = 1f;
            }
        }
    }

    // Runtime server switching
    public void ToggleServer()
    {
        useLocalServer = !useLocalServer;
        string serverType = useLocalServer ? "local" : "Render";
        Debug.Log($"Switched to {serverType} server");
        
        if (alert != null)
        {
            alert.text = $"Switched to {serverType} server. Restart to apply changes.";
            alert.alpha = 1f;
        }
        
        // Disconnect current connection
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
            ws = null;
        }
    }

    // Manual server switching methods
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

    private void RestartConnection()
    {
        if (ws != null && ws.IsAlive)
        {
            ws.Close();
            ws = null;
        }
        
        // Reset reconnection state
        isReconnecting = false;
        reconnectAttempts = 0;
        lastReconnectAttempt = 0f;
        
        // Reinitialize connection
        string serverUrl = useLocalServer ? localServerUrl : renderServerUrl;
        ws = new WebSocket(serverUrl);
        
        // Attach handlers BEFORE connecting so events are captured
        AttachWebSocketHandlers();
        
        // Non-blocking connect
        ws.ConnectAsync();
        
        string serverType = useLocalServer ? "local" : "Render";
        Debug.Log($"Restarted connection to {serverType} server: {serverUrl}");
        
        // Start timeout monitoring for restarted connection
        StartCoroutine(MonitorConnectionTimeout());
    }

    private System.Collections.IEnumerator AttemptReconnection()
    {
        Debug.Log("Attempting to reconnect to WebSocket server...");
        try
        {
            ws.ConnectAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Reconnection attempt failed with exception: {e.Message}");
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                alert.text = $"Reconnection to {serverType} server failed: {e.Message}";
                alert.alpha = 1f;
            }
            isReconnecting = false;
            yield break;
        }

        // Wait for connection to establish or fail (outside try/catch to satisfy CS1626)
        float timeout = 10f;
        float elapsed = 0f;
        while (!ws.IsAlive && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (ws.IsAlive)
        {
            Debug.Log("Reconnection successful!");
            reconnectAttempts = 0; // Reset counter on successful connection
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                alert.text = $"Reconnected to {serverType} server successfully!";
                alert.alpha = 1f;
            }
        }
        else
        {
            Debug.LogWarning("Reconnection attempt failed");
            if (alert != null)
            {
                string serverType = useLocalServer ? "local" : "Render";
                alert.text = $"Reconnection to {serverType} server failed. Will retry in {RECONNECT_COOLDOWN}s.";
                alert.alpha = 1f;
            }
        }

        isReconnecting = false;
    }

    private void AttachWebSocketHandlers()
    {
        if (ws == null) return;
        
        ws.OnOpen += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.Log("WebSocket connection opened successfully!");
                if (alert != null)
                {
                    string serverType = useLocalServer ? "local" : "Render";
                    alert.text = $"Connected to {serverType} server successfully!";
                    alert.alpha = 1f;
                }
            });
        };
        
        ws.OnMessage += SetMessage;
        ws.OnError += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.LogError($"WebSocket error: {e.Message}");
                Debug.LogError($"WebSocket error details: Exception={e.Exception?.Message}, ReadyState={ws.ReadyState}");
                
                string errorMsg = useLocalServer 
                    ? $"Connection error: {e.Message}. Make sure Go server is running on localhost:9000"
                    : $"Connection error: {e.Message}. Render server may be down or not responding.";
                alert.text = errorMsg;
                alert.alpha = 1f;
                isReconnecting = false; // Reset reconnection flag on error
            });
        };
        ws.OnClose += (_, e) =>
        {
            _actions.Enqueue(() =>
            {
                Debug.Log($"WebSocket closed: Code {e.Code}, Reason: {e.Reason}");
                Debug.Log($"WebSocket close details: WasClean={e.WasClean}, ReadyState={ws.ReadyState}");
                
                string serverType = useLocalServer ? "local" : "Render";
                string closeReason = e.Reason;
                
                // Provide more specific error messages based on close codes
                switch (e.Code)
                {
                    case 1000: // Normal closure
                        closeReason = "Normal closure";
                        break;
                    case 1001: // Going away
                        closeReason = "Server going away";
                        break;
                    case 1002: // Protocol error
                        closeReason = "Protocol error";
                        break;
                    case 1003: // Unsupported data
                        closeReason = "Unsupported data type";
                        break;
                    case 1005: // No status received
                        closeReason = "No status received";
                        break;
                    case 1006: // Abnormal closure
                        closeReason = "Abnormal closure - connection lost";
                        break;
                    case 1011: // Server error
                        closeReason = "Server error";
                        break;
                    case 1015: // TLS handshake failure
                        closeReason = "TLS/SSL handshake failure - check server certificate";
                        break;
                    default:
                        closeReason = e.Reason ?? $"Unknown error (Code: {e.Code})";
                        break;
                }
                
                alert.text = $"Connection closed: {closeReason}. {serverType} server may be down.";
                alert.alpha = 1f;
                isReconnecting = false; // Reset reconnection flag on close
                
                if (roomReady)
                {
                    RoomResponse data = default;
                    data.channel = "exit-room";
                    data.roomCode = roomCode;
                    data.isCreator = !amICreator;
                    try { ws.Send(JsonUtility.ToJson(data)); } catch { }
                    SceneManager.LoadScene(0);
                }
            });
        };
    }
}
