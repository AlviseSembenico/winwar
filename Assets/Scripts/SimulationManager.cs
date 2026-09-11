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

        [Header("Troop Movement")]
        [Tooltip("World cells travelled per second. Troops move at this speed regardless of route length.")]
        public float troopMoveSpeed = 35f;

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
        private City selectedCity = null;
        private readonly List<Vector3> troopPathPreview = new List<Vector3>();
        private readonly List<ArmyRoute> armyRoutes = new List<ArmyRoute>();
        private readonly List<MovingTroop> movingTroops = new List<MovingTroop>();
        private int[] pathSearchVisited;
        private int[] pathSearchPrevious;
        private int pathSearchVersion;
        private ArmyRoute selectedArmyRoute;
        private Texture2D overlayTexture;
        private Texture2D garrisonMarkerTexture;
        private GUIStyle fieldGarrisonLabelStyle;

        private class ArmyRoute
        {
            public int nationId;
            public List<Vector3> points;
            public List<TroopGroup> groups;
        }

        private class MovingTroop
        {
            public TroopGroup group;
            public List<Vector2> path;
            public int nextWaypoint;
            public ArmyRoute destinationRoute;
            public City destinationCity;
            public ArmyRoute routeToRemoveOnArrival;
        }

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
                cameraController.ShouldDrawRightPath += CanDrawTroopPath;
                cameraController.OnRightPathUpdated += UpdateTroopPathPreview;
                cameraController.OnRightPathCompleted += DeployArmyAlongPath;
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
                cameraController.ShouldDrawRightPath -= CanDrawTroopPath;
                cameraController.OnRightPathUpdated -= UpdateTroopPathPreview;
                cameraController.OnRightPathCompleted -= DeployArmyAlongPath;
            }

            if (overlayTexture != null)
            {
                Destroy(overlayTexture);
            }
            if (garrisonMarkerTexture != null)
            {
                Destroy(garrisonMarkerTexture);
            }
        }

        private void HandleRightClickTap(Vector3 worldPos)
        {
            if (selectedArmyRoute != null)
            {
                City destinationCity = FindCityAt(worldPos, 4f);
                if (destinationCity != null && destinationCity.nationId == selectedArmyRoute.nationId)
                {
                    MoveRouteToCity(selectedArmyRoute, destinationCity);
                }
                else
                {
                    CreatePointGarrison(worldPos);
                }
                return;
            }

            // A selected city turns a right-click into a deployment order. Do this
            // before opening the construction context menu, even on empty land.
            if (selectedCity != null && selectedNation != null
                && selectedCity.nationId == selectedNation.id)
            {
                DeployCityToGarrison(worldPos);
                return;
            }

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

        private void CreatePointGarrison(Vector3 worldPos)
        {
            if (!IsInsideSelectedTerritory(worldPos))
            {
                commandMessage = "A garrison must be placed inside the selected nation's territory.";
                return;
            }

            Vector3 point = new Vector3(worldPos.x, worldPos.y, 0f);
            if (RelocateRoute(selectedArmyRoute, new List<Vector3> { point }))
                commandMessage = "Troops are moving to the new field garrison.";
        }

        private void DeployCityToGarrison(Vector3 worldPos)
        {
            if (!IsInsideSelectedTerritory(worldPos))
            {
                commandMessage = "Troops must remain inside the selected nation's territory.";
                return;
            }

            int soldiers = selectedCity.armyCount;
            if (soldiers <= 0)
            {
                commandMessage = $"{selectedCity.name} has no troops to deploy.";
                return;
            }

            List<TroopGroup> departingGroups = SplitTroops(
                selectedNation.id, soldiers, new Vector2(selectedCity.position.x, selectedCity.position.y));
            List<TroopGroup> arrivingGroups = CreateGroupsAt(worldPos, departingGroups);
            if (!TryBuildMovementPlans(departingGroups, arrivingGroups, selectedNation.id, out List<List<Vector2>> movementPaths))
            {
                commandMessage = "No friendly-territory route exists to that point.";
                return;
            }

            ArmyRoute garrison = new ArmyRoute
            {
                nationId = selectedNation.id,
                points = new List<Vector3> { new Vector3(worldPos.x, worldPos.y, 0f) },
                groups = new List<TroopGroup>()
            };
            armyRoutes.Add(garrison);
            string cityName = selectedCity.name;
            nationSimulator.DeployTroops(selectedCity, soldiers);
            StartMovements(departingGroups, movementPaths, garrison, null, null);
            selectedCity = null;
            worldRenderer.SetSelectedCity(null);
            selectedArmyRoute = garrison;
            commandMessage = $"Troops are moving from {cityName} to a field garrison.";
        }

        private void HandleNationSelection(Vector3 worldPos)
        {
            if (worldGenerator == null || worldGenerator.Grid == null)
                return;

            // Keep a selected route active when the player clicks a city; return
            // commands use the right mouse button and accept the city label area.
            City destinationCity = FindCityAt(worldPos, selectedArmyRoute != null ? 4f : 0f);
            if (selectedArmyRoute != null && destinationCity != null)
            {
                commandMessage = "Right-click the city to return the selected units.";
                return;
            }

            ArmyRoute route = FindArmyRouteAt(worldPos);
            if (route != null)
            {
                selectedArmyRoute = route;
                selectedCity = null;
                selectedNation = worldGenerator.Nations[route.nationId];
                worldRenderer.SetSelectedNation(selectedNation.id);
                worldRenderer.SetSelectedCity(null);
                commandMessage = "Army route selected. Right-drag to redraw it.";
                return;
            }

            int x = Mathf.FloorToInt(worldPos.x);
            int y = Mathf.FloorToInt(worldPos.y);
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return;

            Cell cell = worldGenerator.Grid[y * worldGenerator.width + x];
            if (!cell.HasOwner || cell.nationId < 0 || cell.nationId >= worldGenerator.Nations.Count)
                return;

            selectedNation = worldGenerator.Nations[cell.nationId];
            selectedCity = FindCityAt(worldPos);
            selectedArmyRoute = null;
            worldRenderer.SetSelectedNation(selectedNation.id);
            worldRenderer.SetSelectedCity(selectedCity);
            commandMessage = $"Now controlling {selectedNation.name}.";
        }

        private bool CanDrawTroopPath()
        {
            return selectedArmyRoute != null
                || (selectedCity != null && selectedCity.armyCount > 0 && selectedNation != null
                    && selectedCity.nationId == selectedNation.id);
        }

        private void UpdateTroopPathPreview(List<Vector3> points)
        {
            troopPathPreview.Clear();
            troopPathPreview.AddRange(GetValidRoutePath(points));
        }

        private void DeployArmyAlongPath(List<Vector3> drawnPoints)
        {
            troopPathPreview.Clear();
            if (!CanDrawTroopPath())
                return;

            // The route begins exactly where the player starts drawing; it does not
            // render an automatic connector between the city and the drawn route.
            List<Vector3> path = GetValidRoutePath(drawnPoints);
            if (path.Count < 2)
            {
                commandMessage = "Troop routes must stay inside the selected nation's territory.";
                return;
            }

            if (selectedArmyRoute != null)
            {
                int routeSoldiers = 0;
                foreach (TroopGroup group in selectedArmyRoute.groups)
                    routeSoldiers += group.soldierCount;

                if (RelocateRoute(selectedArmyRoute, path))
                    commandMessage = $"Redrew the route for {routeSoldiers} soldiers.";
                return;
            }

            int soldiers = selectedCity.armyCount;
            List<TroopGroup> arrivingGroups = BuildTroopGroups(selectedNation.id, soldiers, path);
            List<TroopGroup> departingGroups = SplitTroops(
                selectedNation.id, soldiers, new Vector2(selectedCity.position.x, selectedCity.position.y));
            if (!TryBuildMovementPlans(departingGroups, arrivingGroups, selectedNation.id, out List<List<Vector2>> movementPaths))
            {
                commandMessage = "No friendly-territory route exists to that line.";
                return;
            }

            ArmyRoute route = new ArmyRoute
            {
                nationId = selectedNation.id,
                points = path,
                groups = new List<TroopGroup>()
            };
            armyRoutes.Add(route);
            nationSimulator.DeployTroops(selectedCity, soldiers);
            StartMovements(departingGroups, movementPaths, route, null, null);
            selectedArmyRoute = route;
            commandMessage = $"Deployed {soldiers} soldiers from {selectedCity.name}.";
        }

        private List<Vector3> GetValidRoutePath(List<Vector3> rawPath)
        {
            List<Vector3> validPath = new List<Vector3>(rawPath.Count);
            if (selectedNation == null)
                return validPath;

            for (int i = 0; i < rawPath.Count; i++)
            {
                Vector3 point = rawPath[i];
                if (!IsInsideSelectedTerritory(point))
                    break;

                if (validPath.Count > 0 && !IsSegmentInsideSelectedTerritory(validPath[validPath.Count - 1], point))
                    break;

                validPath.Add(point);
            }

            return validPath;
        }

        private bool RelocateRoute(ArmyRoute route, List<Vector3> destinationPath)
        {
            List<TroopGroup> departingGroups = SplitTroops(route.groups);
            if (departingGroups.Count == 0)
            {
                commandMessage = "There are no stationed troops to move.";
                return false;
            }

            int totalSoldiers = 0;
            foreach (TroopGroup group in departingGroups)
                totalSoldiers += group.soldierCount;

            List<TroopGroup> arrivingGroups = destinationPath.Count == 1
                ? CreateGroupsAt(destinationPath[0], departingGroups)
                : BuildTroopGroups(route.nationId, totalSoldiers, destinationPath);

            if (!TryBuildMovementPlans(departingGroups, arrivingGroups, route.nationId, out List<List<Vector2>> movementPaths))
            {
                commandMessage = "No friendly-territory route exists to that destination.";
                return false;
            }

            route.points = destinationPath;
            route.groups.Clear();
            StartMovements(departingGroups, movementPaths, route, null, null);
            return true;
        }

        private void MoveRouteToCity(ArmyRoute route, City destination)
        {
            List<TroopGroup> departingGroups = SplitTroops(route.groups);
            if (departingGroups.Count == 0)
            {
                commandMessage = "There are no stationed troops to return.";
                return;
            }

            List<TroopGroup> arrivingGroups = new List<TroopGroup>(departingGroups.Count);
            foreach (TroopGroup group in departingGroups)
                arrivingGroups.Add(new TroopGroup(
                    route.nationId, group.soldierCount, new Vector2(destination.position.x, destination.position.y)));

            if (!TryBuildMovementPlans(departingGroups, arrivingGroups, route.nationId, out List<List<Vector2>> movementPaths))
            {
                commandMessage = $"No friendly-territory route exists to {destination.name}.";
                return;
            }

            route.groups.Clear();
            StartMovements(departingGroups, movementPaths, null, destination, route);
            commandMessage = $"Troops are returning to {destination.name}.";
        }

        private List<TroopGroup> SplitTroops(List<TroopGroup> groups)
        {
            List<TroopGroup> result = new List<TroopGroup>();
            foreach (TroopGroup group in groups)
            {
                int remaining = group.soldierCount;
                while (remaining > 0)
                {
                    int count = Mathf.Min(10, remaining);
                    result.Add(new TroopGroup(group.nationId, count, group.position));
                    remaining -= count;
                }
            }
            return result;
        }

        private List<TroopGroup> CreateGroupsAt(Vector3 position, List<TroopGroup> sourceGroups)
        {
            List<TroopGroup> result = new List<TroopGroup>(sourceGroups.Count);
            foreach (TroopGroup group in sourceGroups)
                result.Add(new TroopGroup(group.nationId, group.soldierCount, new Vector2(position.x, position.y)));
            return result;
        }

        private List<TroopGroup> SplitTroops(int nationId, int soldiers, Vector2 position)
        {
            return SplitTroops(new List<TroopGroup> { new TroopGroup(nationId, soldiers, position) });
        }

        private bool TryBuildMovementPlans(
            List<TroopGroup> departingGroups,
            List<TroopGroup> arrivingGroups,
            int nationId,
            out List<List<Vector2>> movementPaths)
        {
            movementPaths = new List<List<Vector2>>(departingGroups.Count);
            if (departingGroups.Count != arrivingGroups.Count)
                return false;

            for (int i = 0; i < departingGroups.Count; i++)
            {
                if (!TryFindFriendlyPath(nationId, departingGroups[i].position, arrivingGroups[i].position, out List<Vector2> path))
                    return false;

                movementPaths.Add(path);
            }

            return true;
        }

        private void StartMovements(
            List<TroopGroup> groups,
            List<List<Vector2>> paths,
            ArmyRoute destinationRoute,
            City destinationCity,
            ArmyRoute routeToRemoveOnArrival)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                movingTroops.Add(new MovingTroop
                {
                    group = groups[i],
                    path = paths[i],
                    nextWaypoint = 1,
                    destinationRoute = destinationRoute,
                    destinationCity = destinationCity,
                    routeToRemoveOnArrival = routeToRemoveOnArrival
                });
            }
        }

        private bool TryFindFriendlyPath(int nationId, Vector2 start, Vector2 destination, out List<Vector2> path)
        {
            path = null;
            if (worldGenerator == null || worldGenerator.Grid == null)
                return false;

            int width = worldGenerator.width;
            int height = worldGenerator.height;
            int startX = Mathf.FloorToInt(start.x);
            int startY = Mathf.FloorToInt(start.y);
            int endX = Mathf.FloorToInt(destination.x);
            int endY = Mathf.FloorToInt(destination.y);
            if (!IsFriendlyCell(startX, startY, nationId) || !IsFriendlyCell(endX, endY, nationId))
                return false;

            int cellCount = width * height;
            if (pathSearchVisited == null || pathSearchVisited.Length != cellCount)
            {
                pathSearchVisited = new int[cellCount];
                pathSearchPrevious = new int[cellCount];
                pathSearchVersion = 0;
            }

            if (pathSearchVersion == int.MaxValue)
            {
                System.Array.Clear(pathSearchVisited, 0, pathSearchVisited.Length);
                pathSearchVersion = 0;
            }
            int searchVersion = ++pathSearchVersion;
            int startIndex = startY * width + startX;
            int endIndex = endY * width + endX;
            Queue<int> open = new Queue<int>();
            open.Enqueue(startIndex);
            pathSearchVisited[startIndex] = searchVersion;
            pathSearchPrevious[startIndex] = -1;

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };
            while (open.Count > 0)
            {
                int current = open.Dequeue();
                if (current == endIndex)
                    break;

                int x = current % width;
                int y = current / width;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nextX = x + dx[direction];
                    int nextY = y + dy[direction];
                    if (!IsFriendlyCell(nextX, nextY, nationId))
                        continue;

                    int next = nextY * width + nextX;
                    if (pathSearchVisited[next] == searchVersion)
                        continue;

                    pathSearchVisited[next] = searchVersion;
                    pathSearchPrevious[next] = current;
                    open.Enqueue(next);
                }
            }

            if (pathSearchVisited[endIndex] != searchVersion)
                return false;

            List<Vector2> reversed = new List<Vector2>();
            for (int current = endIndex; current != -1; current = pathSearchPrevious[current])
                reversed.Add(new Vector2(current % width + 0.5f, current / width + 0.5f));
            reversed.Reverse();
            path = new List<Vector2>(reversed.Count + 2) { start };
            path.AddRange(reversed);
            if (Vector2.Distance(path[path.Count - 1], destination) > 0.01f)
                path.Add(destination);
            return true;
        }

        private bool IsFriendlyCell(int x, int y, int nationId)
        {
            return x >= 0 && x < worldGenerator.width
                && y >= 0 && y < worldGenerator.height
                && worldGenerator.Grid[y * worldGenerator.width + x].nationId == nationId;
        }

        private bool IsInsideSelectedTerritory(Vector3 worldPosition)
        {
            if (worldGenerator == null || selectedNation == null)
                return false;

            int x = Mathf.FloorToInt(worldPosition.x);
            int y = Mathf.FloorToInt(worldPosition.y);
            if (x < 0 || x >= worldGenerator.width || y < 0 || y >= worldGenerator.height)
                return false;

            return worldGenerator.Grid[y * worldGenerator.width + x].nationId == selectedNation.id;
        }

        private bool IsSegmentInsideSelectedTerritory(Vector3 start, Vector3 end)
        {
            float length = Vector3.Distance(start, end);
            int samples = Mathf.Max(1, Mathf.CeilToInt(length * 2f));
            for (int i = 1; i <= samples; i++)
            {
                if (!IsInsideSelectedTerritory(Vector3.Lerp(start, end, i / (float)samples)))
                    return false;
            }
            return true;
        }

        private bool IsCityAt(Vector3 worldPos) => FindCityAt(worldPos) != null;

        private City FindCityAt(Vector3 worldPos, float extraHitRadius = 0f)
        {
            if (worldGenerator == null || worldGenerator.Nations == null)
                return null;

            Vector2 point = new Vector2(worldPos.x, worldPos.y);
            for (int n = 0; n < worldGenerator.Nations.Count; n++)
            {
                foreach (City city in worldGenerator.Nations[n].cities)
                {
                    float hitRadius = (city.isCapital ? 4.5f : 3.5f) + extraHitRadius;
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
            UpdateMovingTroops();

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

        private void UpdateMovingTroops()
        {
            float movementPerTroop = Mathf.Max(0f, troopMoveSpeed) * Time.deltaTime;
            for (int i = movingTroops.Count - 1; i >= 0; i--)
            {
                MovingTroop moving = movingTroops[i];
                // Each rectangle receives a full movement budget this frame, so a
                // formation moves concurrently instead of sharing one budget.
                float distanceThisFrame = movementPerTroop;
                while (distanceThisFrame > 0f && moving.nextWaypoint < moving.path.Count)
                {
                    Vector2 target = moving.path[moving.nextWaypoint];
                    float remaining = Vector2.Distance(moving.group.position, target);
                    if (remaining <= distanceThisFrame)
                    {
                        moving.group.position = target;
                        distanceThisFrame -= remaining;
                        moving.nextWaypoint++;
                    }
                    else
                    {
                        moving.group.position = Vector2.MoveTowards(moving.group.position, target, distanceThisFrame);
                        distanceThisFrame = 0f;
                    }
                }

                if (moving.nextWaypoint < moving.path.Count)
                    continue;

                CompleteMovement(moving);
                movingTroops.RemoveAt(i);
            }
        }

        private void CompleteMovement(MovingTroop moving)
        {
            if (moving.destinationRoute != null)
            {
                // A field garrison is represented by one marker; merge arrivals into it.
                if (moving.destinationRoute.points.Count == 1 && moving.destinationRoute.groups.Count > 0)
                    moving.destinationRoute.groups[0].soldierCount += moving.group.soldierCount;
                else
                    moving.destinationRoute.groups.Add(moving.group);
            }

            if (moving.destinationCity != null)
            {
                if (moving.routeToRemoveOnArrival != null)
                    nationSimulator.ReturnFieldTroops(moving.destinationCity, moving.group.soldierCount);
                else
                    moving.destinationCity.armyCount += moving.group.soldierCount;
            }

            if (moving.routeToRemoveOnArrival != null && CountPendingMovementsFor(moving.routeToRemoveOnArrival) <= 1)
            {
                armyRoutes.Remove(moving.routeToRemoveOnArrival);
                if (selectedArmyRoute == moving.routeToRemoveOnArrival)
                    selectedArmyRoute = null;
            }
        }

        private int CountPendingMovementsFor(ArmyRoute route)
        {
            int count = 0;
            foreach (MovingTroop moving in movingTroops)
            {
                if (moving.routeToRemoveOnArrival == route)
                    count++;
            }
            return count;
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
            if (IsEscapePressed() && selectedCity != null)
            {
                selectedCity = null;
                worldRenderer.SetSelectedCity(null);
                commandMessage = "City deselected.";
            }

            if (IsSpacePressed()) isRunning = !isRunning;
            if (IsSPressed() && !isRunning && nationSimulator != null) nationSimulator.StepSimulation(tickInterval);
            if (IsRPressed()) Regenerate();
            if (IsFPressed() && cameraController != null && worldGenerator != null) cameraController.FocusOnMap(worldGenerator.width, worldGenerator.height);

            if (Is1Pressed()) speedMultiplier = 1;
            if (Is2Pressed()) speedMultiplier = 2;
            if (Is3Pressed()) speedMultiplier = 5;
            if (Is4Pressed()) speedMultiplier = 10;
        }

        private bool IsEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
            return false;
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
            selectedCity = null;
            selectedArmyRoute = null;
            armyRoutes.Clear();
            movingTroops.Clear();
            troopPathPreview.Clear();
            worldRenderer.SetSelectedNation(-1);
            worldRenderer.SetSelectedCity(null);

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
            GUILayout.Label("• <b>Scroll</b>: Zoom | <b>Arrow keys/MMB</b>: Pan");
            GUILayout.Label("• <b>LMB territory</b>: Select nation | <b>RMB city</b>: Recruit");
            GUILayout.Label("• <b>LMB drag city→city</b>: Move troops");
            GUILayout.Label("• Select a city, then <b>RMB-click</b> a garrison or <b>RMB-drag</b> a route");
            GUILayout.Label("• <b>Esc</b>: Deselect the current city");
            GUILayout.Label("• LMB a black route or troop marker, then RMB-drag to redraw it");
            GUILayout.Label("• With a route selected, RMB-click to turn it into a field garrison");
            GUILayout.Label("• With units selected, RMB-click a friendly city to return them");
            GUILayout.Label("• Routes may be drawn only inside your selected territory");
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

            DrawArmyRouteOverlays();
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

        private List<TroopGroup> BuildTroopGroups(int nationId, int soldiers, List<Vector3> path)
        {
            const int soldiersPerGroup = 10;
            int groupCount = Mathf.CeilToInt(soldiers / (float)soldiersPerGroup);
            List<TroopGroup> groups = new List<TroopGroup>(groupCount);
            float totalLength = GetPathLength(path);

            for (int i = 0; i < groupCount; i++)
            {
                int count = Mathf.Min(soldiersPerGroup, soldiers - i * soldiersPerGroup);
                float distance = totalLength * (i + 1) / (groupCount + 1);
                Vector3 position = GetPointOnPath(path, distance);
                groups.Add(new TroopGroup(nationId, count, new Vector2(position.x, position.y)));
            }

            return groups;
        }

        private void DrawArmyRouteOverlays()
        {
            foreach (ArmyRoute route in armyRoutes)
            {
                if (route == selectedArmyRoute)
                    DrawWorldPath(route.points, Color.white, 10f);
                DrawWorldPath(route.points, Color.black, 6f);
                foreach (TroopGroup group in route.groups)
                {
                    if (route.points.Count == 1)
                        DrawFieldGarrisonMarker(group, route == selectedArmyRoute);
                    else
                        DrawTroopGroupMarker(group, route == selectedArmyRoute);
                }
            }

            foreach (MovingTroop moving in movingTroops)
                DrawTroopGroupMarker(moving.group, false);

            if (troopPathPreview.Count > 1 && selectedNation != null)
            {
                DrawWorldPath(troopPathPreview, Color.black, 6f);
            }
        }

        private void DrawWorldPath(List<Vector3> path, Color color, float thickness)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = WorldToGui(path[i - 1]);
                Vector2 b = WorldToGui(path[i]);
                DrawGuiLine(a, b, color, thickness);
            }
        }

        private void DrawTroopGroupMarker(TroopGroup group, bool isSelected)
        {
            Vector2 center = WorldToGui(new Vector3(group.position.x, group.position.y, 0f));
            Rect marker = new Rect(center.x - 10f, center.y - 7f, 20f, 14f);
            Color oldColor = GUI.color;
            if (isSelected)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(marker.x - 2f, marker.y - 2f, marker.width + 4f, marker.height + 4f), Texture2D.whiteTexture);
            }
            GUI.color = Color.black;
            GUI.DrawTexture(marker, Texture2D.whiteTexture);
            DrawGuiLine(new Vector2(marker.x + 3f, marker.y + 3f), new Vector2(marker.xMax - 3f, marker.yMax - 3f), Color.white, 2f);
            DrawGuiLine(new Vector2(marker.xMax - 3f, marker.y + 3f), new Vector2(marker.x + 3f, marker.yMax - 3f), Color.white, 2f);
            GUI.color = oldColor;
        }

        private void DrawFieldGarrisonMarker(TroopGroup group, bool isSelected)
        {
            EnsureGarrisonMarkerTexture();

            Vector2 center = WorldToGui(new Vector3(group.position.x, group.position.y, 0f));
            Rect marker = new Rect(center.x - 13f, center.y - 13f, 26f, 26f);
            Color originalColor = GUI.color;

            if (isSelected)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(marker.x - 3f, marker.y - 3f, marker.width + 6f, marker.height + 6f), garrisonMarkerTexture);
            }

            GUI.color = Color.black;
            GUI.DrawTexture(marker, garrisonMarkerTexture);
            GUI.color = originalColor;

            if (fieldGarrisonLabelStyle == null)
            {
                fieldGarrisonLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                fieldGarrisonLabelStyle.normal.textColor = Color.white;
            }

            GUI.Label(marker, group.soldierCount.ToString(), fieldGarrisonLabelStyle);
        }

        private void EnsureGarrisonMarkerTexture()
        {
            if (garrisonMarkerTexture != null)
                return;

            const int size = 32;
            garrisonMarkerTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float radiusSquared = center * center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    pixels[y * size + x] = dx * dx + dy * dy <= radiusSquared
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            garrisonMarkerTexture.SetPixels32(pixels);
            garrisonMarkerTexture.Apply(false);
        }

        private ArmyRoute FindArmyRouteAt(Vector3 worldPosition)
        {
            // Hit-test in screen pixels so the clickable area matches the visible
            // overlay at every zoom level, rather than becoming huge when zoomed out.
            const float lineHitDistance = 5f;
            Vector2 point = WorldToGui(worldPosition);
            foreach (ArmyRoute route in armyRoutes)
            {
                foreach (TroopGroup group in route.groups)
                {
                    Vector2 center = WorldToGui(new Vector3(group.position.x, group.position.y, 0f));
                    bool isGarrison = route.points.Count == 1;
                    Rect marker = isGarrison
                        ? new Rect(center.x - 13f, center.y - 13f, 26f, 26f)
                        : new Rect(center.x - 10f, center.y - 7f, 20f, 14f);
                    if (isGarrison
                        ? Vector2.Distance(point, center) <= 13f
                        : marker.Contains(point))
                        return route;
                }

                for (int i = 1; i < route.points.Count; i++)
                {
                    Vector2 start = WorldToGui(route.points[i - 1]);
                    Vector2 end = WorldToGui(route.points[i]);
                    if (DistanceToSegment(point, start, end) <= lineHitDistance)
                        return route;
                }
            }

            return null;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.001f)
                return Vector2.Distance(point, start);

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private Vector2 WorldToGui(Vector3 worldPosition)
        {
            Vector3 screenPosition = mainCam.WorldToScreenPoint(worldPosition);
            return new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        }

        private void DrawGuiLine(Vector2 from, Vector2 to, Color color, float width)
        {
            if (overlayTexture == null)
            {
                overlayTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                overlayTexture.SetPixel(0, 0, Color.white);
                overlayTexture.Apply(false);
            }

            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;

            Matrix4x4 originalMatrix = GUI.matrix;
            Color originalColor = GUI.color;
            GUI.color = color;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width * 0.5f, length, width), overlayTexture);
            GUI.matrix = originalMatrix;
            GUI.color = originalColor;
        }

        private static float GetPathLength(List<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        private static Vector3 GetPointOnPath(List<Vector3> path, float targetDistance)
        {
            float traversed = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float segmentLength = Vector3.Distance(path[i - 1], path[i]);
                if (traversed + segmentLength >= targetDistance)
                {
                    float t = (targetDistance - traversed) / segmentLength;
                    return Vector3.Lerp(path[i - 1], path[i], t);
                }
                traversed += segmentLength;
            }
            return path[path.Count - 1];
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
                if (StartCityTransfer(transferOrigin, transferDestination, percentage))
                {
                    commandMessage = $"Troops are moving from {transferOrigin.name} to {transferDestination.name}.";
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

        private bool StartCityTransfer(City origin, City destination, float percentage)
        {
            int soldiers = Mathf.FloorToInt(origin.armyCount * percentage / 100f);
            if (soldiers <= 0)
                return false;

            List<TroopGroup> departingGroups = SplitTroops(
                origin.nationId, soldiers, new Vector2(origin.position.x, origin.position.y));
            List<TroopGroup> arrivingGroups = CreateGroupsAt(
                new Vector3(destination.position.x, destination.position.y, 0f), departingGroups);
            if (!TryBuildMovementPlans(departingGroups, arrivingGroups, origin.nationId, out List<List<Vector2>> movementPaths))
            {
                commandMessage = $"No friendly-territory route exists to {destination.name}.";
                return false;
            }

            // The soldiers are reserved immediately, but do not join the destination
            // garrison until their on-map rectangles actually arrive.
            origin.armyCount -= soldiers;
            StartMovements(departingGroups, movementPaths, null, destination, null);
            return true;
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

            if (selectedNation != null)
            {
                DrawControlledNationPanel(panelWidth, rightMargin);
                int leaderboardEntries = Mathf.Min(6, worldGenerator.Nations.Count);
                int leaderboardHeight = 45 + leaderboardEntries * 22;
                DrawMiniLeaderboard(panelWidth, rightMargin, Screen.height - leaderboardHeight - 15);
                return;
            }

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

        private void DrawControlledNationPanel(int panelWidth, int rightMargin)
        {
            const int maxCitiesToDisplay = 10;
            int displayedCities = Mathf.Min(maxCitiesToDisplay, selectedNation.cities.Count);
            int cardHeight = 205 + displayedCities * 24;
            cardHeight = Mathf.Min(cardHeight, Screen.height - 30);
            Rect cardRect = new Rect(Screen.width - panelWidth - rightMargin, 15, panelWidth, cardHeight);

            GUI.Box(cardRect, "⚔️ Controlled Nation");
            GUILayout.BeginArea(new Rect(cardRect.x + 10, cardRect.y + 30, panelWidth - 20, cardHeight - 40));

            Color originalColor = GUI.color;
            GUI.color = worldRenderer.selectedNationColor;
            GUILayout.Label($"<size=15><b>■ {selectedNation.name}</b></size>");
            GUI.color = originalColor;

            GUILayout.Label($"<b>Territory:</b> {selectedNation.territorySize:N0} pixels");
            GUILayout.Label($"<b>Treasury:</b> <color=#FFD700>{selectedNation.treasury:F1} gold</color>");
            GUILayout.Label($"<b>Income / Upkeep:</b> +{selectedNation.incomePerSec:F1} / -{selectedNation.upkeepPerSec:F1} per sec");

            float netIncome = selectedNation.netIncomePerSec;
            string netColor = netIncome >= 0f ? "#55FF55" : "#FF5555";
            string netPrefix = netIncome >= 0f ? "+" : "";
            GUILayout.Label($"<b>Net cashflow:</b> <color={netColor}>{netPrefix}{netIncome:F1} / sec</color>");
            GUILayout.Label($"<b>Military:</b> {selectedNation.armyCount} / {selectedNation.maxArmyTarget} soldiers");

            GUILayout.Space(6);
            GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
            GUILayout.Label("<b>🏰 CITY GARRISONS</b>");

            for (int i = 0; i < displayedCities; i++)
            {
                City city = selectedNation.cities[i];
                string cityType = city.isCapital ? "Capital" : "City";
                GUILayout.Label($"{cityType}: {city.name} — <b>{city.armyCount}</b> soldiers");
            }

            if (selectedNation.cities.Count > maxCitiesToDisplay)
            {
                GUILayout.Label($"<i>+ {selectedNation.cities.Count - maxCitiesToDisplay} more cities</i>");
            }

            GUILayout.EndArea();
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
