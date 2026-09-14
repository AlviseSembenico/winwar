using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgesOfConflict
{
    /// <summary>Coordinates world generation, territorial expansion, city building, and UI.</summary>
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

        [Header("War Settings")]
        [Tooltip("Maximum number of border cells a war may capture per second.")]
        public float maximumWarAdvanceSpeed = 30f;

        [Tooltip("Treasury-strength advantage required to reach the maximum advance speed.")]
        public float strengthDeltaForMaximumSpeed = 20f;

        [Tooltip("Number of flag pairs per map cell along an active border.")]
        [Range(0.02f, 0.12f)]
        public float warFlagsPerBorderCell = 0.08f;

        [Header("Simulation State")]
        public bool isRunning = true;

        [Range(0.01f, 0.5f)]
        public float tickInterval = 0.04f;
        public int speedMultiplier = 1;

        [Tooltip("The selected nation expands twice as often as normal.")]
        public bool selectedNationGrowsFaster = true;

        [Tooltip("Prevents selecting a different nation after taking control of one.")]
        public bool lockControlledNation;

        [Header("Runtime Info")]
        [SerializeField]
        private int currentSeed;

        [SerializeField]
        private int activeNations;

        private Camera mainCam;
        private float tickTimer;
        private readonly List<Nation> sortedNations = new List<Nation>();
        private Nation hoveredNation;
        private Nation selectedNation;
        private City selectedCity;
        private bool showContextMenu;
        private Vector2 contextMenuScreenPos;
        private Vector2Int contextCellPos;
        private Nation contextNation;
        private City contextCity;
        private string commandMessage;
        private string attackPercentage = "10";
        private readonly List<War> wars = new List<War>();

        public class War
        {
            public int attackerId;
            public int defenderId;
            public float casualtyProgress;
            public float attackingPercentage;
            public readonly List<WarFlag> flags = new List<WarFlag>();
        }

        // Flags are persistent objects: their positions animate independently toward a new front.
        public class WarFlag
        {
            public int nationId;
            public Vector2 position;
            public Color color;
        }

        private struct FlagTarget
        {
            public int nationId;
            public Vector2 position;
            public Color color;
        }

        private void Start()
        {
            mainCam = Camera.main;
            worldGenerator ??= GetComponent<WorldGenerator>() ?? gameObject.AddComponent<WorldGenerator>();
            worldRenderer ??= GetComponent<WorldRenderer>() ?? gameObject.AddComponent<WorldRenderer>();
            nationSimulator ??= GetComponent<NationSimulator>() ?? gameObject.AddComponent<NationSimulator>();
            if (cameraController == null && mainCam != null)
                cameraController =
                    mainCam.GetComponent<CameraController>() ?? mainCam.gameObject.AddComponent<CameraController>();
            if (cameraController != null)
            {
                cameraController.OnRightClickTap += HandleRightClickTap;
                cameraController.OnLeftClickTap += HandleNationSelection;
            }
            Regenerate();
        }

        private void OnDestroy()
        {
            if (cameraController == null)
                return;
            cameraController.OnRightClickTap -= HandleRightClickTap;
            cameraController.OnLeftClickTap -= HandleNationSelection;
        }

        private void HandleNationSelection(Vector3 worldPos)
        {
            if (IsPointerOverNationPanel())
                return;
            if (!TryGetCell(worldPos, out Cell cell))
                return;
            if (!cell.HasOwner || cell.nationId >= worldGenerator.Nations.Count)
                return;
            if (selectedNation != null && lockControlledNation && cell.nationId != selectedNation.id)
            {
                commandMessage = $"Control is locked to {selectedNation.name}.";
                return;
            }
            bool selectingFirstNation = selectedNation == null;
            selectedNation = worldGenerator.Nations[cell.nationId];
            if (selectingFirstNation)
                lockControlledNation = true;
            selectedCity = FindCityAt(worldPos);
            worldRenderer.SetSelectedNation(selectedNation.id);
            worldRenderer.SetSelectedCity(selectedCity);
            commandMessage = $"Now controlling {selectedNation.name}.";
        }

        private void HandleRightClickTap(Vector3 worldPos)
        {
            if (IsPointerOverNationPanel())
                return;
            if (!TryGetCell(worldPos, out Cell cell) || !cell.HasOwner || cell.nationId >= worldGenerator.Nations.Count)
            {
                showContextMenu = false;
                return;
            }
            contextNation = worldGenerator.Nations[cell.nationId];
            contextCity = FindCityAt(worldPos);
            contextCellPos = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
            if (selectedNation == null)
            {
                commandMessage = "Select a nation with a left-click before issuing commands.";
                showContextMenu = false;
                return;
            }
            if (selectedNation.id != contextNation.id)
            {
                ShowAttackMenu();
                return;
            }
            Vector3 mouse = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 pos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mouse = new Vector3(pos.x, pos.y);
            }
#endif
            contextMenuScreenPos = new Vector2(mouse.x, Screen.height - mouse.y);
            showContextMenu = true;
        }

        private bool TryGetCell(Vector3 worldPos, out Cell cell)
        {
            cell = default;
            if (worldGenerator == null || worldGenerator.Grid == null)
                return false;
            int x = Mathf.FloorToInt(worldPos.x),
                y = Mathf.FloorToInt(worldPos.y);
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return false;
            cell = worldGenerator.Grid[y * worldGenerator.width + x];
            return cell.IsLand;
        }

        private City FindCityAt(Vector3 worldPos)
        {
            if (worldGenerator?.Nations == null)
                return null;
            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            foreach (Nation nation in worldGenerator.Nations)
            foreach (City city in nation.cities)
                if (Vector2.Distance(point, city.position) <= (city.isCapital ? 4.5f : 3.5f))
                    return city;
            return null;
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateHoveredNation();
            if (isRunning && nationSimulator != null)
            {
                tickTimer += Time.deltaTime * speedMultiplier;
                while (tickTimer >= tickInterval)
                {
                    tickTimer -= tickInterval;
                    ConfigureSelectedNationExpansionBoost();
                    nationSimulator.StepSimulation(tickInterval);
                    AdvanceWars(tickInterval);
                }
            }
        }

        private void ConfigureSelectedNationExpansionBoost()
        {
            if (nationSimulator == null)
                return;
            nationSimulator.expansionBoostNationId =
                selectedNationGrowsFaster && selectedNation != null ? selectedNation.id : -1;
            nationSimulator.expansionBoostMultiplier = 2;
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                selectedCity = null;
                showContextMenu = false;
                worldRenderer?.SetSelectedCity(null);
            }
            if (Input.GetKeyDown(KeyCode.Space))
                isRunning = !isRunning;
            if (Input.GetKeyDown(KeyCode.S) && !isRunning)
            {
                ConfigureSelectedNationExpansionBoost();
                nationSimulator?.StepSimulation(tickInterval);
                AdvanceWars(tickInterval);
            }
            if (Input.GetKeyDown(KeyCode.R))
                Regenerate();
            if (Input.GetKeyDown(KeyCode.F))
                cameraController?.FocusOnMap(worldGenerator.width, worldGenerator.height);
            if (Input.GetKeyDown(KeyCode.Alpha1))
                speedMultiplier = 1;
            if (Input.GetKeyDown(KeyCode.Alpha2))
                speedMultiplier = 2;
            if (Input.GetKeyDown(KeyCode.Alpha3))
                speedMultiplier = 5;
            if (Input.GetKeyDown(KeyCode.Alpha4))
                speedMultiplier = 10;
        }

        private void UpdateHoveredNation()
        {
            if (mainCam == null || worldGenerator?.Grid == null)
                return;
            Vector3 point = mainCam.ScreenToWorldPoint(Input.mousePosition);
            if (TryGetCell(point, out Cell cell) && cell.HasOwner && cell.nationId < worldGenerator.Nations.Count)
                hoveredNation = worldGenerator.Nations[cell.nationId];
            else
                hoveredNation = null;
        }

        [ContextMenu("Regenerate World")]
        public void Regenerate()
        {
            if (worldGenerator == null || worldRenderer == null)
                return;
            showContextMenu = false;
            selectedNation = null;
            selectedCity = null;
            commandMessage = null;
            wars.Clear();
            worldGenerator.GenerateWorld();
            currentSeed = worldGenerator.seed;
            activeNations = worldGenerator.Nations.Count;
            worldRenderer.SetSelectedNation(-1);
            worldRenderer.SetSelectedCity(null);
            worldRenderer.RenderWorld(
                worldGenerator.Grid,
                worldGenerator.Nations,
                worldGenerator.width,
                worldGenerator.height
            );
            RefreshWarBorderHighlight();
            nationSimulator?.Initialize(
                worldGenerator.Grid,
                worldGenerator.Nations,
                worldGenerator.width,
                worldGenerator.height,
                worldRenderer
            );
            cameraController?.FocusOnMap(worldGenerator.width, worldGenerator.height);
            tickTimer = 0f;
        }

        private void OnGUI()
        {
            if (worldGenerator == null)
                return;
            DrawControlPanel();
            DrawRightPanel();
            if (showContextMenu)
                DrawCityContextMenu();
            DrawWarFronts();
            if (!string.IsNullOrEmpty(commandMessage))
                GUI.Box(new Rect(Screen.width / 2f - 190, 15, 380, 30), commandMessage);
        }

        private void DrawControlPanel()
        {
            GUI.Box(new Rect(15, 15, 280, 365), "Ages of Conflict - Simulation");
            GUILayout.BeginArea(new Rect(25, 40, 260, 330));
            GUILayout.Label($"<b>Resolution:</b> {worldGenerator.width} x {worldGenerator.height}");
            GUILayout.Label($"<b>Seed:</b> {currentSeed} | <b>Nations:</b> {activeNations}");
            GUILayout.Label(
                $"<b>Controlling:</b> {(selectedNation == null ? "None (left-click a nation)" : selectedNation.name)}"
            );
            selectedNationGrowsFaster = GUILayout.Toggle(
                selectedNationGrowsFaster,
                "Selected state grows 2× faster",
                GUILayout.Height(26)
            );
            GUI.enabled = selectedNation != null;
            lockControlledNation = GUILayout.Toggle(
                lockControlledNation,
                "Lock controlled nation",
                GUILayout.Height(26)
            );
            GUI.enabled = true;
            if (nationSimulator != null)
                GUILayout.Label($"<b>Colonization:</b> {nationSimulator.ColonizedPercentage:F1}% Claimed");
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isRunning ? "⏸ Pause [Space]" : "▶ Play [Space]", GUILayout.Height(30)))
                isRunning = !isRunning;
            GUI.enabled = !isRunning;
            if (GUILayout.Button("Step [S]", GUILayout.Height(30), GUILayout.Width(75)))
            {
                ConfigureSelectedNationExpansionBoost();
                nationSimulator?.StepSimulation(tickInterval);
                AdvanceWars(tickInterval);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("🔄 Regenerate Map [R]", GUILayout.Height(28)))
                Regenerate();
            if (GUILayout.Button("🎯 Reset Camera [F]", GUILayout.Height(24)))
                cameraController?.FocusOnMap(worldGenerator.width, worldGenerator.height);
            GUILayout.Space(5);
            GUILayout.Label("• <b>Scroll</b>: Zoom | <b>Arrow keys/MMB</b>: Pan");
            GUILayout.Label("• <b>LMB</b>: Select nation or city");
            GUILayout.Label("• <b>RMB</b>: Build cities in selected territory");
            GUILayout.Label("• Territory expands automatically");
            GUILayout.EndArea();
        }

        private void ShowAttackMenu()
        {
            Vector3 mouse = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 position = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mouse = new Vector3(position.x, position.y);
            }
