using System;
using System.Linq;
using System.Windows;

namespace Sudoku_Game
{
    public partial class StatsWindow : Window
    {
        private DialogOverlay _dialog;

        public StatsWindow()
        {
            InitializeComponent();
            _dialog = new DialogOverlay(DialogOverlayGrid, DialogTitle, DialogMessage, DialogButtons);
            LoadStats();
        }

        private void LoadStats()
        {
            var stats = StatsService.GetAllStats();

            if (stats.Count == 0)
            {
                StatsListControl.Visibility = Visibility.Collapsed;
                EmptyStateText.Visibility = Visibility.Visible;
                LastUpdatedText.Text = "No games played yet";
            }
            else
            {
                StatsListControl.ItemsSource = stats;
                StatsListControl.Visibility = Visibility.Visible;
                EmptyStateText.Visibility = Visibility.Collapsed;

                // Show last updated time
                var lastGame = stats.Max(s => s.TotalGames);
                LastUpdatedText.Text = $"Total games played: {stats.Sum(s => s.TotalGames)}";
            }
        }

        private void ClearStats_Click(object sender, RoutedEventArgs e)
        {
            _dialog.ShowConfirmation(
                "Clear Stats",
                "Are you sure you want to clear all your stats? This cannot be undone.",
                onYes: () =>
                {
                    StatsService.ClearAllStats();
                    LoadStats();
                    _dialog.ShowMessage("Stats Cleared", "All stats have been cleared!");
                }
            );
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
