using System.Collections.Generic;
using UnityEngine;

namespace AgesOfConflict
{
    public class SimulationManager : MonoBehaviour
    {
        [Header("Components")]
        public WorldGenerator worldGenerator;
        public WorldRenderer worldRenderer;
        public NationSimulator nationSimulator;
        public CameraController cameraController;

        [Header("City Construction")]
        public float buildCityCost = 100f;
        public int minCitySpacing = 6;

        [Header("Simulation State")]
        public bool isRunning = true;
        [Range(0.01f, 0.5f)] public float tickInterval = 0.04f; // 25 ticks/sec
        public int speedMultiplier = 1;

        [Header("Runtime Info")]
        [SerializeField] private int currentSeed;
        [SerializeField] private int activeNations;

        private Camera mainCam;
        private float tickTimer = 0f;
        private List<Nation> sortedNations = new List<Nation>();
        private Nation hoveredNation = null;
        private Nation selectedNation = null;

        // Context menu state
        private bool showContextMenu = false;
        private Vector2 contextMenuScreenPos;
        private Vector2Int contextCellPos;
        private Nation contextNation;
        private City contextCity;

        // City-to-city troop transfer state
        private bool showTransferDialog = false;
        private City transferOrigin;
        private City transferDestination;
        private string transferPercentage = "50";
        private string recruitAmount = "1";
        private string commandMessage;
        private GUIStyle garrisonLabelStyle;

        private void Start()
        {
            mainCam = Camera.main;

            if (worldGenerator == null)
            {
                worldGenerator = GetComponent<WorldGenerator>();
                if (worldGenerator == null) worldGenerator = gameObject.AddComponent<WorldGenerator>();
            }

            if (worldRenderer == null)
            {
                worldRenderer = GetComponent<WorldRenderer>();
                if (worldRenderer == null) worldRenderer = gameObject.AddComponent<WorldRenderer>();
            }

            if (nationSimulator == null)
            {
                nationSimulator = GetComponent<NationSimulator>();
                if (nationSimulator == null) nationSimulator = gameObject.AddComponent<NationSimulator>();
            }

            if (cameraController == null && mainCam != null)
            {
                cameraController = mainCam.GetComponent<CameraController>();
                if (cameraController == null) cameraController = mainCam.gameObject.AddComponent<CameraController>();
            }

            if (cameraController != null)
            {
                cameraController.OnRightClickTap += HandleRightClickTap;
                cameraController.OnLeftClickTap += HandleNationSelection;
                cameraController.OnLeftDragCompleted += HandleCityDrag;
                cameraController.ShouldReserveLeftDrag += IsCityAt;
            }

            Regenerate();
        }

        private void OnDestroy()
        {
            if (cameraController != null)
            {
                cameraController.OnRightClickTap -= HandleRightClickTap;
                cameraController.OnLeftClickTap -= HandleNationSelection;
                cameraController.OnLeftDragCompleted -= HandleCityDrag;
                cameraController.ShouldReserveLeftDrag -= IsCityAt;
            }
        }

        private void HandleRightClickTap(Vector3 worldPos)
        {
            City city = FindCityAt(worldPos);
            if (city != null)
            {
                contextNation = worldGenerator.Nations[city.nationId];
                contextCity = city;
            }
            else
            {
                if (worldGenerator == null || worldGenerator.Grid == null) return;

                int gx = Mathf.FloorToInt(worldPos.x);
                int gy = Mathf.FloorToInt(worldPos.y);
                if (gx < 0 || gx >= worldGenerator.width || gy < 0 || gy >= worldGenerator.height)
                {
                    showContextMenu = false;
                    return;
                }

                Cell cell = worldGenerator.Grid[gy * worldGenerator.width + gx];
                if (!cell.HasOwner || cell.nationId < 0 || cell.nationId >= worldGenerator.Nations.Count)
                {
                    showContextMenu = false;
                    return;
                }

                contextNation = worldGenerator.Nations[cell.nationId];
                contextCity = null;
            }

            contextCellPos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
            if (selectedNation == null || contextNation.id != selectedNation.id)
            {
                showContextMenu = false;
                commandMessage = "Select this nation with a left-click before issuing commands.";
                return;
            }
            commandMessage = null;

            Vector3 screen = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 mPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                screen = new Vector3(mPos.x, mPos.y, 0f);
            }
#endif
            // Convert to GUI coordinate (y is inverted in OnGUI)
            contextMenuScreenPos = new Vector2(screen.x, Screen.height - screen.y);
            showContextMenu = true;
        }

