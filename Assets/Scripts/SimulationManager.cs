using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        public float buildCityCost = 1000f;
        public int minCitySpacing = 6;

        [Header("War Settings")]
        [Tooltip("Maximum number of border cells a war may capture per second.")]
        public float maximumWarAdvanceSpeed = 300f;
        public float warArrowWeight = 10f;

        [Tooltip("Conquest-priority multiplier for cells disconnected from all defending cities.")]
        [Min(1f)]
        public float isolatedCellConquestMultiplier = 3f;

        [Tooltip("Treasury-strength advantage required to reach the maximum advance speed.")]
        public float strengthDeltaForMaximumSpeed = 20f;

        [Tooltip("Number of flag pairs per map cell along an active border.")]
        [Range(0.02f, 0.12f)]
        public float warFlagsPerBorderCell = 0.08f;

        [Header("Defensive Wall Settings")]
        [Tooltip("Gold charged for each unique map cell covered by a confirmed defensive wall.")]
        [Min(0f)]
        public float wallCostPerPixel = 10f;

        [Tooltip("Attacking soldiers lost when conquering a connected defensive-wall cell.")]
        [Min(0f)]
        public float wallBreachCasualtiesPerPixel = 10f;

        [Tooltip(
            "Rate at which casualties required to breach a wall cell disconnected from all defending cities decay."
        )]
        [Min(0f)]
        public float isolatedWallCostDecayPerSecond = 0.1f;

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
        private ConvolutionOperation conquestConvolution;
        private float tickTimer;
        private readonly List<Nation> sortedNations = new List<Nation>();
        private Nation hoveredNation;
        private Nation selectedNation;
        private City selectedCity;
        private bool showContextMenu;
        private Vector2 contextMenuGuiPos;
        private Vector2Int contextCellPos;
        private Nation contextNation;
        private City contextCity;
        private string commandMessage;
        private float attackPercentage = 10f;
        private readonly List<War> wars = new List<War>();
        private readonly List<AttackDirection> attackDirections = new List<AttackDirection>();
        private AttackDirection inProgressAttackDirection;
        private bool attackDirectionGestureCancelled;
        private readonly List<DefensiveWall> defensiveWalls = new List<DefensiveWall>();
        private float[] isolatedSince;
        private bool[] connectedToCity;
        private DefensiveWall inProgressDefensiveWall;
        private DefensiveWall pendingDefensiveWall;
        private bool defensiveWallGestureCancelled;
        private float attackDirectionBannerExpiry;
        private string attackDirectionBanner;
        private bool warRoutesDirty;
        private Rect warsPanelRect;
        private Vector2 warsScrollPosition;
        private Vector2 controlPanelScrollPosition;
        private float controlPanelContentHeight = 460f;
        private GUIStyle controlPanelLabelStyle;
        private readonly WelcomeGuide welcomeGuide = new WelcomeGuide();
        private int welcomeGuideClosedFrame = -1;
        private bool IsGuideBlockingInput => welcomeGuide.IsOpen || Time.frameCount <= welcomeGuideClosedFrame;

        private float uiScale = 1f;
        private float uiWidth = 1280f;
        private float uiHeight = 720f;
        private int screenHeight = 720;
        private Matrix4x4 uiMatrix = Matrix4x4.identity;

        private void InitializeUiScaling()
        {
            // Choose the scale once at startup; the game uses a fixed screen size for the session.
            // Fit both axes so ultrawide displays do not make the panels taller than the screen.
            int screenWidth = Mathf.Max(1, Screen.width);
            screenHeight = Mathf.Max(1, Screen.height);
            uiScale = Mathf.Min(screenWidth / 1280f, screenHeight / 720f);
            uiWidth = screenWidth / uiScale;
            uiHeight = screenHeight / uiScale;
            uiMatrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
        }

        private Vector2 ScreenToGuiPoint(Vector3 screenPoint)
        {
            return new Vector2(screenPoint.x, screenHeight - screenPoint.y) / uiScale;
        }

        private Rect GetControlPanelRect()
        {
            return new Rect(15f, 15f, 340f, Mathf.Min(controlPanelContentHeight + 39f, uiHeight - 30f));
        }

        private Rect GetContextMenuRect(bool warMenu)
        {
            float width = warMenu ? 250f : 230f;
            float height = warMenu ? 140f : 215f;
            float x = Mathf.Clamp(contextMenuGuiPos.x, 10f, uiWidth - width - 10f);
            float y = Mathf.Clamp(contextMenuGuiPos.y, 10f, uiHeight - height - 10f);
            return new Rect(x, y, width, height);
        }

        public class War
        {
            public int attackerId;
            public int defenderId;
            public float casualtyProgress;
            public float attackingPercentage;
            public float defendingPercentage;
            public bool combatStarted;
            public float routeRetryTime;
            public WarMobilization mobilization = new WarMobilization();
            public Vector2? connectionEndpoint;
            public readonly List<WarFlag> flags = new List<WarFlag>();
        }

        // Flags are persistent objects: their positions animate independently toward a new front.
        public class WarFlag
        {
            public int nationId;
            public Vector2 position;
            public Color color;
        }

        private class AttackDirection
        {
            public int nationId;
            public readonly List<Vector2> points = new List<Vector2>();
            public readonly HashSet<int> coveredCells = new HashSet<int>();
            public readonly Dictionary<int, Vector2> coveredCellDirections = new Dictionary<int, Vector2>();
        }

        private class DefensiveWall
        {
            public int nationId;
            public readonly List<Vector2> points = new List<Vector2>();
            public readonly HashSet<int> coveredCells = new HashSet<int>();
        }

        private struct FlagTarget
        {
            public int nationId;
            public Vector2 position;
            public Color color;
        }

        private void Start()
        {
            InitializeUiScaling();
            mainCam = Camera.main;
            worldGenerator ??= GetComponent<WorldGenerator>() ?? gameObject.AddComponent<WorldGenerator>();
            conquestConvolution = new ConvolutionOperation(worldGenerator);
            worldRenderer ??= GetComponent<WorldRenderer>() ?? gameObject.AddComponent<WorldRenderer>();
            nationSimulator ??= GetComponent<NationSimulator>() ?? gameObject.AddComponent<NationSimulator>();
            if (cameraController == null && mainCam != null)
                cameraController =
                    mainCam.GetComponent<CameraController>() ?? mainCam.gameObject.AddComponent<CameraController>();
            if (cameraController != null)
            {
                cameraController.OnRightClickTap += HandleRightClickTap;
                cameraController.OnLeftClickTap += HandleNationSelection;
                cameraController.OnAttackDirectionDragStart += BeginAttackDirection;
                cameraController.OnAttackDirectionDrag += ContinueAttackDirection;
                cameraController.OnAttackDirectionDragEnd += EndAttackDirection;
                cameraController.OnDefensiveWallDragStart += BeginDefensiveWall;
                cameraController.OnDefensiveWallDrag += ContinueDefensiveWall;
                cameraController.OnDefensiveWallDragEnd += EndDefensiveWall;
            }
            Regenerate();
            if (welcomeGuide.ShouldShowOnStartup)
                OpenWelcomeGuide();
        }

        private void OpenWelcomeGuide()
        {
            showContextMenu = false;
            inProgressAttackDirection = null;
            inProgressDefensiveWall = null;
            attackDirectionGestureCancelled = true;
            defensiveWallGestureCancelled = true;
            welcomeGuide.Open(this);
            if (cameraController != null)
                cameraController.InputBlocked = true;
        }

        private void OnDestroy()
        {
            if (cameraController == null)
                return;
            cameraController.OnRightClickTap -= HandleRightClickTap;
            cameraController.OnLeftClickTap -= HandleNationSelection;
            cameraController.OnAttackDirectionDragStart -= BeginAttackDirection;
            cameraController.OnAttackDirectionDrag -= ContinueAttackDirection;
            cameraController.OnAttackDirectionDragEnd -= EndAttackDirection;
            cameraController.OnDefensiveWallDragStart -= BeginDefensiveWall;
            cameraController.OnDefensiveWallDrag -= ContinueDefensiveWall;
            cameraController.OnDefensiveWallDragEnd -= EndDefensiveWall;
        }

        private void HandleNationSelection(Vector3 worldPos)
        {
            if (IsGuideBlockingInput || IsPointerOverAttackDirectionUi())
                return;
            if (pendingDefensiveWall != null)
            {
                ConfirmPendingDefensiveWall();
                return;
            }
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
            {
                selectedNation.population += 5000f;
                lockControlledNation = true;
            }
            selectedCity = FindCityAt(worldPos);
            worldRenderer.SetSelectedNation(selectedNation.id);
            worldRenderer.SetSelectedCity(selectedCity);
            commandMessage = selectingFirstNation
                ? $"Now controlling {selectedNation.name}. Added 5,000 population."
                : $"Now controlling {selectedNation.name}.";
        }

        private void HandleRightClickTap(Vector3 worldPos)
        {
            if (IsGuideBlockingInput || IsPointerOverAttackDirectionUi())
                return;
            if (pendingDefensiveWall != null)
            {
                CancelPendingDefensiveWall("Defensive wall cancelled.");
                return;
            }
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
            contextMenuGuiPos = ScreenToGuiPoint(mouse);
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

        private void BeginAttackDirection(Vector3 worldPos)
        {
            inProgressAttackDirection = null;
            attackDirectionGestureCancelled = false;
            if (IsPointerOverAttackDirectionUi())
            {
                attackDirectionGestureCancelled = true;
                return;
            }

            if (TryRemoveAttackDirectionAt(worldPos))
            {
                attackDirectionGestureCancelled = true;
                return;
            }

            if (!CanStartAttackDirection(worldPos, out string reason))
            {
                CancelAttackDirection(reason);
                return;
            }

            inProgressAttackDirection = new AttackDirection { nationId = selectedNation.id };
            inProgressAttackDirection.points.Add(new Vector2(worldPos.x, worldPos.y));
        }

        private void ContinueAttackDirection(Vector3 worldPos)
        {
            if (attackDirectionGestureCancelled || inProgressAttackDirection == null)
                return;

            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            Vector2 previous = inProgressAttackDirection.points[inProgressAttackDirection.points.Count - 1];
            if (!IsMeaningfulScreenDistance(previous, point, 2f))
                return;

            if (!CanContinueAttackDirection(previous, point, out string reason))
            {
                CancelAttackDirection(reason);
                return;
            }

            inProgressAttackDirection.points.Add(point);
        }

        private void EndAttackDirection(Vector3 worldPos)
        {
            ContinueAttackDirection(worldPos);
            if (attackDirectionGestureCancelled || inProgressAttackDirection == null)
                return;

            if (GetAttackDirectionScreenLength(inProgressAttackDirection) >= 8f)
            {
                BuildAttackDirectionCoveredCells(inProgressAttackDirection);
                attackDirections.Add(inProgressAttackDirection);
            }
            inProgressAttackDirection = null;
        }

        private void BeginDefensiveWall(Vector3 worldPos)
        {
            inProgressDefensiveWall = null;
            defensiveWallGestureCancelled = false;
            if (pendingDefensiveWall != null)
            {
                defensiveWallGestureCancelled = true;
                ShowDrawingBanner("Confirm or cancel the current defensive wall first.");
                return;
            }
            if (IsPointerOverAttackDirectionUi())
            {
                defensiveWallGestureCancelled = true;
                return;
            }
            if (!CanStartDefensiveWall(worldPos, out string reason))
            {
                CancelDefensiveWallDraft(reason);
                return;
            }

            inProgressDefensiveWall = new DefensiveWall { nationId = selectedNation.id };
            inProgressDefensiveWall.points.Add(new Vector2(worldPos.x, worldPos.y));
        }

        private void ContinueDefensiveWall(Vector3 worldPos)
        {
            if (defensiveWallGestureCancelled || inProgressDefensiveWall == null)
                return;

            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            Vector2 previous = inProgressDefensiveWall.points[inProgressDefensiveWall.points.Count - 1];
            if (!IsMeaningfulScreenDistance(previous, point, 2f))
                return;
            if (!CanContinueDefensiveWall(previous, point, inProgressDefensiveWall.nationId, out string reason))
            {
                CancelDefensiveWallDraft(reason);
                return;
            }
            inProgressDefensiveWall.points.Add(point);
        }

        private void EndDefensiveWall(Vector3 worldPos)
        {
            ContinueDefensiveWall(worldPos);
            if (defensiveWallGestureCancelled || inProgressDefensiveWall == null)
                return;

            if (GetDefensiveWallScreenLength(inProgressDefensiveWall) < 8f)
            {
                inProgressDefensiveWall = null;
                return;
            }

            BuildDefensiveWallCoveredCells(inProgressDefensiveWall);
            if (inProgressDefensiveWall.coveredCells.Count == 0)
            {
                inProgressDefensiveWall = null;
                return;
            }
            pendingDefensiveWall = inProgressDefensiveWall;
            inProgressDefensiveWall = null;
        }

        private bool CanStartDefensiveWall(Vector3 worldPos, out string reason)
        {
            reason = null;
            if (selectedNation == null)
            {
                reason = "Defensive wall cancelled: select a controlled nation first.";
                return false;
            }
            if (!TryGetCell(worldPos, out Cell cell) || !cell.HasOwner)
            {
                reason = "Defensive wall cancelled: must start on controlled land.";
                return false;
            }
            if (cell.nationId != selectedNation.id)
            {
                reason = "Defensive wall cancelled: must start in controlled territory.";
                return false;
            }
            return true;
        }

        private bool CanContinueDefensiveWall(Vector2 from, Vector2 to, int nationId, out string reason)
        {
            reason = null;
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) * 2f)
            );
            for (int step = 1; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(from, to, step / (float)steps);
                if (!TryGetCell(new Vector3(point.x, point.y), out Cell cell) || !cell.HasOwner)
                {
                    reason = "Defensive wall cancelled: cannot draw over sea or unclaimed land.";
                    return false;
                }
                if (cell.nationId != nationId)
                {
                    reason = "Defensive wall cancelled: must stay in controlled territory.";
                    return false;
                }
            }
            return true;
        }

        private void CancelDefensiveWallDraft(string reason)
        {
            inProgressDefensiveWall = null;
            defensiveWallGestureCancelled = true;
            ShowDrawingBanner(reason);
        }

        private void ConfirmPendingDefensiveWall()
        {
            if (pendingDefensiveWall == null)
                return;
            Nation nation = worldGenerator.Nations[pendingDefensiveWall.nationId];
            float cost = GetDefensiveWallCost(pendingDefensiveWall);
            if (nation.treasury < cost)
            {
                ShowDrawingBanner($"Defensive wall needs {cost:F0}g; {nation.name} has {nation.treasury:F0}g.");
                return;
            }
            nation.treasury -= cost;
            defensiveWalls.Add(pendingDefensiveWall);
            pendingDefensiveWall = null;
            commandMessage = $"Defensive wall built for {cost:F0}g.";
        }

        private void CancelPendingDefensiveWall(string message)
        {
            pendingDefensiveWall = null;
            inProgressDefensiveWall = null;
            defensiveWallGestureCancelled = true;
            ShowDrawingBanner(message);
        }

        private float GetDefensiveWallCost(DefensiveWall wall)
        {
            return wall.coveredCells.Count * wallCostPerPixel;
        }

        private bool CanStartAttackDirection(Vector3 worldPos, out string reason)
        {
            reason = null;
            if (selectedNation == null)
            {
                reason = "Attack direction cancelled: select a controlled nation first.";
                return false;
            }
            if (!TryGetCell(worldPos, out Cell cell) || !cell.HasOwner)
            {
                reason = "Attack direction cancelled: must start on owned land.";
                return false;
            }
            if (cell.nationId != selectedNation.id)
            {
                reason = "Attack direction cancelled: must start in controlled territory.";
                return false;
            }
            return true;
        }

        private bool CanContinueAttackDirection(Vector2 from, Vector2 to, out string reason)
        {
            reason = null;
            int steps = Mathf.Max(
                1,
                Mathf.CeilToInt(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) * 2f)
            );
            for (int step = 1; step <= steps; step++)
            {
                Vector2 point = Vector2.Lerp(from, to, step / (float)steps);
                if (!TryGetCell(new Vector3(point.x, point.y), out Cell cell) || !cell.HasOwner)
                {
                    reason = "Attack direction cancelled: cannot draw over sea or unclaimed land.";
                    return false;
                }
            }
            return true;
        }

        private void CancelAttackDirection(string reason)
        {
            inProgressAttackDirection = null;
            attackDirectionGestureCancelled = true;
            ShowDrawingBanner(reason);
        }

        private void ShowDrawingBanner(string message)
        {
            attackDirectionBanner = message;
            attackDirectionBannerExpiry = Time.unscaledTime + 2.5f;
        }

        private float GetDefensiveWallScreenLength(DefensiveWall wall)
        {
            float length = 0f;
            for (int i = 1; i < wall.points.Count; i++)
                length += Vector2.Distance(WorldToGuiPoint(wall.points[i - 1]), WorldToGuiPoint(wall.points[i]));
            return length;
        }

        private void BuildDefensiveWallCoveredCells(DefensiveWall wall)
        {
            wall.coveredCells.Clear();
            for (int i = 1; i < wall.points.Count; i++)
                AddRasterizedSegmentCells(wall.coveredCells, wall.points[i - 1], wall.points[i]);
        }

        private bool IsPointerOverAttackDirectionUi()
        {
            if (IsGuideBlockingInput)
                return true;
            Vector3 mouse = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                Vector2 position = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                mouse = new Vector3(position.x, position.y);
            }
