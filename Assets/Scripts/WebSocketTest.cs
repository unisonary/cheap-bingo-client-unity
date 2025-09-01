using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple test script to verify WebSocket functionality on both platforms
/// </summary>
public class WebSocketTest : MonoBehaviour
{
    [Header("UI References")]
    public Text statusText;
    public Text platformText;
    public Button connectButton;
    public Button disconnectButton;
    public Button testMessageButton;
    
    [Header("Test Configuration")]
    public string testServerUrl = "ws://localhost:3000";
    
    private NetworkManager networkManager;
    
    void Start()
    {
        networkManager = NetworkManager.Instance;
        
        // Display platform information
        DisplayPlatformInfo();
        
        // Subscribe to network events
        networkManager.OnConnected += OnConnected;
        networkManager.OnDisconnected += OnDisconnected;
        networkManager.OnError += OnError;
        
        // Setup UI
        SetupUI();
        
        // Update status
        UpdateStatus();
    }
    
    void DisplayPlatformInfo()
    {
        string platform = "Unknown";
        
#if UNITY_WEBGL && !UNITY_EDITOR
        platform = "WebGL (NativeWebSocket)";
#else
        platform = "Windows/Other (WebSocketSharp)";
#endif
        
        if (platformText != null)
        {
            platformText.text = $"Platform: {platform}";
        }
        
        Debug.Log($"WebSocket Test - Platform: {platform}");
    }
    
    void SetupUI()
    {
        if (connectButton != null)
        {
            connectButton.onClick.AddListener(TestConnection);
        }
        
        if (disconnectButton != null)
        {
            disconnectButton.onClick.AddListener(TestDisconnection);
        }
        
        if (testMessageButton != null)
        {
            testMessageButton.onClick.AddListener(TestMessage);
        }
    }
    
    void UpdateStatus()
    {
        if (statusText != null)
        {
            string status = networkManager.IsConnected ? "Connected" : "Disconnected";
            statusText.text = $"Status: {status}";
        }
    }
    
    void TestConnection()
    {
        Debug.Log("Testing WebSocket connection...");
        
        // This would normally connect to your server
        // For testing, we'll just log the attempt
        Debug.Log($"Attempting to connect to: {networkManager.CurrentServerUrl}");
        Debug.Log($"Server Type: {networkManager.CurrentServerType}");
        
        UpdateStatus();
    }
    
    void TestDisconnection()
    {
        Debug.Log("Testing WebSocket disconnection...");
        networkManager.DisconnectFromRoom();
        UpdateStatus();
    }
    
    void TestMessage()
    {
        if (!networkManager.IsConnected)
        {
            Debug.LogWarning("Cannot send test message - not connected");
            return;
        }
        
        Debug.Log("Sending test message...");
        networkManager.SendMessage("test", "Hello from Unity!", "", false);
    }
    
    void OnConnected()
    {
        Debug.Log("✅ WebSocket connected successfully!");
        UpdateStatus();
    }
    
    void OnDisconnected(string reason)
    {
        Debug.Log($"❌ WebSocket disconnected: {reason}");
        UpdateStatus();
    }
    
    void OnError(string error)
    {
        Debug.LogError($"❌ WebSocket error: {error}");
        UpdateStatus();
    }
    
    void Update()
    {
        // Update status every frame
        UpdateStatus();
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (networkManager != null)
        {
            networkManager.OnConnected -= OnConnected;
            networkManager.OnDisconnected -= OnDisconnected;
            networkManager.OnError -= OnError;
        }
    }
}
