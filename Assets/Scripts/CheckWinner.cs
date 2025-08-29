public class CheckWinner
{
    public int GetConnections(int[,] mat)
    {
        int connections = 0, n = mat.GetLength(0);
        bool h = true, v = true, ltr = true, rtl = true;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (h && mat[i, j] != 0)
                    h = false;

                if (v && mat[j, i] != 0)
                    v = false;
            }
            if (h) connections++;
            h = true;

            if (v) connections++;
            v = true;


            if (ltr && mat[i, i] != 0)
                ltr = false;
            if (rtl && mat[i, n - i - 1] != 0)
                rtl = false;
        }
        if (ltr) connections++;
        if (rtl) connections++;
        return connections;
    }

    public int[] GetIndex(int move, int[,] mat)
    {
        int[] ndx = new int[] { 0, 0 };
        int n = mat.GetLength(0);
        for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
                if (mat[x, y] == move)
                {
                    ndx[0] = x;
                    ndx[1] = y;
                    break;
                }
        return ndx;
    }
}