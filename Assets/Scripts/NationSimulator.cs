using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public class NationSimulator : MonoBehaviour
    {
        [Header("Expansion Settings")]
        [Tooltip("Number of expansion attempts each nation makes per simulation tick")]
        [Range(1, 50)] public int expansionRate = 8;

        [Header("Economy Tuning")]
        [Tooltip("Gold earned per second for each owned pixel")]
        public float incomePerPixel = 0.04f;

        public int TotalLandCells { get; private set; }
        public int ClaimedLandCells { get; private set; }
        public bool IsFullyColonized => ClaimedLandCells >= TotalLandCells && TotalLandCells > 0;
        public float ColonizedPercentage => TotalLandCells > 0 ? (float)ClaimedLandCells / TotalLandCells * 100f : 0f;

        private Cell[] grid;
        private List<Nation> nations;
        private int width;
        private int height;
        private WorldRenderer worldRenderer;

        private static readonly int[] dx = { 0, 0, 1, -1 };
        private static readonly int[] dy = { 1, -1, 0, 0 };

        public void Initialize(Cell[] grid, List<Nation> nations, int width, int height, WorldRenderer renderer)
        {
            this.grid = grid;
            this.nations = nations;
            this.width = width;
            this.height = height;
            this.worldRenderer = renderer;

            TotalLandCells = 0;
            ClaimedLandCells = 0;

            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i].IsLand)
                {
                    TotalLandCells++;
                    if (grid[i].HasOwner)
                    {
                        ClaimedLandCells++;
                    }
                }
            }

            // Initialize nation economic baselines
            for (int n = 0; n < nations.Count; n++)
            {
                nations[n].incomePerSec = nations[n].territorySize * incomePerPixel;
                nations[n].upkeepPerSec = 0f;
            }

            BuildInitialFrontiers();
        }

        private void BuildInitialFrontiers()
        {
            for (int n = 0; n < nations.Count; n++)
                nations[n].frontier.Clear();

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    if (grid[idx].HasOwner && grid[idx].IsLand
                        && HasUnclaimedLandNeighbor(x, y))
                    {
                        int ownerId = grid[idx].nationId;
                        if (ownerId >= 0 && ownerId < nations.Count)
                            nations[ownerId].frontier.Add(idx);
                    }
                }
            }
        }

        private bool HasUnclaimedLandNeighbor(int x, int y)
        {
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    Cell neighbor = grid[ny * width + nx];
                    if (neighbor.IsLand && !neighbor.HasOwner)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Advances the economy and territorial-colonization simulation.</summary>
        public bool StepSimulation(float deltaTime)
        {
            if (nations == null || nations.Count == 0)
            {
                return false;
            }

            UpdateEconomy(deltaTime);
            if (IsFullyColonized)
                return false;

            bool anyExpanded = false;
            List<int> nationIndices = new List<int>(nations.Count);
            for (int i = 0; i < nations.Count; i++)
                nationIndices.Add(i);

            for (int i = nationIndices.Count - 1; i > 0; i--)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                int temporary = nationIndices[i];
                nationIndices[i] = nationIndices[swap];
                nationIndices[swap] = temporary;
            }

            for (int i = 0; i < nationIndices.Count; i++)
            {
                Nation nation = nations[nationIndices[i]];
                int attempts = Mathf.Min(expansionRate, nation.frontier.Count);
                for (int attempt = 0; attempt < attempts && nation.frontier.Count > 0; attempt++)
                {
                    int frontierIndex = UnityEngine.Random.Range(0, nation.frontier.Count);
                    int cellIndex = nation.frontier[frontierIndex];
                    int cellX = cellIndex % width;
                    int cellY = cellIndex / width;
                    List<int> candidates = new List<int>(4);

                    for (int direction = 0; direction < 4; direction++)
                    {
                        int nx = cellX + dx[direction];
                        int ny = cellY + dy[direction];
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            int neighborIndex = ny * width + nx;
                            if (grid[neighborIndex].IsLand && !grid[neighborIndex].HasOwner)
                                candidates.Add(neighborIndex);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        RemoveFrontierCell(nation, frontierIndex);
                        continue;
                    }

                    int claimedIndex = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    grid[claimedIndex].nationId = (short)nation.id;
                    nation.territorySize++;
                    ClaimedLandCells++;
                    nation.frontier.Add(claimedIndex);
                    UpdateClaimedCellRendering(nation, claimedIndex);
                    anyExpanded = true;

                    if (candidates.Count == 1)
                        RemoveFrontierCell(nation, frontierIndex);
                }
            }

            if (anyExpanded && worldRenderer != null)
                worldRenderer.ApplyTextureChanges();

            return anyExpanded;
        }

        private void RemoveFrontierCell(Nation nation, int index)
        {
            int last = nation.frontier.Count - 1;
            nation.frontier[index] = nation.frontier[last];
            nation.frontier.RemoveAt(last);
        }

        private void UpdateClaimedCellRendering(Nation nation, int cellIndex)
        {
            if (worldRenderer == null)
                return;

            int x = cellIndex % width;
            int y = cellIndex / width;
            worldRenderer.SetPixelColor(
                cellIndex,
                worldRenderer.showBorders && WorldRenderer.IsBorderCell(grid, x, y, width, height, nation.id)
                    ? worldRenderer.borderColor
                    : worldRenderer.GetDisplayColor(nation.id, nation.color));

            for (int direction = 0; direction < 4; direction++)
            {
                int nx = x + dx[direction];
                int ny = y + dy[direction];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    int neighborIndex = ny * width + nx;
                    if (grid[neighborIndex].nationId == nation.id
                        && !WorldRenderer.IsBorderCell(grid, nx, ny, width, height, nation.id))
                    {
                        worldRenderer.SetPixelColor(neighborIndex, worldRenderer.GetDisplayColor(nation.id, nation.color));
                    }
                }
            }
        }

        /// <summary>
        /// Calculates a nation's current war strength. Kept separate so later military,
        /// diplomacy, technology, or logistics systems can extend the calculation.
        /// </summary>
        public float ComputeStrength(Nation nation)
        {
            return nation == null ? 0f : Mathf.Max(0f, nation.treasury);
        }

        /// <summary>Transfers border cells during a war and refreshes ownership/frontier data.</summary>
        public int CaptureCells(IEnumerable<int> cellIndices, int attackerId)
        {
            if (grid == null || nations == null || attackerId < 0 || attackerId >= nations.Count)
                return 0;

            int captured = 0;
            HashSet<int> refresh = new HashSet<int>();
            foreach (int index in cellIndices)
            {
                if (index < 0 || index >= grid.Length || !grid[index].IsLand || grid[index].nationId == attackerId)
                    continue;
                int defenderId = grid[index].nationId;
                if (defenderId < 0 || defenderId >= nations.Count) continue;
                grid[index].nationId = (short)attackerId;
                nations[attackerId].territorySize++;
                nations[defenderId].territorySize = Mathf.Max(0, nations[defenderId].territorySize - 1);
                captured++;
                refresh.Add(index);
                int x = index % width, y = index / width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d], ny = y + dy[d];
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height) refresh.Add(ny * width + nx);
                }
            }
            if (captured == 0) return 0;
            BuildInitialFrontiers();
            foreach (int index in refresh)
            {
                Cell cell = grid[index];
                if (cell.IsLand && cell.HasOwner && cell.nationId >= 0 && cell.nationId < nations.Count)
                    UpdateClaimedCellRendering(nations[cell.nationId], index);
            }
            worldRenderer?.ApplyTextureChanges();
            return captured;
        }

        private void UpdateEconomy(float deltaTime)
        {
            for (int i = 0; i < nations.Count; i++)
            {
                Nation nation = nations[i];
                nation.incomePerSec = nation.territorySize * incomePerPixel;
                nation.upkeepPerSec = 0f;
                nation.treasury += nation.incomePerSec * deltaTime;
            }
        }
    }
}
