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
        public Color32 borderColor = new Color32(10, 10, 10, 255); // Black frontier line
        public Color32 capitalMarkerColor = new Color32(255, 255, 255, 255);

        [Header("Settings")]
        public bool showBorders = true;
        public bool showCapitalMarkers = true;

        private Texture2D worldTexture;
        private Color32[] pixelBuffer;
        private MeshRenderer meshRenderer;
        private MeshFilter meshFilter;
        private Material displayMaterial;

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
                            pixelBuffer[i] = borderColor;
                        }
                        else
                        {
                            pixelBuffer[i] = nColor;
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

            if (showCapitalMarkers)
            {
                for (int n = 0; n < nations.Count; n++)
                {
                    Vector2Int cap = nations[n].capital;
                    DrawCapitalMarker(cap.x, cap.y, width, height);
                }
            }

            ApplyTextureChanges();
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

        private void DrawCapitalMarker(int cx, int cy, int width, int height)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        if (dx == 0 || dy == 0)
                        {
                            pixelBuffer[py * width + px] = capitalMarkerColor;
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (worldTexture != null) Destroy(worldTexture);
            if (displayMaterial != null) Destroy(displayMaterial);
        }
    }
}
