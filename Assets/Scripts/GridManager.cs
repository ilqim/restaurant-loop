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

        [Header("Conveyor Görseli (her Conveyor hücresine 1x1 basılır — 2x2 boyama zaten görsel genişlik veriyor, ekstra ölçekleme YOK)")]
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
            new FoodCustomerPrefab { food = FoodType.Dessert },
        };

        [Header("Koordinat Yönü — (0,0) HER ZAMAN bu objenin transform pozisyonunda olur")]
        [SerializeField] private bool invertRow = false;
        [SerializeField] private bool invertCol = false;
        [SerializeField] private bool swapAxes = true;

        [Header("Conveyor Yönü")]
        [SerializeField] private bool reversePathDirection = false;

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

        public int ExitWaypointIndex { get; private set; } = -1;

        private readonly Dictionary<Vector2Int, GameObject> spawnedCustomers = new();

        void Awake()
        {
            if (unityGrid == null) unityGrid = GetComponent<Grid>();
            if (customerManager == null) customerManager = FindFirstObjectByType<CustomerManager>();

            int r = levelData != null ? levelData.rows : 10;
            int c = levelData != null ? levelData.columns : 10;

            Grid = new GameGrid(
                unityGrid,
                r,
                c,
                invertRow,
                invertCol,
                swapAxes
            );
        }

        void Start()
        {
            if (levelData != null)
                BuildFromData(levelData);
        }

        public void BuildFromData(LevelData data)
        {
            levelData = data;

            Grid = new GameGrid(
                unityGrid,
                data.rows,
                data.columns,
                invertRow,
                invertCol,
                swapAxes
            );

            ClearExisting();
            BuildWaypoints(data);

            if (customerManager == null)
            {
                Debug.LogWarning(
                    "GridManager: CustomerManager bulunamadı — spawn edilen müşteriler kaydedilemeyecek, hiçbir food teslim edilemez."
                );
            }

            for (int r = 0; r < data.rows; r++)
            {
                for (int c = 0; c < data.columns; c++)
                {
                    CellType type = data.GetCell(r, c);

                    if (type == CellType.Empty)
                        continue;

                    Vector3 pos = Grid.GetCellCenterWorld(r, c);

                    if (type == CellType.Conveyor)
                    {
                        if (conveyorCellPrefab == null)
                            continue;

                        SpawnFromPool(
                            conveyorCellPrefab,
                            pos,
                            conveyorCellPrefab.transform.rotation,
                            $"Conveyor_{r}_{c}"
                        );
                    }
                    else if (type == CellType.CustomerSlot)
                    {
                        if (!data.TryGetCustomerFood(r, c, out FoodType food))
                            continue;

                        GameObject prefab = GetCustomerPrefab(food);

                        if (prefab == null)
                        {
                            Debug.LogWarning(
                                $"'{food}' için customer prefab atanmamış (GridManager > Customer Prefabs)."
                            );
                            continue;
                        }

                        var instance = SpawnFromPool(
                            prefab,
                            pos,
                            prefab.transform.rotation,
                            $"Customer_{food}_{r}_{c}"
                        );

                        spawnedCustomers[new Vector2Int(r, c)] = instance;

                        var customerComp = instance.GetComponent<Customer>();

                        if (customerComp != null)
                            customerComp.Init(r, c, food, customerManager);
                        else
                            Debug.LogWarning(
                                $"'{prefab.name}' prefabında Customer component'i yok."
                            );
                    }
                }
            }
        }

        private GameObject SpawnFromPool(
            GameObject prefab,
            Vector3 pos,
            Quaternion rot,
            string name
        )
        {
            GameObject instance = ObjectPool.Instance != null
                ? ObjectPool.Instance.Get(prefab, pos, rot, transform)
                : Instantiate(prefab, pos, rot, transform);

            instance.name = name;

            return instance;
        }

        private Vector3 GetBlockCenterWorld(int originRow, int originCol)
        {
            Vector3 a = Grid.GetCellCenterWorld(originRow, originCol);
            Vector3 b = Grid.GetCellCenterWorld(originRow + 1, originCol + 1);

            return (a + b) * 0.5f;
        }

        private static Vector3 GetGridCornerWorld(
            GameGrid grid,
            int rowLine,
            int colLine
        )
        {
            Vector3 origin = grid.GetCellCenterWorld(0, 0);

            Vector3 rowStep =
                grid.GetCellCenterWorld(1, 0) - origin;

            Vector3 colStep =
                grid.GetCellCenterWorld(0, 1) - origin;

            return origin
                - rowStep * 0.5f
                - colStep * 0.5f
                + rowStep * rowLine
                + colStep * colLine;
        }

        /// <summary>
        /// Bir hücre için 2x2 conveyor'ın orta çizgisindeki
        /// grid köşe noktasını hesaplar.
        ///
        /// Önemli:
        /// Waypoint hiçbir zaman hücre merkezine veya hücre kenarına
        /// yerleştirilmez. Her zaman gerçek grid çizgilerinin kesişimindedir.
        /// </summary>
        private static Vector2Int GetCenterlineCorner(
            LevelData data,
            List<Vector2Int> path,
            int index
        )
        {
            Vector2Int cell = path[index];

            Vector2Int dirIn = Vector2Int.zero;
            Vector2Int dirOut = Vector2Int.zero;

            if (index > 0)
                dirIn = cell - path[index - 1];

            if (index < path.Count - 1)
                dirOut = path[index + 1] - cell;

            Vector2Int direction =
                dirOut != Vector2Int.zero
                    ? dirOut
                    : dirIn;

            int rowLine;
            int colLine;

            // Yatay hareket:
            // row genişlik eksenidir.
            if (direction.y != 0)
            {
                Vector2Int? widthCell = null;

                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (Mathf.Abs(dr) + Mathf.Abs(dc) != 1)
                            continue;

                        Vector2Int candidate =
                            cell + new Vector2Int(dr, dc);

                        if (candidate == cell)
                            continue;

                        if (candidate.x == cell.x)
                            continue;

                        if (candidate.x < 0 ||
                            candidate.x >= data.rows ||
                            candidate.y < 0 ||
                            candidate.y >= data.columns)
                            continue;

                        if (data.GetCell(candidate.x, candidate.y)
                            != CellType.Conveyor)
                            continue;

                        widthCell = candidate;
                        break;
                    }

                    if (widthCell.HasValue)
                        break;
                }

                if (widthCell.HasValue)
                {
                    int minRow =
                        Mathf.Min(cell.x, widthCell.Value.x);

                    rowLine = minRow + 1;
                }
                else
                {
                    rowLine =
                        direction.x >= 0
                            ? cell.x + 1
                            : cell.x;
                }

                colLine =
                    direction.y >= 0
                        ? cell.y + 1
                        : cell.y;
            }
            else
            {
                // Dikey hareket:
                // col genişlik eksenidir.
                Vector2Int? widthCell = null;

                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        if (Mathf.Abs(dr) + Mathf.Abs(dc) != 1)
                            continue;

                        Vector2Int candidate =
                            cell + new Vector2Int(dr, dc);

                        if (candidate == cell)
                            continue;

                        if (candidate.y == cell.y)
                            continue;

                        if (candidate.x < 0 ||
                            candidate.x >= data.rows ||
                            candidate.y < 0 ||
                            candidate.y >= data.columns)
                            continue;

                        if (data.GetCell(candidate.x, candidate.y)
                            != CellType.Conveyor)
                            continue;

                        widthCell = candidate;
                        break;
                    }

                    if (widthCell.HasValue)
                        break;
                }

                if (widthCell.HasValue)
                {
                    int minCol =
                        Mathf.Min(cell.y, widthCell.Value.y);

                    colLine = minCol + 1;
                }
                else
                {
                    colLine =
                        direction.y >= 0
                            ? cell.y + 1
                            : cell.y;
                }

                rowLine =
                    direction.x >= 0
                        ? cell.x + 1
                        : cell.x;
            }

            return new Vector2Int(rowLine, colLine);
        }

        private static List<(Vector2Int cell, Vector2Int corner)>
            BuildCornerSequence(
                LevelData data,
                List<Vector2Int> path
            )
        {
            var result =
                new List<(Vector2Int cell, Vector2Int corner)>();

            if (path == null || path.Count == 0)
                return result;

            // BASE:
            // 2x2 bloğun tam merkezindeki grid kesişimi.
            Vector2Int baseCorner =
                new Vector2Int(
                    data.baseRow + 1,
                    data.baseCol + 1
                );

            result.Add(
                (path[0], baseCorner)
            );

            // Ara waypointler.
            for (int i = 1; i < path.Count - 1; i++)
            {
                Vector2Int corner =
                    GetCenterlineCorner(
                        data,
                        path,
                        i
                    );

                if (result.Count > 0 &&
                    result[^1].corner == corner)
                    continue;

                result.Add(
                    (path[i], corner)
                );
            }

            // EXIT:
            // 2x2 bloğun tam merkezindeki grid kesişimi.
            if (path.Count > 1)
            {
                Vector2Int exitCorner =
                    new Vector2Int(
                        data.exitRow + 1,
                        data.exitCol + 1
                    );

                if (result.Count == 0 ||
                    result[^1].corner != exitCorner)
                {
                    result.Add(
                        (
                            path[path.Count - 1],
                            exitCorner
                        )
                    );
                }
            }

            return result;
        }

        private void BuildWaypoints(LevelData data)
        {
            WaypointWorldPositions.Clear();
            WaypointBlockOrigins.Clear();
            ExitWaypointIndex = -1;

            var fullPath =
                ConveyorPathBuilder.BuildPath(
                    data,
                    out bool valid,
                    out string reason,
                    reversePathDirection
                );

            if (!valid)
            {
                Debug.LogWarning(
                    $"Conveyor waypoint path geçersiz: {reason}"
                );

                return;
            }

            int exitIdx =
                ConveyorPathBuilder.FindExitIndex(
                    data,
                    fullPath
                );

            if (exitIdx < 0)
            {
                Debug.LogWarning(
                    "Conveyor path: Exit indexi bulunamadı — tüm kontur kullanılacak."
                );

                exitIdx = fullPath.Count - 1;
            }

            var path =
                fullPath.GetRange(
                    0,
                    exitIdx + 1
                );

            /*
             * Base'den çıkış yönü world Z yönünde pozitif olmalı.
             *
             * İlk gerçek path adımının world Z'sini kontrol ediyoruz.
             * Negatif yöndeyse path yönünü ters çeviriyoruz.
             *
             * Böylece:
             * Base merkezi
             *      ↓
             * ilk waypoint
             *      ↓
             * ikinci waypoint
             *
             * world Z boyunca artarak ilerler.
             */
            if (path.Count >= 3)
            {
                Vector3 baseWorld =
                    GetGridCornerWorld(
                        Grid,
                        data.baseRow + 1,
                        data.baseCol + 1
                    );

                Vector2Int firstCorner =
                    GetCenterlineCorner(
                        data,
                        path,
                        1
                    );

                Vector3 firstWorld =
                    GetGridCornerWorld(
                        Grid,
                        firstCorner.x,
                        firstCorner.y
                    );

                if (firstWorld.z < baseWorld.z)
                {
                    path.Reverse();
                }
            }

            var corners =
                BuildCornerSequence(
                    data,
                    path
                );

            foreach (var (cell, corner) in corners)
            {
                WaypointBlockOrigins.Add(cell);

                WaypointWorldPositions.Add(
                    GetGridCornerWorld(
                        Grid,
                        corner.x,
                        corner.y
                    )
                );
            }

            ExitWaypointIndex =
                WaypointWorldPositions.Count - 1;
        }

        private GameObject GetCustomerPrefab(FoodType food)
        {
            foreach (var entry in customerPrefabs)
            {
                if (entry.food == food)
                    return entry.prefab;
            }

            return null;
        }

        void ClearExisting()
        {
            spawnedCustomers.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child =
                    transform.GetChild(i).gameObject;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(child);
                    continue;
                }
