using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public enum TerrainType : byte { DeepOcean = 0, ShallowOcean = 1, Land = 2 }

    public enum CellRenderKind : byte { Territory = 0, Border = 1, Frontier = 2 }

    [Serializable]
    public struct Cell
    {
        public byte terrain;
        public short nationId;
        public bool IsLand => terrain == (byte)TerrainType.Land;
        public bool IsWater => !IsLand;
        public bool HasOwner => nationId >= 0;
    }

    public static class GridExtensions
    {
        private static readonly int[] dx = { 0, 0, 1, -1 };
        private static readonly int[] dy = { 1, -1, 0, 0 };


        public static bool IsBorder(this Cell[] grid, int x, int y, int width, int height)
        {
            int ownerId = grid[y * width + x].nationId;
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= width || ny < 0 || ny >= height) return true;
                if (grid[ny * width + nx].nationId != ownerId) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the owned cell at (x, y) touches a cell owned by a *different nation*.
        /// Unclaimed land, ocean and the map edge do not count, so this is the contested
        /// boundary between two states rather than the whole outline.
        /// </summary>
        public static bool IsFrontier(this Cell[] grid, int x, int y, int width, int height)
        {
            int ownerId = grid[y * width + x].nationId;
            if (ownerId < 0) return false;

            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int neighborId = grid[ny * width + nx].nationId;
                if (neighborId >= 0 && neighborId != ownerId) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the owned cell at (x, y) touches a cell owned by <paramref name="enemyId"/>,
        /// so this is the frontier shared with one specific rival rather than with any rival.
        /// </summary>
        public static bool IsFrontierWith(this Cell[] grid, int x, int y, int width, int height, int enemyId)
        {
            int ownerId = grid[y * width + x].nationId;
            if (ownerId < 0 || ownerId == enemyId) return false;

            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                if (grid[ny * width + nx].nationId == enemyId) return true;
            }
            return false;
        }
    }

    [Serializable]
    public class City
    {
        public int id;
        public string name;
        public int nationId;
        public Vector2Int position;
        public bool isCapital;
        public float incomeBonus;

        public City(int id, string name, int nationId, Vector2Int position, bool isCapital, float incomeBonus = 5f)
        {
            this.id = id;
            this.name = name;
            this.nationId = nationId;
            this.position = position;
            this.isCapital = isCapital;
            this.incomeBonus = incomeBonus;
        }
    }

    [Serializable]
    public class Nation
    {
        public int id;
        public string name;
        public Color32 color;
        public Vector2Int capital;
        public int territorySize;

        [Header("Economy")]
        public float treasury = 1000f;

        [Header("Population")]
        public float population;
        public float incomePerSec;
        public float upkeepPerSec;
        public float netIncomePerSec => incomePerSec - upkeepPerSec;

        [Header("Cities")]
        public List<City> cities = new List<City>();

        [NonSerialized] public List<int> border = new List<int>();
        [NonSerialized] public List<int> frontier = new List<int>();

        public Nation(int id, string name, Color32 color, Vector2Int capital)
        {
            this.id = id; this.name = name; this.color = color; this.capital = capital;
            population = 100f;
            cities.Add(new City(0, $"{name} City", id, capital, true, 10f));
        }
    }
}
