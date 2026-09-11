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
        public int armyCount;

        public City(int id, string name, int nationId, Vector2Int position, bool isCapital, float incomeBonus = 5f)
        {
            this.id = id;
            this.name = name;
            this.nationId = nationId;
            this.position = position;
            this.isCapital = isCapital;
            this.incomeBonus = incomeBonus;
            this.armyCount = 0;
        }
    }

    [System.Serializable]
    public class TroopGroup
    {
        public int nationId;
        public int soldierCount;
        public Vector2 position;

        public TroopGroup(int nationId, int soldierCount, Vector2 position)
        {
            this.nationId = nationId;
            this.soldierCount = soldierCount;
            this.position = position;
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
        public float treasury = 1000f;
        public float incomePerSec = 0f;
        public float upkeepPerSec = 0f;
        public float netIncomePerSec => incomePerSec - upkeepPerSec;

        [Header("Military (Player Managed)")]
        public int armyCount = 20;
        public int fieldArmyCount;
        public int maxArmyTarget = 500;

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
            this.treasury = 1000f;
            this.armyCount = 20;
            this.maxArmyTarget = 500;
            this.frontier = new List<int>();
            this.cities = new List<City>();

            // Add capital city
            City capitalCity = new City(0, $"{name} City", id, capital, true, 10f);
            capitalCity.armyCount = armyCount;
            cities.Add(capitalCity);
        }
    }
}
