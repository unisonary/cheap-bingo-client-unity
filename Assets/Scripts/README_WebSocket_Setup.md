# WebSocket Setup for Unity Bingo Game

This document describes the dual-platform WebSocket implementation for the Unity Bingo game, supporting both WebGL (browser) and Windows/other platforms.

## 🏗️ Architecture Overview

The `NetworkManager` uses conditional compilation to support two different WebSocket libraries:

- **🌐 WebGL Builds**: Uses `NativeWebSocket` for browser compatibility
- **🖥️ Windows/Other Platforms**: Uses `WebSocketSharp` for native applications

## 📦 Package Dependencies

### WebGL (Browser) Support
```json
{
  "dependencies": {
    "com.endel.nativewebsocket": "https://github.com/endel/NativeWebSocket.git#upm"
  }
}
```

### Windows/Other Platform Support
- `websocket-sharp.dll` (already included in `Assets/Plugins/`)

## 🔧 Implementation Details

### Conditional Compilation
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
using NativeWebSocket;   // WebGL build
#else
using WebSocketSharp;    // Windows/Editor build
#endif
```

### WebSocket Field Declaration
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
private WebSocket ws;  // NativeWebSocket.WebSocket
#else
private WebSocket ws;  // WebSocketSharp.WebSocket
#endif
```

### Connection Methods
```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
// WebGL: Async connection
ws = new WebSocket(serverUrl);
await ws.Connect();
#else
// Windows: Async connection
ws = new WebSocket(serverUrl);
ws.ConnectAsync();
#endif
```

### Message Dispatching
```csharp
void Update()
{
    // Process queued actions on main thread
    while (_actions.Count > 0)
    {
        if (_actions.TryDequeue(out var action))
        {
            action?.Invoke();
        }
    }
    
#if UNITY_WEBGL && !UNITY_EDITOR
    ws?.DispatchMessageQueue();  // Required for NativeWebSocket
#endif
    
    // Monitor connection status...
}
```

### Event Handling
Both platforms use the same event pattern but with different signatures:

**WebGL (NativeWebSocket):**
```csharp
ws.OnOpen += () => { /* handle open */ };
ws.OnMessage += (bytes) => { /* handle message */ };
ws.OnError += (err) => { /* handle error */ };
ws.OnClose += (code) => { /* handle close */ };
```

**Windows (WebSocketSharp):**
```csharp
ws.OnOpen += (sender, e) => { /* handle open */ };
ws.OnMessage += (sender, e) => { /* handle message */ };
ws.OnError += (sender, e) => { /* handle error */ };
ws.OnClose += (sender, e) => { /* handle close */ };
```

## 🚀 Usage

### Building for Different Platforms

1. **WebGL Build**:
   - Set platform to WebGL in Build Settings
   - Build and deploy to web server
   - Uses NativeWebSocket automatically

2. **Windows Build**:
   - Set platform to Windows in Build Settings
   - Build executable
   - Uses WebSocketSharp automatically

### Game Integration
The `GameManager` subscribes to `NetworkManager` events:

```csharp
void Start()
{
    NetworkManager.Instance.OnConnected += HandleConnected;
    NetworkManager.Instance.OnDisconnected += HandleDisconnected;
    NetworkManager.Instance.OnRoomCreated += HandleRoomCreated;
    // ... other event subscriptions
}
```

## 🔄 Thread Safety

All WebSocket callbacks are queued to the main thread using a `ConcurrentQueue<Action>`:

```csharp
private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

// In event handlers:
_actions.Enqueue(() => {
    // Handle event on main thread
    Debug.Log("Message received on main thread");
});
```

## 🛠️ Troubleshooting

### Common Issues

1. **WebGL Build Errors**:
   - Ensure `com.endel.nativewebsocket` is in `Packages/manifest.json`
   - Check that `#if UNITY_WEBGL && !UNITY_EDITOR` conditions are correct

2. **Windows Build Errors**:
   - Verify `websocket-sharp.dll` is in `Assets/Plugins/`
   - Check SSL configuration for secure connections

3. **Connection Issues**:
   - Verify server URLs are correct
   - Check firewall/network settings
   - Ensure server is running and accessible

### Debug Logging
The NetworkManager includes comprehensive debug logging:
- Connection status
- Message sending/receiving
- Error handling
- Reconnection attempts

## 📋 API Reference

### Public Methods
- `CreateRoom(string playerName)`
- `JoinRoom(string roomCode, string playerName)`
- `SendGameMove(string roomCode, int move, bool isCreator)`
- `SendWinClaim(string roomCode, bool isCreator)`
- `SendRetry(string roomCode, bool isCreator)`
- `SendExitRoom(string roomCode, bool isCreator)`

### Properties
- `IsConnected` - Current connection status
- `IsReconnecting` - Reconnection in progress
- `CurrentServerUrl` - Active server URL
- `CurrentServerType` - "Local" or "Render"

### Events
- `OnConnected` - Connection established
- `OnDisconnected` - Connection lost
- `OnRoomCreated` - Room creation successful
- `OnGameReady` - Game ready to start
- `OnGameMove` - Game move received
- `OnWinClaim` - Win claim received
- `OnRetry` - Retry request received
- `OnExitRoom` - Exit room request
- `OnError` - Error occurred

## 🎯 Best Practices

1. **Always check `IsConnected` before sending messages**
2. **Handle all events appropriately in your game logic**
3. **Use the singleton pattern: `NetworkManager.Instance`**
4. **Test both WebGL and Windows builds regularly**
5. **Monitor connection status and handle reconnections gracefully**

This implementation provides a robust, cross-platform WebSocket solution that works seamlessly across different Unity build targets.
