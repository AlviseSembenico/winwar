using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public enum TerrainType : byte
    {
        DeepOcean = 0,
        ShallowOcean = 1,
        Land = 2
    }

    [System.Serializable]
    public struct Cell
    {
        public byte terrain; // 0 = Deep Ocean, 1 = Shallow Ocean, 2 = Land
        public short nationId; // -1 = Unclaimed, >= 0 = Nation ID

        public bool IsLand => terrain == (byte)TerrainType.Land;
        public bool IsWater => terrain == (byte)TerrainType.DeepOcean || terrain == (byte)TerrainType.ShallowOcean;
        public bool HasOwner => nationId >= 0;
    }

    [System.Serializable]
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

    [System.Serializable]
    public class Nation
    {
        public int id;
        public string name;
        public Color32 color;
        public Vector2Int capital;
        public int territorySize;

        [Header("Economy")]
        public float treasury = 100f;
        public float incomePerSec = 0f;
        public float upkeepPerSec = 0f;
        public float netIncomePerSec => incomePerSec - upkeepPerSec;

        [Header("Military (Fixed Low Cap)")]
        public int armyCount = 20;
        public int maxArmyTarget = 50;

        [Header("Cities")]
        public List<City> cities = new List<City>();

        [System.NonSerialized]
        public List<int> frontier = new List<int>();

        public Nation(int id, string name, Color32 color, Vector2Int capital)
        {
            this.id = id;
            this.name = name;
            this.color = color;
            this.capital = capital;
            this.territorySize = 0;
            this.treasury = 100f;
            this.armyCount = 20;
            this.maxArmyTarget = 50;
            this.frontier = new List<int>();
            this.cities = new List<City>();

            // Add capital city
            City capitalCity = new City(0, $"{name} City", id, capital, true, 10f);
            cities.Add(capitalCity);
        }
    }
}