#endif

                Destroy(child);
            }
        }

        public bool TryGetCellFromScreenPoint(
            Camera cam,
            Vector2 screenPos,
            out int row,
            out int col
        )
        {
            Ray ray =
                cam.ScreenPointToRay(screenPos);

            Plane groundPlane =
                new Plane(
                    Vector3.up,
                    transform.position
                );

            if (groundPlane.Raycast(
                    ray,
                    out float distance
                ))
            {
                Vector3 worldPoint =
                    ray.GetPoint(distance);

                return Grid.TryGetRowColFromWorld(
                    worldPoint,
                    out row,
                    out col
                );
            }

            row = col = -1;
            return false;
        }

        void OnDrawGizmos()
        {
            if (!drawGridGizmo || levelData == null)
                return;

            var g =
                unityGrid != null
                    ? unityGrid
                    : GetComponent<Grid>();

            if (g == null)
                return;

            var gizmoGrid =
                new GameGrid(
                    g,
                    levelData.rows,
                    levelData.columns,
                    invertRow,
                    invertCol,
                    swapAxes
                );

            Gizmos.color = gizmoLineColor;

            Vector3 half =
                new Vector3(
                    g.cellSize.x * 0.5f,
                    0,
                    g.cellSize.z * 0.5f
                );

            float waypointRadius =
                Mathf.Max(
                    g.cellSize.x,
                    g.cellSize.z
                ) * 0.22f;

            for (int r = 0; r < levelData.rows; r++)
            {
                for (int c = 0; c < levelData.columns; c++)
                {
                    Vector3 center =
                        gizmoGrid.GetCellCenterWorld(r, c);

                    Vector3 p1 =
                        center +
                        new Vector3(
                            -half.x,
                            0,
                            -half.z
                        );

                    Vector3 p2 =
                        center +
                        new Vector3(
                            half.x,
                            0,
                            -half.z
                        );

                    Vector3 p3 =
                        center +
                        new Vector3(
                            half.x,
                            0,
                            half.z
                        );

                    Vector3 p4 =
                        center +
                        new Vector3(
                            -half.x,
                            0,
                            half.z
                        );

                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p3);
                    Gizmos.DrawLine(p3, p4);
                    Gizmos.DrawLine(p4, p1);

#if UNITY_EDITOR
                    bool isCorner =
                        (r == 0 ||
                         r == levelData.rows - 1) &&
                        (c == 0 ||
                         c == levelData.columns - 1);

                    if (drawCoordinateLabels && isCorner)
                    {
                        var style =
                            new GUIStyle
                            {
                                normal =
                                {
                                    textColor = Color.cyan
                                },
                                fontSize = 11,
                                fontStyle = FontStyle.Bold
                            };

                        Handles.Label(
                            center + Vector3.up * 0.3f,
                            $"({r},{c})",
                            style
                        );
                    }
#endif
                }
            }