#endif
            Vector2 pointer = ScreenToGuiPoint(mouse);
            if (GetControlPanelRect().Contains(pointer))
                return true;
            if (pointer.x >= uiWidth - 285)
                return true;
            if (!showContextMenu)
                return false;
            bool warMenu = contextNation != null && selectedNation != null && contextNation.id != selectedNation.id;
            return GetContextMenuRect(warMenu).Contains(pointer);
        }

        private bool TryRemoveAttackDirectionAt(Vector3 worldPos)
        {
            if (mainCam == null)
                return false;
            Vector2 pointer = WorldToGuiPoint(new Vector2(worldPos.x, worldPos.y));
            int closestArrow = -1;
            float closestDistance = 12f;
            for (int i = 0; i < attackDirections.Count; i++)
            {
                List<Vector2> points = attackDirections[i].points;
                for (int point = 1; point < points.Count; point++)
                {
                    float distance = DistanceToSegment(
                        pointer,
                        WorldToGuiPoint(points[point - 1]),
                        WorldToGuiPoint(points[point])
                    );
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestArrow = i;
                    }
                }
            }
            if (closestArrow < 0)
                return false;
            attackDirections.RemoveAt(closestArrow);
            return true;
        }

        private bool IsMeaningfulScreenDistance(Vector2 from, Vector2 to, float minimumPixels)
        {
            return Vector2.Distance(WorldToGuiPoint(from), WorldToGuiPoint(to)) >= minimumPixels;
        }

        private float GetAttackDirectionScreenLength(AttackDirection direction)
        {
            float length = 0f;
            for (int i = 1; i < direction.points.Count; i++)
                length += Vector2.Distance(
                    WorldToGuiPoint(direction.points[i - 1]),
                    WorldToGuiPoint(direction.points[i])
                );
            return length;
        }

        private Vector2 WorldToGuiPoint(Vector2 worldPos)
        {
            Vector3 screen = mainCam.WorldToScreenPoint(new Vector3(worldPos.x, worldPos.y));
            return ScreenToGuiPoint(screen);
        }

        private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, from);
            float position = Mathf.Clamp01(Vector2.Dot(point - from, segment) / lengthSquared);
            return Vector2.Distance(point, from + segment * position);
        }

        private void BuildAttackDirectionCoveredCells(AttackDirection direction)
        {
            direction.coveredCells.Clear();
            direction.coveredCellDirections.Clear();
            for (int i = 1; i < direction.points.Count; i++)
            {
                Vector2 heading = (direction.points[i] - direction.points[i - 1]).normalized;
                AddRasterizedSegmentCells(
                    direction.coveredCells,
                    direction.points[i - 1],
                    direction.points[i],
                    cell =>
                    {
                        if (direction.coveredCellDirections.TryGetValue(cell, out Vector2 existing))
                            direction.coveredCellDirections[cell] = existing + heading;
                        else
                            direction.coveredCellDirections[cell] = heading;
                    }
                );
            }
        }

        private void AddRasterizedSegmentCells(
            HashSet<int> cells,
            Vector2 start,
            Vector2 end,
            System.Action<int> onCellAdded = null
        )
        {
            int x = Mathf.FloorToInt(start.x);
            int y = Mathf.FloorToInt(start.y);
            int targetX = Mathf.FloorToInt(end.x);
            int targetY = Mathf.FloorToInt(end.y);
            int deltaX = Mathf.Abs(targetX - x);
            int deltaY = Mathf.Abs(targetY - y);
            int stepX = x < targetX ? 1 : -1;
            int stepY = y < targetY ? 1 : -1;
            int error = deltaX - deltaY;

            while (true)
            {
                if (x >= 0 && x < worldGenerator.width && y >= 0 && y < worldGenerator.height)
                {
                    int cell = y * worldGenerator.width + x;
                    cells.Add(cell);
                    onCellAdded?.Invoke(cell);
                }
                if (x == targetX && y == targetY)
                    break;
                int twiceError = error * 2;
                if (twiceError > -deltaY)
                {
                    error -= deltaY;
                    x += stepX;
                }
                if (twiceError < deltaX)
                {
                    error += deltaX;
                    y += stepY;
                }
            }
        }

        private void Update()
        {
            if (cameraController != null)
                cameraController.InputBlocked = IsGuideBlockingInput;
            if (welcomeGuide.IsOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.H))
                {
                    welcomeGuide.Close();
                    welcomeGuideClosedFrame = Time.frameCount;
                }
                return;
            }
            if (IsGuideBlockingInput)
                return;
            if (Input.GetKeyDown(KeyCode.H))
            {
                OpenWelcomeGuide();
                return;
            }
            HandleHotkeys();
            UpdateHoveredNation();
            if (isRunning && nationSimulator != null)
            {
                tickTimer += Time.deltaTime * speedMultiplier;
                while (tickTimer >= tickInterval)
                {
                    tickTimer -= tickInterval;
                    ConfigureSelectedNationExpansionBoost();
                    StepWorldSimulation();
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

        private void StepWorldSimulation()
        {
            if (nationSimulator?.StepSimulation(tickInterval) == true)
                warRoutesDirty = true;
            AdvanceWars(tickInterval);

            // Remove completed arrows after this tick's territory changes.
            attackDirections.RemoveAll(direction =>
                direction.coveredCells.Count > 0
                && direction.coveredCells.All(cell => worldGenerator.Grid[cell].nationId == direction.nationId)
            );
        }

        private void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (pendingDefensiveWall != null || inProgressDefensiveWall != null)
                {
                    CancelPendingDefensiveWall("Defensive wall cancelled.");
                    return;
                }
                selectedCity = null;
                showContextMenu = false;
                worldRenderer?.SetSelectedCity(null);
            }
            if (Input.GetKeyDown(KeyCode.Space))
                isRunning = !isRunning;
            if (Input.GetKeyDown(KeyCode.S) && !isRunning)
            {
                ConfigureSelectedNationExpansionBoost();
                StepWorldSimulation();
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
            attackDirections.Clear();
            inProgressAttackDirection = null;
            attackDirectionGestureCancelled = false;
            defensiveWalls.Clear();

            inProgressDefensiveWall = null;
            pendingDefensiveWall = null;
            defensiveWallGestureCancelled = false;
            warRoutesDirty = false;
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
            int size = worldGenerator.width * worldGenerator.height;
            isolatedSince = new float[size];
            connectedToCity = new bool[size];
        }

        private void OnGUI()
        {
            if (worldGenerator == null)
                return;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = previousMatrix * uiMatrix;
            try
            {
                DrawAttackDirections();
                DrawDefensiveWalls();
                DrawWarFronts();
                if (welcomeGuide.IsOpen)
                {
                    welcomeGuide.Draw(uiWidth, uiHeight);
                    if (!welcomeGuide.IsOpen)
                        welcomeGuideClosedFrame = Time.frameCount;
                    return;
                }
                DrawControlPanel();
                DrawRightPanel();
                if (showContextMenu)
                    DrawCityContextMenu();
                if (!string.IsNullOrEmpty(commandMessage))
                    GUI.Box(new Rect(uiWidth / 2f - 190, 15, 380, 30), commandMessage);
                DrawAttackDirectionBanner();
                DrawDefensiveWallCost();
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawControlPanel()
        {
            controlPanelLabelStyle ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            Rect rect = GetControlPanelRect();
            GUI.Box(rect, "Ages of Conflict - Simulation");
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 25f, rect.width - 20f, rect.height - 35f));
            controlPanelScrollPosition = GUILayout.BeginScrollView(controlPanelScrollPosition);
            // Reserve scrollbar space even when it is hidden so wrapping and measured height stay stable.
            float contentWidth =
                rect.width - 20f - GUI.skin.verticalScrollbar.fixedWidth
                - GUI.skin.verticalScrollbar.margin.left;
            GUILayout.BeginVertical(GUILayout.Width(contentWidth), GUILayout.ExpandHeight(false));
            GUILayout.Label(
                $"<b>Resolution:</b> {worldGenerator.width} x {worldGenerator.height}",
                controlPanelLabelStyle
            );
            GUILayout.Label($"<b>Seed:</b> {currentSeed} | <b>Nations:</b> {activeNations}", controlPanelLabelStyle);
            GUILayout.Label(
                $"<b>Controlling:</b> {(selectedNation == null ? "None (left-click a nation)" : selectedNation.name)}",
                controlPanelLabelStyle
            );
            if (GUILayout.Button("Help / How to play [H]", GUILayout.Height(30)))
                OpenWelcomeGuide();
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
                GUILayout.Label(
                    $"<b>Colonization:</b> {nationSimulator.ColonizedPercentage:F1}% Claimed",
                    controlPanelLabelStyle
                );
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isRunning ? "⏸ Pause [Space]" : "▶ Play [Space]", GUILayout.Height(30)))
                isRunning = !isRunning;
            GUI.enabled = !isRunning;
            if (GUILayout.Button("Step [S]", GUILayout.Height(30), GUILayout.Width(75)))
            {
                ConfigureSelectedNationExpansionBoost();
                StepWorldSimulation();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("🔄 Regenerate Map [R]", GUILayout.Height(28)))
                Regenerate();
            if (GUILayout.Button("🎯 Reset Camera [F]", GUILayout.Height(24)))
                cameraController?.FocusOnMap(worldGenerator.width, worldGenerator.height);
            GUILayout.Space(5);
            GUILayout.Label("• <b>Scroll</b>: Zoom | <b>Arrow keys/MMB</b>: Pan", controlPanelLabelStyle);
            GUILayout.Label("• <b>LMB</b>: Select nation or city", controlPanelLabelStyle);
            GUILayout.Label("• <b>RMB</b>: Build cities in selected territory", controlPanelLabelStyle);
            GUILayout.Label("• <b>A + drag</b>: Draw attack direction", controlPanelLabelStyle);
            GUILayout.Label("• <b>A + click</b> an arrow: Remove it", controlPanelLabelStyle);
            GUILayout.Label("• <b>D + drag</b>: Draft defensive wall", controlPanelLabelStyle);
            GUILayout.Label("• Territory expands automatically", controlPanelLabelStyle);
            GUILayout.EndVertical();
            // Layout rectangles are final during repaint; use the result for the next layout pass.
            if (Event.current.type == EventType.Repaint)
                controlPanelContentHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
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
            contextMenuGuiPos = ScreenToGuiPoint(mouse);
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
            SetWarPercentage(newWar, newWar.attackerId, percentage);
            int defendingFronts = wars.Count(war =>
                war.attackerId == newWar.defenderId || war.defenderId == newWar.defenderId
            );
            SetWarPercentage(newWar, newWar.defenderId, 100f / defendingFronts);
            PlanMobilization(newWar);
            commandMessage =
                newWar.mobilization.path.Count > 0
                    ? $"{selectedNation.name} is mobilizing against {contextNation.name}."
                    : "War declared. Waiting for a reachable front from a friendly city.";
        }

        private bool IsMobilizationFront(War war, int cell)
        {
            return worldGenerator.Grid.IsFrontierWith(
                cell % worldGenerator.width,
                cell / worldGenerator.width,
                worldGenerator.width,
                worldGenerator.height,
                war.defenderId
            );
        }

        private bool CanMobilizeThrough(War war, int cell)
        {
            return worldGenerator.Grid[cell].IsLand && worldGenerator.Grid[cell].nationId == war.attackerId;
        }

        private void PlanMobilization(War war, int currentCell = -1)
        {
            var origins = new List<int>();
            if (currentCell >= 0 && CanMobilizeThrough(war, currentCell))
                origins.Add(currentCell);
            else
                foreach (City city in worldGenerator.Nations[war.attackerId].cities)
                    if (city.nationId == war.attackerId)
                        origins.Add(city.position.y * worldGenerator.width + city.position.x);

            war.mobilization.Plan(
                worldGenerator.width,
                worldGenerator.height,
                origins,
                cell => CanMobilizeThrough(war, cell),
                cell => IsMobilizationFront(war, cell)
            );
            war.routeRetryTime = 0f;
        }

        private void AdvanceMobilization(War war, float deltaTime)
        {
            float expansionInterval =
                tickInterval
                * (nationSimulator != null ? nationSimulator.GetExpansionIntervalTicks(war.attackerId) : 10);
            List<int> path = war.mobilization.path;
            if (path.Count == 0)
            {
                // Avoid searching the whole country every tick while waiting for contact.
                war.routeRetryTime += deltaTime;
                if (war.routeRetryTime < expansionInterval)
                    return;
                PlanMobilization(war);
            }
            else
            {
                int current = war.mobilization.CurrentPathIndex;
                bool valid =
                    IsMobilizationFront(war, path[path.Count - 1])
                    && war.mobilization.CanFollowRemainingPath(cell => CanMobilizeThrough(war, cell));
                if (!valid)
                    PlanMobilization(war, path[current]);
            }

            war.mobilization.Advance(deltaTime, expansionInterval);
            if (!war.mobilization.HasArrived)
                return;

            war.combatStarted = true;
            // Leave the completed route in place. Subsequent map changes reconnect it
            // from the closest city to the updated front's center without animating again.
            RefreshWarFlags(war);
            RefreshWarBorderHighlight();
            commandMessage = $"{worldGenerator.Nations[war.attackerId].name} reached the front. Battle begins.";
        }

        private void RefreshWarConnections()
        {
            if (!warRoutesDirty)
                return;
            warRoutesDirty = false;
            foreach (War war in wars)
            {
                if (!war.combatStarted)
                    continue;

                List<int> front = FindDefenderBorderCells(war.attackerId, war.defenderId);
                // Keep the last connection if contact is temporarily lost. Only ending
                // the war removes the line; a future territory change will retry it.
                if (front.Count == 0)
                    continue;
                int center = front[front.Count / 2];
                int x = center % worldGenerator.width,
                    y = center / worldGenerator.width;
                var targets = new HashSet<int>();
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + frontDx[direction],
                        ny = y + frontDy[direction];
                    if (nx < 0 || nx >= worldGenerator.width || ny < 0 || ny >= worldGenerator.height)
                        continue;
                    int cell = ny * worldGenerator.width + nx;
                    if (CanMobilizeThrough(war, cell))
                        targets.Add(cell);
                }

                var origins = new List<int>();
                foreach (City city in worldGenerator.Nations[war.attackerId].cities)
                    if (city.nationId == war.attackerId)
                        origins.Add(city.position.y * worldGenerator.width + city.position.x);

                // Re-select the closest city by actual route length, including captured
                // or newly built cities. Plan separately so failure never erases the line.
                var connection = new WarMobilization();
                if (
                    !connection.Plan(
                        worldGenerator.width,
                        worldGenerator.height,
                        origins,
                        cell => CanMobilizeThrough(war, cell),
                        cell => targets.Contains(cell)
                    )
                )
                    continue;

                connection.Complete();
                war.mobilization = connection;
                int end = connection.path[connection.path.Count - 1];
                war.connectionEndpoint = (GetCellCenter(end) + GetCellCenter(center)) * 0.5f;
            }
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
            SetWarPercentage(war, war.attackerId, 0f);
            SetWarPercentage(war, war.defenderId, 0f);
            wars.Remove(war);
            // Troops stay in the population and are shared equally across remaining fronts.
        }

        private void AdvanceWars(float deltaTime)
        {
            bool warsChanged = false;
            for (int i = wars.Count - 1; i >= 0; i--)
            {
                War war = wars[i];
                if (
                    worldGenerator.Nations[war.attackerId].territorySize <= 0
                    || worldGenerator.Nations[war.defenderId].territorySize <= 0
                    || worldGenerator.Nations[war.attackerId].armyPopulation < 1f
                )
                {
                    RemoveWar(war);
                    warsChanged = true;
                    continue;
                }
                // A zero allocation pauses the assault; only "Cancel war" declares peace.
                if (GetAttackingForce(war) < 1f)
                    continue;
                if (!war.combatStarted)
                {
                    AdvanceMobilization(war, deltaTime);
                    continue;
                }
                // Contact can be lost to another war. Never inflict casualties across a gap.
                if (!HaveSharedBorder(war.attackerId, war.defenderId))
                {
                    war.casualtyProgress = 0f;
                    war.flags.Clear();
                    continue;
                }
                UpdateWarUI(war);

                ApplyWarCasualties(war, deltaTime);
                float attackingForce = GetAttackingForce(war);
                float defendingForce = GetDefendingForce(war);
                if (attackingForce < 1f)
                    continue;

                var attackerCities = findCities(FindDefenderBorderCells(war.defenderId, war.attackerId), 3f);
                // An assault only advances with at least a 50% force advantage.
                if (attackingForce < defendingForce * 1.5f)
                    continue;
                float advantage = attackingForce / Mathf.Max(1f, defendingForce) - 1.5f;
                float speed = Mathf.Min(maximumWarAdvanceSpeed, maximumWarAdvanceSpeed * advantage);
                float toCapture = speed * deltaTime;
                int cellsToCapture = Mathf.FloorToInt(toCapture) + Mathf.FloorToInt(attackerCities.Count * 0.5f);

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
            RefreshWarConnections();
            // Reuse the existing renderer so neighbouring borders and city markers
            // are refreshed too, rather than just painting over the captured pixels.
            worldRenderer?.RenderWorld(
                worldGenerator.Grid,
                worldGenerator.Nations,
                worldGenerator.width,
                worldGenerator.height
            );
        }

        // the border is of defender
        private void CaptureCells(War war, List<int> border, int amount)
        {
            amount = Mathf.Clamp(amount, 0, border.Count);
            if (amount == 0)
                return;
            warRoutesDirty = true;

            // Prioritize vulnerable border cells, including pockets cut off from defending cities.
            Nation defender = worldGenerator.Nations[war.defenderId];
            Nation attacker = worldGenerator.Nations[war.attackerId];
            var defenderCapital = defender.capital;
            // map border to their distance to the defender capital
            var distances = border
                .Select(index => (worldGenerator.IndexToVec2(index) - defenderCapital).sqrMagnitude)
                .ToList();
            // Calculate the average distance
            float average = (float)distances.Average();
            RebuildIsolationCache();
            // Jitter must be baked into the key once per cell; drawing it inside a
            // comparator makes comparisons inconsistent and Sort throws.
            border = border
                .OrderByDescending(index => GetConquestScore(index, defenderCapital, average, war.attackerId))
                .ToList();
            int capturedCells = 0;
            for (int i = 0; i < border.Count && capturedCells < amount; i++)
            {
                int cellIndex = border[i];
                float conquestCost = GetConquestCost(cellIndex, war.attackerId);
                if (GetAttackingForce(war) < conquestCost)
                    continue;

                attacker.population -= conquestCost;
                worldGenerator.Grid[cellIndex].nationId = (short)war.attackerId;
                worldGenerator.Nations[war.attackerId].territorySize++;
                defender.territorySize--;
                // Otherwise the next border rebuild can paint these cells with
                // the former owner's colour again.
                defender.border.Remove(cellIndex);
                defender.frontier.Remove(cellIndex);
                capturedCells++;
            }
            if (capturedCells == 0)
                return;
            nationSimulator?.RebuildBorders();

            // A city changes hands only when its own cell is captured.
            // Iterate backwards because transferring removes it from the defender's list.
            for (int i = defender.cities.Count - 1; i >= 0; i--)
            {
                City city = defender.cities[i];
                int cityIndex = city.position.y * worldGenerator.width + city.position.x;
                if (worldGenerator.Grid[cityIndex].nationId != war.attackerId)
                    continue;

                defender.cities.RemoveAt(i);
                city.nationId = war.attackerId;
                attacker.cities.Add(city);
            }
        }

        private float GetConquestCost(int cellIndex, int attackerId)
        {
            int defenderId = worldGenerator.Grid[cellIndex].nationId;
            if (defenderId < 0 || defenderId == attackerId || !IsDefensiveWallCell(cellIndex, defenderId))
                return 0f;

            if (connectedToCity[cellIndex])
            {
                return wallBreachCasualtiesPerPixel;
            }

            float isolatedDuration = Time.time - isolatedSince[cellIndex];
            return wallBreachCasualtiesPerPixel * Mathf.Exp(-isolatedWallCostDecayPerSecond * isolatedDuration);
        }

        private bool IsDefensiveWallCell(int cellIndex, int nationId)
        {
            foreach (DefensiveWall wall in defensiveWalls)
                if (wall.nationId == nationId && wall.coveredCells.Contains(cellIndex))
                    return true;
            return false;
        }

        private List<City> GetCities()
        {
            List<City> cities = new List<City>();
            foreach (Nation nation in worldGenerator.Nations)
            {
                foreach (City city in nation.cities)
                {
                    if (city.nationId != nation.id)
                        throw new System.Exception(
                            $"City {city.name} has nationId {city.nationId} but is listed in nation {nation.name} with id {nation.id}"
                        );
                    cities.Add(city);
                }
            }
            return cities;
        }

        private void RebuildIsolationCache()
        {
            Cell[] grid = worldGenerator.Grid;
            int cellCount = grid.Length;

            bool[] connected = connectedToCity;
            System.Array.Clear(connected, 0, cellCount);

            var cities = GetCities();

            foreach (City city in cities)
            {
                int x = city.position.x;
                int y = city.position.y;
                int cell = y * worldGenerator.width + x;
                connected[cell] = true;
            }

            // Parallel.ForEach uses the worker pool and joins before returning,
            // so captures cannot change ownership while searches are in flight.
            // WebGL has no managed thread pool; a single nation also needs no dispatch.
#if !UNITY_WEBGL || UNITY_EDITOR
            Parallel.ForEach(
                cities,
                city => MarkCityConnectedCells(connected, worldGenerator.CoordToIndex(city.position), city.nationId)
            );
#endif
            {
                foreach (City city in cities)
                {
                    MarkCityConnectedCells(connected, worldGenerator.CoordToIndex(city.position), city.nationId);
                }
            }
        }

        private void MarkCityConnectedCells(bool[] connected, int cellId, int nationId)
        {
            Queue<int> cellsToVisit = new Queue<int>();
            cellsToVisit.Enqueue(cellId);
            int width = worldGenerator.width;
            int height = worldGenerator.height;
            while (cellsToVisit.Count > 0)
            {
                int current = cellsToVisit.Dequeue();
                var pos = worldGenerator.IndexToVec2(current);
                int x = pos.x;
                int y = pos.y;
                for (int direction = 0; direction < frontDx.Length; direction++)
                {
                    int nextX = x + frontDx[direction];
                    int nextY = y + frontDy[direction];
                    if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                        continue;
                    int next = nextY * width + nextX;
                    if (connected[next] || worldGenerator.Grid[next].nationId != nationId)
                        continue;
                    connected[next] = true;
                    cellsToVisit.Enqueue(next);
                }
            }
        }

        private float GetConquestScore(
            int cellIndex,
            Vector2Int defenderCapital,
            float averageBorderDistance,
            int attackerId
        )
        {
            float score = (worldGenerator.IndexToVec2(cellIndex) - defenderCapital).sqrMagnitude
                + Random.Range(0f, averageBorderDistance)
                + CountEnemyNeighbours(cellIndex, attackerId) * 100f
                + warArrowWeight * GetAttackDirectionConquestWeight(cellIndex, attackerId) * 100;
            return connectedToCity[cellIndex] ? score : score * Mathf.Max(1f, isolatedCellConquestMultiplier);
        }

        private float GetAttackDirectionConquestWeight(int cellIndex, int attackerId)
        {
            return conquestConvolution.Evaluate(
                worldGenerator.IndexToVec2(cellIndex),
                10,
                GetDirectionalArrowConvolutionKernel(attackerId)
            );
        }

        private System.Func<Vector2Int, Vector2Int, float> GetDirectionalArrowConvolutionKernel(int attackerId)
        {
            return (target, arrowPosition) =>
            {
                if (!TryGetAttackDirectionAt(arrowPosition, attackerId, out Vector2 arrowDirection))
                    return 0f;

                Vector2 towardTarget = new Vector2(target.x - arrowPosition.x, target.y - arrowPosition.y);
                float distance = towardTarget.magnitude;
                float proximity = 1f - Mathf.Clamp01(distance / Mathf.Sqrt(50f));
                if (distance < 0.001f)
                    return proximity;
                float forwardAlignment = Mathf.Max(0f, Vector2.Dot(arrowDirection, towardTarget / distance));
                return proximity * forwardAlignment;
            };
        }

        private bool TryGetAttackDirectionAt(Vector2Int position, int attackerId, out Vector2 direction)
        {
            direction = Vector2.zero;
            int cellIndex = position.y * worldGenerator.width + position.x;
            foreach (AttackDirection attackDirection in attackDirections)
            {
                if (attackDirection.nationId != attackerId)
                    continue;
                if (attackDirection.coveredCellDirections.TryGetValue(cellIndex, out Vector2 arrowDirection))
                    direction += arrowDirection;
            }
            if (direction.sqrMagnitude < 0.001f)
                return false;
            direction.Normalize();
            return true;
        }

        // Counts adjacent cells belonging to the specified enemy, including diagonals.
        private int CountEnemyNeighbours(int cellIndex, int enemyId)
        {
            Vector2Int position = worldGenerator.IndexToVec2(cellIndex);
            int enemyNeighbours = 0;
            for (int direction = 0; direction < frontDx.Length; direction++)
            {
                int x = position.x + frontDx[direction];
                int y = position.y + frontDy[direction];
                if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                    continue;
                if (worldGenerator.Grid[y * worldGenerator.width + x].nationId == enemyId)
                    enemyNeighbours++;
            }

            return enemyNeighbours;
        }

        private void RefreshWarBorderHighlight()
        {
            if (worldRenderer == null)
                return;
            List<Vector2Int> nationPairs = new List<Vector2Int>(wars.Count);
            foreach (War war in wars)
                if (war.combatStarted)
                    nationPairs.Add(new Vector2Int(war.attackerId, war.defenderId));
            worldRenderer.SetActiveWarBorders(nationPairs);
        }

        private List<City> findCities(List<int> cellIndices, float threshold)
        {
            List<City> result = new List<City>();
            foreach (int index in cellIndices)
            {
                Vector2Int pos = worldGenerator.IndexToVec2(index);
                foreach (Nation nation in worldGenerator.Nations)
                foreach (City city in nation.cities)
                    if (Vector2Int.Distance(pos, city.position) <= threshold)
                        result.Add(city);
            }
            return result;
        }

        private void ApplyWarCasualties(War war, float deltaTime)
        {
            war.casualtyProgress += deltaTime;
            while (war.casualtyProgress >= .2f)
            {
                war.casualtyProgress -= .2f;
                // check nearby defender cities
                var defenderCities = findCities(FindDefenderBorderCells(war.attackerId, war.defenderId), 5f);

                float attackingForce = GetAttackingForce(war);
                float attackerLosses = Mathf.Ceil(attackingForce * .05f) + Mathf.Ceil(defenderCities.Count * 2f);
                // Defenders lose one third of the attacker's *casualties*, not one third
                // of the entire attacking force each second.
                float defenderLosses = Mathf.Ceil(attackerLosses / 3f);

                Nation attacker = worldGenerator.Nations[war.attackerId];
                Nation defender = worldGenerator.Nations[war.defenderId];
                attacker.population -= attackerLosses;
                defender.population -= Mathf.Min(defenderLosses, defender.armyPopulation);
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
            if (!war.combatStarted)
                return 0f;
            Nation defender = worldGenerator.Nations[war.defenderId];
            return defender.armyPopulation * Mathf.Clamp(war.defendingPercentage, 0f, 100f) / 100f;
        }

        private float GetWarPercentage(War war, int nationId)
        {
            return war.attackerId == nationId ? war.attackingPercentage : war.defendingPercentage;
        }

        private void SetWarPercentage(War target, int nationId, float percentage)
        {
            List<War> fronts = wars.FindAll(war => war.attackerId == nationId || war.defenderId == nationId);
            int index = fronts.IndexOf(target);
            if (index < 0)
                return;
            List<float> percentages = fronts.Select(war => GetWarPercentage(war, nationId)).ToList();
            WarAllocation.SetPercentage(percentages, index, percentage);
            for (int i = 0; i < fronts.Count; i++)
                if (fronts[i].attackerId == nationId)
                    fronts[i].attackingPercentage = percentages[i];
                else
                    fronts[i].defendingPercentage = percentages[i];
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
                DrawMobilization(war);
                if (!war.combatStarted)
                    continue;
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

        private void DrawAttackDirections()
        {
            if (mainCam == null)
                return;
            Color blue = new Color(0.15f, 0.6f, 1f, 1f);
            foreach (AttackDirection direction in attackDirections)
                DrawAttackDirection(direction, blue);
            if (inProgressAttackDirection != null)
                DrawAttackDirection(inProgressAttackDirection, new Color(blue.r, blue.g, blue.b, 0.75f));
        }

        private void DrawDefensiveWalls()
        {
            if (mainCam == null)
                return;
            Color gray = new Color(0.55f, 0.55f, 0.55f, 1f);
            foreach (DefensiveWall wall in defensiveWalls)
                DrawDefensiveWall(wall, gray);
            if (inProgressDefensiveWall != null)
                DrawDefensiveWall(inProgressDefensiveWall, new Color(gray.r, gray.g, gray.b, 0.65f));
            if (pendingDefensiveWall != null)
                DrawDefensiveWall(pendingDefensiveWall, new Color(gray.r, gray.g, gray.b, 0.75f));
        }

        private void DrawDefensiveWall(DefensiveWall wall, Color color)
        {
            for (int i = 1; i < wall.points.Count; i++)
                DrawMapLine(wall.points[i - 1], wall.points[i], color, 4f);
        }

        private void DrawAttackDirection(AttackDirection direction, Color color)
        {
            if (direction.points.Count < 2)
                return;
            for (int i = 1; i < direction.points.Count; i++)
                DrawMapLine(direction.points[i - 1], direction.points[i], color, 3f);

            Vector2 tip = WorldToGuiPoint(direction.points[direction.points.Count - 1]);
            Vector2 previous = WorldToGuiPoint(direction.points[direction.points.Count - 2]);
            Vector2 heading = tip - previous;
            if (heading.sqrMagnitude < 0.01f)
                return;
            heading.Normalize();
            Vector2 backward = -heading;
            Vector2 perpendicular = new Vector2(-heading.y, heading.x);
            DrawGuiLine(tip, tip + (backward + perpendicular * 0.65f) * 12f, color, 4f);
            DrawGuiLine(tip, tip + (backward - perpendicular * 0.65f) * 12f, color, 4f);
        }

        private void DrawGuiLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            Vector2 direction = to - from;
            if (direction.sqrMagnitude < 0.01f)
                return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            // Apply the local translation/rotation before the UI scale so the pivot stays on the map.
            GUI.matrix =
                previousMatrix
                * Matrix4x4.TRS(
                    new Vector3(from.x, from.y, 0f),
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg),
                    Vector3.one
                );
            GUI.DrawTexture(
                new Rect(0f, -thickness / 2f, direction.magnitude, thickness),
                Texture2D.whiteTexture
            );
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private void DrawAttackDirectionBanner()
        {
            if (string.IsNullOrEmpty(attackDirectionBanner) || Time.unscaledTime > attackDirectionBannerExpiry)
                return;
            const float width = 360f;
            GUI.Box(new Rect(uiWidth / 2f - width / 2f, 52f, width, 26f), attackDirectionBanner);
        }

        private void DrawDefensiveWallCost()
        {
            DefensiveWall wall = inProgressDefensiveWall ?? pendingDefensiveWall;
            if (wall == null)
                return;

            int pixelCount = wall.coveredCells.Count;
            if (inProgressDefensiveWall != null && wall.points.Count > 1)
            {
                var preview = new DefensiveWall();
                preview.points.AddRange(wall.points);
                BuildDefensiveWallCoveredCells(preview);
                pixelCount = preview.coveredCells.Count;
            }

            float cost = pixelCount * wallCostPerPixel;
            Vector2 anchor = WorldToGuiPoint(wall.points[wall.points.Count - 1]);
            const float width = 285f;
            float x = Mathf.Clamp(anchor.x + 12f, 10f, uiWidth - width - 10f);
            float y = Mathf.Clamp(anchor.y + 12f, 10f, uiHeight - 34f);
            string text =
                pendingDefensiveWall != null
                    ? $"Wall: {pixelCount} pixels • {cost:F0}g • LMB confirm / RMB or Esc cancel"
                    : $"Wall: {pixelCount} pixels • {cost:F0}g";
            GUI.Box(new Rect(x, y, width, 24f), text);
        }

        private void DrawMobilization(War war)
        {
            List<int> path = war.mobilization.path;
            if (path.Count == 0)
                return;
            Color blue = new Color(0.15f, 0.6f, 1f, 1f);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 start = GetCellCenter(path[i - 1]);
                Vector2 end = GetCellCenter(path[i]);
                DrawMapLine(start, end, new Color(blue.r, blue.g, blue.b, 0.3f), 2f);
                float traveled = war.mobilization.GetSegmentProgress(i - 1);
                if (traveled > 0f)
                    DrawMapLine(start, Vector2.Lerp(start, end, traveled), blue, 4f);
            }

            int headIndex = war.mobilization.CurrentPathIndex;
            Vector2 head = GetCellCenter(path[headIndex]);
            if (headIndex + 1 < path.Count)
                head = Vector2.Lerp(
                    head,
                    GetCellCenter(path[headIndex + 1]),
                    war.mobilization.GetSegmentProgress(headIndex)
                );
            if (war.combatStarted && war.connectionEndpoint.HasValue)
            {
                DrawMapLine(head, war.connectionEndpoint.Value, blue, 4f);
                head = war.connectionEndpoint.Value;
            }
            Vector3 screen = mainCam.WorldToScreenPoint(new Vector3(head.x, head.y, 0f));
            if (screen.z <= 0f)
                return;
            Vector2 guiPoint = ScreenToGuiPoint(screen);
            Color previous = GUI.color;
            GUI.color = blue;
            GUI.DrawTexture(new Rect(guiPoint.x - 4f, guiPoint.y - 4f, 8f, 8f), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private Vector2 GetCellCenter(int cell)
        {
            return new Vector2(cell % worldGenerator.width + 0.5f, cell / worldGenerator.width + 0.5f);
        }

        private void DrawMapLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector3 a = mainCam.WorldToScreenPoint(new Vector3(start.x, start.y, 0f));
            Vector3 b = mainCam.WorldToScreenPoint(new Vector3(end.x, end.y, 0f));
            if (a.z <= 0f || b.z <= 0f)
                return;
            DrawGuiLine(ScreenToGuiPoint(a), ScreenToGuiPoint(b), color, thickness);
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
            Vector2 guiPoint = ScreenToGuiPoint(screen);
            Rect bg = new Rect(guiPoint.x - w / 2f, guiPoint.y - h / 2f, w, h);

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
            bool warMenu = contextNation != null && selectedNation != null && contextNation.id != selectedNation.id;
            Rect rect = GetContextMenuRect(warMenu);
            if (warMenu)
            {
                GUI.Box(rect, "War Declaration");
                GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 25, rect.width - 20, rect.height - 30));
                float strength = nationSimulator.ComputeStrength(contextNation);
                GUILayout.Label($"Attack <b>{contextNation.name}</b>?");
                GUILayout.Label($"Army: {selectedNation.armyPopulation:F0} | Enemy: {contextNation.armyPopulation:F0}");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Commit army:", GUILayout.Width(125));
                // IMGUI text fields never receive typed characters in WebGL builds
                // (known Unity input-system bug), so use a slider instead.
                attackPercentage = Mathf.Round(GUILayout.HorizontalSlider(attackPercentage, 1f, 100f));
                GUILayout.Label($"{attackPercentage:F0}%", GUILayout.Width(42));
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Attack", GUILayout.Height(30)))
                {
                    StartWar(attackPercentage);
                    showContextMenu = false;
                }
                if (GUILayout.Button("Cancel"))
                    showContextMenu = false;
                GUILayout.EndArea();
                return;
            }
            GUI.Box(rect, "🏰 City Construction");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 25, rect.width - 20, rect.height - 30));
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
            warRoutesDirty = true;
            RefreshWarConnections();
            worldRenderer.DrawCityMarker(city, worldGenerator.width, worldGenerator.height);
            worldRenderer.ApplyTextureChanges();
        }

        private Rect GetNationPanelRect(Nation nation)
        {
            return new Rect(uiWidth - 285, 15, 270, 260 + nation.cities.Count * 22);
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
            {
                warsPanelRect = default;
                return top;
            }

            const int width = 270;
            int height = Mathf.Min(40 + activeWars.Count * 84, Mathf.Max(100, Mathf.FloorToInt(uiHeight) - top - 15));
            float x = uiWidth - 285;
            warsPanelRect = new Rect(x, top, width, height);
            GUI.Box(warsPanelRect, "⚔ Nations at War");
            GUILayout.BeginArea(new Rect(x + 10, top + 25, width - 20, height - 30));
            warsScrollPosition = GUILayout.BeginScrollView(warsScrollPosition);
            foreach (War war in activeWars)
            {
                int opponentId = war.attackerId == selectedNation.id ? war.defenderId : war.attackerId;
                Nation opponent = worldGenerator.Nations[opponentId];
                GUILayout.BeginHorizontal();
                float force = war.attackerId == selectedNation.id ? GetAttackingForce(war) : GetDefendingForce(war);
                string status =
                    war.combatStarted ? $"{force:F0}"
                    : war.mobilization.path.Count > 0 ? "Mobilizing"
                    : "Awaiting route";
                GUILayout.Label($"{opponent.name} ({status})", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Cancel war", GUILayout.Height(24)))
                {
                    RemoveWar(war);
                    RefreshWarBorderHighlight();
                    commandMessage = $"Peace declared with {opponent.name}.";
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
                float percentage = GetWarPercentage(war, selectedNation.id);
                GUILayout.Label($"Army committed: {percentage:F1}%");
                float updatedPercentage = GUILayout.HorizontalSlider(percentage, 0f, 100f);
                if (!Mathf.Approximately(updatedPercentage, percentage))
                    SetWarPercentage(war, selectedNation.id, updatedPercentage);
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
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
            GUI.Box(new Rect(uiWidth - 285, top, 270, 45 + count * 22), "🏆 Nations");
            GUILayout.BeginArea(new Rect(uiWidth - 275, top + 27, 250, count * 22));
            for (int i = 0; i < count; i++)
                GUILayout.Label(
                    $"#{i + 1} {sortedNations[i].name}: {sortedNations[i].territorySize:N0} px | 🏰{sortedNations[i].cities.Count}"
                );
            GUILayout.EndArea();
        }
    }
}
