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
        [Tooltip("Simulation ticks between expansion passes. Higher values slow territorial growth without slowing the economy.")]
        [Range(1, 100)] public int ticksBetweenExpansions = 10;
        [HideInInspector] public int expansionBoostNationId = -1;
        [HideInInspector] public int expansionBoostMultiplier = 1;

        [Header("Economy Tuning")]
        [Tooltip("Gold earned per second for each owned pixel")]
        public float incomePerPixel = 0.04f;

        [Header("Population Tuning")]
        [Tooltip("People gained per owned pixel each second, until the population cap is reached.")]
        [Min(0f)] public float populationGrowthPerPixel = 0.05f;
        [Tooltip("Maximum population supported by each owned pixel.")]
        [Min(0f)] public float maximumPopulationPerPixel = 100f;

        public int TotalLandCells { get; private set; }
        public int ClaimedLandCells { get; private set; }
        public bool IsFullyColonized => ClaimedLandCells >= TotalLandCells && TotalLandCells > 0;
        public float ColonizedPercentage => TotalLandCells > 0 ? (float)ClaimedLandCells / TotalLandCells * 100f : 0f;

        private Cell[] grid;
        private List<Nation> nations;
        private int width;
        private int height;
        private WorldRenderer worldRenderer;
        private int expansionTicks;

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
            expansionTicks = 0;

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

            BuildBorders();
            BuildFrontiers();
        }

        private void BuildBorders()
        {
            for (int n = 0; n < nations.Count; n++)
            {
                foreach (int border in nations[n].border)
                    UpdateClaimedCellRendering(nations[n], border, CellRenderKind.Territory);
                nations[n].border.Clear();
            }

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    if (!grid[idx].HasOwner || !grid[idx].IsLand)
                        continue;

                    int ownerId = grid[idx].nationId;

                    if (grid.IsBorder(x, y, width, height))
                        nations[ownerId].border.Add(idx);
                }
            }

        }

        private void BuildFrontiers()
        {
            for (int n = 0; n < nations.Count; n++)
            {
                foreach (int frontier in nations[n].frontier)
                    UpdateClaimedCellRendering(nations[n], frontier, CellRenderKind.Territory);
                nations[n].frontier.Clear();

            }

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    if (!grid[idx].HasOwner || !grid[idx].IsLand)
                        continue;

                    int ownerId = grid[idx].nationId;
                    if (grid.IsFrontier(x, y, width, height))
                        nations[ownerId].frontier.Add(idx);
                }
            }
        }

        public void expand(Nation nation)
        {
            HashSet<int> candidates = new HashSet<int>();
            foreach (int cell in nation.border)
            {
                int cellX = cell % width;
                int cellY = cell / width;

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

            }
            foreach (int candidate in candidates)
            {
                grid[candidate].nationId = (short)nation.id;
                nation.territorySize++;
                ClaimedLandCells++;
                nation.border.Add(candidate);
                UpdateClaimedCellRendering(nation, candidate, CellRenderKind.Territory);
            }
        }

        /// <summary>Advances the economy and territorial-colonization simulation.</summary>
        public bool StepSimulation(float deltaTime)
        {
            if (nations == null || nations.Count == 0)
            {
                return false;
            }

            UpdateEconomy(deltaTime);
            UpdatePopulation(deltaTime);
            if (IsFullyColonized)
                return false;

            // Expansion runs on a slower cadence than the economy, so growth speed can be
            // tuned without changing the tick rate that drives income and population.
            expansionTicks++;

            bool anyExpanded = false;
            List<int> nationIndices = new List<int>(nations.Count);
            for (int i = 0; i < nations.Count; i++)
                nationIndices.Add(i);

            // randomize the expansion
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
                int multiplier = nation.id == expansionBoostNationId ? Mathf.Max(1, expansionBoostMultiplier) : 1;
                int interval = Mathf.Max(1, Mathf.Max(1, ticksBetweenExpansions) / multiplier);
                if (expansionTicks % interval != 0)
                    continue;

                expand(nation);
                anyExpanded = true;
            }

            if (!anyExpanded)
                return false;

            BuildBorders();
            BuildFrontiers();
            foreach (Nation nation in nations)
                RenderNation(nation);

            if (worldRenderer != null)
                worldRenderer.ApplyTextureChanges();

            return anyExpanded;
        }

        private void RenderNation(Nation nation)
        {
            foreach (int border in nation.border)
                UpdateClaimedCellRendering(nation, border, CellRenderKind.Border);
            foreach (int frontier in nation.frontier)
                UpdateClaimedCellRendering(nation, frontier, CellRenderKind.Frontier);
        }


        private void RemoveFrontierCell(Nation nation, int index)
        {
            int last = nation.frontier.Count - 1;
            int cell = nation.frontier[index];
            nation.frontier[index] = nation.frontier[last];
            nation.frontier.RemoveAt(last);
            UpdateClaimedCellRendering(nation, cell, CellRenderKind.Territory);
        }



        private void UpdateClaimedCellRendering(Nation nation, int cellIndex, CellRenderKind kind)
        {
            if (worldRenderer == null)
                return;

            int x = cellIndex % width;
            int y = cellIndex / width;
            if (!worldRenderer.showBorders)
                kind = CellRenderKind.Territory;

            Color32 color;
            switch (kind)
            {
                case CellRenderKind.Frontier:
                    color = worldRenderer.GetBorderColor(grid, x, y, width, height, nation.id);
                    break;
                case CellRenderKind.Border:
                    color = worldRenderer.borderColor;
                    break;
                default:
                    color = worldRenderer.GetDisplayColor(nation.id, nation.color);
                    break;
            }
            worldRenderer.SetPixelColor(cellIndex, color);
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
            foreach (int index in refresh)
            {
                Cell cell = grid[index];
                if (cell.IsLand && cell.HasOwner && cell.nationId >= 0 && cell.nationId < nations.Count)
                    UpdateClaimedCellRendering(nations[cell.nationId], index, CellRenderKind.Territory);
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

        private void UpdatePopulation(float deltaTime)
        {
            for (int i = 0; i < nations.Count; i++)
            {
                Nation nation = nations[i];
                float populationCap = nation.territorySize * maximumPopulationPerPixel;
                float growth = nation.territorySize * populationGrowthPerPixel * deltaTime;
                nation.population = Mathf.Min(populationCap, nation.population + growth);
            }
        }
    }
}
