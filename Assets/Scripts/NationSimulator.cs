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
        [Tooltip("Fixed low target/limit for army size")]
        public int fixedArmyTarget = 50;

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
            {
                nations[n].frontier.Clear();
            }

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    if (grid[idx].HasOwner && grid[idx].IsLand)
                    {
                        int ownerId = grid[idx].nationId;
                        if (ownerId >= 0 && ownerId < nations.Count)
                        {
                            if (HasUnclaimedLandNeighbor(x, y))
                            {
                                nations[ownerId].frontier.Add(idx);
                            }
                        }
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
                    int nIdx = ny * width + nx;
                    if (grid[nIdx].IsLand && !grid[nIdx].HasOwner)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool StepSimulation(float deltaTime)
        {
            if (nations == null || nations.Count == 0)
            {
                return false;
            }

            // 1. Update Economy & Military
            UpdateEconomyAndMilitary(deltaTime);

            // 2. Territorial Expansion
            if (IsFullyColonized)
            {
                return false;
            }

            bool anyExpanded = false;

            // Randomize nation order each tick
            List<int> nationIndices = new List<int>(nations.Count);
            for (int i = 0; i < nations.Count; i++) nationIndices.Add(i);
            for (int i = nationIndices.Count - 1; i > 0; i--)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                int temp = nationIndices[i];
                nationIndices[i] = nationIndices[swap];
                nationIndices[swap] = temp;
            }

            for (int i = 0; i < nationIndices.Count; i++)
            {
                Nation nation = nations[nationIndices[i]];
                if (nation.frontier.Count == 0) continue;

                int attempts = Mathf.Min(expansionRate, nation.frontier.Count);

                for (int a = 0; a < attempts; a++)
                {
                    if (nation.frontier.Count == 0) break;

                    int pickIdx = UnityEngine.Random.Range(0, nation.frontier.Count);
                    int cellIdx = nation.frontier[pickIdx];
                    int cx = cellIdx % width;
                    int cy = cellIdx / width;

                    List<int> candidates = new List<int>(4);
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d];
                        int ny = cy + dy[d];

                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            int nIdx = ny * width + nx;
                            if (grid[nIdx].IsLand && !grid[nIdx].HasOwner)
                            {
                                candidates.Add(nIdx);
                            }
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        int chosenNIdx = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                        grid[chosenNIdx].nationId = (short)nation.id;
                        nation.territorySize++;
                        ClaimedLandCells++;

                        nation.frontier.Add(chosenNIdx);

                        if (worldRenderer != null)
                        {
                            int chosenX = chosenNIdx % width;
                            int chosenY = chosenNIdx / width;

                            if (worldRenderer.showBorders && WorldRenderer.IsBorderCell(grid, chosenX, chosenY, width, height, nation.id))
                            {
                                worldRenderer.SetPixelColor(chosenNIdx, worldRenderer.borderColor);
                            }
                            else
                            {
                                worldRenderer.SetPixelColor(chosenNIdx, nation.color);
                            }

                            for (int d = 0; d < 4; d++)
                            {
                                int adjX = chosenX + dx[d];
                                int adjY = chosenY + dy[d];
                                if (adjX >= 0 && adjX < width && adjY >= 0 && adjY < height)
                                {
                                    int adjIdx = adjY * width + adjX;
                                    if (grid[adjIdx].nationId == nation.id)
                                    {
                                        if (!WorldRenderer.IsBorderCell(grid, adjX, adjY, width, height, nation.id))
                                        {
                                            worldRenderer.SetPixelColor(adjIdx, nation.color);
                                        }
                                    }
                                }
                            }
                        }

                        anyExpanded = true;

                        if (candidates.Count <= 1)
                        {
                            int lastIdx = nation.frontier.Count - 1;
                            nation.frontier[pickIdx] = nation.frontier[lastIdx];
                            nation.frontier.RemoveAt(lastIdx);
                        }
                    }
                    else
                    {
                        int lastIdx = nation.frontier.Count - 1;
                        nation.frontier[pickIdx] = nation.frontier[lastIdx];
                        nation.frontier.RemoveAt(lastIdx);
                    }
                }
            }

            if (anyExpanded && worldRenderer != null)
            {
                worldRenderer.ApplyTextureChanges();
            }

            return anyExpanded;
        }

        private void UpdateEconomyAndMilitary(float deltaTime)
        {
            for (int i = 0; i < nations.Count; i++)
            {
                Nation n = nations[i];

                // 1. Income based on territory size
                n.incomePerSec = n.territorySize * incomePerPixel;

                // 2. Upkeep cost for active military
                n.upkeepPerSec = n.armyCount * upkeepPerSoldier;

                // 3. Treasury balance
                n.treasury += (n.incomePerSec - n.upkeepPerSec) * deltaTime;

                // 4. Recruitment logic (up to fixed low target)
                n.maxArmyTarget = fixedArmyTarget;
                if (n.armyCount < n.maxArmyTarget && n.treasury >= recruitCost * 1.5f)
                {
                    n.armyCount++;
                    n.treasury -= recruitCost;
                }

                // 5. Desertion if bankrupt
                if (n.treasury < 0f)
                {
                    n.treasury = 0f;
                    if (n.armyCount > 5)
                    {
                        n.armyCount--; // 1 soldier leaves due to unpaid wages
                    }
                }
            }
        }
    }
}
