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
            new FoodCustomerPrefab { food = FoodType.Dessert },
        };

        [Header("Koordinat Yönü — (0,0) HER ZAMAN bu objenin transform pozisyonunda olur")]
        [SerializeField] private bool invertRow = false;
        [SerializeField] private bool invertCol = false;
        [SerializeField] private bool swapAxes = true;

        [Header("Conveyor Yönü")]
        [Tooltip("Varsayılan (işaretsiz) hal artık doğru yönü veriyor. Sadece ters gerekirse işaretle.")]
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
        public int ExitWaypointIndex { get; private set; } = -1;

        void Awake()
        {
            if (unityGrid == null) unityGrid = GetComponent<Grid>();
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
            ClearExisting();
            BuildWaypoints(data);

            for (int r = 0; r < data.rows; r++)
            {
                for (int c = 0; c < data.columns; c++)
                {
                    CellType type = data.GetCell(r, c);
                    if (type == CellType.Empty) continue;

                    Vector3 pos = Grid.GetCellCenterWorld(r, c);

                    if (type == CellType.Conveyor)
                    {
                        if (conveyorCellPrefab == null) continue;
                        var go = Instantiate(conveyorCellPrefab, pos, conveyorCellPrefab.transform.rotation, transform);
                        go.name = $"Conveyor_{r}_{c}";
                    }
                    else if (type == CellType.CustomerSlot)
                    {
                        if (!data.TryGetCustomerFood(r, c, out FoodType food)) continue;
                        GameObject prefab = GetCustomerPrefab(food);
                        if (prefab == null)
                        {
                            Debug.LogWarning($"'{food}' için customer prefab atanmamış (GridManager > Customer Prefabs).");
                            continue;
                        }
                        var go = Instantiate(prefab, pos, prefab.transform.rotation, transform);
                        go.name = $"Customer_{food}_{r}_{c}";
                    }
                }
            }
        }

        /// Bir bloğun (origin = sol-üst hücre) TAM ORTASININ world
        /// pozisyonu — origin hücresi ile karşı köşe (origin+1,+1)
        /// hücresinin merkezlerinin ortalaması. Base/Exit dahil her
        /// waypoint bunu kullanıyor.
        private Vector3 GetBlockCenterWorld(Vector2Int origin)
        {
            Vector3 a = Grid.GetCellCenterWorld(origin.x, origin.y);
            Vector3 b = Grid.GetCellCenterWorld(origin.x + 1, origin.y + 1);
            return (a + b) * 0.5f;
        }

        private void BuildWaypoints(LevelData data)
        {
            WaypointWorldPositions.Clear();
            ExitWaypointIndex = -1;

            var path = ConveyorPathBuilder.BuildPath(data, out bool valid, out string reason, reversePathDirection);
            if (!valid)
            {
                Debug.LogWarning($"Conveyor waypoint path geçersiz: {reason}");
                return;
            }

            foreach (var blockOrigin in path)
                WaypointWorldPositions.Add(GetBlockCenterWorld(blockOrigin));

            ExitWaypointIndex = ConveyorPathBuilder.FindExitIndex(data, path);
        }

        private GameObject GetCustomerPrefab(FoodType food)
        {
            foreach (var entry in customerPrefabs)
                if (entry.food == food) return entry.prefab;
            return null;
        }

        void ClearExisting()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
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
                    Vector3 p1 = center + new Vector3(-half.x, 0, -half.z);
                    Vector3 p2 = center + new Vector3(half.x, 0, -half.z);
                    Vector3 p3 = center + new Vector3(half.x, 0, half.z);
                    Vector3 p4 = center + new Vector3(-half.x, 0, half.z);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p3);
                    Gizmos.DrawLine(p3, p4);
                    Gizmos.DrawLine(p4, p1);

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
                var path = ConveyorPathBuilder.BuildPath(levelData, out bool valid, out string reason, reversePathDirection);
                if (valid)
                {
                    int exitIdx = ConveyorPathBuilder.FindExitIndex(levelData, path);
                    var style = new GUIStyle { normal = { textColor = Color.white }, fontSize = 11, fontStyle = FontStyle.Bold };
                    float yOffset = Mathf.Max(g.cellSize.x, g.cellSize.z) * 0.4f;

                    Vector3 GetWorld(Vector2Int origin)
                    {
                        Vector3 a = gizmoGrid.GetCellCenterWorld(origin.x, origin.y);
                        Vector3 b = gizmoGrid.GetCellCenterWorld(origin.x + 1, origin.y + 1);
                        return (a + b) * 0.5f;
                    }

                    for (int i = 0; i < path.Count; i++)
                    {
                        Vector3 pos = GetWorld(path[i]) + Vector3.up * yOffset;

                        Gizmos.color = (i == exitIdx) ? Color.red : Color.magenta;
                        Gizmos.DrawSphere(pos, waypointRadius);
                        Handles.Label(pos + Vector3.up * (waypointRadius * 1.5f), i.ToString(), style);

                        if (i > 0)
                        {
                            Vector3 prev = GetWorld(path[i - 1]) + Vector3.up * yOffset;
                            Handles.color = Color.magenta;
                            Handles.DrawAAPolyLine(6f, prev, pos);
                        }
                    }
                    if (path.Count > 1)
                    {
                        Vector3 last = GetWorld(path[^1]) + Vector3.up * yOffset;
                        Vector3 first = GetWorld(path[0]) + Vector3.up * yOffset;
                        Handles.color = new Color(1f, 0f, 1f, 0.4f);
                        Handles.DrawDottedLine(last, first, 4f);
                    }
                }
            }
#endif
        }
    }
}