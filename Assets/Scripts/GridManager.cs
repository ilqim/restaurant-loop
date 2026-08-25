using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RestaurantLoop
{
    [System.Serializable]
    public struct FoodCustomerPrefab
    {
        public FoodType food;
        public GameObject prefab;
    }

    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Grid))]
    public class GridManager : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private LevelData levelData;

        [Header("References")]
        [SerializeField] private Grid unityGrid;
        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private CustomerManager customerManager;

        [Header("Conveyor Segment Prefabları (Tam Otomatik Yerleşim)")]
        [SerializeField] private GameObject straightConveyorPrefab;
        [SerializeField] private GameObject innerCornerConveyorPrefab;
        [SerializeField] private GameObject outerCornerConveyorPrefab;
        [SerializeField] private GameObject startConveyorPrefab;
        [SerializeField] private GameObject exitConveyorPrefab;
        [SerializeField] private GameObject baseOpeningPrefab;
        [SerializeField] private GameObject baseCoverPrefab;

        [Header("Customer Prefabs — her yemek tipi için ayrı prefab sürükle")]
        [SerializeField]
        private List<FoodCustomerPrefab> customerPrefabs = new()
        {
            new FoodCustomerPrefab { food = FoodType.Hamburger },
            new FoodCustomerPrefab { food = FoodType.Fries },
            new FoodCustomerPrefab { food = FoodType.Drink },
            new FoodCustomerPrefab { food = FoodType.Sushi },
            new FoodCustomerPrefab { food = FoodType.Steak },
            new FoodCustomerPrefab { food = FoodType.Donut },
        };

        [Header("Koordinat Yönü — (0,0) HER ZAMAN bu objenin transform pozisyonunda olur")]
        [SerializeField] private bool invertRow = false;
        [SerializeField] private bool invertCol = false;
        [SerializeField] private bool swapAxes = true;

        [Header("Conveyor Yönü")]
        [SerializeField] private bool reversePathDirection = false;

        [Header("Köşe Yuvarlatma")]
        [SerializeField] private float cornerRadius = 0.4f;
        [SerializeField, Range(1, 12)] private int cornerSegments = 4;

        [Header("Tepsi Yönelimi")]
        [SerializeField] private bool invertFacingSide = false;

        [Header("Editor Gizmo")]
        [SerializeField] private bool drawGridGizmo = true;
        [SerializeField] private Color gizmoLineColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private bool drawCoordinateLabels = true;
        [SerializeField] private bool drawWaypointPath = true;

        public GameGrid Grid { get; private set; }
        public LevelData LevelDataRef => levelData;
        public Grid UnityGridRef => unityGrid;

        public List<Vector3> WaypointWorldPositions { get; private set; } = new();
        public List<Vector2Int> WaypointBlockOrigins { get; private set; } = new();
        public List<Vector3> WaypointFacingDirections { get; private set; } = new();
        public List<(float t, Vector2Int cell)> DeliveryCheckpoints { get; private set; } = new();

        public List<WaypointMoveAxis> WaypointMoveAxes { get; private set; } = new();
        public int ExitWaypointIndex { get; private set; } = -1;
        public Vector3 GridCenterWorld { get; private set; }

        private readonly Dictionary<Vector2Int, GameObject> spawnedCustomers = new();

        void Awake()
        {
            if (unityGrid == null) unityGrid = GetComponent<Grid>();
            if (customerManager == null) customerManager = FindFirstObjectByType<CustomerManager>();

            int r = levelData != null ? levelData.rows : 10;
            int c = levelData != null ? levelData.columns : 10;
            Grid = new GameGrid(unityGrid, r, c, invertRow, invertCol, swapAxes);
        }

        void Start()
        {
            if (levelData != null) BuildFromData(levelData);
        }

        public Vector3 GetTrayBaseCenterWorld()
        {
            if (levelData == null || levelData.trayBaseRow < 0 || levelData.trayBaseCol < 0 || Grid == null)
                return transform.position;

            Vector3 p00 = Grid.GetCellCenterWorld(levelData.trayBaseRow, levelData.trayBaseCol);
            int r1 = Mathf.Min(levelData.rows - 1, levelData.trayBaseRow + LevelData.ConveyorBlockSize - 1);
            int c1 = Mathf.Min(levelData.columns - 1, levelData.trayBaseCol + LevelData.ConveyorBlockSize - 1);
            Vector3 p11 = Grid.GetCellCenterWorld(r1, c1);

            return (p00 + p11) * 0.5f;
        }

        public void BuildFromData(LevelData data)
        {
            levelData = data;
            Grid = new GameGrid(unityGrid, data.rows, data.columns, invertRow, invertCol, swapAxes);

            GridCenterWorld = (GetGridCornerWorld(Grid, 0, 0) + GetGridCornerWorld(Grid, data.rows, data.columns)) * 0.5f;

            ClearExisting();
            BuildWaypoints(data);

            if (customerManager == null)
                Debug.LogWarning("GridManager: CustomerManager bulunamadı.");

            for (int r = 0; r < data.rows; r++)
            {
                for (int c = 0; c < data.columns; c++)
                {
                    CellType type = data.GetCell(r, c);
                    if (type == CellType.Empty || type == CellType.BaseTray) continue;

                    Vector3 pos = Grid.GetCellCenterWorld(r, c);

                    if (type == CellType.Conveyor)
                    {
                        if (type == CellType.Conveyor)
                        {
                            SpawnConveyorTile(r, c);
                        }
                    }
                    else if (type == CellType.CustomerSlot)
                    {
                        if (!data.TryGetCustomerFood(r, c, out FoodType food)) continue;

                        GameObject prefab = GetCustomerPrefab(food);
                        if (prefab == null)
                        {
                            Debug.LogWarning($"'{food}' için customer prefab atanmamış.");
                            continue;
                        }

                        var instance = SpawnFromPool(prefab, pos, prefab.transform.rotation, $"Customer_{food}_{r}_{c}");
                        spawnedCustomers[new Vector2Int(r, c)] = instance;

                        var customerComp = instance.GetComponent<Customer>();
                        if (customerComp != null)
                            customerComp.Init(r, c, food, customerManager);
                        else
                            Debug.LogWarning($"'{prefab.name}' prefabında Customer component'i yok.");
                    }
                }
            }
            SpawnTrayBaseTiles(data);
        }

        private void SpawnConveyorTile(int row, int col)
        {
            var info = ConveyorAutoTiler.Classify(levelData, Grid, row, col);
            GameObject prefab = GetTilePrefab(info.Type);
            if (prefab == null) return;

            Vector3 pos = Grid.GetCellCenterWorld(row, col);
            Quaternion rot = info.Forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(info.Forward, Vector3.up)
                : prefab.transform.rotation;

            SpawnFromPool(prefab, pos, rot, $"Conveyor_{info.Type}_{row}_{col}");
        }

        private GameObject GetTilePrefab(ConveyorTileType type) => type switch
        {
            ConveyorTileType.Straight => straightConveyorPrefab,
            ConveyorTileType.InnerCorner => innerCornerConveyorPrefab,
            ConveyorTileType.OuterCorner => outerCornerConveyorPrefab,
            ConveyorTileType.Start => startConveyorPrefab,
            ConveyorTileType.Exit => exitConveyorPrefab,
            ConveyorTileType.BaseOpening => baseOpeningPrefab,
            ConveyorTileType.BaseCover => baseCoverPrefab,
            _ => null
        };

        private void SpawnTrayBaseTiles(LevelData data)
        {
            if (data.trayBaseRow < 0 || data.trayBaseCol < 0) return;
            if (data.baseRow < 0 || data.baseCol < 0) return;

            int rowDiff = data.baseRow - data.trayBaseRow;
            int colDiff = data.baseCol - data.trayBaseCol;
            bool splitByRow = Mathf.Abs(rowDiff) >= Mathf.Abs(colDiff);

            // Start'a yakın taraf hangi index (0 mı 1 mi)?
            int nearIndex = splitByRow ? (rowDiff > 0 ? 1 : 0) : (colDiff > 0 ? 1 : 0);

            // TEK bir eksen yön vektörü — hücre bazlı nokta hesaplaması YOK.
            // Bu sayede tüm Opening tile'ları birebir aynı açıda, tüm Cover
            // tile'ları da bunun tam 180° tersinde durur.
            Vector3 axisDir = splitByRow
                ? Grid.GetCellCenterWorld(data.trayBaseRow + 1, data.trayBaseCol) -
                  Grid.GetCellCenterWorld(data.trayBaseRow, data.trayBaseCol)
                : Grid.GetCellCenterWorld(data.trayBaseRow, data.trayBaseCol + 1) -
                  Grid.GetCellCenterWorld(data.trayBaseRow, data.trayBaseCol);
            axisDir.y = 0f;
            axisDir.Normalize();

            Vector3 openingFacing = nearIndex == 1 ? axisDir : -axisDir;
            Vector3 coverFacing = -openingFacing;

            Quaternion openingRot = Quaternion.LookRotation(openingFacing, Vector3.up);
            Quaternion coverRot = Quaternion.LookRotation(coverFacing, Vector3.up);

            for (int dr = 0; dr < LevelData.ConveyorBlockSize; dr++)
            {
                for (int dc = 0; dc < LevelData.ConveyorBlockSize; dc++)
                {
                    int rr = data.trayBaseRow + dr;
                    int cc = data.trayBaseCol + dc;
                    if (rr >= data.rows || cc >= data.columns) continue;

                    bool isNearSide = splitByRow ? dr == nearIndex : dc == nearIndex;

                    ConveyorTileType type = isNearSide ? ConveyorTileType.BaseOpening : ConveyorTileType.BaseCover;
                    GameObject prefab = GetTilePrefab(type);
                    if (prefab == null)
                    {
                        Debug.LogWarning($"GridManager: {type} prefabı Inspector'da atanmamış — ({rr},{cc}) atlandı.");
                        continue;
                    }

                    Vector3 cellWorld = Grid.GetCellCenterWorld(rr, cc);
                    Quaternion rot = isNearSide ? openingRot : coverRot;

                    SpawnFromPool(prefab, cellWorld, rot, $"TrayBase_{type}_{rr}_{cc}");
                }
            }
        }

        private GameObject SpawnFromPool(GameObject prefab, Vector3 pos, Quaternion rot, string name)
        {
            GameObject instance = ObjectPool.Instance != null
                ? ObjectPool.Instance.Get(prefab, pos, rot, transform)
                : Instantiate(prefab, pos, rot, transform);
            instance.name = name;
            return instance;
        }

        private static Vector3 GetGridCornerWorld(GameGrid grid, int rowLine, int colLine)
        {
            Vector3 origin = grid.GetCellCenterWorld(0, 0);
            Vector3 rowStep = grid.GetCellCenterWorld(1, 0) - origin;
            Vector3 colStep = grid.GetCellCenterWorld(0, 1) - origin;
            return origin - rowStep * 0.5f - colStep * 0.5f + rowStep * rowLine + colStep * colLine;
        }

        private static Vector2Int GetCenterlineCorner(LevelData data, List<Vector2Int> path, int index)
        {
            Vector2Int cell = path[index];
            Vector2Int dirIn = Vector2Int.zero;
            Vector2Int dirOut = Vector2Int.zero;
            if (index > 0) dirIn = cell - path[index - 1];
            if (index < path.Count - 1) dirOut = path[index + 1] - cell;
            Vector2Int direction = dirOut != Vector2Int.zero ? dirOut : dirIn;

            int rowLine, colLine;

            if (direction.y != 0)
            {
                Vector2Int? widthCell = null;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (Mathf.Abs(dr) + Mathf.Abs(dc) != 1) continue;
                        Vector2Int candidate = cell + new Vector2Int(dr, dc);
                        if (candidate == cell || candidate.x == cell.x) continue;
                        if (candidate.x < 0 || candidate.x >= data.rows || candidate.y < 0 || candidate.y >= data.columns) continue;
                        if (data.GetCell(candidate.x, candidate.y) != CellType.Conveyor) continue;
                        widthCell = candidate; break;
                    }
                    if (widthCell.HasValue) break;
                }
                rowLine = widthCell.HasValue ? Mathf.Min(cell.x, widthCell.Value.x) + 1 : (direction.x >= 0 ? cell.x + 1 : cell.x);
                colLine = direction.y >= 0 ? cell.y + 1 : cell.y;
            }
            else
            {
                Vector2Int? widthCell = null;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (Mathf.Abs(dr) + Mathf.Abs(dc) != 1) continue;
                        Vector2Int candidate = cell + new Vector2Int(dr, dc);
                        if (candidate == cell || candidate.y == cell.y) continue;
                        if (candidate.x < 0 || candidate.x >= data.rows || candidate.y < 0 || candidate.y >= data.columns) continue;
                        if (data.GetCell(candidate.x, candidate.y) != CellType.Conveyor) continue;
                        widthCell = candidate; break;
                    }
                    if (widthCell.HasValue) break;
                }
                colLine = widthCell.HasValue ? Mathf.Min(cell.y, widthCell.Value.y) + 1 : (direction.y >= 0 ? cell.y + 1 : cell.y);
                rowLine = direction.x >= 0 ? cell.x + 1 : cell.x;
            }

            return new Vector2Int(rowLine, colLine);
        }

        private static List<(Vector2Int cell, Vector2Int corner)> BuildCornerSequence(LevelData data, List<Vector2Int> path)
        {
            var result = new List<(Vector2Int cell, Vector2Int corner)>();
            if (path == null || path.Count == 0) return result;

            Vector2Int baseCorner = new Vector2Int(data.baseRow + 1, data.baseCol + 1);
            result.Add((path[0], baseCorner));

            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2Int corner = GetCenterlineCorner(data, path, i);
                if (result.Count > 0 && result[^1].corner == corner) continue;
                result.Add((path[i], corner));
            }

            if (path.Count > 1)
            {
                Vector2Int exitCorner = new Vector2Int(data.exitRow + 1, data.exitCol + 1);
                if (result.Count == 0 || result[^1].corner != exitCorner)
                    result.Add((path[path.Count - 1], exitCorner));
            }

            return result;
        }

        private static List<(Vector2Int cell, Vector2Int corner)> DecimateStraightRuns(List<(Vector2Int cell, Vector2Int corner)> corners)
        {
            if (corners.Count <= 2) return new List<(Vector2Int, Vector2Int)>(corners);

            var result = new List<(Vector2Int cell, Vector2Int corner)> { corners[0] };
            for (int i = 1; i < corners.Count - 1; i++)
            {
                Vector2Int dirIn = corners[i].corner - corners[i - 1].corner;
                Vector2Int dirOut = corners[i + 1].corner - corners[i].corner;
                if (dirIn == dirOut) continue;
                result.Add(corners[i]);
            }
            result.Add(corners[^1]);
            return result;
        }

        private void BuildWaypoints(LevelData data)
        {
            WaypointWorldPositions.Clear();
            WaypointBlockOrigins.Clear();
            WaypointFacingDirections.Clear();
            DeliveryCheckpoints.Clear();
            ExitWaypointIndex = -1;

            var fullPath = ConveyorPathBuilder.BuildPath(data, out bool valid, out string reason, reversePathDirection);
            if (!valid)
            {
                Debug.LogWarning($"Conveyor waypoint path geçersiz: {reason}");
                return;
            }

            int exitIdx = ConveyorPathBuilder.FindExitIndex(data, fullPath);
            if (exitIdx < 0)
            {
                Debug.LogWarning("Conveyor path: Exit indexi bulunamadı — tüm kontur kullanılacak.");
                exitIdx = fullPath.Count - 1;
            }

            var path = fullPath.GetRange(0, exitIdx + 1);
            var fineCorners = BuildCornerSequence(data, path);

            int fineCount = fineCorners.Count;
            for (int i = 0; i < fineCount; i++)
            {
                float t = fineCount > 1 ? i / (float)(fineCount - 1) : 0f;
                DeliveryCheckpoints.Add((t, fineCorners[i].cell));
            }

            var movementCorners = DecimateStraightRuns(fineCorners);

            var rawPositions = new List<Vector3>();
            var rawCells = new List<Vector2Int>();
            foreach (var (cell, corner) in movementCorners)
            {
                rawCells.Add(cell);
                rawPositions.Add(GetGridCornerWorld(Grid, corner.x, corner.y));
            }

            PathSmoothing.RoundCorners(
                rawPositions, rawCells, cornerRadius, cornerSegments, invertFacingSide,
                out var smoothedPositions, out var smoothedCells, out var smoothedFacings);

            WaypointWorldPositions.AddRange(smoothedPositions);
            WaypointBlockOrigins.AddRange(smoothedCells);
            WaypointFacingDirections.AddRange(smoothedFacings);

            // ==========================================
            // YENİ EKLENEN KISIM: Eksenleri hesaplayıp listeye kaydediyoruz
            // ==========================================
            WaypointMoveAxes.Clear();
            if (WaypointBlockOrigins.Count > 0)
            {
                // İlk noktanın kıyaslanacak bir önceki noktası yok, None ile başlar
                WaypointMoveAxes.Add(WaypointMoveAxis.None);

                for (int i = 1; i < WaypointBlockOrigins.Count; i++)
                {
                    Vector2Int prevCell = WaypointBlockOrigins[i - 1];
                    Vector2Int currCell = WaypointBlockOrigins[i];
                    Vector2Int delta = currCell - prevCell;

                    if (delta == Vector2Int.zero)
                    {
                        // Aynı hücre içindeyiz (köşe yuvarlatmasının ara noktaları).
                        // None döner, böylece Tray düzlükteki son eksenini korur.
                        WaypointMoveAxes.Add(WaypointMoveAxis.None);
                    }
                    else
                    {
                        // Hücre değişti, ana ilerleme eksenini belirle
                        // Y (Col) değişimi daha büyükse Row ekseninde (Yatay) ilerliyoruz demektir.
                        WaypointMoveAxis axis = Mathf.Abs(delta.y) > Mathf.Abs(delta.x)
                            ? WaypointMoveAxis.Row
                            : WaypointMoveAxis.Col;

                        WaypointMoveAxes.Add(axis);
                    }
                }
            }
            // ==========================================

            ExitWaypointIndex = WaypointWorldPositions.Count - 1;
        }

        private GameObject GetCustomerPrefab(FoodType food)
        {
            foreach (var entry in customerPrefabs)
                if (entry.food == food) return entry.prefab;
            return null;
        }

        void ClearExisting()
        {
            spawnedCustomers.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
                if (!Application.isPlaying) { DestroyImmediate(child); continue; }
#endif
                Destroy(child);
            }
        }

        public bool TryGetCellFromScreenPoint(Camera cam, Vector2 screenPos, out int row, out int col)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            Plane groundPlane = new Plane(Vector3.up, transform.position);
            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPoint = ray.GetPoint(distance);
                return Grid.TryGetRowColFromWorld(worldPoint, out row, out col);
            }
            row = col = -1;
            return false;
        }

        void OnDrawGizmos()
        {
            if (!drawGridGizmo || levelData == null) return;
            var g = unityGrid != null ? unityGrid : GetComponent<Grid>();
            if (g == null) return;

            var gizmoGrid = new GameGrid(g, levelData.rows, levelData.columns, invertRow, invertCol, swapAxes);
            Gizmos.color = gizmoLineColor;

            Vector3 half = new Vector3(g.cellSize.x * 0.5f, 0, g.cellSize.z * 0.5f);
            float waypointRadius = Mathf.Max(g.cellSize.x, g.cellSize.z) * 0.22f;

            for (int r = 0; r < levelData.rows; r++)
            {
                for (int c = 0; c < levelData.columns; c++)
                {
                    Vector3 center = gizmoGrid.GetCellCenterWorld(r, c);
                    Gizmos.DrawLine(center + new Vector3(-half.x, 0, -half.z), center + new Vector3(half.x, 0, -half.z));
                    Gizmos.DrawLine(center + new Vector3(half.x, 0, -half.z), center + new Vector3(half.x, 0, half.z));
                    Gizmos.DrawLine(center + new Vector3(half.x, 0, half.z), center + new Vector3(-half.x, 0, half.z));
                    Gizmos.DrawLine(center + new Vector3(-half.x, 0, half.z), center + new Vector3(-half.x, 0, -half.z));

#if UNITY_EDITOR
                    bool isCorner = (r == 0 || r == levelData.rows - 1) && (c == 0 || c == levelData.columns - 1);
                    if (drawCoordinateLabels && isCorner)
                    {
                        var style = new GUIStyle { normal = { textColor = Color.cyan }, fontSize = 11, fontStyle = FontStyle.Bold };
                        Handles.Label(center + Vector3.up * 0.3f, $"({r},{c})", style);
                    }
#endif
                }
            }

#if UNITY_EDITOR
            if (drawWaypointPath)
            {
                var fullPath = ConveyorPathBuilder.BuildPath(levelData, out bool valid, out string reason, reversePathDirection);
                if (valid)
                {
                    int exitIdx = ConveyorPathBuilder.FindExitIndex(levelData, fullPath);
                    if (exitIdx < 0) exitIdx = fullPath.Count - 1;
                    var path = fullPath.GetRange(0, exitIdx + 1);
                    var fineCorners = BuildCornerSequence(levelData, path);
                    var movementCorners = DecimateStraightRuns(fineCorners);

                    var rawPositions = new List<Vector3>();
                    var rawCells = new List<Vector2Int>();
                    foreach (var (cell, corner) in movementCorners)
                    {
                        rawCells.Add(cell);
                        rawPositions.Add(GetGridCornerWorld(gizmoGrid, corner.x, corner.y));
                    }

                    PathSmoothing.RoundCorners(
                        rawPositions, rawCells, cornerRadius, cornerSegments, invertFacingSide,
                        out var smoothedPositions, out _, out var smoothedFacings);

                    var style = new GUIStyle { normal = { textColor = Color.white }, fontSize = 11, fontStyle = FontStyle.Bold };
                    float yOffset = Mathf.Max(g.cellSize.x, g.cellSize.z) * 0.4f;
                    Vector3? prevDrawn = null;

                    for (int i = 0; i < smoothedPositions.Count; i++)
                    {
                        Vector3 drawPos = smoothedPositions[i] + Vector3.up * yOffset;
                        bool isExit = i == smoothedPositions.Count - 1;
                        bool isBase = i == 0;

                        Gizmos.color = isExit ? Color.red : (isBase ? Color.yellow : Color.magenta);
                        Gizmos.DrawSphere(drawPos, waypointRadius * 0.6f);
                        if (isBase || isExit)
                            Handles.Label(drawPos + Vector3.up * (waypointRadius * 1.5f), isBase ? "Base" : "Exit", style);

                        if (prevDrawn.HasValue)
                        {
                            Handles.color = Color.magenta;
                            Handles.DrawAAPolyLine(4f, prevDrawn.Value, drawPos);
                        }

                        if (i < smoothedFacings.Count)
                        {
                            Gizmos.color = Color.green;
                            Vector3 arrowEnd = drawPos + smoothedFacings[i] * waypointRadius * 2f;
                            Gizmos.DrawLine(drawPos, arrowEnd);
                            Gizmos.DrawSphere(arrowEnd, waypointRadius * 0.25f);
                        }

                        prevDrawn = drawPos;
                    }

                    foreach (var (cell, corner) in fineCorners)
                    {
                        Vector3 p = GetGridCornerWorld(gizmoGrid, corner.x, corner.y) + Vector3.up * (yOffset * 0.5f);
                        Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.6f);
                        Gizmos.DrawSphere(p, waypointRadius * 0.25f);
                    }

                    Vector3 gizmoCenter = (GetGridCornerWorld(gizmoGrid, 0, 0) + GetGridCornerWorld(gizmoGrid, levelData.rows, levelData.columns)) * 0.5f;
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(gizmoCenter, waypointRadius * 0.8f);
                }
            }
#endif
        }
    }
}