        private void HandleNationSelection(Vector3 worldPos)
        {
            if (worldGenerator == null || worldGenerator.Grid == null)
                return;

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return;

            Cell cell = worldGenerator.Grid[y * worldGenerator.width + x];
            if (!cell.HasOwner || cell.nationId < 0 || cell.nationId >= worldGenerator.Nations.Count)
                return;

            selectedNation = worldGenerator.Nations[cell.nationId];
            worldRenderer.SetSelectedNation(selectedNation.id);
            commandMessage = $"Now controlling {selectedNation.name}.";
        }

        private bool IsCityAt(Vector3 worldPos) => FindCityAt(worldPos) != null;

        private City FindCityAt(Vector3 worldPos)
        {
            if (worldGenerator == null || worldGenerator.Nations == null)
                return null;

            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            for (int n = 0; n < worldGenerator.Nations.Count; n++)
            {
                foreach (City city in worldGenerator.Nations[n].cities)
                {
                    float hitRadius = city.isCapital ? 4.5f : 3.5f;
                    if (Vector2.Distance(point, city.position) <= hitRadius)
                        return city;
                }
            }
            return null;
        }

        private void HandleCityDrag(Vector3 originWorld, Vector3 destinationWorld)
        {
            City origin = FindCityAt(originWorld);
            City destination = FindCityAt(destinationWorld);
            showContextMenu = false;

            if (origin == null || destination == null || origin == destination)
            {
                commandMessage = "Drag from one city to a different city.";
                return;
            }

            if (origin.nationId != destination.nationId)
            {
                commandMessage = "Troops can currently move only between cities of the same nation.";
                return;
            }

            if (selectedNation == null || origin.nationId != selectedNation.id)
            {
                commandMessage = "Select the nation before moving its troops.";
                return;
            }

            transferOrigin = origin;
            transferDestination = destination;
            transferPercentage = "50";
            showTransferDialog = true;
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateHoveredNation();

            // Close context menu if left-clicked outside
            if (showContextMenu && (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)))
            {
                Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                Rect menuRect = new Rect(contextMenuScreenPos.x, contextMenuScreenPos.y, 230, 310);
                if (!menuRect.Contains(mouseGui))
                {
                    showContextMenu = false;
                }
            }

            // Simulation loop
            if (isRunning && nationSimulator != null)
            {
                tickTimer += Time.deltaTime * speedMultiplier;
                while (tickTimer >= tickInterval)
                {
                    tickTimer -= tickInterval;
                    nationSimulator.StepSimulation(tickInterval);
                }
            }
        }

        private void UpdateHoveredNation()
        {
            hoveredNation = null;
            if (mainCam == null || worldGenerator == null || worldGenerator.Grid == null) return;

            Vector3 mousePos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 mPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mousePos = new Vector3(mPos.x, mPos.y, 0f);
            }
#endif
            Vector3 worldPt = mainCam.ScreenToWorldPoint(mousePos);
            int gx = Mathf.FloorToInt(worldPt.x);
            int gy = Mathf.FloorToInt(worldPt.y);

            if (gx >= 0 && gx < worldGenerator.width && gy >= 0 && gy < worldGenerator.height)
            {
                int idx = gy * worldGenerator.width + gx;
                Cell cell = worldGenerator.Grid[idx];
                if (cell.HasOwner && cell.nationId >= 0 && cell.nationId < worldGenerator.Nations.Count)
                {
                    hoveredNation = worldGenerator.Nations[cell.nationId];
                }
            }
        }

        private void HandleHotkeys()
        {
            if (IsSpacePressed()) isRunning = !isRunning;
            if (IsSPressed() && !isRunning && nationSimulator != null) nationSimulator.StepSimulation(tickInterval);
            if (IsRPressed()) Regenerate();
            if (IsFPressed() && cameraController != null && worldGenerator != null) cameraController.FocusOnMap(worldGenerator.width, worldGenerator.height);

            if (Is1Pressed()) speedMultiplier = 1;
            if (Is2Pressed()) speedMultiplier = 2;
            if (Is3Pressed()) speedMultiplier = 5;
            if (Is4Pressed()) speedMultiplier = 10;
        }

        private bool IsSpacePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) return true;
