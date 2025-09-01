using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
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
    private int winners = 0;

    private static GameObject mainManagerInstance;
    
    private void Awake()
    {
        if (mainManagerInstance != null)
            Destroy(mainManagerInstance);

        mainManagerInstance = gameObject;
        DontDestroyOnLoad(this);
    }

    private void Start()
    {
        checkWinner = new CheckWinner();
        SetupNetworkEvents();
    }

    private void SetupNetworkEvents()
    {
        // Subscribe to network events
        NetworkManager.Instance.OnRoomCreated += HandleRoomCreated;
        NetworkManager.Instance.OnGameReady += HandleGameReady;
        NetworkManager.Instance.OnGameMove += HandleGameMove;
        NetworkManager.Instance.OnWinClaim += HandleWinClaim;
        NetworkManager.Instance.OnRetry += HandleRetry;
        NetworkManager.Instance.OnExitRoom += HandleExitRoom;
        NetworkManager.Instance.OnError += HandleNetworkError;
        NetworkManager.Instance.OnConnected += HandleConnected;
        NetworkManager.Instance.OnDisconnected += HandleDisconnected;
    }

    #region Network Event Handlers

    private void HandleRoomCreated(string roomCode, string playerName)
    {
        this.roomCode = roomCode;
        Debug.Log($"Room code = {roomCode}");
        
        SceneManager.LoadSceneAsync(1).completed += delegate
        {
            SetupGameScene();
            gameStatus.text = "Share room code with someone to join";
        };
    }

    private void HandleGameReady(string playerName, string roomCode)
    {
        roomReady = true;
        isMyMove = amICreator;

        if (amICreator)
        {
            joinerName = playerName;
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
            creatorName = playerName;
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
    }

    private void HandleGameMove(int move)
    {
        gameStatus.text = $"Current move:\n{move}\nYour turn now";

        if (!string.IsNullOrEmpty(lastMove))
            GameObject.Find(lastMove).GetComponent<Button>().image.color = markedColor;

        // Search the incoming move in two dimensional array using linear search algorithm
        int[] ndx = checkWinner.GetIndex(move, GridPopulator.arrBoard);
        GridPopulator.arrBoard[ndx[0], ndx[1]] = 0;
        Button btn = GameObject.Find($"{ndx[0]}{ndx[1]}").GetComponent<Button>();
        btn.GetComponentInChildren<TMP_Text>().text = "x";
        btn.image.color = oppLastColor;
        isMyMove = true;
        lastMove = $"{ndx[0]}{ndx[1]}";

        SetBingoStatus();
    }

    private void HandleWinClaim(string playerName)
    {
        winners++;
        winner = amICreator ? joinerName : creatorName;
        gameStatus.text = $"Yayy, {winner} is the winner\nYou lost";
        Debug.Log($"Winner is {winner}");

        // Check for draw
        if (winners > 1)
            gameStatus.text = "Oh wait! It's a draw\nGame over";

        // Show retry button
        retryColor.a = 255;
        retryButton.image.color = retryColor;
    }

    private void HandleRetry(string playerName)
    {
        ResetGame(false, amICreator ? joinerName : creatorName);
    }

    private void HandleExitRoom()
    {
        SceneManager.LoadScene(0);
    }

    private void HandleNetworkError(string error)
    {
        alert.text = error;
        alert.alpha = 1f;
    }

    private void HandleConnected()
    {
        if (alert != null)
        {
            alert.text = $"Connected to {NetworkManager.Instance.CurrentServerType} server successfully!";
            alert.alpha = 1f;
        }
    }

    private void HandleDisconnected(string reason)
    {
        if (alert != null)
        {
            alert.text = $"Disconnected: {reason}";
            alert.alpha = 1f;
        }
    }

    #endregion

    #region UI Event Handlers

    public void CreateRoomClick()
    {
        creatorName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(creatorName))
        {
            alert.text = "Please enter your name";
            alert.alpha = 1f;
            return;
        }

        if (!NetworkManager.Instance.IsConnected)
        {
            alert.text = "Not connected to server. Please wait...";
            alert.alpha = 1f;
            return;
        }

        amICreator = true;
        NetworkManager.Instance.CreateRoom(creatorName);
    }

    public void JoinRoomClick()
    {
        roomCode = roomCodeInput.text.Trim();
        joinerName = nameInput.text.Trim();
        if (string.IsNullOrEmpty(roomCode) || string.IsNullOrEmpty(joinerName))
        {
            alert.text = "Please enter all required details";
            alert.alpha = 1f;
            return;
        }

        if (!NetworkManager.Instance.IsConnected)
        {
            alert.text = "Not connected to server. Please wait...";
            alert.alpha = 1f;
            return;
        }

        amICreator = false;
        NetworkManager.Instance.JoinRoom(roomCode, joinerName);
    }

    public void BingoBoardBtnClick(Button button)
    {
        int indices = int.Parse(button.name);
        int x = indices / 10;
        int y = indices % 10;

        if (isMyMove && string.IsNullOrEmpty(winner) && roomReady && GridPopulator.arrBoard[x, y] != 0)
        {
            if (!string.IsNullOrEmpty(lastMove))
                GameObject.Find(lastMove).GetComponent<Button>().image.color = markedColor;

            lastMove = button.name;
            button.GetComponentInChildren<TMP_Text>().text = "x";
            button.image.color = myLastColor;

            NetworkManager.Instance.SendGameMove(roomCode, GridPopulator.arrBoard[x, y], !amICreator);

            string turn = amICreator ? joinerName : creatorName;
            gameStatus.text = $"Current move:\n{GridPopulator.arrBoard[x, y]}\n{turn}'s turn now";

            GridPopulator.arrBoard[x, y] = 0;
            isMyMove = false;

            SetBingoStatus();
        }
    }

    #endregion

    #region Game Logic

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
            NetworkManager.Instance.SendRetry(roomCode, !amICreator);
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

    private void SetBingoStatus()
    {
        int connections = checkWinner.GetConnections(GridPopulator.arrBoard);
        for (int i = 0; i < connections && i < 5; i++)
            GameObject.Find($"MarkT{i}").GetComponent<TMP_Text>().alpha = 1f;

        if (connections > 4)
        {
            // Stop game and declare winner
            winners++;
            winner = amICreator ? creatorName : joinerName;
            gameStatus.text = "Yayy, you won!";

            NetworkManager.Instance.SendWinClaim(roomCode, !amICreator);

            if (winners > 1)
                gameStatus.text = "Oh wait! It's a draw\nGame over";

            // Show retry button
            retryColor.a = 255;
            retryButton.image.color = retryColor;
        }
    }

    private void ResetGame(bool whoseTurn, string player)
    {
        if (!string.IsNullOrEmpty(winner))
        {
            GridPopulator.SetBingoGrid();

            isMyMove = whoseTurn;
            winner = "";
            lastMove = "";
            winners = 0;
            gameStatus.text = $"Game is ready\n{(whoseTurn ? "You move" : player + " moves")} first";

            for (int i = 0; i < 5; i++)
                GameObject.Find($"MarkT{i}").GetComponent<TMP_Text>().alpha = 0f;

            // Hide retry button
            retryColor.a = 0;
            retryButton.image.color = retryColor;
        }
    }

    #endregion

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (SceneManager.GetActiveScene().buildIndex == 1)
            {
                NetworkManager.Instance.SendExitRoom(roomCode, !amICreator);
                SceneManager.LoadScene(0);
            }
            else
                Application.Quit();
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from network events
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnRoomCreated -= HandleRoomCreated;
            NetworkManager.Instance.OnGameReady -= HandleGameReady;
            NetworkManager.Instance.OnGameMove -= HandleGameMove;
            NetworkManager.Instance.OnWinClaim -= HandleWinClaim;
            NetworkManager.Instance.OnRetry -= HandleRetry;
            NetworkManager.Instance.OnExitRoom -= HandleExitRoom;
            NetworkManager.Instance.OnError -= HandleNetworkError;
            NetworkManager.Instance.OnConnected -= HandleConnected;
            NetworkManager.Instance.OnDisconnected -= HandleDisconnected;
        }
        
        // Notify NetworkManager about scene change
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.OnSceneChanged();
        }
    }
} 
