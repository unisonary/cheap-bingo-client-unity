using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using WebSocketSharp;
using System.Collections.Concurrent;
using System;

public class GameManager : MonoBehaviour
{
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

    private void Start()
    {
        checkWinner = new CheckWinner();

        if (ws == null)
        {
            // ws = new WebSocket("ws://bingo-omp.herokuapp.com/ws");
            ws = new WebSocket("ws://localhost:9000/ws");
            ws.Connect();
            Debug.Log("Connection tried");
        }

        ws.OnMessage += SetMessage;
        ws.OnClose += (_, e) =>
        {
            if (roomReady)
            {
                RoomResponse data = default;
                data.channel = "exit-room";
                data.roomCode = roomCode;
                data.isCreator = !amICreator;
                ws.Send(JsonUtility.ToJson(data));
                SceneManager.LoadScene(0);
            }
        };

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
            ws.Connect();
            if (!ws.IsAlive)
            {
                alert.text = "Please check your network connection";
                alert.alpha = 1f;
                return;
            }
        }
        creatorName = nameInput.text.Trim();
        if (creatorName.IsNullOrEmpty())
        {
            //UnityEditor.EditorUtility.DisplayDialog("Error!", "Please enter your name", "Ok");
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
        if (!ws.IsAlive)
        {
            ws.Connect();
            if (!ws.IsAlive)
            {
                alert.text = "Please check your network connection";
                alert.alpha = 1f;
                return;
            }
        }
        roomCode = roomCodeInput.text.Trim();
        joinerName = nameInput.text.Trim();
        if (roomCode.IsNullOrEmpty() || joinerName.IsNullOrEmpty())
        {
            //UnityEditor.EditorUtility.DisplayDialog("Error!", "Please enter all required details", "Ok");
            alert.text = "Please enter all required details";
            alert.alpha = 1f;
        }
        else
        {
            amICreator = false;
            RoomResponse data = default;
            data.channel = "join-room";
            data.res = joinerName;
            data.roomCode = roomCode;
            data.appVersion = Application.version;
            ws.Send(JsonUtility.ToJson(data));
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
                ws.Send(JsonUtility.ToJson(data));
                SceneManager.LoadScene(0);
            }
            else
                Application.Quit();
        }

        while (_actions.Count > 0)
        {
            if (_actions.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }
}
