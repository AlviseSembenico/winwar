using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public class WorldGenerator : MonoBehaviour
    {
        [Header("Map Dimensions")]
        [Range(100, 2000)] public int width = 1000;
        [Range(100, 2000)] public int height = 1000;

        [Header("Terrain Noise")]
        public int seed = 42;
        public bool randomSeedOnStart = true;
        public float noiseScale = 0.004f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0f, 1f)] public float persistence = 0.5f;
        public float lacunarity = 2.0f;

        [Header("Sea & Land Thresholds")]
        [Range(0f, 1f)] public float seaLevel = 0.46f;
        [Range(0f, 1f)] public float deepOceanThreshold = 0.34f;
        public bool applyEdgeFalloff = true;

        [Header("Nations")]
        [Range(2, 200)] public int nationCount = 30;
        [Range(4, 50)] public int initialTerritoryRadius = 18;
        public int minCapitalDistance = 30;

        // Generated data
        public Cell[] Grid { get; private set; }
        public List<Nation> Nations { get; private set; } = new List<Nation>();

        public (int, int) IndexToCoord(int index)
        {
            int x = index % width;
            int y = index / width;
            return (x, y);
        }


        public Vector2Int IndexToVec2(int index)
        {
            var coord = IndexToCoord(index);
            return new Vector2Int(coord.Item1, coord.Item2);
        }

        public void GenerateWorld()
        {
            if (randomSeedOnStart)
            {
                seed = UnityEngine.Random.Range(1, 1000000);
            }

            Grid = new Cell[width * height];
            Nations.Clear();

            GenerateTerrain();
            SpawnNations();
        }

        private void GenerateTerrain()
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            System.Random prng = new System.Random(seed);
            Vector2[] octaveOffsets = new Vector2[octaves];
            for (int i = 0; i < octaves; i++)
            {
                float offsetX = prng.Next(-100000, 100000);
                float offsetY = prng.Next(-100000, 100000);
                octaveOffsets[i] = new Vector2(offsetX, offsetY);
            }

            float[] rawHeights = new float[width * height];
            float minNoise = float.MaxValue;
            float maxNoise = float.MinValue;

            // Pass 1: Multi-octave noise calculation
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    float amplitude = 1f;
                    float frequency = 1f;
                    float noiseHeight = 0f;

                    for (int i = 0; i < octaves; i++)
                    {
                        float sampleX = (x + octaveOffsets[i].x) * noiseScale * frequency;
                        float sampleY = (y + octaveOffsets[i].y) * noiseScale * frequency;

                        float perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
                        noiseHeight += perlinValue * amplitude;

                        amplitude *= persistence;
                        frequency *= lacunarity;
                    }

                    int idx = rowOffset + x;
                    rawHeights[idx] = noiseHeight;

                    if (noiseHeight < minNoise) minNoise = noiseHeight;
                    if (noiseHeight > maxNoise) maxNoise = noiseHeight;
                }
            }

            float noiseRange = maxNoise - minNoise;
            if (noiseRange < 0.0001f) noiseRange = 1f;

            // Pass 2: Normalize to [0, 1], apply edge falloff, assign terrain
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                float dy = (y - halfH) / halfH;

                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    float normalized = (rawHeights[idx] - minNoise) / noiseRange;

                    if (applyEdgeFalloff)
                    {
                        float dx = (x - halfW) / halfW;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist > 0.75f)
                        {
                            // Gentle fade only near the outer borders
                            float edgeFade = Mathf.InverseLerp(0.75f, 1.2f, dist);
                            normalized = Mathf.Clamp01(normalized - edgeFade * 0.4f);
                        }
                    }

                    Grid[idx].nationId = -1;

                    if (normalized < deepOceanThreshold)
                    {
                        Grid[idx].terrain = (byte)TerrainType.DeepOcean;
                    }
                    else if (normalized < seaLevel)
                    {
                        Grid[idx].terrain = (byte)TerrainType.ShallowOcean;
                    }
                    else
                    {
                        Grid[idx].terrain = (byte)TerrainType.Land;
                    }
                }
            }
        }

        private void SpawnNations()
        {
            List<Vector2Int> landCells = new List<Vector2Int>(width * height / 4);
            for (int y = 10; y < height - 10; y++)
            {
                int rowOffset = y * width;
                for (int x = 10; x < width - 10; x++)
                {
                    if (Grid[rowOffset + x].IsLand)
                    {
                        landCells.Add(new Vector2Int(x, y));
                    }
                }
            }

            if (landCells.Count == 0)
            {
                Debug.LogWarning("No land generated. Try adjusting sea level.");
                return;
            }

            // Shuffle land candidates
            System.Random prng = new System.Random(seed + 888);
            for (int i = landCells.Count - 1; i > 0; i--)
            {
                int swapIdx = prng.Next(i + 1);
                var temp = landCells[i];
                landCells[i] = landCells[swapIdx];
                landCells[swapIdx] = temp;
            }

            List<Vector2Int> capitals = new List<Vector2Int>();
            int minDistanceSq = minCapitalDistance * minCapitalDistance;

            foreach (var candidate in landCells)
            {
                if (capitals.Count >= nationCount) break;

                bool tooClose = false;
                for (int c = 0; c < capitals.Count; c++)
                {
                    int dx = candidate.x - capitals[c].x;
                    int dy = candidate.y - capitals[c].y;
                    if (dx * dx + dy * dy < minDistanceSq)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    capitals.Add(candidate);
                }
            }

            // Generate bright, distinct colors
            float goldenRatio = 0.618033988749895f;
            float hue = (float)prng.NextDouble();

            for (int i = 0; i < capitals.Count; i++)
            {
                hue = (hue + goldenRatio) % 1.0f;
                Color rgb = Color.HSVToRGB(hue, 0.85f, 0.95f);
                Color32 nationColor = new Color32((byte)(rgb.r * 255), (byte)(rgb.g * 255), (byte)(rgb.b * 255), 255);

                string nationName = GenerateNationName(prng);
                Nation nation = new Nation(i, nationName, nationColor, capitals[i]);
                Nations.Add(nation);

                ExpandInitialTerritory(nation, initialTerritoryRadius);
            }
        }

        private void ExpandInitialTerritory(Nation nation, int radius)
        {
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            HashSet<int> visited = new HashSet<int>();

            Vector2Int cap = nation.capital;
            queue.Enqueue(cap);
            visited.Add(cap.y * width + cap.x);

            int radiusSq = radius * radius;
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            while (queue.Count > 0)
            {
                Vector2Int curr = queue.Dequeue();
                int currIdx = curr.y * width + curr.x;

                if (Grid[currIdx].IsLand && (!Grid[currIdx].HasOwner || Grid[currIdx].nationId == nation.id))
                {
                    Grid[currIdx].nationId = (short)nation.id;
                    nation.territorySize++;
                }

                for (int i = 0; i < 4; i++)
                {
                    int nx = curr.x + dx[i];
                    int ny = curr.y + dy[i];

                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        int distSq = (nx - cap.x) * (nx - cap.x) + (ny - cap.y) * (ny - cap.y);
                        if (distSq <= radiusSq)
                        {
                            int nIdx = ny * width + nx;
                            if (!visited.Contains(nIdx) && Grid[nIdx].IsLand && !Grid[nIdx].HasOwner)
                            {
                                visited.Add(nIdx);
                                queue.Enqueue(new Vector2Int(nx, ny));
                            }
                        }
                    }
                }
            }
        }

        private string GenerateNationName(System.Random prng)
        {
            string[] prefixes = { "Nord", "Val", "Aethel", "Khor", "Oron", "Zan", "Eld", "Drak", "Sol", "Lun", "Mor", "Rhun", "Ver", "Thar", "Kal", "Ar", "Ost", "Gond", "Rohan" };
            string[] suffixes = { "ia", "land", "aria", "mark", "dor", "gard", "istan", "val", "reach", "shire", "realm", "berg", "onia", "terra", "ica", "moor" };

            string p = prefixes[prng.Next(prefixes.Length)];
            string s = suffixes[prng.Next(suffixes.Length)];
            return p + s;
        }
    }
}
