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

        [Header("Economy & Military Tuning")]
        [Tooltip("Gold earned per second for each owned pixel")]
        public float incomePerPixel = 0.04f;
        [Tooltip("Gold cost per second to maintain each soldier")]
        public float upkeepPerSoldier = 0.15f;
        [Tooltip("Gold cost to recruit 1 new soldier")]
        public float recruitCost = 5f;
        [Tooltip("Maximum number of soldiers a nation may maintain. Players recruit up to this limit manually.")]
        public int fixedArmyTarget = 500;

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
                nations[n].maxArmyTarget = fixedArmyTarget;
                nations[n].incomePerSec = nations[n].territorySize * incomePerPixel;
                nations[n].upkeepPerSec = nations[n].armyCount * upkeepPerSoldier;
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

        /// <summary>
        /// Advances the passive economy and the existing territorial-colonization simulation.
        /// Army recruitment remains a player action.
        /// </summary>
        public bool StepSimulation(float deltaTime)
        {
            if (nations == null || nations.Count == 0)
            {
                return false;
            }

            UpdateEconomyAndMilitary(deltaTime);
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

        public bool CanRecruit(City city, int amount)
        {
            Nation nation = GetNation(city);
            return city != null
                && nation != null
                && amount > 0
                && nation.armyCount + amount <= fixedArmyTarget
                && nation.treasury >= recruitCost * amount;
        }

        public bool TryRecruit(City city, int amount)
        {
            if (!CanRecruit(city, amount))
                return false;

            Nation nation = GetNation(city);
            city.armyCount += amount;
            nation.armyCount += amount;
            nation.treasury -= recruitCost * amount;
            nation.upkeepPerSec = nation.armyCount * upkeepPerSoldier;
            return true;
        }

        public void Disband(City city, int amount)
        {
            Nation nation = GetNation(city);
            if (city == null || nation == null || amount <= 0)
                return;

            int removed = Mathf.Min(city.armyCount, amount);
            city.armyCount -= removed;
            nation.armyCount -= removed;
            nation.upkeepPerSec = nation.armyCount * upkeepPerSoldier;
        }

        public bool MoveTroops(City origin, City destination, float percentage)
        {
            if (origin == null || destination == null || origin == destination)
                return false;

            Nation nation = GetNation(origin);
            if (nation == null || destination.nationId != nation.id || percentage <= 0f)
                return false;

            int moving = Mathf.Clamp(Mathf.FloorToInt(origin.armyCount * percentage / 100f), 0, origin.armyCount);
            if (moving == 0)
                return false;

            origin.armyCount -= moving;
            destination.armyCount += moving;
            return true;
        }

        public bool DeployTroops(City origin, int amount)
        {
            Nation nation = GetNation(origin);
            if (origin == null || nation == null || amount <= 0 || amount > origin.armyCount)
                return false;

            origin.armyCount -= amount;
            nation.fieldArmyCount += amount;
            return true;
        }

        public void ReturnFieldTroops(City destination, int amount)
        {
            Nation nation = GetNation(destination);
            if (nation == null || amount <= 0)
                return;

            destination.armyCount += amount;
            nation.fieldArmyCount = Mathf.Max(0, nation.fieldArmyCount - amount);
        }

        public void RemoveFieldTroops(int nationId, int amount)
        {
            if (nations == null || nationId < 0 || nationId >= nations.Count || amount <= 0)
                return;

            nations[nationId].fieldArmyCount = Mathf.Max(0, nations[nationId].fieldArmyCount - amount);
        }

        /// <summary>Transfers enemy-owned land to an attacker and refreshes affected borders.</summary>
        public int CaptureEnemyCells(IEnumerable<int> cellIndices, int attackerId)
        {
            if (grid == null || nations == null || attackerId < 0 || attackerId >= nations.Count)
                return 0;

            int captured = 0;
            HashSet<int> refreshCells = new HashSet<int>();
            foreach (int index in cellIndices)
            {
                if (index < 0 || index >= grid.Length || !grid[index].IsLand)
                    continue;

                int defenderId = grid[index].nationId;
                if (defenderId < 0 || defenderId == attackerId)
                    continue;

                grid[index].nationId = (short)attackerId;
                nations[attackerId].territorySize++;
                if (defenderId < nations.Count)
                    nations[defenderId].territorySize = Mathf.Max(0, nations[defenderId].territorySize - 1);
                captured++;

                refreshCells.Add(index);
                int x = index % width;
                int y = index / width;
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        refreshCells.Add(ny * width + nx);
                }
            }

            if (captured == 0)
                return 0;

            BuildInitialFrontiers();
            if (worldRenderer != null)
            {
                foreach (int index in refreshCells)
                    RefreshCellRendering(index);
                worldRenderer.ApplyTextureChanges();
            }
            return captured;
        }

        private void RefreshCellRendering(int cellIndex)
        {
            int x = cellIndex % width;
            int y = cellIndex / width;
            Cell cell = grid[cellIndex];
            if (cell.HasOwner && cell.nationId >= 0 && cell.nationId < nations.Count)
            {
                Nation nation = nations[cell.nationId];
                worldRenderer.SetPixelColor(cellIndex,
                    worldRenderer.showBorders && WorldRenderer.IsBorderCell(grid, x, y, width, height, nation.id)
                        ? worldRenderer.borderColor
                        : worldRenderer.GetDisplayColor(nation.id, nation.color));
                return;
            }

            worldRenderer.SetPixelColor(cellIndex, cell.IsLand
                ? worldRenderer.unclaimedLandColor
                : cell.terrain == (byte)TerrainType.DeepOcean
                    ? worldRenderer.deepOceanColor
                    : worldRenderer.shallowOceanColor);
        }

        private Nation GetNation(City city)
        {
            if (city == null || nations == null || city.nationId < 0 || city.nationId >= nations.Count)
                return null;

            return nations[city.nationId];
        }

        private void UpdateEconomyAndMilitary(float deltaTime)
        {
            for (int i = 0; i < nations.Count; i++)
            {
                Nation n = nations[i];
                int totalArmy = 0;
                for (int c = 0; c < n.cities.Count; c++)
                    totalArmy += n.cities[c].armyCount;
                n.armyCount = totalArmy + n.fieldArmyCount;

                // 1. Income based on territory size
                n.incomePerSec = n.territorySize * incomePerPixel;

                // 2. Upkeep cost for active military
                n.upkeepPerSec = n.armyCount * upkeepPerSoldier;

                // 3. Treasury balance
                n.treasury += (n.incomePerSec - n.upkeepPerSec) * deltaTime;

                // Army size is player-managed. This simulation only applies payroll.
                n.maxArmyTarget = fixedArmyTarget;

                // Desertion if bankrupt
                if (n.treasury < 0f)
                {
                    n.treasury = 0f;
                    if (n.armyCount > 0)
                    {
                        City largestGarrison = null;
                        for (int c = 0; c < n.cities.Count; c++)
                        {
                            if (largestGarrison == null || n.cities[c].armyCount > largestGarrison.armyCount)
                                largestGarrison = n.cities[c];
                        }

                        if (largestGarrison != null && largestGarrison.armyCount > 0)
                        {
                            largestGarrison.armyCount--;
                            n.armyCount--;
                        }
                    }
                }
            }
        }
    }
}
