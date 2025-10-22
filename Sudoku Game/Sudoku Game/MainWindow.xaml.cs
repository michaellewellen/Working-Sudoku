using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Sudoku_Game
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
      
        public MainWindow()
        {
            InitializeComponent();
          
        }
        private void GeneratePuzzle_Click(object sender, RoutedEventArgs e)
        {
            // get size
            int size = GetSelectedSize();
            string style = (StyleJigsaw.IsChecked == true) ? "Jigsaw" : "Traditional";
            string difficulty =
                (DiffHard.IsChecked == true) ? "Hard" :
                (DiffMedium.IsChecked == true) ? "Medium" : "Easy";

            var gameWindow = new GameWindow(size, style, difficulty);
            gameWindow.Show();
            Close();
        }

        private void ViewStats_Click(object sender, RoutedEventArgs e)
        {
            var statsWindow = new StatsWindow();
            statsWindow.ShowDialog();
        }

        private int GetSelectedSize()
        {
            if (Size4.IsChecked == true) return 4;
            if (Size6.IsChecked == true) return 6;
            if (Size8.IsChecked == true) return 8;
            if (Size9.IsChecked == true) return 9;
            if (Size12.IsChecked == true) return 12;
            if (Size16.IsChecked == true) return 16;
            return 9;
        }        
    }
}