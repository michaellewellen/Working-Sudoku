using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Windows;
using WorkingSudoku.Engine;

namespace Sudoku_Game
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {   
        static string RegionAscii(int[,] region)
        {
            int n = region.GetLength(0);
            var sb = new StringBuilder();
            for (int r = 0; r < n; r ++)
            {
                for (int c = 0; c < n; c++)
                {
                    int k = region[r, c];
                    char ch = (k >=0 && k < 26) ? (char)('A' + k) : '#';
                    sb.Append(ch).Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string BoardAscii(int[,] cells)
        {
            int n = cells.GetLength(0);
            var sb = new StringBuilder();
            for(int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                   sb.Append(cells[r,c]).Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        
    }

}
