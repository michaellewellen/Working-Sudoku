using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using WorkingSudoku.Engine;
using System.Diagnostics;
using System.Windows.Shapes;
using System.Windows.Media.Animation;

namespace Sudoku_Game
{
    public partial class GameWindow : Window
    {
        private readonly int _size;
        private readonly string _styleStr;
        private readonly string _difficultyStr;

        private PuzzleResult _result = null!;
        private bool[,] _isClue = null!;      // original givens
        private bool[,] _mistake = null!;     // cells flagged as wrong (via check or final check)
        private (int r, int c)? _sel;         // selected board cell
        private int? _selectedNumber;         // number chosen on pad (null=none, 0=clear)
        private int _checksRemaining = 0;     // Medium only
        private bool _gameCompleted = false;  // track if game ended (win/loss/give-up)
        private DialogOverlay _dialog = null!; // in-game dialog system

        public GameWindow(int size, string style, string difficulty)
        {
            InitializeComponent();
            _size = size;
            _styleStr = style;
            _difficultyStr = difficulty;

            // Initialize dialog overlay system
            _dialog = new DialogOverlay(DialogOverlayGrid, DialogTitle, DialogMessage, DialogButtons);

            HdrInfo.Text = $"{_size}×{_size} • {style} • {difficulty} — generating…";
            _ = BuildAsync();

            // optional keyboard shortcuts
            PuzzleGrid.Focusable = true;
            PuzzleGrid.PreviewKeyDown += PuzzleGrid_PreviewKeyDown;
        }
        // ==== Region colors (paste inside GameWindow class) ====

        private static Brush[] BuildRegionBrushes(int regions, bool background)
        {
            var arr = new Brush[regions];
            for (int k = 0; k < regions; k++)
            {
                double hue = (360.0 * k) / regions;     // evenly spaced hues
                double s = background ? 0.45 : 0.65;    // muted background
                double l = background ? 0.22 : 0.35;

                var c = Hsl(hue, s, l);
                var br = new SolidColorBrush(c) { Opacity = 0.95 };
                if (br.CanFreeze) br.Freeze();
                arr[k] = br;
            }
            return arr;
        }

        // HSL -> RGB helper
        private static Color Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r1 = 0, g1 = 0, b1 = 0;

            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            byte R = (byte)Math.Round((r1 + m) * 255);
            byte G = (byte)Math.Round((g1 + m) * 255);
            byte B = (byte)Math.Round((b1 + m) * 255);
            return Color.FromRgb(R, G, B);
        }

        private async Task BuildAsync()
        {
            try
            {
                var styleEnum = (_styleStr == "Jigsaw") ? SudokuStyle.Jigsaw : SudokuStyle.Classic;
                var diffEnum = _difficultyStr == "Hard" ? Difficulty.Hard :
                                _difficultyStr == "Easy" ? Difficulty.Easy : Difficulty.Medium;

                int seed = Environment.TickCount;

                // Update header to show we're working
                HdrInfo.Text = $"{_size}×{_size} • {_styleStr} • {_difficultyStr} — generating…";

                // Heavy work off-UI
                _result = await Task.Run(() => 
                {
                    // The PuzzleService will now retry automatically if generation fails
                    return PuzzleService.CreatePuzzle(_size, styleEnum, diffEnum, seed);
                });

                // Init maps
                _isClue = new bool[_size, _size];
                _mistake = new bool[_size, _size];
                for (int r = 0; r < _size; r++)
                    for (int c = 0; c < _size; c++)
                        _isClue[r, c] = _result.Puzzle.Cells[r, c] != 0;

                _checksRemaining = (_difficultyStr == "Medium") ? 3 : 0;
                UpdateCheckUI();

                // UI thread: render
                HdrInfo.Text = $"{_size}×{_size} • {_styleStr} • {_difficultyStr}";
                RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);
                BuildNumberPicker();

                Debug.WriteLine($"[Render] Size={_result.Puzzle.Size}, Children={PuzzleGrid.Children.Count}");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to generate"))
            {
                // This should be extremely rare now with retry logic
                _dialog.ShowMessage("Generation Failed", 
                    $"Unable to generate this puzzle configuration after many attempts.\n\nTry a different size or style.",
                    () => Close());
            }
            catch (Exception ex)
            {
                _dialog.ShowMessage("Error", 
                    $"An error occurred while generating the puzzle:\n\n{ex.Message}",
                    () => Close());
            }
        }


