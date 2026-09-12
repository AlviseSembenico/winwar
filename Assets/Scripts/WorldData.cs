using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public enum TerrainType : byte { DeepOcean = 0, ShallowOcean = 1, Land = 2 }

    [Serializable]
    public struct Cell
    {
        public byte terrain;
        public short nationId;
        public bool IsLand => terrain == (byte)TerrainType.Land;
        public bool IsWater => !IsLand;
        public bool HasOwner => nationId >= 0;
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

        [NonSerialized] public List<int> frontier = new List<int>();

        public Nation(int id, string name, Color32 color, Vector2Int capital)
        {
            this.id = id; this.name = name; this.color = color; this.capital = capital;
            population = 100f;
            cities.Add(new City(0, $"{name} City", id, capital, true, 10f));
        }
    }
}
