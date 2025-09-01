# Network Classes Documentation

This document describes the network-related classes created for the Unity Bingo project to separate networking concerns from game logic.

## Overview

The networking functionality has been refactored into two main classes:

1. **NetworkManager** - Core WebSocket connection and message handling
2. **GameManager** - Game logic using NetworkManager for networking

## NetworkManager Class

### Purpose
Handles all WebSocket connections, message sending/receiving, and connection management.

### Key Features
- **Singleton Pattern**: Easy access from anywhere in the project
- **Event-Driven Architecture**: Uses C# events for communication
- **Automatic Reconnection**: Handles connection drops with configurable retry logic
- **Server Switching**: Can switch between local and remote servers
- **Platform-Specific Handling**: Different behavior for WebGL vs standalone builds

### Configuration
```csharp
[Header("Server Configuration")]
[SerializeField] private bool useLocalServer = false;
[SerializeField] private string localServerUrl = "ws://localhost:9000/ws";
[SerializeField] private string renderServerUrl = "wss://cheap-bingo-go-server.onrender.com/ws";

[Header("Connection Settings")]
[SerializeField] private float connectionTimeout = 15f;
[SerializeField] private float reconnectCooldown = 5f;
[SerializeField] private int maxReconnectAttempts = 3;
```

### Events
```csharp
public event Action<string, string> OnRoomCreated;
public event Action<string, string> OnGameReady;
public event Action<int> OnGameMove;
public event Action<string> OnWinClaim;
public event Action<string> OnRetry;
public event Action OnExitRoom;
public event Action<string> OnError;
public event Action OnConnected;
public event Action<string> OnDisconnected;
```

### Usage Example
```csharp
// Subscribe to events
NetworkManager.Instance.OnRoomCreated += HandleRoomCreated;
NetworkManager.Instance.OnGameReady += HandleGameReady;

// Send messages
NetworkManager.Instance.CreateRoom("PlayerName");
NetworkManager.Instance.JoinRoom("ABC123", "PlayerName");
NetworkManager.Instance.SendGameMove("ABC123", 42, true);

// Check connection status
if (NetworkManager.Instance.IsConnected)
{
    // Connected to server
}

// Switch servers
NetworkManager.Instance.SwitchToLocalServer();
NetworkManager.Instance.SwitchToRenderServer();
```

## GameManager Class

### Purpose
Handles all game logic and UI interactions, using NetworkManager for networking operations.

### Key Changes from Original
- **Event-Based Communication**: Uses NetworkManager events instead of direct WebSocket handling
- **Cleaner Separation**: Game logic is separate from networking logic
- **Simplified Error Handling**: Network errors are handled through events
- **Better Maintainability**: Easier to modify game logic without affecting networking

### Migration Steps
1. Replace direct WebSocket calls with NetworkManager method calls
2. Subscribe to NetworkManager events in Start()
3. Unsubscribe from events in OnDestroy()
4. Remove WebSocket-related fields and methods
5. Update UI event handlers to use NetworkManager

## Integration Guide

### 1. Setup NetworkManager
```csharp
// Add NetworkManager to a GameObject in your scene
// Or it will be created automatically when first accessed
GameObject networkManagerGO = new GameObject("NetworkManager");
NetworkManager networkManager = networkManagerGO.AddComponent<NetworkManager>();
```

### 2. Subscribe to Events
```csharp
private void Start()
{
    // Subscribe to network events
    NetworkManager.Instance.OnRoomCreated += HandleRoomCreated;
    NetworkManager.Instance.OnGameReady += HandleGameReady;
    NetworkManager.Instance.OnError += HandleNetworkError;
}
```

### 3. Handle Network Events
```csharp
private void HandleRoomCreated(string roomCode, string playerName)
{
    // Handle room creation
    Debug.Log($"Room created: {roomCode} by {playerName}");
}

private void HandleNetworkError(string error)
{
    // Handle network errors
    Debug.LogError($"Network error: {error}");
}
```

### 4. Send Messages
```csharp
// Instead of direct WebSocket calls
NetworkManager.Instance.CreateRoom("PlayerName");
NetworkManager.Instance.SendGameMove("ABC123", 42, true);
```

## Benefits of This Architecture

1. **Separation of Concerns**: Networking logic is separate from game logic
2. **Reusability**: NetworkManager can be used by other game systems
3. **Maintainability**: Easier to debug and modify networking code
4. **Testability**: NetworkManager can be tested independently
5. **Event-Driven**: Loose coupling between components
6. **Platform Support**: Better handling of WebGL vs standalone differences
7. **Error Handling**: Centralized error handling and reconnection logic

## Troubleshooting

### Common Issues

1. **Connection Failed**: Check server URLs and firewall settings
2. **WebGL Issues**: Some features may be limited in WebGL builds
3. **Event Not Firing**: Ensure proper event subscription in Start()
4. **Reconnection Loops**: Check maxReconnectAttempts and reconnectCooldown settings

### Debug Commands

The NetworkManager includes several debug features accessible through keyboard shortcuts:
- **C**: Test connection status
- **X**: Reset reconnection state
- **T**: Test server connectivity
- **W**: Test WebSocket connection
- **L**: Switch to local server
- **R**: Switch to Render server

## Future Enhancements

1. **Message Queuing**: Queue messages when disconnected
2. **Compression**: Add message compression for better performance
3. **Encryption**: Add end-to-end encryption for sensitive data
4. **Metrics**: Add network performance metrics and analytics
5. **Fallback Protocols**: Support for HTTP fallback when WebSocket fails

## Project Structure

```
Assets/Scripts/
├── NetworkManager.cs          # Core networking functionality
├── GameManager.cs             # Game logic using NetworkManager
├── GridPopulator.cs           # Bingo grid management
├── CheckWinner.cs             # Win condition checking
└── README_NetworkClasses.md   # This documentation
```

## Notes

- The original GameManager.cs with mixed networking logic has been removed
- NetworkUtils.cs was removed as it was optional and not currently used
- The project now has a clean separation between networking and game logic
- All networking operations go through the NetworkManager singleton