#endif
            return false;
        }

        private bool IsSPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.sKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.S)) return true;
#endif
            return false;
        }

        private bool IsRPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.R)) return true;
#endif
            return false;
        }

        private bool IsFPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F)) return true;
#endif
            return false;
        }

        private bool Is1Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.digit1Key.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha1)) return true;
#endif
            return false;
        }

        private bool Is2Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.digit2Key.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha2)) return true;
#endif
            return false;
        }

        private bool Is3Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.digit3Key.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha3)) return true;
#endif
            return false;
        }

        private bool Is4Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.digit4Key.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Alpha4)) return true;
#endif
            return false;
        }

        [ContextMenu("Regenerate World")]
        public void Regenerate()
        {
            if (worldGenerator == null || worldRenderer == null) return;

            showContextMenu = false;
            float startTime = Time.realtimeSinceStartup;

            worldGenerator.GenerateWorld();
            currentSeed = worldGenerator.seed;
            activeNations = worldGenerator.Nations.Count;
            selectedNation = null;
            worldRenderer.SetSelectedNation(-1);

            worldRenderer.RenderWorld(worldGenerator.Grid, worldGenerator.Nations, worldGenerator.width, worldGenerator.height);

            if (nationSimulator != null)
            {
                nationSimulator.Initialize(worldGenerator.Grid, worldGenerator.Nations, worldGenerator.width, worldGenerator.height, worldRenderer);
            }

            if (cameraController != null)
            {
                cameraController.FocusOnMap(worldGenerator.width, worldGenerator.height);
            }

            tickTimer = 0f;

            float duration = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log($"World Generated ({worldGenerator.width}x{worldGenerator.height}) in {duration:F1} ms. Seed: {currentSeed}");
        }

        private void OnGUI()
        {
            if (worldGenerator == null) return;

            DrawCityGarrisonLabels();

            // Left Side: Control Box
            GUI.Box(new Rect(15, 15, 280, 400), "Ages of Conflict - Simulation");

            GUILayout.BeginArea(new Rect(25, 40, 260, 365));

            GUILayout.Label($"<b>Resolution:</b> {worldGenerator.width} x {worldGenerator.height}");
            GUILayout.Label($"<b>Seed:</b> {currentSeed} | <b>Nations:</b> {activeNations}");
            string selectedName = selectedNation == null ? "None (left-click a nation)" : selectedNation.name;
            GUILayout.Label($"<b>Controlling:</b> {selectedName}");

            // Colonization Status
            if (nationSimulator != null)
            {
                float pct = nationSimulator.ColonizedPercentage;
                string statusText = nationSimulator.IsFullyColonized ? "<color=#55FF55><b>100% Colonized!</b></color>" : $"{pct:F1}% Claimed";
                GUILayout.Label($"<b>Colonization:</b> {statusText} ({nationSimulator.ClaimedLandCells:N0}/{nationSimulator.TotalLandCells:N0})");

                Rect r = GUILayoutUtility.GetRect(250, 14);
                GUI.Box(r, "");
                Rect fillRect = new Rect(r.x + 1, r.y + 1, (r.width - 2) * (pct / 100f), r.height - 2);
                GUI.DrawTexture(fillRect, Texture2D.whiteTexture);
            }

            GUILayout.Space(6);

            // Playback Controls
            GUILayout.BeginHorizontal();
            string playBtnText = isRunning ? "⏸ Pause [Space]" : "▶ Play [Space]";
            if (GUILayout.Button(playBtnText, GUILayout.Height(30)))
            {
                isRunning = !isRunning;
            }

            GUI.enabled = !isRunning;
            if (GUILayout.Button("Step [S]", GUILayout.Height(30), GUILayout.Width(75)))
            {
                if (nationSimulator != null) nationSimulator.StepSimulation(tickInterval);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // Speed Controls
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Speed:</b>", GUILayout.Width(50));
            int[] speeds = { 1, 2, 5, 10 };
            for (int i = 0; i < speeds.Length; i++)
            {
                int s = speeds[i];
                bool isCur = speedMultiplier == s;
                if (GUILayout.Toggle(isCur, $"{s}x", "Button", GUILayout.Width(45)))
                {
                    speedMultiplier = s;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (GUILayout.Button("🔄 Regenerate Map [R]", GUILayout.Height(28)))
            {
                Regenerate();
            }

            if (GUILayout.Button("🎯 Reset Camera [F]", GUILayout.Height(24)))
            {
                if (cameraController != null && worldGenerator != null)
                {
                    cameraController.FocusOnMap(worldGenerator.width, worldGenerator.height);
                }
            }

            GUILayout.Space(4);
            GUILayout.Label("• <b>Scroll</b>: Zoom | <b>WASD/MMB</b>: Pan");
            GUILayout.Label("• <b>LMB territory</b>: Select nation | <b>RMB city</b>: Recruit");
            GUILayout.Label("• <b>LMB drag city→city</b>: Move troops");
            GUILayout.Label("• Territory expands automatically; recruitment is manual");

            // Mouse hover inspector info
            Vector3 mousePos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 mPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mousePos = new Vector3(mPos.x, mPos.y, 0f);
            }
#endif
            if (mainCam != null && worldGenerator.Grid != null)
            {
                Vector3 worldPt = mainCam.ScreenToWorldPoint(mousePos);
                int gx = Mathf.FloorToInt(worldPt.x);
                int gy = Mathf.FloorToInt(worldPt.y);

                if (gx >= 0 && gx < worldGenerator.width && gy >= 0 && gy < worldGenerator.height)
                {
                    int idx = gy * worldGenerator.width + gx;
                    Cell cell = worldGenerator.Grid[idx];

                    string cellInfo = "Deep Ocean";
                    if (cell.terrain == (byte)TerrainType.ShallowOcean) cellInfo = "Shallow Ocean";
                    else if (cell.IsLand)
                    {
                        if (cell.HasOwner && cell.nationId >= 0 && cell.nationId < worldGenerator.Nations.Count)
                        {
                            cellInfo = $"{worldGenerator.Nations[cell.nationId].name}";
                        }
                        else
                        {
                            cellInfo = "Unclaimed Land";
                        }
                    }
                    GUILayout.Label($"<b>Hover:</b> ({gx}, {gy}) - <i>{cellInfo}</i>");
                }
            }

            GUILayout.EndArea();

            // Right Side Panel: State Overview or Leaderboard
            DrawRightPanel();

            // Context Menu: Build City Popup
            if (showContextMenu)
            {
                DrawCityContextMenu();
            }

            if (showTransferDialog)
            {
                DrawTroopTransferDialog();
            }

            if (!string.IsNullOrEmpty(commandMessage))
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 190, 15, 380, 30), commandMessage);
            }
        }

        private void DrawCityGarrisonLabels()
        {
            if (mainCam == null || worldGenerator.Nations == null)
                return;

            if (garrisonLabelStyle == null)
            {
                garrisonLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
                garrisonLabelStyle.normal.textColor = Color.white;
            }

            for (int n = 0; n < worldGenerator.Nations.Count; n++)
            {
                foreach (City city in worldGenerator.Nations[n].cities)
                {
                    if (city.armyCount <= 0)
                        continue;

                    float heightAboveCity = city.isCapital ? 6f : 5f;
                    Vector3 screenPosition = mainCam.WorldToScreenPoint(
                        new Vector3(city.position.x + 0.5f, city.position.y + heightAboveCity, 0f));
                    if (screenPosition.z < 0f)
                        continue;

                    Rect labelRect = new Rect(screenPosition.x - 28f, Screen.height - screenPosition.y - 10f, 56f, 20f);
                    Color previousColor = GUI.color;
                    GUI.color = new Color(0f, 0f, 0f, 0.7f);
                    GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
                    GUI.color = previousColor;
                    GUI.Label(labelRect, city.armyCount.ToString(), garrisonLabelStyle);
                }
            }
        }

        private void DrawCityContextMenu()
        {
            if (contextNation == null) return;

            float menuW = 230;
            float menuH = 310;
            float posX = Mathf.Clamp(contextMenuScreenPos.x, 10, Screen.width - menuW - 10);
            float posY = Mathf.Clamp(contextMenuScreenPos.y, 10, Screen.height - menuH - 10);

            GUI.Box(new Rect(posX, posY, menuW, menuH), contextCity != null ? "⚔️ Nation Commands" : "🏰 City Construction");

            GUILayout.BeginArea(new Rect(posX + 10, posY + 25, menuW - 20, menuH - 30));

            Color orig = GUI.color;
            GUI.color = contextNation.color;
            GUILayout.Label($"<b>{contextNation.name}</b>");
            GUI.color = orig;

            GUILayout.Label($"Location: ({contextCellPos.x}, {contextCellPos.y})");
            GUILayout.Label($"Treasury: <color=#FFD700>{contextNation.treasury:F0} gold</color>");

            if (contextCity != null)
            {
                GUILayout.Label($"City: <b>{contextCity.name}</b>");
                GUILayout.Label($"Garrison: <b>{contextCity.armyCount}</b> | Nation army: {contextNation.armyCount} / {contextNation.maxArmyTarget}");
                GUILayout.Space(3);
                GUILayout.Label("<b>Troop Management</b>");
                GUILayout.BeginHorizontal();

                GUI.SetNextControlName("RecruitAmount");
                recruitAmount = GUILayout.TextField(recruitAmount, 4, GUILayout.Width(42), GUILayout.Height(25));
                bool hasValidRecruitAmount = int.TryParse(recruitAmount, out int amount) && amount > 0;
                bool canRecruit = hasValidRecruitAmount && nationSimulator != null && nationSimulator.CanRecruit(contextCity, amount);
                Event currentEvent = Event.current;
                bool submitRecruitWithEnter = GUI.GetNameOfFocusedControl() == "RecruitAmount"
                    && currentEvent.type == EventType.KeyDown
                    && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);

                GUI.enabled = canRecruit;
                if (GUILayout.Button("Recruit", GUILayout.Height(25)) || (submitRecruitWithEnter && canRecruit))
                {
                    nationSimulator.TryRecruit(contextCity, amount);
                    if (submitRecruitWithEnter)
                    {
                        currentEvent.Use();
                        GUI.FocusControl(null);
                    }
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label($"Recruit any amount at {nationSimulator.recruitCost:F0} gold per soldier.");

                GUILayout.BeginHorizontal();
                GUI.enabled = contextCity.armyCount > 0;
                if (GUILayout.Button("Disband 1", GUILayout.Height(22)))
                {
                    nationSimulator.Disband(contextCity, 1);
                }

                GUI.enabled = contextCity.armyCount >= 5;
                if (GUILayout.Button("Disband 5", GUILayout.Height(22)))
                {
                    nationSimulator.Disband(contextCity, 5);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // Check if too close to an existing city
            bool tooClose = false;
            for (int i = 0; i < contextNation.cities.Count; i++)
            {
                if (Vector2Int.Distance(contextNation.cities[i].position, contextCellPos) < minCitySpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            bool canAfford = contextNation.treasury >= buildCityCost;

            GUILayout.Space(5);
            GUILayout.Label("<b>City Construction</b>");

            if (tooClose)
            {
                GUILayout.Label("<color=#FF7777>Too close to existing city!</color>");
            }
            else if (!canAfford)
            {
                GUILayout.Label($"<color=#FF7777>Need {buildCityCost:F0} gold (Short: {buildCityCost - contextNation.treasury:F0})</color>");
            }

            GUI.enabled = canAfford && !tooClose;
            if (GUILayout.Button($"🏰 Build City ({buildCityCost:F0}g)", GUILayout.Height(30)))
            {
                BuildCityAt(contextNation, contextCellPos);
                showContextMenu = false;
            }
            GUI.enabled = true;

            if (GUILayout.Button("Cancel", GUILayout.Height(22)))
            {
                showContextMenu = false;
            }

            GUILayout.EndArea();
        }

        private void DrawTroopTransferDialog()
        {
            if (transferOrigin == null || transferDestination == null)
            {
                showTransferDialog = false;
                return;
            }

            const float width = 310f;
            const float height = 190f;
            float x = (Screen.width - width) * 0.5f;
            float y = (Screen.height - height) * 0.5f;

            GUI.Box(new Rect(x, y, width, height), "⚔️ Move Troops");
            GUILayout.BeginArea(new Rect(x + 15, y + 28, width - 30, height - 38));
            GUILayout.Label($"From: <b>{transferOrigin.name}</b> ({transferOrigin.armyCount} soldiers)");
            GUILayout.Label($"To: <b>{transferDestination.name}</b> ({transferDestination.armyCount} soldiers)");
            GUILayout.Space(8);
            GUILayout.Label("Percentage of the origin garrison to move:");
            GUI.SetNextControlName("TransferPercentage");
            transferPercentage = GUILayout.TextField(transferPercentage, 3, GUILayout.Width(55));

            bool validPercentage = float.TryParse(transferPercentage, out float percentage)
                && percentage > 0f
                && percentage <= 100f
                && transferOrigin.armyCount > 0;
            Event currentEvent = Event.current;
            bool submitMoveWithEnter = GUI.GetNameOfFocusedControl() == "TransferPercentage"
                && currentEvent.type == EventType.KeyDown
                && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);

            GUILayout.BeginHorizontal();
            GUI.enabled = validPercentage;
            if (GUILayout.Button("Move Troops", GUILayout.Height(28)) || (submitMoveWithEnter && validPercentage))
            {
                if (nationSimulator.MoveTroops(transferOrigin, transferDestination, percentage))
                {
                    commandMessage = $"Moved {percentage:F0}% from {transferOrigin.name} to {transferDestination.name}.";
                    showTransferDialog = false;
                    if (submitMoveWithEnter)
                    {
                        currentEvent.Use();
                        GUI.FocusControl(null);
                    }
                }
            }
            GUI.enabled = true;

            if (GUILayout.Button("Cancel", GUILayout.Height(28), GUILayout.Width(80)))
            {
                showTransferDialog = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void BuildCityAt(Nation nation, Vector2Int pos)
        {
            nation.treasury -= buildCityCost;

            string[] suffixes = { "ton", "burg", "polis", "ford", "grad", "haven", "port", "gate", "keep", "stead" };
            string cityName = $"{nation.name}{suffixes[nation.cities.Count % suffixes.Length]}";

            City newCity = new City(nation.cities.Count, cityName, nation.id, pos, false, 5f);
            nation.cities.Add(newCity);

            if (worldRenderer != null)
            {
                worldRenderer.DrawCityMarker(newCity, worldGenerator.width, worldGenerator.height);
                worldRenderer.ApplyTextureChanges();
            }

            Debug.Log($"Built city '{cityName}' for {nation.name} at ({pos.x}, {pos.y}). Remaining Treasury: {nation.treasury:F1}");
        }

        private void DrawRightPanel()
        {
            int panelWidth = 270;
            int rightMargin = 15;

            if (hoveredNation != null)
            {
                int cardHeight = 340;
                Rect cardRect = new Rect(Screen.width - panelWidth - rightMargin, 15, panelWidth, cardHeight);
                GUI.Box(cardRect, "📊 State Overview");

                GUILayout.BeginArea(new Rect(cardRect.x + 10, cardRect.y + 30, panelWidth - 20, cardHeight - 40));

                Color orig = GUI.color;
                GUI.color = hoveredNation.color;
                GUILayout.Label($"<size=15><b>■ {hoveredNation.name}</b></size>");
                GUI.color = orig;

                GUILayout.Label($"<b>Territory:</b> {hoveredNation.territorySize:N0} pixels");
                GUILayout.Label($"<b>Cities:</b> {hoveredNation.cities.Count} (Capital: {hoveredNation.capital.x}, {hoveredNation.capital.y})");

                GUILayout.Space(6);
                GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
                GUILayout.Label("<b>💰 ECONOMY</b>");

                GUILayout.Label($"• <b>Treasury:</b> <color=#FFD700>{hoveredNation.treasury:F1} gold</color>");
                GUILayout.Label($"• <b>Taxes (Land):</b> +{hoveredNation.incomePerSec:F1} / sec");
                GUILayout.Label($"• <b>Military Upkeep:</b> -{hoveredNation.upkeepPerSec:F1} / sec");

                float net = hoveredNation.netIncomePerSec;
                string netColor = net >= 0 ? "#55FF55" : "#FF5555";
                string netPrefix = net >= 0 ? "+" : "";
                GUILayout.Label($"• <b>Net Cashflow:</b> <color={netColor}><b>{netPrefix}{net:F1} / sec</b></color>");

                GUILayout.Space(6);
                GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
                GUILayout.Label("<b>⚔️ MILITARY</b>");

                GUILayout.Label($"• <b>Army:</b> {hoveredNation.armyCount} / {hoveredNation.maxArmyTarget} soldiers");
                string armyStatus = hoveredNation.armyCount >= hoveredNation.maxArmyTarget
                    ? "<color=#88FF88>At army cap</color>"
                    : "<color=#FFFF55>Manual recruitment</color>";
                GUILayout.Label($"• <b>Status:</b> {armyStatus}");

                GUILayout.EndArea();

                DrawMiniLeaderboard(panelWidth, rightMargin, 15 + cardHeight + 10);
            }
            else
            {
                DrawFullLeaderboard(panelWidth, rightMargin, 15);
            }
        }

        private void DrawFullLeaderboard(int listWidth, int rightMargin, int topMargin)
        {
            if (worldGenerator.Nations == null || worldGenerator.Nations.Count == 0) return;

            sortedNations.Clear();
            sortedNations.AddRange(worldGenerator.Nations);
            sortedNations.Sort((a, b) => b.territorySize.CompareTo(a.territorySize));

            int maxDisplay = Mathf.Min(12, sortedNations.Count);
            int boxHeight = 45 + maxDisplay * 24;

            GUI.Box(new Rect(Screen.width - listWidth - rightMargin, topMargin, listWidth, boxHeight), "🏆 Nations Leaderboard");

            GUILayout.BeginArea(new Rect(Screen.width - listWidth - rightMargin + 10, topMargin + 30, listWidth - 20, maxDisplay * 24));
            for (int i = 0; i < maxDisplay; i++)
            {
                var n = sortedNations[i];
                Color orig = GUI.color;
                GUI.color = n.color;
                GUILayout.Label($"#{i + 1} {n.name}: {n.territorySize:N0} px | 🏰{n.cities.Count} | 🪙{n.treasury:F0}");
                GUI.color = orig;
            }
            GUILayout.EndArea();
        }

        private void DrawMiniLeaderboard(int listWidth, int rightMargin, int topMargin)
        {
            if (worldGenerator.Nations == null || worldGenerator.Nations.Count == 0) return;

            sortedNations.Clear();
            sortedNations.AddRange(worldGenerator.Nations);
            sortedNations.Sort((a, b) => b.territorySize.CompareTo(a.territorySize));

            int maxDisplay = Mathf.Min(6, sortedNations.Count);
            int boxHeight = 45 + maxDisplay * 22;

            GUI.Box(new Rect(Screen.width - listWidth - rightMargin, topMargin, listWidth, boxHeight), "🏆 Top Nations");

            GUILayout.BeginArea(new Rect(Screen.width - listWidth - rightMargin + 10, topMargin + 25, listWidth - 20, maxDisplay * 22));
            for (int i = 0; i < maxDisplay; i++)
            {
                var n = sortedNations[i];
                Color orig = GUI.color;
                GUI.color = n.color;
                GUILayout.Label($"#{i + 1} {n.name}: {n.territorySize:N0} px | 🏰{n.cities.Count} | 🪙{n.treasury:F0}");
                GUI.color = orig;
            }
            GUILayout.EndArea();
        }
    }
}
