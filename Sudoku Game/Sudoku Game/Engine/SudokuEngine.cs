using System;
using System.Collections.Generic;
using System.Diagnostics.PerformanceData;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WorkingSudoku.Engine
{
    public enum SudokuStyle { Classic, Jigsaw }
    public enum Difficulty { Easy, Medium, Hard }

    public sealed class PuzzleResult
    {
        public required SudokuBoard Puzzle { get; init; }
        public required SudokuBoard Solution { get; init; }
    }
    public static class PuzzleCarver
    {
        public static SudokuBoard Carve(SudokuBoard solved, Difficulty diff, Random? rng = null)
        {
            rng ??= new Random();
            int n = solved.Size;
            int cells = n * n;

            // Target clues scaled by size (rougly matches 9x9 ranges you wanted):
            int targetClues = diff switch
            {
                Difficulty.Easy => (int)Math.Round(cells * 0.52), // ~42/81
                Difficulty.Medium => (int)Math.Round(cells * .43), // ~35/81
                Difficulty.Hard => (int)Math.Round(cells * 0.34), // ~28/81
                _ => (int)Math.Round(cells * 0.45),
            };

            // Work on a plain int [,] so we can rebuild masks for solver snapshots
            var puzzle = (int[,])solved.Cells.Clone();
            int clues = cells;

            var order = AllCells(n);
            Shuffle(order, rng);

            foreach(var (r,c) in order)
            {
                if (clues <= targetClues) break;
                if (puzzle[r, c] == 0) continue;

                int keep = puzzle[r, c];
                puzzle[r, c] = 0; clues--;

                // Uniqueness check (cap at 2)
                var test = SudokuBoard.FromCells(solved.RegionId, puzzle);
                int solCount = SudokuSolver.CountSolutions(test, limit: 2);

                if (solCount != 1)
                {
                    // put it back
                    puzzle[r, c] = keep; 
                    clues++;
                }
            }
            // return as a sudokuboard with masks set
            return SudokuBoard.FromCells(solved.RegionId, puzzle);
        }
        private static List<(int r, int c)> AllCells(int n)
        {
            var list = new List<(int, int)>(n * n);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    list.Add((r, c));
            return list;
        }
        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

    }
    public static class PuzzleService
    {
        public static PuzzleResult CreatePuzzle(int size, SudokuStyle style, Difficulty difficulty, int? seed = null)
        {
            int baseAttempt = 0;
            int maxAttempts = 100; // Safety cap to prevent infinite loops (should never reach this)

            while (true)
            {
                baseAttempt++;
                
                // Use provided seed for first attempt, then generate new seeds for retries
                int currentSeed = seed.HasValue && baseAttempt == 1 
                    ? seed.Value 
                    : Environment.TickCount + baseAttempt * 12345;

                try
                {
                    var rng = new Random(currentSeed);

                    int[,] regionId = style switch
                    {
                        SudokuStyle.Classic => RegionMaps.ClassicAuto(size),
                        SudokuStyle.Jigsaw => RegionMaps.RandomJigsaw(
                                                   size,
                                                   rng: new Random(rng.Next()),
                                                   successfulSwapsTarget: Math.Clamp(size * size * 4, 300, 900) // e.g., 9×9 -> ~324
                                               ),
                        _ => throw new ArgumentOutOfRangeException(nameof(style))
                    };

                    var gen = new SudokuGenerator(rng.Next());                // see node limit in part C
                    var solved = gen.GenerateSolved(regionId);
                    var puzzle = PuzzleCarver.Carve(solved, difficulty, new Random(rng.Next()));
                    
                    // Success! Log the attempt count for debugging
                    if (baseAttempt > 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PuzzleGen] Succeeded on attempt #{baseAttempt} for {size}×{size} {style} {difficulty}");
                    }
                    
                    return new PuzzleResult { Puzzle = puzzle, Solution = solved };
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Failed to generate"))
                {
                    // Generation failed - try again with fresh seed
                    System.Diagnostics.Debug.WriteLine($"[PuzzleGen] Attempt #{baseAttempt} failed for {size}×{size} {style} {difficulty}, retrying...");
                    
                    // Safety check - if we've tried way too many times, something is fundamentally wrong
                    if (baseAttempt >= maxAttempts)
                    {
                        throw new InvalidOperationException(
                            $"Failed to generate puzzle after {maxAttempts} attempts. " +
                            $"This may indicate an issue with the {size}×{size} {style} configuration.",
                            ex
                        );
                    }
                    
                    // Continue to next attempt
                    continue;
                }
            }
        }

    }
    public static class SudokuSolver
    {
        // returns 0, 1 or >=2 (capped at 'limit', default 2)
        public static int CountSolutions(SudokuBoard board, int limit = 2)
        {
            int count = 0;
            SolveCount(board, ref count, limit);
            return count;
        }

        private static bool SolveCount(SudokuBoard board, ref int count, int limit)
        {
            int n = board.Size;
            // MRV: pick empty with fewest candidates
            int bestR = -1, bestC = -1, bestCnt = int.MaxValue, bestMask = 0;
            for (int r = 0; r < n; r ++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (!board.IsEmpty(r, c)) continue;
                    int mask = board.CandidatesMask(r, c);
                    int cnt = System.Numerics.BitOperations.PopCount((uint)mask);
                    if (cnt == 0) return false; // dead end
                    if (cnt < bestCnt)
                    {
                        bestCnt = cnt; bestMask = mask; bestR = r; bestC = c;
                        if (bestCnt == 1) goto HaveCell;
                    }
                }
            }
        HaveCell:
            if (bestR == -1)
            {
                count++;
                return count >= limit; // early exit as soon as we hit the cap
            }
            // Try candidates
            var order = CollectDigitsFromMask(bestMask);
            ThreadLocalShuffle(order);

            foreach (int v in order)
            {
                board.Place(bestR, bestC, v);
                if (SolveCount(board, ref count, limit)) { board.Unplace(bestR, bestC); return true; } // early stop
                board.Unplace(bestR, bestC);
            }
            return false;

            static List<int> CollectDigitsFromMask(int mask)
            {
                var list = new List<int>(32);
                for (int d = 1; d <= 32; d++)
                    if ((mask & (1 << (d - 1))) != 0) list.Add(d);
                return list;
            }

            static void ThreadLocalShuffle<T>(IList<T> list)
            {
                var rng = _rnd.Value!;
                for (int i = list.Count -1; i>0; i--)
                {
                    int j = rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);    
                }
            }
        }
        private static readonly ThreadLocal<Random> _rnd = new(() => new Random());
    }
    public sealed class SudokuBoard
    {
        public int Size { get; } 
        public int[,] Cells { get; }

        // Region assignment: 0..Size-1; each region must have exactly Size cells
        public int[,] RegionId { get; }

        // Bit i (0-based) set means digit (i+1) is already used in that row/col/box
        private readonly uint[] _rowMask;
        private readonly uint[] _colMask;
        private readonly uint[] _regionMask;

        private readonly uint _ALL; // low Size bits = 1

        public static SudokuBoard FromCells(int[,] regionId, int[,] cells)
        {
            if (regionId.GetLength(0) != cells.GetLength(0) ||
                regionId.GetLength(1) != cells.GetLength(1))
                throw new ArgumentException("regionId and cells must be same size.");
            var b = new SudokuBoard(regionId);
            int n = b.Size;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    int v = cells[r, c];
                    if (v < 0 || v > n) throw new ArgumentException("cell out of range.");
                    if (v != 0) b.Place(r, c, v);    
                }
            return b;
        }
        public SudokuBoard(int[,] regionId)
        {
            if (regionId is null) throw new ArgumentNullException(nameof(regionId));
            int n = regionId.GetLength(0);
            if (n != regionId.GetLength(1)) throw new ArgumentException("regionId must be NxN.");
            Size = n;
            RegionId = regionId;
            Cells = new int[n, n];

            _rowMask = new uint[n];
            _colMask = new uint[n];
            _regionMask = new uint[n]; 
            _ALL = Size == 32 ? 0xFFFF_FFFFu : ((1u << Size) - 1u); // supports up to 32x32
        }

        public bool IsEmpty(int r, int c) => Cells[r, c] == 0;

        public int CandidatesMask(int r, int c)
        {
            if (!IsEmpty(r, c))
                return 0;
            uint used = _rowMask[r] | _colMask[c] | _regionMask[RegionId[r, c]];
            return (int)((~used) & _ALL);
        }

        public void Place(int r, int c, int d) // d in 1..9
        {
            int bit = 1 << (d - 1);
            Cells[r, c] = d;
            _rowMask[r] |= (uint)bit;
            _colMask[c] |= (uint)bit;
            _regionMask[RegionId[r, c]] |= (uint)bit;
        }

        public void Unplace(int r, int c)
        {
            int d = Cells[r, c];
            if (d == 0)
                return;
            int bit = 1 << (d - 1);
            _rowMask[r] &= ~(uint)bit;
            _colMask[c] &= ~(uint)bit;
            _regionMask[RegionId[r, c]] &= ~(uint)bit;
            Cells[r, c] = 0;
        }

        public IEnumerable<int> DigitsFromMask(int mask)
        {
            for (int d = 1; d <= Size; d++)
            {
                if ((mask & (1 << (d - 1))) != 0) yield return d;
            }
        }       
    }
    public sealed class SudokuGenerator
    {
        private readonly Random _rnd;
        private readonly int _nodeLimit;

        public SudokuGenerator(int? seed = null, int nodeLimit = 2_000_000)
        {
            _rnd = seed.HasValue ? new Random(seed.Value) : new Random();
            _nodeLimit = nodeLimit;
        }

        public SudokuBoard GenerateSolved(int[,] regionId)
        {
            RegionMaps.ValidateRegionMap(regionId);

            // Try a few times; some jigsaw maps/seed orders are unlucky
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var board = new SudokuBoard(regionId);
                int nodes = 0;
                if (BacktrackFill(board, ref nodes))
                    return board;
            }
            throw new InvalidOperationException("Failed to generate a solved grid after several attempts.");
        }

        private bool BacktrackFill(SudokuBoard board, ref int nodes)
        {
            if (++nodes > _nodeLimit) return false;

            int n = board.Size;
            int bestCount = int.MaxValue;
            var ties = new List<(int r, int c, int mask)>(8);

            // MRV: collect all cells with the minimum candidate count
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    if (!board.IsEmpty(r, c)) continue;
                    int mask = board.CandidatesMask(r, c);
                    int cnt = BitOperations.PopCount((uint)mask);
                    if (cnt == 0) return false;

                    if (cnt < bestCount)
                    {
                        bestCount = cnt;
                        ties.Clear();
                        ties.Add((r, c, mask));
                        if (bestCount == 1) goto DoneScan;
                    }
                    else if (cnt == bestCount)
                    {
                        ties.Add((r, c, mask));
                    }
                }
            }
        DoneScan:
            if (ties.Count == 0) return true; // no empties: solved

            // Randomly choose among equal-best cells
            var pick = ties[_rnd.Next(ties.Count)];

            var candidates = Shuffle(board.DigitsFromMask(pick.mask));
            foreach (var d in candidates)
            {
                board.Place(pick.r, pick.c, d);
                if (BacktrackFill(board, ref nodes)) return true;
                board.Unplace(pick.r, pick.c);
            }
            return false;
        }

        private List<int> Shuffle(IEnumerable<int> items)
        {
            var list = new List<int>(items);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }
    }

    public static class SudokuValidator
    {
        // Sanity check: row/col/box each contain digits 1..9 exactly once
        public static bool IsSolvedValid(SudokuBoard board)
        {
            uint full = board.Size == 32 ? 0xFFFF_FFFFu : ((1u << board.Size) - 1u);

            for (int i = 0; i < board.Size; i++)
            {
                uint row = 0, col = 0;
                for (int j = 0; j < board.Size; j++)
                {
                    int dr = board.Cells[i, j]; if (dr < 1 || dr > board.Size) return false;
                    int dc = board.Cells[j, i]; if (dc < 1 || dc > board.Size) return false;
                    row |= 1u << (dr - 1);
                    col |= 1u << (dc - 1);
                }
                if (row != full || col != full) return false;
            }

            var seen = new uint[board.Size];
            for (int r = 0; r < board.Size; r++)
                for (int c = 0; c < board.Size; c++)
                    seen[board.RegionId[r,c]] |= 1u << (board.Cells[r, c] - 1);

            for (int k = 0; k < board.Size; k++)
                if (seen[k] != full) return false;
            return true;
        }
    }
    public static class  RegionMaps
    {
        //Classic rectangular boxes: N = boxRows * boxCols eg 9 = 3*3, 6 = 2*3, 16 = 4*4)
        public static int[,] ClassicRect(int n, int boxRows, int boxCols)
        {
            if (boxRows * boxCols != n ) throw new ArgumentException("bowRows * boxCols must equal n.");
            var id = new int[n, n];
            int boxesPerRow = n / boxCols;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    id[r, c] = (r / boxRows) * boxesPerRow + (c / boxCols);
            return id;
        }

        // Choose near-square factors rxc with r,c > = 2 and r*c == n
        public static int[,] ClassicAuto(int n)
        {
            int bestR = 0, bestC = 0, bestDiff = int.MaxValue;
            for (int r = 2; r <= n; r++)
            {
                if (n % r != 0) continue;
                int c = n / r;
                if (c < 2) continue;
                int diff = Math.Abs(r - c);
                if (diff < bestDiff) { bestDiff = diff; bestR = r; bestC = c; }
            }
            if (bestR == 0) throw new ArgumentException($"Classic' regions required factorization; {n} has no rxc with r, c>=2. Use Jigsaw for this size.");
            return ClassicRect(n, bestR, bestC);
        }

        // Quick helper: standard 9x9 Sudoku
        public static int[,] Classic9x9() => ClassicRect(9, 3, 3);

        public static void ValidateRegionMap(int[,] regionId)
        {
            int n = regionId.GetLength(0);
            if (n != regionId.GetLength(1)) throw new ArgumentException("regionId must be NxN.");
            var counts = new int[n];
            var seen = new bool[n, n];

            // Count cells per region, check ids in range
            for (int r = 0; r < n; r++)
               for (int c = 0; c < n; c++)
                {
                   int k = regionId[r, c];
                   if (k < 0 || k >= n) throw new ArgumentException("regionId values must be in 0..N-1.");
                   counts[k]++;
                }
            if (counts.Any(x => x != n)) throw new ArgumentException("Each region must have exactly N cells.");

            // Connectivity check (4-neighbor) for each region
            var dirs = new (int dr, int dc)[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
            var visited = new bool[n,n];

            for (int region = 0; region < n; region++)
            {
                // find one cell of this region
                (int sr, int sc) = (-1, -1);
                for (int r = 0; r < n && sr < 0; r++)
                    for (int c = 0; c < n; c++)
                        if (regionId[r, c] == region) { sr = r; sc = c; break; }

                // BFS
                int reached = 0;
                var q = new Queue<(int r, int c)>();
                var local = new bool[n, n];
                q.Enqueue((sr, sc));
                local[sr, sc] = true;

                while (q.Count > 0)
                {
                    var (r, c) = q.Dequeue();
                    reached++;
                    foreach (var (dr, dc) in dirs)
                    {
                        int nr = r + dr, nc = c + dc;
                        if (nr < 0 || nr >= n || nc < 0 || nc >= n) continue;
                        if (local[nr, nc]) continue;
                        if (regionId[nr, nc] != region) continue;
                        local[nr, nc] = true;
                        q.Enqueue((nr, nc));                       
                    }
                }
                if (reached != n) throw new ArgumentException($"region {region} is not 4-connected.");
            }
        }
        

        // ====================== Jigsaw by Notches + Safe Swaps (collision-free) ======================

        public static int[,] RandomJigsaw(int n, Random? rng = null, int successfulSwapsTarget = 1600)
        {
            rng ??= new Random();

            // Start from a simple tiling we generate here (no dependency on ClassicRect)
            int[,] map;
            if (n == 9)
            {
                // classic 3×3 boxes as a starting point
                map = JG_StartRectTiling(9, 3, 3);
            }
            else if (JG_TryFactorNearSquare(n, out int r, out int c))
            {
                map = JG_StartRectTiling(n, r, c);
            }
            else
            {
                // Prime N: horizontal strips
                map = JG_StartRectTiling(n, 1, n);
            }

            // Warp: make notches (2×2 diagonal flips), then safe adjacent swaps
            map = JG_WarpBySafeBoundarySwaps(map, rng, successfulSwapsTarget);

            // Validate with your existing ValidateRegionMap (counts + 4-connectedness)
            ValidateRegionMap(map);

            // Just in case: if still classic for 9×9 (shouldn’t happen), warp again
            if (n == 9 && JG_LooksClassic3x3(map))
                map = JG_WarpBySafeBoundarySwaps(map, rng, successfulSwapsTarget);

            return map;
        }

        // Create a rectangular tiling: rows × cols blocks covering the n×n grid
        private static int[,] JG_StartRectTiling(int n, int rows, int cols)
        {
            var id = new int[n, n];
            int rSize = n / rows;
            int cSize = n / cols;
            int k = 0;
            for (int br = 0; br < rows; br++)
            {
                for (int bc = 0; bc < cols; bc++)
                {
                    for (int r = br * rSize; r < (br + 1) * rSize; r++)
                        for (int c = bc * cSize; c < (bc + 1) * cSize; c++)
                            id[r, c] = k;
                    k++;
                }
            }
            return id;
        }

        // Try to factor n into near-square rows×cols, each ≥ 2
        private static bool JG_TryFactorNearSquare(int n, out int rows, out int cols)
        {
            rows = cols = 0;
            int bestDiff = int.MaxValue;
            for (int r = 2; r <= n; r++)
            {
                if (n % r != 0) continue;
                int c = n / r;
                int diff = Math.Abs(r - c);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    rows = r;
                    cols = c;
                }
            }
            return rows >= 2 && cols >= 2;
        }

        // Phase 1: 2×2 diagonal flips to create notches,
        // Phase 2: safe adjacent swaps along region boundaries.
        private static int[,] JG_WarpBySafeBoundarySwaps(int[,] region, Random rng, int targetSwaps)
        {
            int n = region.GetLength(0);
            bool useMini = (n == 9);
            int mini = 3, blocksPerRow = useMini ? n / mini : 0;
            int cap = useMini ? 9 : int.MaxValue; // permissive: allow up to 9 in a 3×3 for any single region

            // per-3×3 counts (for 9×9 aesthetics only)
            int[,] blockCount = useMini ? new int[blocksPerRow * blocksPerRow, n] : new int[1, 1];
            if (useMini)
            {
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                        blockCount[(r / mini) * blocksPerRow + (c / mini), region[r, c]]++;
            }

            int swaps = 0;

            // -------- Phase 1: 2×2 diagonal flips to break flat borders --------
            int notchTarget = Math.Max(200, targetSwaps / 3);
            for (int pass = 0; swaps < notchTarget && pass < 8; pass++)
            {
                var diags = JG_GetDiagonalPairs(region);
                JG_ShuffleInPlace(diags, rng);

                int made = 0;
                foreach (var (a, b) in diags)
                {
                    int aId = region[a.r, a.c];
                    int bId = region[b.r, b.c];
                    if (aId == bId) continue;

                    if (useMini)
                    {
                        int mba = (a.r / mini) * blocksPerRow + (a.c / mini);
                        int mbb = (b.r / mini) * blocksPerRow + (b.c / mini);
                        if (blockCount[mba, bId] + 1 > cap) continue;
                        if (blockCount[mbb, aId] + 1 > cap) continue;
                    }

                    if (!JG_SwapPreservesConnectivity(region, a, b, aId, bId)) continue;

                    // commit
                    if (useMini)
                    {
                        int mba = (a.r / mini) * blocksPerRow + (a.c / mini);
                        int mbb = (b.r / mini) * blocksPerRow + (b.c / mini);
                        blockCount[mba, aId]--; blockCount[mba, bId]++;
                        blockCount[mbb, bId]--; blockCount[mbb, aId]++;
                    }
                    region[a.r, a.c] = bId;
                    region[b.r, b.c] = aId;

                    swaps++;
                    made++;
                    if (swaps >= notchTarget) break;
                }
                // no need to relax cap; cap is permissive already
            }

            // -------- Phase 2: adjacent boundary swaps along current borders --------
            for (int pass = 0; swaps < targetSwaps && pass < 16; pass++)
            {
                var edges = JG_GetBoundaryPairs(region);
                JG_ShuffleInPlace(edges, rng);

                int made = 0;
                foreach (var (a, b) in edges)
                {
                    int aId = region[a.r, a.c];
                    int bId = region[b.r, b.c];
                    if (aId == bId) continue;

                    if (useMini)
                    {
                        int mba = (a.r / mini) * blocksPerRow + (a.c / mini);
                        int mbb = (b.r / mini) * blocksPerRow + (b.c / mini);
                        if (blockCount[mba, bId] + 1 > cap) continue;
                        if (blockCount[mbb, aId] + 1 > cap) continue;
                    }

                    // quick articulation pre-checks
                    if (!JG_HasSameRegionNeighbor(region, a, aId, b)) continue;
                    if (!JG_HasSameRegionNeighbor(region, b, bId, a)) continue;

                    if (!JG_SwapPreservesConnectivity(region, a, b, aId, bId)) continue;

                    // commit
                    if (useMini)
                    {
                        int mba = (a.r / mini) * blocksPerRow + (a.c / mini);
                        int mbb = (b.r / mini) * blocksPerRow + (b.c / mini);
                        blockCount[mba, aId]--; blockCount[mba, bId]++;
                        blockCount[mbb, bId]--; blockCount[mbb, aId]++;
                    }
                    region[a.r, a.c] = bId;
                    region[b.r, b.c] = aId;

                    swaps++;
                    made++;
                    if (swaps >= targetSwaps) break;
                }
            }

            return region;
        }

        // Adjacent (4-neighbor) pairs where labels differ
        private static List<((int r, int c) a, (int r, int c) b)> JG_GetBoundaryPairs(int[,] region)
        {
            int n = region.GetLength(0);
            var list = new List<((int r, int c), (int r, int c))>();

            // horizontal boundaries
            for (int r = 0; r < n; r++)
                for (int c = 0; c + 1 < n; c++)
                    if (region[r, c] != region[r, c + 1])
                        list.Add(((r, c), (r, c + 1)));

            // vertical boundaries
            for (int r = 0; r + 1 < n; r++)
                for (int c = 0; c < n; c++)
                    if (region[r, c] != region[r + 1, c])
                        list.Add(((r, c), (r + 1, c)));

            return list;
        }

        // 2×2 diagonals (tl<->br and tr<->bl) when labels differ
        private static List<((int r, int c) a, (int r, int c) b)> JG_GetDiagonalPairs(int[,] region)
        {
            int n = region.GetLength(0);
            var list = new List<((int r, int c), (int r, int c))>();
            for (int r = 0; r + 1 < n; r++)
            {
                for (int c = 0; c + 1 < n; c++)
                {
                    int tl = region[r, c], tr = region[r, c + 1];
                    int bl = region[r + 1, c], br = region[r + 1, c + 1];

                    if (tl != br) list.Add(((r, c), (r + 1, c + 1)));
                    if (tr != bl) list.Add(((r, c + 1), (r + 1, c)));
                }
            }
            return list;
        }

        // Swap a<->b if BOTH regions remain 4-connected
        private static bool JG_SwapPreservesConnectivity(int[,] id, (int r, int c) a, (int r, int c) b, int ra, int rb)
        {
            int n = id.GetLength(0);

            int oldA = id[a.r, a.c], oldB = id[b.r, b.c];
            id[a.r, a.c] = rb;
            id[b.r, b.c] = ra;

            bool okA = JG_IsRegionConnected(id, n, ra, b);
            bool okB = JG_IsRegionConnected(id, n, rb, a);

            id[a.r, a.c] = oldA;
            id[b.r, b.c] = oldB;

            return okA && okB;
        }

        // BFS connectivity for one region id, starting from 'hint'
        private static bool JG_IsRegionConnected(int[,] id, int n, int reg, (int r, int c) hint)
        {
            var q = new Queue<(int r, int c)>();
            var seen = new bool[n, n];

            (int r, int c) start = (-1, -1);
            if (id[hint.r, hint.c] == reg) start = hint;
            else
            {
                for (int r = 0; r < n && start.r < 0; r++)
                    for (int c = 0; c < n; c++)
                        if (id[r, c] == reg) { start = (r, c); break; }
            }
            if (start.r < 0) return false;

            q.Enqueue(start);
            seen[start.r, start.c] = true;
            int reached = 0;

            while (q.Count > 0)
            {
                var (r, c) = q.Dequeue();
                reached++;

                if (r > 0 && !seen[r - 1, c] && id[r - 1, c] == reg) { seen[r - 1, c] = true; q.Enqueue((r - 1, c)); }
                if (r + 1 < n && !seen[r + 1, c] && id[r + 1, c] == reg) { seen[r + 1, c] = true; q.Enqueue((r + 1, c)); }
                if (c > 0 && !seen[r, c - 1] && id[r, c - 1] == reg) { seen[r, c - 1] = true; q.Enqueue((r, c - 1)); }
                if (c + 1 < n && !seen[r, c + 1] && id[r, c + 1] == reg) { seen[r, c + 1] = true; q.Enqueue((r, c + 1)); }
            }

            int total = 0;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (id[r, c] == reg) total++;

            return reached == total;
        }

        // Avoid obvious articulation points: cell must have ≥1 same-region neighbor besides the pair partner
        private static bool JG_HasSameRegionNeighbor(int[,] id, (int r, int c) cell, int reg, (int r, int c) exclude)
        {
            int n = id.GetLength(0);
            int r = cell.r, c = cell.c;
            int cnt = 0;
            if (r > 0 && !(r - 1 == exclude.r && c == exclude.c) && id[r - 1, c] == reg) cnt++;
            if (r + 1 < n && !(r + 1 == exclude.r && c == exclude.c) && id[r + 1, c] == reg) cnt++;
            if (c > 0 && !(r == exclude.r && c - 1 == exclude.c) && id[r, c - 1] == reg) cnt++;
            if (c + 1 < n && !(r == exclude.r && c + 1 == exclude.c) && id[r, c + 1] == reg) cnt++;
            return cnt >= 1;
        }

        // 9×9 classic detector (only used as a final sanity check)
        private static bool JG_LooksClassic3x3(int[,] region)
        {
            int n = region.GetLength(0);
            if (n != 9) return false;

            for (int br = 0; br < 3; br++)
                for (int bc = 0; bc < 3; bc++)
                {
                    int id0 = region[br * 3, bc * 3];
                    for (int r = br * 3; r < br * 3 + 3; r++)
                        for (int c = bc * 3; c < bc * 3 + 3; c++)
                            if (region[r, c] != id0) goto NotClassic;
                }
            return true;
        NotClassic:
            return false;
        }

        // Fisher–Yates shuffle
        private static void JG_ShuffleInPlace<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }





    }
}