#if UNITY_EDITOR
            if (drawWaypointPath)
            {
                var fullPath =
                    ConveyorPathBuilder.BuildPath(
                        levelData,
                        out bool valid,
                        out string reason,
                        reversePathDirection
                    );

                if (valid)
                {
                    int exitIdx =
                        ConveyorPathBuilder.FindExitIndex(
                            levelData,
                            fullPath
                        );

                    if (exitIdx < 0)
                        exitIdx = fullPath.Count - 1;

                    var path =
                        fullPath.GetRange(
                            0,
                            exitIdx + 1
                        );

                    var corners =
                        BuildCornerSequence(
                            levelData,
                            path
                        );

                    var style =
                        new GUIStyle
                        {
                            normal =
                            {
                                textColor = Color.white
                            },
                            fontSize = 11,
                            fontStyle = FontStyle.Bold
                        };

                    float yOffset =
                        Mathf.Max(
                            g.cellSize.x,
                            g.cellSize.z
                        ) * 0.4f;

                    Vector3? prevDrawn = null;

                    for (int i = 0; i < corners.Count; i++)
                    {
                        var corner =
                            corners[i].corner;

                        Vector3 drawPos =
                            GetGridCornerWorld(
                                gizmoGrid,
                                corner.x,
                                corner.y
                            ) +
                            Vector3.up * yOffset;

                        bool isExit =
                            i == corners.Count - 1;

                        Gizmos.color =
                            isExit
                                ? Color.red
                                : (i == 0
                                    ? Color.yellow
                                    : Color.magenta);

                        Gizmos.DrawSphere(
                            drawPos,
                            waypointRadius
                        );

                        Handles.Label(
                            drawPos +
                            Vector3.up *
                            (waypointRadius * 1.5f),
                            i.ToString(),
                            style
                        );

                        if (prevDrawn.HasValue)
                        {
                            Handles.color =
                                Color.magenta;

                            Handles.DrawAAPolyLine(
                                6f,
                                prevDrawn.Value,
                                drawPos
                            );
                        }

                        prevDrawn = drawPos;
                    }
                }
            }
#endif
        }
    }
}