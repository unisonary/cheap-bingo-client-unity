using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GridPopulator : MonoBehaviour
{
    public static int[,] arrBoard;

    private void Start()
    {
        SetBingoGrid();
    }

    public static void SetBingoGrid()
    {
        System.Random random = new System.Random();
        arrBoard = new int[,] { { 1, 2, 3, 4, 5 }, { 6, 7, 8, 9, 10 },
        { 11, 12, 13, 14, 15 }, { 16, 17, 18, 19, 20 }, { 21, 22, 23, 24, 25 } };
        Color32 color = new Color32(255, 255, 255, 255);

        int lengthRow = arrBoard.GetLength(1);
        Button btn;

        for (int i = arrBoard.Length - 1; i >= 0; i--)
        {
            int i0 = i / lengthRow;
            int i1 = i % lengthRow;

            int j = random.Next(i + 1);
            int j0 = j / lengthRow;
            int j1 = j % lengthRow;

            int temp = arrBoard[i0, i1];
            arrBoard[i0, i1] = arrBoard[j0, j1];
            arrBoard[j0, j1] = temp;

            btn = GameObject.Find($"{i0}{i1}").GetComponent<Button>();
            btn.GetComponentInChildren<TMP_Text>().text = $"{arrBoard[i0, i1]}";
            btn.image.color = color;
        }
    }
}
