using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sudoku_Game
{
    /// <summary>
    /// Result from a dialog overlay
    /// </summary>
    public enum DialogResult
    {
        None,
        OK,
        Yes,
        No,
        Cancel
    }

    /// <summary>
    /// Type of dialog to show
    /// </summary>
    public enum DialogType
    {
        Message,      // Just OK button
        Confirmation, // Yes/No buttons
        YesNoCancel   // Yes/No/Cancel buttons
    }

    /// <summary>
    /// Reusable in-game dialog overlay system (no popups!)
    /// </summary>
    public class DialogOverlay
    {
        private readonly Grid _overlayGrid;
        private readonly TextBlock _titleText;
        private readonly TextBlock _messageText;
        private readonly StackPanel _buttonPanel;
        private DialogResult _result = DialogResult.None;

        public DialogOverlay(Grid overlayGrid, TextBlock titleText, TextBlock messageText, StackPanel buttonPanel)
        {
            _overlayGrid = overlayGrid;
            _titleText = titleText;
            _messageText = messageText;
            _buttonPanel = buttonPanel;
        }

        /// <summary>
        /// Show a simple message dialog with OK button
        /// </summary>
        public void ShowMessage(string title, string message, Action onOK = null)
        {
            Show(title, message, DialogType.Message, onOK);
        }

        /// <summary>
        /// Show a confirmation dialog with Yes/No buttons
        /// </summary>
        public void ShowConfirmation(string title, string message, Action onYes, Action onNo = null)
        {
            Show(title, message, DialogType.Confirmation, () =>
            {
                if (_result == DialogResult.Yes)
                    onYes?.Invoke();
                else if (_result == DialogResult.No)
                    onNo?.Invoke();
            });
        }

        private void Show(string title, string message, DialogType type, Action callback)
        {
            _titleText.Text = title;
            _messageText.Text = message;
            _buttonPanel.Children.Clear();

            // Create buttons based on dialog type
            switch (type)
            {
                case DialogType.Message:
                    AddButton("OK", DialogResult.OK, callback);
                    break;

                case DialogType.Confirmation:
                    AddButton("Yes", DialogResult.Yes, callback);
                    AddButton("No", DialogResult.No, callback);
                    break;

                case DialogType.YesNoCancel:
                    AddButton("Yes", DialogResult.Yes, callback);
                    AddButton("No", DialogResult.No, callback);
                    AddButton("Cancel", DialogResult.Cancel, callback);
                    break;
            }

            _overlayGrid.Visibility = Visibility.Visible;
        }

        private void AddButton(string text, DialogResult result, Action callback)
        {
            var button = new Button
            {
                Content = text,
                Width = 120,
                Height = 45,
                Margin = new Thickness(8, 0, 8, 0),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Color coding for different button types
            button.Background = result switch
            {
                DialogResult.Yes or DialogResult.OK => new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green
                DialogResult.No => new SolidColorBrush(Color.FromRgb(244, 67, 54)), // Red
                DialogResult.Cancel => new SolidColorBrush(Color.FromRgb(158, 158, 158)), // Gray
                _ => new SolidColorBrush(Color.FromRgb(33, 150, 243)) // Blue
            };
            button.Foreground = Brushes.White;

            button.Click += (s, e) =>
            {
                _result = result;
                _overlayGrid.Visibility = Visibility.Collapsed;
                callback?.Invoke();
            };

            _buttonPanel.Children.Add(button);
        }

        /// <summary>
        /// Hide the dialog
        /// </summary>
        public void Hide()
        {
            _overlayGrid.Visibility = Visibility.Collapsed;
        }
    }
}
