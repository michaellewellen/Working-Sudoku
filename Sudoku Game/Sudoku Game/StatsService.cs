using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sudoku_Game
{
    /// <summary>
    /// Represents stats for a specific puzzle configuration
    /// </summary>
    public class PuzzleStats
    {
        public int Size { get; set; }
        public string Style { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int GiveUps { get; set; }
        public int TotalGames => Wins + Losses + GiveUps;
        public double WinRate => TotalGames > 0 ? (double)Wins / TotalGames * 100 : 0;
    }

    /// <summary>
    /// Container for all stats
    /// </summary>
    public class StatsData
    {
        public List<PuzzleStats> Stats { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Service for tracking and persisting Sudoku game statistics
    /// </summary>
    public static class StatsService
    {
        private static readonly string StatsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SudokuGame",
            "stats.json"
        );

        private static StatsData? _cachedStats = null;

        /// <summary>
        /// Load stats from JSON file
        /// </summary>
        private static StatsData LoadStats()
        {
            if (_cachedStats != null)
                return _cachedStats;

            try
            {
                if (File.Exists(StatsFilePath))
                {
                    string json = File.ReadAllText(StatsFilePath);
                    _cachedStats = JsonSerializer.Deserialize<StatsData>(json) ?? new StatsData();
                    return _cachedStats;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Stats] Load error: {ex.Message}");
            }

            _cachedStats = new StatsData();
            return _cachedStats;
        }

        /// <summary>
        /// Save stats to JSON file
        /// </summary>
        private static void SaveStats(StatsData data)
        {
            try
            {
                var dir = Path.GetDirectoryName(StatsFilePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                data.LastUpdated = DateTime.Now;
                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(StatsFilePath, json);
                _cachedStats = data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Stats] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get or create stats entry for a specific puzzle configuration
        /// </summary>
        private static PuzzleStats GetOrCreateStats(int size, string style, string difficulty)
        {
            var data = LoadStats();
            var existing = data.Stats.FirstOrDefault(s =>
                s.Size == size && s.Style == style && s.Difficulty == difficulty
            );

            if (existing != null)
                return existing;

            var newStats = new PuzzleStats
            {
                Size = size,
                Style = style,
                Difficulty = difficulty
            };
            data.Stats.Add(newStats);
            SaveStats(data);
            return newStats;
        }

        /// <summary>
        /// Record a win (puzzle completed correctly)
        /// </summary>
        public static void RecordWin(int size, string style, string difficulty)
        {
            var data = LoadStats();
            var stats = GetOrCreateStats(size, style, difficulty);
            stats.Wins++;
            SaveStats(data);
        }

        /// <summary>
        /// Record a loss (puzzle completed incorrectly)
        /// </summary>
        public static void RecordLoss(int size, string style, string difficulty)
        {
            var data = LoadStats();
            var stats = GetOrCreateStats(size, style, difficulty);
            stats.Losses++;
            SaveStats(data);
        }

        /// <summary>
        /// Record a give-up (player clicked "Give Up")
        /// </summary>
        public static void RecordGiveUp(int size, string style, string difficulty)
        {
            var data = LoadStats();
            var stats = GetOrCreateStats(size, style, difficulty);
            stats.GiveUps++;
            SaveStats(data);
        }

        /// <summary>
        /// Get all recorded stats
        /// </summary>
        public static List<PuzzleStats> GetAllStats()
        {
            return LoadStats().Stats.OrderBy(s => s.Size)
                                     .ThenBy(s => s.Style)
                                     .ThenBy(s => s.Difficulty)
                                     .ToList();
        }

        /// <summary>
        /// Get stats for a specific configuration
        /// </summary>
        public static PuzzleStats? GetStats(int size, string style, string difficulty)
        {
            return LoadStats().Stats.FirstOrDefault(s =>
                s.Size == size && s.Style == style && s.Difficulty == difficulty
            );
        }

        /// <summary>
        /// Clear all stats (useful for testing or user reset)
        /// </summary>
        public static void ClearAllStats()
        {
            var data = new StatsData();
            SaveStats(data);
        }
    }
}
