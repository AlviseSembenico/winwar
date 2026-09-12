using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WorldRenderer : MonoBehaviour
    {
        [Header("Terrain Colors (Pixel Palette)")]
        public Color32 deepOceanColor = new Color32(21, 34, 56, 255);
        public Color32 shallowOceanColor = new Color32(40, 75, 99, 255);
        public Color32 unclaimedLandColor = new Color32(200, 196, 183, 255);
        public Color32 borderColor = new Color32(10, 10, 10, 255);
        [Tooltip("Border colour for two nations currently at war.")]
        public Color32 activeWarBorderColor = new Color32(220, 40, 40, 255);

        [Header("City Logo Colors")]
        public Color32 cityDarkRing = new Color32(20, 20, 20, 255);
        public Color32 capitalGold = new Color32(255, 215, 0, 255);
        public Color32 capitalCore = new Color32(255, 255, 255, 255);
        public Color32 citySilver = new Color32(240, 240, 240, 255);
        public Color32 cityCore = new Color32(30, 30, 30, 255);

        [Header("Settings")]
        public bool showBorders = true;
        public bool showCities = true;
        [Tooltip("Display color for the nation currently controlled by the player.")]
        public Color32 selectedNationColor = new Color32(255, 225, 65, 255);
        public Color32 selectedCityColor = new Color32(70, 255, 170, 255);

        private Texture2D worldTexture;
        private Color32[] pixelBuffer;
        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private Material displayMaterial;
        private Cell[] renderedGrid;
        private List<Nation> renderedNations;
        private int renderedWidth;
        private int renderedHeight;
        private int selectedNationId = -1;
        private City selectedCity;
        private readonly HashSet<ulong> activeWarBorders = new HashSet<ulong>();

        public int SelectedNationId => selectedNationId;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshFilter = GetComponent<MeshFilter>();
            EnsureMesh();
        }

        private void EnsureMesh()
        {
            if (meshFilter.sharedMesh == null)
            {
                Mesh mesh = new Mesh { name = "WorldQuad" };
                mesh.vertices = new Vector3[]
                {
                    new Vector3(0, 0, 0),
                    new Vector3(1, 0, 0),
                    new Vector3(0, 1, 0),
                    new Vector3(1, 1, 0)
                };
                mesh.uv = new Vector2[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                };
                mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
                mesh.RecalculateNormals();
                meshFilter.sharedMesh = mesh;
            }
        }

        public void InitializeTexture(int width, int height)
        {
            EnsureMesh();

            if (worldTexture != null && (worldTexture.width != width || worldTexture.height != height))
            {
                Destroy(worldTexture);
                worldTexture = null;
            }

            if (worldTexture == null)
            {
                worldTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                worldTexture.filterMode = FilterMode.Point; // Crisp pixel art style
                worldTexture.wrapMode = TextureWrapMode.Clamp;
                pixelBuffer = new Color32[width * height];
            }

            SetupMaterial(width, height);
        }

        private void SetupMaterial(int width, int height)
        {
            if (displayMaterial == null)
            {
                Shader unlitShader = Shader.Find("Sprites/Default");
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Texture");
                displayMaterial = new Material(unlitShader);
                meshRenderer.sharedMaterial = displayMaterial;
            }

            displayMaterial.mainTexture = worldTexture;
            transform.position = Vector3.zero;
            transform.localScale = new Vector3(width, height, 1f);
        }

        public void RenderWorld(Cell[] grid, List<Nation> nations, int width, int height)
        {
            renderedGrid = grid;
            renderedNations = nations;
            renderedWidth = width;
            renderedHeight = height;

            if (worldTexture == null || pixelBuffer == null || pixelBuffer.Length != width * height)
            {
                InitializeTexture(width, height);
            }

            Dictionary<int, Color32> nationColorLookup = new Dictionary<int, Color32>();
            for (int i = 0; i < nations.Count; i++)
            {
                nationColorLookup[nations[i].id] = nations[i].color;
            }

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x;

                    if (grid[i].HasOwner && nationColorLookup.TryGetValue(grid[i].nationId, out Color32 nColor))
                    {
                        if (showBorders && IsBorderCell(grid, x, y, width, height, grid[i].nationId))
                        {
                            pixelBuffer[i] = GetBorderColor(grid, x, y, width, height, grid[i].nationId);
                        }
                        else
                        {
                            pixelBuffer[i] = GetDisplayColor(grid[i].nationId, nColor);
                        }
                    }
                    else
                    {
                        switch ((TerrainType)grid[i].terrain)
                        {
                            case TerrainType.DeepOcean:
                                pixelBuffer[i] = deepOceanColor;
                                break;
                            case TerrainType.ShallowOcean:
                                pixelBuffer[i] = shallowOceanColor;
                                break;
                            case TerrainType.Land:
                            default:
                                pixelBuffer[i] = unclaimedLandColor;
                                break;
                        }
                    }
                }
            }

            if (showCities)
            {
                for (int n = 0; n < nations.Count; n++)
                {
                    for (int c = 0; c < nations[n].cities.Count; c++)
                    {
                        DrawCityMarker(nations[n].cities[c], width, height);
                    }
                }
            }

            ApplyTextureChanges();
        }

        public void SetSelectedNation(int nationId)
        {
            if (selectedNationId == nationId)
                return;

            selectedNationId = nationId;
            if (renderedGrid != null && renderedNations != null)
            {
                RenderWorld(renderedGrid, renderedNations, renderedWidth, renderedHeight);
            }
        }

        public Color32 GetDisplayColor(int nationId, Color32 nationColor)
        {
            return nationId == selectedNationId ? selectedNationColor : nationColor;
        }

        public void SetSelectedCity(City city)
        {
            if (selectedCity == city)
                return;

            selectedCity = city;
            if (renderedGrid != null && renderedNations != null)
            {
                RenderWorld(renderedGrid, renderedNations, renderedWidth, renderedHeight);
            }
        }

        public void DrawCityMarker(City city, int width, int height)
        {
            int cx = city.position.x;
            int cy = city.position.y;
            bool isSelected = city == selectedCity;

            if (city.isCapital)
            {
                // 9x9 concentric circular crest for capitals
                int radius = 4;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            float distSq = dx * dx + dy * dy;
                            if (distSq <= 17.5f) // Circle of radius ~4.1
                            {
                                int pIdx = py * width + px;
                                if (distSq > 9.5f)
                                {
                                    pixelBuffer[pIdx] = cityDarkRing; // Outer dark ring
                                }
                                else if (distSq > 1.5f)
                                {
                                    pixelBuffer[pIdx] = isSelected ? selectedCityColor : capitalGold;
                                }
                                else
                                {
                                    pixelBuffer[pIdx] = (dx == 0 && dy == 0) ? cityDarkRing : capitalCore; // Core spire
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // 7x7 circular stone emblem for regular cities
                int radius = 3;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            float distSq = dx * dx + dy * dy;
                            if (distSq <= 9.5f) // Circle of radius ~3.1
                            {
                                int pIdx = py * width + px;
                                if (distSq > 4.5f)
                                {
                                    pixelBuffer[pIdx] = cityDarkRing; // Outer dark ring
                                }
                                else if (distSq > 0.5f)
                                {
                                    pixelBuffer[pIdx] = isSelected ? selectedCityColor : citySilver;
                                }
                                else
                                {
                                    pixelBuffer[pIdx] = cityCore; // Center dot
                                }
                            }
                        }
                    }
                }
            }
        }

        public static bool IsBorderCell(Cell[] grid, int x, int y, int width, int height, int ownerId)
        {
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (nx < 0 || nx >= width || ny < 0 || ny >= height) return true;
                if (grid[ny * width + nx].nationId != ownerId) return true;
            }
            return false;
        }

        public void SetActiveWarBorders(IEnumerable<Vector2Int> nationPairs)
        {
            activeWarBorders.Clear();
            foreach (Vector2Int pair in nationPairs)
                activeWarBorders.Add(GetNationPairKey(pair.x, pair.y));

            if (renderedGrid != null && renderedNations != null)
                RenderWorld(renderedGrid, renderedNations, renderedWidth, renderedHeight);
        }

        public Color32 GetBorderColor(Cell[] grid, int x, int y, int width, int height, int ownerId)
        {
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i], ny = y + dy[i];
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                int neighborId = grid[ny * width + nx].nationId;
                if (neighborId >= 0 && neighborId != ownerId && activeWarBorders.Contains(GetNationPairKey(ownerId, neighborId)))
                    return activeWarBorderColor;
            }
            return borderColor;
        }

        private static ulong GetNationPairKey(int firstId, int secondId)
        {
            uint low = (uint)Mathf.Min(firstId, secondId);
            uint high = (uint)Mathf.Max(firstId, secondId);
            return ((ulong)high << 32) | low;
        }

        public void SetPixelColor(int index, Color32 color)
        {
            if (pixelBuffer != null && index >= 0 && index < pixelBuffer.Length)
            {
                pixelBuffer[index] = color;
            }
        }

        public void ApplyTextureChanges()
        {
            if (worldTexture != null && pixelBuffer != null)
            {
                worldTexture.SetPixels32(pixelBuffer);
                worldTexture.Apply(false);
            }
        }

        private void OnDestroy()
        {
            if (worldTexture != null) Destroy(worldTexture);
            if (displayMaterial != null) Destroy(displayMaterial);
        }
    }
}