#endif
            contextMenuScreenPos = new Vector2(mouse.x, Screen.height - mouse.y);
            showContextMenu = true;
        }

        private void StartWar(float percentage)
        {
            if (selectedNation == null || contextNation == null || selectedNation == contextNation)
                return;
            foreach (War war in wars)
                if (
                    (war.attackerId == selectedNation.id && war.defenderId == contextNation.id)
                    || (war.attackerId == contextNation.id && war.defenderId == selectedNation.id)
                )
                {
                    commandMessage = $"{selectedNation.name} is already at war with {contextNation.name}.";
                    return;
                }
            float committedForce = selectedNation.armyPopulation * percentage / 100f;
            if (committedForce < 1f)
            {
                commandMessage = "Commit at least one soldier to attack. Increase the army allocation if needed.";
                return;
            }
            War newWar = new War
            {
                attackerId = selectedNation.id,
                defenderId = contextNation.id,
                attackingPercentage = percentage,
            };
            wars.Add(newWar);
            RefreshWarFlags(newWar);
            RefreshWarBorderHighlight();
            commandMessage = HaveSharedBorder(selectedNation.id, contextNation.id)
                ? $"{selectedNation.name} attacked {contextNation.name}."
                : $"War declared. The front will form when the nations share a border.";
        }

        private void UpdateWarUI(War war)
        {
            // A declared war remains active while expanding nations are still separated.
            // Hide its old formation until a new shared border exists.
            if (!HaveSharedBorder(war.attackerId, war.defenderId))
            {
                war.flags.Clear();
                return;
            }
            bool hadVisibleFront = war.flags.Count > 0;
            RefreshWarFlags(war);
            if (!hadVisibleFront)
                RefreshWarBorderHighlight();
        }

        private void RemoveWar(War war)
        {
            wars.Remove(war);
            // Troops remain part of the population; ending a war frees them for defense.
        }

        private void AdvanceWars(float deltaTime)
        {
            bool warsChanged = false;
            for (int i = wars.Count - 1; i >= 0; i--)
            {
                War war = wars[i];
                UpdateWarUI(war);

                ApplyWarCasualties(war, deltaTime);
                float attackingForce = GetAttackingForce(war);
                float defendingForce = GetDefendingForce(war);
                // remove war if the
                if (attackingForce < 1f)
                {
                    RemoveWar(war);
                    warsChanged = true;
                    continue;
                }

                // An assault only advances with at least a 50% force advantage.
                if (attackingForce < defendingForce * 1.5f)
                    continue;
                float advantage = attackingForce / Mathf.Max(1f, defendingForce) - 1.5f;
                float speed = Mathf.Min(maximumWarAdvanceSpeed, maximumWarAdvanceSpeed * advantage);
                float toCapture = speed * deltaTime;
                int cellsToCapture = Mathf.FloorToInt(toCapture);

                List<int> border = FindDefenderBorderCells(war.attackerId, war.defenderId);
                if (border.Count == 0)
                {
                    RemoveWar(war);
                    warsChanged = true;
                    continue;
                }
                CaptureCells(war, border, cellsToCapture);
            }
            if (warsChanged)
                RefreshWarBorderHighlight();
        }

        // the border is of defender
        private void CaptureCells(War war, List<int> border, int amount)
        {
            amount = Mathf.Clamp(amount, 0, border.Count);
            if (amount == 0)
                return;

            // conquer the amount that are the further from the capital of the defending team.
            Nation defender = worldGenerator.Nations[war.defenderId];
            Nation attacker = worldGenerator.Nations[war.attackerId];
            var defenderCapital = defender.capital;
            var attackCapital = attacker.capital;
            // map border to their distance to the defender capital
            var distances = border
                .Select(index => (worldGenerator.IndexToVec2(index) - defenderCapital).sqrMagnitude)
                .ToList();
            // Calculate the average distance
            float average = (float)distances.Average();
            // Jitter must be baked into the key once per cell; drawing it inside a
            // comparator makes comparisons inconsistent and Sort throws.
            border = border
                .OrderByDescending(index =>
                    (worldGenerator.IndexToVec2(index) - defenderCapital).sqrMagnitude + Random.Range(0, average)
                )
                .ToList();
            for (int i = 0; i < amount; i++)
            {
                worldGenerator.Grid[border[i]].nationId = (short)war.attackerId;
                worldGenerator.Nations[war.attackerId].territorySize++;
                defender.territorySize--;
                // Otherwise the next border rebuild can paint these cells with
                // the former owner's colour again.
                defender.border.Remove(border[i]);
                defender.frontier.Remove(border[i]);
            }
            // Reuse the existing renderer so neighbouring borders and city markers
            // are refreshed too, rather than just painting over the captured pixels.
            worldRenderer?.RenderWorld(
                worldGenerator.Grid,
                worldGenerator.Nations,
                worldGenerator.width,
                worldGenerator.height
            );
        }

        private void RefreshWarBorderHighlight()
        {
            if (worldRenderer == null)
                return;
            List<Vector2Int> nationPairs = new List<Vector2Int>(wars.Count);
            foreach (War war in wars)
                nationPairs.Add(new Vector2Int(war.attackerId, war.defenderId));
            worldRenderer.SetActiveWarBorders(nationPairs);
        }

        private void ApplyWarCasualties(War war, float deltaTime)
        {
            war.casualtyProgress += deltaTime;
            while (war.casualtyProgress >= 1f)
            {
                war.casualtyProgress -= 1f;
                float attackingForce = GetAttackingForce(war);
                float attackerLosses = attackingForce * .01f;
                // Defenders lose one third of the attacker's *casualties*, not one third
                // of the entire attacking force each second.
                float defenderLosses = attackerLosses / 3f;
                Nation attacker = worldGenerator.Nations[war.attackerId];
                Nation defender = worldGenerator.Nations[war.defenderId];
                attackerLosses = Mathf.Min(attackerLosses, attackingForce, attacker.population);
                defenderLosses = Mathf.Min(defenderLosses, GetDefendingForce(war), defender.population);
                attacker.population -= attackerLosses;
                defender.population -= defenderLosses;
            }
        }

        // The committed percentage is a share of the army, so its force follows
        // population growth, casualties, and changes to the nation's army allocation.
        private float GetAttackingForce(War war)
        {
            Nation attacker = worldGenerator.Nations[war.attackerId];
            return attacker.armyPopulation * Mathf.Clamp(war.attackingPercentage, 0f, 100f) / 100f;
        }

        private float GetDefendingForce(War war)
        {
            Nation defender = worldGenerator.Nations[war.defenderId];
            float availableDefenders = defender.armyPopulation;
            foreach (War outgoingWar in wars)
                if (outgoingWar.attackerId == defender.id)
                    availableDefenders -= GetAttackingForce(outgoingWar);
            availableDefenders = Mathf.Max(0f, availableDefenders);
            float totalIncomingForce = 0f;
            foreach (War incomingWar in wars)
                if (incomingWar.defenderId == defender.id)
                    totalIncomingForce += GetAttackingForce(incomingWar);
            return totalIncomingForce <= 0f ? 0f : availableDefenders * GetAttackingForce(war) / totalIncomingForce;
        }

        private bool HaveSharedBorder(int first, int second) => FindDefenderBorderCells(first, second).Count > 0;

        private List<int> FindDefenderBorderCells(int attacker, int defender)
        {
            List<int> result = new List<int>();
            Cell[] grid = worldGenerator.Grid;
            int width = worldGenerator.width,
                height = worldGenerator.height;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (grid[index].nationId != defender)
                    continue;
                if (grid.IsFrontierWith(x, y, width, height, attacker))
                    result.Add(index);
            }
            return SortAlongFront(result);
        }

        private static readonly int[] frontDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] frontDy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// Orders front cells along the line: the walk starts anywhere and follows the two
        /// directions out of that cell, one growing the back of the deque and the other its
        /// front, so the result runs end to end and its middle entry is the middle of the front.
        /// A front split into several disconnected stretches yields one walked run after another.
        /// </summary>
        private List<int> SortAlongFront(List<int> cells)
        {
            HashSet<int> remaining = new HashSet<int>(cells);
            List<int> ordered = new List<int>(cells.Count);
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                int seed = cells[i];
                if (!remaining.Remove(seed))
                    continue;

                LinkedList<int> line = new LinkedList<int>();
                line.AddLast(seed);
                for (
                    int current = FindNextFrontCell(remaining, seed);
                    current >= 0;
                    current = FindNextFrontCell(remaining, current)
                )
                {
                    remaining.Remove(current);
                    line.AddLast(current);
                }
                for (
                    int current = FindNextFrontCell(remaining, seed);
                    current >= 0;
                    current = FindNextFrontCell(remaining, current)
                )
                {
                    remaining.Remove(current);
                    line.AddFirst(current);
                }
                ordered.AddRange(line);
            }
            return ordered;
        }

        private int FindNextFrontCell(HashSet<int> remaining, int index)
        {
            for (int d = 0; d < 8; d++)
                if (TryGetFrontNeighbour(remaining, index, d, out int neighbour))
                    return neighbour;
            return -1;
        }

        // Orthogonal directions come first, so the walk only steps diagonally when the line does.
        private bool TryGetFrontNeighbour(HashSet<int> remaining, int index, int direction, out int neighbour)
        {
            neighbour = -1;
            int x = index % worldGenerator.width + frontDx[direction];
            int y = index / worldGenerator.width + frontDy[direction];
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return false;
            int candidate = y * worldGenerator.width + x;
            if (!remaining.Contains(candidate))
                return false;
            neighbour = candidate;
            return true;
        }

        private static void Shuffle(List<int> values)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private void RefreshWarFlags(War war)
        {
            List<int> border = FindDefenderBorderCells(war.attackerId, war.defenderId);
            // Clamp old serialized inspector values from earlier iterations as well.
            float flagDensity = Mathf.Clamp(warFlagsPerBorderCell, 0.02f, 0.12f);
            int borderStride = Mathf.Max(1, Mathf.RoundToInt(1f / flagDensity));
            List<FlagTarget> targets = new List<FlagTarget>();
            int[] dx =  { 0, 0, 1, -1 },
                dy =  { 1, -1, 0, 0 };
            for (int i = 0; i < border.Count; i += borderStride)
            {
                int index = border[i];
                int x = index % worldGenerator.width,
                    y = index / worldGenerator.width;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d],
                        ny = y + dy[d];
                    if (
                        nx < 0
                        || nx >= worldGenerator.width
                        || ny < 0
                        || ny >= worldGenerator.height
                        || worldGenerator.Grid[ny * worldGenerator.width + nx].nationId != war.attackerId
                    )
                        continue;
                    // Place each flag just behind its side of the front, rather than
                    // on the black border pixels themselves.
                    targets.Add(
                        new FlagTarget
                        {
                            nationId = war.attackerId,
                            position = GetSafeBorderInset(
                                new Vector2(nx + .5f, ny + .5f),
                                new Vector2(-dx[d], -dy[d]),
                                war.attackerId,
                                2
                            ),
                            color = worldGenerator.Nations[war.attackerId].color,
                        }
                    );
                    targets.Add(
                        new FlagTarget
                        {
                            nationId = war.defenderId,
                            position = GetSafeBorderInset(
                                new Vector2(x + .5f, y + .5f),
                                new Vector2(dx[d], dy[d]),
                                war.defenderId,
                                2
                            ),
                            color = worldGenerator.Nations[war.defenderId].color,
                        }
                    );
                    break;
                }
            }

            // Match every new front position to the nearest existing same-side flag.
            // This preserves flag identity and prevents reshuffling/flickering on border updates.
            bool[] used = new bool[war.flags.Count];
            foreach (FlagTarget target in targets)
            {
                int closest = -1;
                float closestDistance = float.MaxValue;
                for (int i = 0; i < war.flags.Count; i++)
                {
                    if (used[i] || war.flags[i].nationId != target.nationId)
                        continue;
                    float distance = (war.flags[i].position - target.position).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closest = i;
                        closestDistance = distance;
                    }
                }
                if (closest < 0)
                {
                    war.flags.Add(
                        new WarFlag
                        {
                            nationId = target.nationId,
                            position = target.position,
                            color = target.color,
                        }
                    );
                    System.Array.Resize(ref used, war.flags.Count);
                    used[war.flags.Count - 1] = true;
                }
                else
                {
                    WarFlag flag = war.flags[closest];
                    // Flags are markers, not units. Teleport them to a new border
                    // position rather than making them travel through either nation.
                    flag.position = target.position;
                    flag.color = target.color;
                    used[closest] = true;
                }
            }
            for (int i = war.flags.Count - 1; i >= 0; i--)
                if (!used[i])
                    war.flags.RemoveAt(i);
        }

        private void DrawWarFronts()
        {
            if (mainCam == null)
                return;
            foreach (War war in wars)
            {
                // Labels derive directly from the current border, never from a flag.
                if (TryGetWarLabelPositions(war, out Vector2 attackerLabel, out Vector2 defenderLabel))
                {
                    DrawWarForceLabel(
                        attackerLabel,
                        GetAttackingForce(war),
                        worldGenerator.Nations[war.attackerId].color
                    );
                    DrawWarForceLabel(
                        defenderLabel,
                        GetDefendingForce(war),
                        worldGenerator.Nations[war.defenderId].color
                    );
                }
            }
        }

        private bool TryGetWarLabelPositions(War war, out Vector2 attackerLabel, out Vector2 defenderLabel)
        {
            attackerLabel = Vector2.zero;
            defenderLabel = Vector2.zero;
            List<int> border = FindDefenderBorderCells(war.attackerId, war.defenderId);
            if (border.Count == 0)
                return false;

            // The front is sorted end to end, so its middle entry is the middle of the front
            // and the entries around it are its neighbours on the map.
            int index = border.Count / 2;
            int delta = Mathf.Min(5, index, border.Count - 1 - index);
            if (delta == 0)
                return false;
            double[] X = new double[2 * delta + 1];
            double[] Y = new double[2 * delta + 1];
            for (int i = -delta; i <= delta; i++)
            {
                int cell = border[index + i];
                (int x, int y) = worldGenerator.IndexToCoord(cell);
                X[i + delta] = x;
                Y[i + delta] = y;
            }
            var (intercept, slope) = MathNet.Numerics.Fit.Line(X, Y);

            // A perfectly vertical front has no finite slope, but it still runs straight up.
            Vector2 front =
                double.IsNaN(slope) || double.IsInfinity(slope) ? Vector2.up : new Vector2(1f, (float)slope).normalized;
            Vector2 center = new Vector2((float)X[delta] + .5f, (float)Y[delta] + .5f);

            Vector2 normal = new Vector2(-front.y, front.x);
            if (GetOwnerAt(center + normal * 2f) != war.attackerId)
                normal = -normal;
            if (GetOwnerAt(center + normal * 2f) != war.attackerId)
                return false;

            attackerLabel = GetSafeBorderInset(center + normal * 2f, normal, war.attackerId, 6);
            defenderLabel = GetSafeBorderInset(center, -normal, war.defenderId, 8);
            return true;
        }

        private int GetOwnerAt(Vector2 position)
        {
            int x = Mathf.FloorToInt(position.x),
                y = Mathf.FloorToInt(position.y);
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return -1;
            return worldGenerator.Grid[y * worldGenerator.width + x].nationId;
        }

        // Move inward only as far as requested, stopping before leaving the owner.
        private Vector2 GetSafeBorderInset(Vector2 start, Vector2 step, int nationId, int cellsInward)
        {
            Vector2 position = start;
            for (int distance = 0; distance < cellsInward; distance++)
            {
                Vector2 next = position + step;
                if (GetOwnerAt(next) != nationId)
                    break;
                position = next;
            }
            return position;
        }

        private GUIStyle forceLabelStyle;

        private void DrawWarForceLabel(Vector2 worldPosition, float force, Color color)
        {
            Vector3 screen = mainCam.WorldToScreenPoint(new Vector3(worldPosition.x, worldPosition.y, 0f));
            if (screen.z < 0)
                return;

            if (forceLabelStyle == null)
            {
                forceLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 14,
                };
            }

            string text = $"{force:F0}";
            Vector2 size = forceLabelStyle.CalcSize(new GUIContent(text));
            float padX = 8f,
                padY = 4f;
            float w = size.x + padX * 2;
            float h = size.y + padY * 2;
            Rect bg = new Rect(screen.x - w / 2f, Screen.height - screen.y - h / 2f, w, h);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Box(bg, string.Empty);
            GUI.color = Color.white;
            forceLabelStyle.normal.textColor = Color.white;
            GUI.Label(bg, text, forceLabelStyle);
            GUI.color = previous;
        }

        private void DrawCityContextMenu()
        {
            if (contextNation != null && selectedNation != null && contextNation.id != selectedNation.id)
            {
                const float warWidth = 250,
                    warHeight = 140;
                float warX = Mathf.Clamp(contextMenuScreenPos.x, 10, Screen.width - warWidth - 10),
                    warY = Mathf.Clamp(contextMenuScreenPos.y, 10, Screen.height - warHeight - 10);
                GUI.Box(new Rect(warX, warY, warWidth, warHeight), "War Declaration");
                GUILayout.BeginArea(new Rect(warX + 10, warY + 25, warWidth - 20, warHeight - 30));
                float strength = nationSimulator.ComputeStrength(contextNation);
                GUILayout.Label($"Attack <b>{contextNation.name}</b>?");
                GUILayout.Label($"Army: {selectedNation.armyPopulation:F0} | Enemy: {contextNation.armyPopulation:F0}");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Commit army:", GUILayout.Width(125));
                attackPercentage = GUILayout.TextField(attackPercentage, 3, GUILayout.Width(42));
                GUILayout.Label("%");
                GUILayout.EndHorizontal();
                bool validPercent =
                    float.TryParse(attackPercentage, out float parsedPercent)
                    && parsedPercent > 0f
                    && parsedPercent <= 100f;
                GUI.enabled = validPercent;
                if (GUILayout.Button("Attack", GUILayout.Height(30)))
                {
                    StartWar(parsedPercent);
                    showContextMenu = false;
                }
                GUI.enabled = true;
                if (GUILayout.Button("Cancel"))
                    showContextMenu = false;
                GUILayout.EndArea();
                return;
            }
            const float width = 230,
                height = 215;
            float x = Mathf.Clamp(contextMenuScreenPos.x, 10, Screen.width - width - 10),
                y = Mathf.Clamp(contextMenuScreenPos.y, 10, Screen.height - height - 10);
            GUI.Box(new Rect(x, y, width, height), "🏰 City Construction");
            GUILayout.BeginArea(new Rect(x + 10, y + 25, width - 20, height - 30));
            GUILayout.Label($"<b>{contextNation.name}</b>");
            GUILayout.Label($"Location: ({contextCellPos.x}, {contextCellPos.y})");
            GUILayout.Label($"Treasury: <color=#FFD700>{contextNation.treasury:F0} gold</color>");
            if (contextCity != null)
                GUILayout.Label($"Existing city: <b>{contextCity.name}</b>");
            bool tooClose = false;
            foreach (City city in contextNation.cities)
                if (Vector2Int.Distance(city.position, contextCellPos) < minCitySpacing)
                {
                    tooClose = true;
                    break;
                }
            bool canAfford = contextNation.treasury >= buildCityCost;
            if (tooClose)
                GUILayout.Label("<color=#FF7777>Too close to an existing city.</color>");
            else if (!canAfford)
                GUILayout.Label("<color=#FF7777>Insufficient gold.</color>");
            GUI.enabled = canAfford && !tooClose;
            if (GUILayout.Button($"Build City ({buildCityCost:F0}g)", GUILayout.Height(30)))
            {
                BuildCityAt(contextNation, contextCellPos);
                showContextMenu = false;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Cancel"))
                showContextMenu = false;
            GUILayout.EndArea();
        }

        private void BuildCityAt(Nation nation, Vector2Int position)
        {
            nation.treasury -= buildCityCost;
            string[] suffixes = { "ton", "burg", "polis", "ford", "grad", "haven", "port", "gate", "keep", "stead" };
            City city = new City(
                nation.cities.Count,
                $"{nation.name}{suffixes[nation.cities.Count % suffixes.Length]}",
                nation.id,
                position,
                false
            );
            nation.cities.Add(city);
            worldRenderer.DrawCityMarker(city, worldGenerator.width, worldGenerator.height);
            worldRenderer.ApplyTextureChanges();
        }

        private Rect GetNationPanelRect(Nation nation)
        {
            return new Rect(Screen.width - 285, 15, 270, 260 + nation.cities.Count * 22);
        }

        private bool IsPointerOverNationPanel()
        {
            Nation nation = selectedNation ?? hoveredNation;
            if (nation == null)
                return false;
            Vector3 mouse = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 position = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mouse = new Vector3(position.x, position.y);
            }
#endif
            return GetNationPanelRect(nation).Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }

        private void DrawRightPanel()
        {
            Nation nation = selectedNation ?? hoveredNation;
            if (nation == null)
            {
                DrawLeaderboard(12, 15);
                return;
            }
            Rect rect = GetNationPanelRect(nation);
            GUI.Box(rect, selectedNation != null ? "Controlled Nation" : "State Overview");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 30, rect.width - 20, rect.height - 40));
            GUILayout.Label($"<size=15><b>■ {nation.name}</b></size>");
            GUILayout.Label($"Territory: {nation.territorySize:N0} pixels");
            GUILayout.Label($"Cities: {nation.cities.Count}");
            GUILayout.Label($"Treasury: <color=#FFD700>{nation.treasury:F1} gold</color>");
            GUILayout.Label($"Population: {nation.population:F0}");
            GUILayout.Label($"Income: +{nation.incomePerSec:F1} / sec");
            GUILayout.Space(6);
            GUILayout.Label($"Population in army: {nation.armyPercentage:F0}%");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && selectedNation != null;
            float armyPercentage = GUILayout.HorizontalSlider(nation.armyPercentage, 0f, 100f);
            if (selectedNation != null)
                nation.armyPercentage = Mathf.Round(armyPercentage);
            GUI.enabled = wasEnabled;
            GUILayout.BeginHorizontal();
            GUILayout.Label("0%");
            GUILayout.FlexibleSpace();
            GUILayout.Label("100%");
            GUILayout.EndHorizontal();
            GUILayout.Label($"Army: {nation.armyPopulation:F0} soldiers");
            foreach (City city in nation.cities)
                GUILayout.Label($"{(city.isCapital ? "Capital" : "City")}: {city.name}");
            GUILayout.EndArea();
            int nextPanelTop = Mathf.CeilToInt(rect.yMax) + 15;
            int leaderboardTop = selectedNation == null ? nextPanelTop : DrawWarsPanel(nextPanelTop);
            DrawLeaderboard(6, leaderboardTop);
        }

        private int DrawWarsPanel(int top)
        {
            List<War> activeWars = new List<War>();
            foreach (War war in wars)
                if (war.attackerId == selectedNation.id || war.defenderId == selectedNation.id)
                    activeWars.Add(war);
            if (activeWars.Count == 0)
                return top;

            const int width = 270;
            int height = 40 + activeWars.Count * 48;
            float x = Screen.width - 285;
            GUI.Box(new Rect(x, top, width, height), "⚔ Nations at War");
            GUILayout.BeginArea(new Rect(x + 10, top + 25, width - 20, height - 30));
            foreach (War war in activeWars)
            {
                int opponentId = war.attackerId == selectedNation.id ? war.defenderId : war.attackerId;
                Nation opponent = worldGenerator.Nations[opponentId];
                GUILayout.BeginHorizontal();
                float force = war.attackerId == selectedNation.id ? GetAttackingForce(war) : GetDefendingForce(war);
                GUILayout.Label($"{opponent.name} ({force:F0})", GUILayout.Width(145));
                if (GUILayout.Button("Cancel war", GUILayout.Height(24)))
                {
                    wars.Remove(war);
                    RefreshWarBorderHighlight();
                    commandMessage = $"Peace declared with {opponent.name}.";
                    break;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
            return top + height + 10;
        }

        private void DrawLeaderboard(int max, int top)
        {
            if (worldGenerator.Nations == null)
                return;
            sortedNations.Clear();
            sortedNations.AddRange(worldGenerator.Nations);
            sortedNations.Sort((a, b) => b.territorySize.CompareTo(a.territorySize));
            int count = Mathf.Min(max, sortedNations.Count);
            GUI.Box(new Rect(Screen.width - 285, top, 270, 45 + count * 22), "🏆 Nations");
            GUILayout.BeginArea(new Rect(Screen.width - 275, top + 27, 250, count * 22));
            for (int i = 0; i < count; i++)
                GUILayout.Label(
                    $"#{i + 1} {sortedNations[i].name}: {sortedNations[i].territorySize:N0} px | 🏰{sortedNations[i].cities.Count}"
                );
            GUILayout.EndArea();
        }
    }
}