        // ================= Number Picker =================

        private void BuildNumberPicker()
        {
            PickerGrid.Children.Clear();

            int n = _size;
            int rows = (int)Math.Ceiling(Math.Sqrt(n));
            int cols = (int)Math.Ceiling((double)n / rows);
            PickerGrid.Rows = rows;
            PickerGrid.Columns = cols;

            for (int v = 1; v <= n; v++)
            {
                int val = v; // capture bug fix
                var b = new Button
                {
                    Content = val.ToString(),
                    Style = (Style)Resources["PadButton"]
                };
                b.Click += (_, __) => SelectNumber(val);
                PickerGrid.Children.Add(b);
            }
        }

        private void SelectNumber(int v)
        {
            _selectedNumber = v;
            SelectedText.Text = v.ToString();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _selectedNumber = 0;
            SelectedText.Text = "—";

            if (_sel is { } s && !_isClue[s.r, s.c])
            {
                _result.Puzzle.Unplace(s.r, s.c);
                _mistake[s.r, s.c] = false;
                RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);
            }
        }

        private void NewPuzzle_Click(object sender, RoutedEventArgs e)
        {
            // Track incomplete game as a loss
            if (!_gameCompleted)
            {
                StatsService.RecordLoss(_size, _styleStr, _difficultyStr);
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void GiveUp_Click(object sender, RoutedEventArgs e)
        {
            if (_gameCompleted) return; // Already ended

            _dialog.ShowConfirmation(
                "Give Up?",
                "Are you sure you want to give up? This will count as a loss and show the solution.",
                onYes: () =>
                {
                    _gameCompleted = true;
                    StatsService.RecordGiveUp(_size, _styleStr, _difficultyStr);

                    // Show the solution
                    for (int r = 0; r < _size; r++)
                        for (int c = 0; c < _size; c++)
                            _result.Puzzle.Cells[r, c] = _result.Solution.Cells[r, c];

                    RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: false);
                    
                    // Show result message
                    _dialog.ShowMessage("Gave Up", "Solution revealed. Better luck next time!");
                }
            );
        }

        // ================= Checks & Endgame =================

        private void Check_Click(object sender, RoutedEventArgs e)
        {
            if (_checksRemaining <= 0) return;

            RunFullCheckAndMark(); // mark _mistake on wrong entries only
            _checksRemaining--;
            UpdateCheckUI();
            RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);
        }

        private void UpdateCheckUI()
        {
            if (FindName("CheckBtn") is Button check)
            {
                if (_difficultyStr == "Medium")
                {
                    check.Visibility = Visibility.Visible;
                    check.IsEnabled = _checksRemaining > 0;
                    check.Content = $"Check ({_checksRemaining} left)";
                }
                else
                {
                    check.Visibility = Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Marks _mistake[r,c] true for every non-clue cell whose value != solution.
        /// Returns true if ANY wrong cell exists.
        /// </summary>
        private bool RunFullCheckAndMark()
        {
            bool anyWrong = false;
            for (int r = 0; r < _size; r++)
                for (int c = 0; c < _size; c++)
                {
                    if (_isClue[r, c]) { _mistake[r, c] = false; continue; }
                    int v = _result.Puzzle.Cells[r, c];
                    if (v == 0) { _mistake[r, c] = false; continue; }
                    bool wrong = (v != _result.Solution.Cells[r, c]);
                    _mistake[r, c] = wrong;
                    anyWrong |= wrong;
                }
            return anyWrong;
        }

        private void TryFinalCheckAndCelebrate()
        {
            if (_gameCompleted) return; // Already ended

            // Only when every cell is filled
            for (int r = 0; r < _size; r++)
                for (int c = 0; c < _size; c++)
                    if (_result.Puzzle.Cells[r, c] == 0)
                        return;

            // All modes: full check on last placement
            bool anyWrong = RunFullCheckAndMark();
            RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);

            _gameCompleted = true;

            if (!anyWrong)
            {
                StatsService.RecordWin(_size, _styleStr, _difficultyStr);
                ShowCelebration();
            }
            else
            {
                StatsService.RecordLoss(_size, _styleStr, _difficultyStr);
                _dialog.ShowMessage("Almost There!", "Not quite—red cells are incorrect. Keep trying!");
            }
        }

        private void ShowCelebration()
        {
            CelebrationOverlay.Visibility = Visibility.Visible;
            StartFireworks();
        }

        private void CelebrationContinue_Click(object sender, RoutedEventArgs e)
        {
            CelebrationOverlay.Visibility = Visibility.Collapsed;
            FireworksCanvas.Children.Clear();
        }

        private void StartFireworks()
        {
            // Clear any existing fireworks
            FireworksCanvas.Children.Clear();

            // Start continuous firework spawning
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            timer.Tick += (s, e) =>
            {
                if (CelebrationOverlay.Visibility != Visibility.Visible)
                {
                    timer.Stop();
                    return;
                }
                CreateFirework();
            };
            timer.Start();

            // Pulse animation for main text
            var pulseAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.15,
                Duration = TimeSpan.FromSeconds(0.8),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            CelebrationText.RenderTransform = new ScaleTransform(1, 1);
            CelebrationText.RenderTransformOrigin = new Point(0.5, 0.5);
            CelebrationText.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
            CelebrationText.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
        }

        private void CreateFirework()
        {
            var random = new Random();
            var width = FireworksCanvas.ActualWidth;
            var height = FireworksCanvas.ActualHeight;

            if (width <= 0 || height <= 0) return;

            // Random starting position
            double startX = random.NextDouble() * width;
            double startY = height;

            // Random end position (upper portion of screen)
            double endY = random.NextDouble() * height * 0.4;

            // Create multiple particles for explosion effect
            int particleCount = random.Next(8, 15);
            for (int i = 0; i < particleCount; i++)
            {
                CreateFireworkParticle(startX, endY, random);
            }
        }

        private void CreateFireworkParticle(double centerX, double centerY, Random random)
        {
            // Random color
            var colors = new[] { 
                Colors.Gold, Colors.Orange, Colors.Red, Colors.DeepPink, 
                Colors.Cyan, Colors.Lime, Colors.Yellow, Colors.White 
            };
            var color = colors[random.Next(colors.Length)];

            // Create particle (small circle)
            var particle = new Ellipse
            {
                Width = random.Next(4, 10),
                Height = random.Next(4, 10),
                Fill = new SolidColorBrush(color),
                Opacity = 1.0
            };

            // Random direction for explosion
            double angle = random.NextDouble() * 2 * Math.PI;
            double distance = random.Next(50, 150);
            double endX = centerX + Math.Cos(angle) * distance;
            double endY = centerY + Math.Sin(angle) * distance;

            Canvas.SetLeft(particle, centerX);
            Canvas.SetTop(particle, centerY);
            FireworksCanvas.Children.Add(particle);

            // Animate position
            var moveX = new DoubleAnimation
            {
                From = centerX,
                To = endX,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var moveY = new DoubleAnimation
            {
                From = centerY,
                To = endY,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Animate opacity (fade out)
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.8),
                BeginTime = TimeSpan.FromSeconds(0.3)
            };

            // Remove particle after animation
            fadeOut.Completed += (s, e) =>
            {
                FireworksCanvas.Children.Remove(particle);
            };

            particle.BeginAnimation(Canvas.LeftProperty, moveX);
            particle.BeginAnimation(Canvas.TopProperty, moveY);
            particle.BeginAnimation(OpacityProperty, fadeOut);
        }

        // ================= Board Interaction =================

        private void Cell_Click(object? sender, MouseButtonEventArgs e)
        {
            if (sender is not Border b || b.Tag is not ValueTuple<int, int> pos) return;
            var (r, c) = pos;
            _sel = (r, c);

            if (_isClue[r, c]) { RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true); return; }
            if (_selectedNumber is null) { RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true); return; }

            var board = _result.Puzzle;
            board.Unplace(r, c);
            _mistake[r, c] = false; // editing clears prior mistake flag

            if (_selectedNumber > 0)
            {
                board.Place(r, c, _selectedNumber.Value);

                // Easy: immediate feedback (mark red if wrong, else not)
                if (_difficultyStr == "Easy")
                    _mistake[r, c] = (board.Cells[r, c] != _result.Solution.Cells[r, c]);
            }

            RenderBoardWithRegions(PuzzleGrid, board, blankZeros: true);
            TryFinalCheckAndCelebrate();
        }

        private void PuzzleGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // number keys set selection
            int? map = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 1,
                Key.D2 or Key.NumPad2 => 2,
                Key.D3 or Key.NumPad3 => 3,
                Key.D4 or Key.NumPad4 => 4,
                Key.D5 or Key.NumPad5 => 5,
                Key.D6 or Key.NumPad6 => 6,
                Key.D7 or Key.NumPad7 => 7,
                Key.D8 or Key.NumPad8 => 8,
                Key.D9 or Key.NumPad9 => 9,
                _ => null
            };

            if (map is not null && map.Value <= _size)
            {
                SelectNumber(map.Value);
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Back or Key.Delete)
            {
                _selectedNumber = 0;
                SelectedText.Text = "—";
                if (_sel is { } s && !_isClue[s.r, s.c])
                {
                    _result.Puzzle.Unplace(s.r, s.c);
                    _mistake[s.r, s.c] = false;
                    RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);
                }
                e.Handled = true;
            }

            if (e.Key == Key.Enter && _sel is { } cell && _selectedNumber is not null && !_isClue[cell.r, cell.c])
            {
                _result.Puzzle.Unplace(cell.r, cell.c);
                if (_selectedNumber > 0)
                {
                    _result.Puzzle.Place(cell.r, cell.c, _selectedNumber.Value);
                    if (_difficultyStr == "Easy")
                        _mistake[cell.r, cell.c] = (_result.Puzzle.Cells[cell.r, cell.c] != _result.Solution.Cells[cell.r, cell.c]);
                }
                RenderBoardWithRegions(PuzzleGrid, _result.Puzzle, blankZeros: true);
                TryFinalCheckAndCelebrate();
                e.Handled = true;
            }
        }

        // ================= Rendering =================

        private void RenderBoardWithRegions(Grid grid, SudokuBoard board, bool blankZeros)
        {
            int n = board.Size;
            const int cell = 46;
            grid.Width = n * cell;
            grid.Height = n * cell;

            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.Children.Clear();

            for (int i = 0; i < n; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cell) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell) });
            }

            var regionBrushes = BuildRegionBrushes(n, background: true);
            const double THICK = 3.0, THIN = 0.6, NONE = 0.0;

            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int reg = board.RegionId[r, c];

                    bool leftBoundary = (c == 0) || (board.RegionId[r, c - 1] != reg);
                    bool topBoundary = (r == 0) || (board.RegionId[r - 1, c] != reg);
                    bool rightOuter = (c == n - 1);
                    bool bottomOuter = (r == n - 1);

                    double left = leftBoundary ? THICK : THIN;
                    double top = topBoundary ? THICK : THIN;
                    double right = rightOuter ? THICK : NONE;
                    double bottom = bottomOuter ? THICK : NONE;

                    var baseBorder = new Border
                    {
                        Background = regionBrushes[reg],
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(left, top, right, bottom),
                        SnapsToDevicePixels = true,
                        Tag = (r, c)
                    };
                    baseBorder.MouseLeftButtonDown += Cell_Click;
                    Grid.SetRow(baseBorder, r);
                    Grid.SetColumn(baseBorder, c);

                    // ===== FIX: Actually add the cell to the grid! =====
                    grid.Children.Add(baseBorder);

                    // ===== FIX: Create and add the number display =====
                    int val = board.Cells[r, c];
                    if (val != 0 || !blankZeros)
                    {
                        var tb = new TextBlock
                        {
                            Text = val == 0 ? "" : val.ToString(),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontSize = Math.Max(12, 28 - n),  // Scale font with board size
                            FontWeight = _isClue[r, c] ? FontWeights.Bold : FontWeights.Normal,
                            Foreground = _mistake[r, c] ? Brushes.Red : 
                                        (_isClue[r, c] ? Brushes.White : new SolidColorBrush(Color.FromRgb(180, 180, 255))),
                            IsHitTestVisible = false
                        };

                        // Highlight selected cell
                        if (_sel is { } s && s.r == r && s.c == c)
                        {
                            baseBorder.Background = new SolidColorBrush(Color.FromRgb(60, 60, 100));
                        }

                        Grid.SetRow(tb, r);
                        Grid.SetColumn(tb, c);
                        grid.Children.Add(tb);
                    }
                }
        }
    }
}
