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

        [Header("Conveyor Görseli")]
        [SerializeField] private GameObject conveyorCellPrefab;

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

        /// <summary>
        /// HAREKET/animasyon için — artık SADECE gerçek köşelerde nokta
        /// içeriyor (düz bir yol tek segment). Az sayıda, ucuz.
        /// </summary>
        public List<Vector3> WaypointWorldPositions { get; private set; } = new();
        public List<Vector2Int> WaypointBlockOrigins { get; private set; } = new();
        public List<Vector3> WaypointFacingDirections { get; private set; } = new();

        /// <summary>
        /// TESLİMAT kontrolü için — eski, HÜCRE BAŞINA bir nokta olan tam
        /// çözünürlüklü liste. "t" (0..1), Base'den Exit'e olan konum
        /// oranı (index bazlı, kabaca eşit aralıklı) — Tray bunu, hareket
        /// waypoint'lerinden TAMAMEN bağımsız olarak, "genel yolculuk
        /// ilerlemesi"ne göre sırayla tetikler. Bu ayrım, düz yolda
        /// waypoint sayısını azaltırken teslimat hassasiyetini (her
        /// hücre kontrol edilir) hiç kaybetmememizi sağlıyor — VE iki
        /// teslimatın asla aynı frame'de sıkışmamasını garanti ediyor,
        /// çünkü artık her checkpoint gerçek zamana (t) yayılmış durumda.
        /// </summary>
        public List<(float t, Vector2Int cell)> DeliveryCheckpoints { get; private set; } = new();

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
                    if (type == CellType.Empty) continue;

                    Vector3 pos = Grid.GetCellCenterWorld(r, c);

                    if (type == CellType.Conveyor || type == CellType.BaseTray)
                    {
                        if (conveyorCellPrefab == null) continue;
                        SpawnFromPool(conveyorCellPrefab, pos, conveyorCellPrefab.transform.rotation, $"Conveyor_{r}_{c}");
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
        }

        private GameObject SpawnFromPool(GameObject prefab, Vector3 pos, Quaternion rot, string name)
        {
            GameObject instance = ObjectPool.Instance != null
                ? ObjectPool.Instance.Get(prefab, pos, rot, transform)
                : Instantiate(prefab, pos, rot, transform);
            instance.name = name;
            return instance;
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

        /// <summary>
        /// Fine-grained corner listesinden, SADECE gerçek yön değişimlerinde
        /// (dirIn != dirOut) bir nokta tutan azaltılmış bir liste üretir.
        /// Düz bir uzantı boyunca ardışık noktaların hepsi aynı yönde
        /// ilerlediği için elenir — HAREKET path'i artık gerçekten
        /// "Base -> köşe1 -> köşe2 -> ... -> Exit" kadar az noktaya sahip.
        /// </summary>
        private static List<(Vector2Int cell, Vector2Int corner)> DecimateStraightRuns(List<(Vector2Int cell, Vector2Int corner)> corners)
        {
            if (corners.Count <= 2) return new List<(Vector2Int, Vector2Int)>(corners);

            var result = new List<(Vector2Int cell, Vector2Int corner)> { corners[0] };
            for (int i = 1; i < corners.Count - 1; i++)
            {
                Vector2Int dirIn = corners[i].corner - corners[i - 1].corner;
                Vector2Int dirOut = corners[i + 1].corner - corners[i].corner;
                if (dirIn == dirOut) continue; // düz devam ediyor, bu ara nokta gereksiz
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

            // NOT: Burada eskiden path'in dünya Z ekseninde her zaman
            // pozitif yönde başlamasını zorlayan bir "path.Reverse()"
            // bloğu vardı. O blok KALDIRILDI çünkü ConveyorPathBuilder
            // artık Tray Base'e göre DOĞRU yönü zaten seçiyor (bkz.
            // ConveyorPathBuilder.BuildPath — Tray Base tanımlıysa ilk
            // adımı ondan uzağa giden yönü otomatik buluyor) — bu Z
            // tabanlı zorlama, o doğru kararı görmezden gelip path'i
            // tekrar Tray Base yönüne çevirebiliyordu. Yön kontrolü artık
            // TEK bir yerde (ConveyorPathBuilder) ve TEK bir kaynağa
            // (Tray Base konumu, yoksa manuel reversePathDirection) göre
            // yapılıyor.

            var fineCorners = BuildCornerSequence(data, path);

            // ---- TESLİMAT checkpoint'leri: fine (hücre başına 1) çözünürlük ----
            int fineCount = fineCorners.Count;
            for (int i = 0; i < fineCount; i++)
            {
                float t = fineCount > 1 ? i / (float)(fineCount - 1) : 0f;
                DeliveryCheckpoints.Add((t, fineCorners[i].cell));
            }

            // ---- HAREKET waypoint'leri: sadece gerçek köşeler ----
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

                    // Fine-grained teslimat checkpoint'lerini de küçük mavi
                    // noktalarla göster — "gerçekten her hücre kontrol
                    // ediliyor mu" diye doğrulayabilesin.
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