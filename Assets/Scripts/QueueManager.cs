using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct FoodTypePrefab
    {
        public FoodType food;
        public GameObject prefab;
    }

    public class QueueManager : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Boş bırakılırsa GridManager'daki LevelDataRef kullanılır.")]
        [SerializeField] private LevelData levelData;
        [SerializeField] private GridManager gridManager;

        [Header("Food Prefabs — her yemek tipi için conveyor'a giren gerçek Food prefabı")]
        [SerializeField]
        private List<FoodTypePrefab> foodPrefabs = new()
        {
            new FoodTypePrefab { food = FoodType.Hamburger },
            new FoodTypePrefab { food = FoodType.Fries },
            new FoodTypePrefab { food = FoodType.Drink },
            new FoodTypePrefab { food = FoodType.Sushi },
            new FoodTypePrefab { food = FoodType.Steak },
            new FoodTypePrefab { food = FoodType.Dessert },
        };

        [Header("Yerleşim — sabit Transform listesi YOK, matematikle hesaplanıyor")]
        [Tooltip("Queue'nun sahnedeki başlangıç noktası — satır 0, sütun 0'ın (varsayımsal, TAM dolu bir satırdaki) yeri.")]
        [SerializeField] private Transform originPoint;
        [Tooltip("Bir hücrenin kapladığı yatay/dikey mesafe (dünya birimi).")]
        [SerializeField] private float cellSpacingX = 1f;
        [SerializeField] private float cellSpacingZ = 1f;
        [Tooltip("Yemekler queue'nun (origin/slot yüksekliğinin) ne kadar yukarısında dursun.")]
        [SerializeField] private float foodYOffset = 0.3f;

        [Header("Görünür Satır Sayısı")]
        [Tooltip("Şu an sabit 3 — ileride level bazlı ayarlanabilir hale getirilecek.")]
        [SerializeField] private int visibleRows = 3;

        [Header("Kilitli (row>0) satırların soluklaştırma miktarı")]
        [Range(0f, 1f)]
        [SerializeField] private float lockedAlpha = 0.35f;

        private readonly Dictionary<int, List<QueueEntry>> columnData = new();
        private readonly Dictionary<int, List<GameObject>> columnVisuals = new();
        private readonly Dictionary<Food, int> availableFoodColumn = new();

        private void Start()
        {
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;
            if (levelData == null) gridManager = FindFirstObjectByType<GridManager>();
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;

            if (levelData == null)
            {
                Debug.LogError("QueueManager: LevelData bulunamadı.");
                enabled = false;
                return;
            }

            if (originPoint == null)
            {
                Debug.LogError("QueueManager: Origin Point atanmamış.");
                enabled = false;
                return;
            }

            BuildColumnData();
            RebuildAllVisibleRows();
        }

        private void BuildColumnData()
        {
            columnData.Clear();
            for (int col = 0; col < levelData.queueColumns; col++)
            {
                columnData[col] = levelData.queue
                    .Where(e => e.col == col)
                    .OrderBy(e => e.row)
                    .ToList();
            }
        }

        /// <summary>
        /// TÜM görünür satırları (0..visibleRows-1) sıfırdan kurar. Sabit
        /// slot Transform'ları yerine, HER SATIRIN o anki dolu kolon
        /// sayısına göre pozisyonlar hesaplanır ve merkeze hizalanır —
        /// referans görseldeki "2'li satır ortada 2 tane, 4'lü satır baştan
        /// sona 4 tane" görünümü budur.
        /// </summary>
        private void RebuildAllVisibleRows()
        {
            ClearAllVisuals();

            for (int visualRow = 0; visualRow < visibleRows; visualRow++)
            {
                // Bu satırda (visualRow'daki, yani her kolonun kendi
                // veri listesinden visualRow'ıncı elemanı) DOLU olan
                // kolonları bul.
                var occupiedCols = new List<(int col, QueueEntry entry)>();
                for (int col = 0; col < levelData.queueColumns; col++)
                {
                    if (columnData.TryGetValue(col, out var list) && visualRow < list.Count)
                        occupiedCols.Add((col, list[visualRow]));
                }

                if (occupiedCols.Count == 0) continue;

                // Ortalama: N tane item varsa, merkez etrafında
                // -((N-1)/2)..+((N-1)/2) aralığında x ofseti dağıt.
                int n = occupiedCols.Count;
                for (int i = 0; i < n; i++)
                {
                    float xOffset = (i - (n - 1) / 2f) * cellSpacingX;
                    float zOffset = -visualRow * cellSpacingZ; // satır arttıkça geriye/aşağıya

                    Vector3 pos = originPoint.position + originPoint.right * xOffset + originPoint.forward * zOffset;
                    pos.y += foodYOffset;

                    SpawnAt(occupiedCols[i].col, visualRow, occupiedCols[i].entry, pos);
                }
            }
        }

        private void SpawnAt(int col, int visualRow, QueueEntry entry, Vector3 worldPos)
        {
            GameObject prefab = GetPrefab(entry.food);
            if (prefab == null)
            {
                Debug.LogWarning($"QueueManager: '{entry.food}' için Food Prefabs listesinde prefab yok.");
                return;
            }

            var go = Instantiate(prefab, worldPos, prefab.transform.rotation);

            if (!columnVisuals.TryGetValue(col, out var visuals))
            {
                visuals = new List<GameObject>();
                columnVisuals[col] = visuals;
            }
            visuals.Add(go);

            var food = go.GetComponent<Food>();
            if (food == null)
            {
                Debug.LogWarning($"'{prefab.name}' prefabında Food component'i yok.");
                return;
            }

            if (visualRow == 0)
            {
                food.PresetQueueState(FoodState.AvailableInQueue);
                availableFoodColumn[food] = col;
                food.StateChanged += OnAvailableFoodStateChanged;
            }
            else
            {
                food.PresetQueueState(FoodState.LockedInQueue);
                ApplyLockedVisual(go);
            }
        }

        private void ClearAllVisuals()
        {
            foreach (var visuals in columnVisuals.Values)
            {
                foreach (var go in visuals)
                {
                    if (go == null) continue;

                    var food = go.GetComponent<Food>();
                    if (food != null)
                    {
                        if (food.CurrentState == FoodState.OnConveyor ||
                            food.CurrentState == FoodState.InFoodSlot)
                        {
                            continue;
                        }

                        if (availableFoodColumn.ContainsKey(food))
                        {
                            food.StateChanged -= OnAvailableFoodStateChanged;
                            availableFoodColumn.Remove(food);
                        }
                    }

                    Destroy(go);
                }
            }
            columnVisuals.Clear();
            availableFoodColumn.Clear();
        }

        /// <summary>
        /// row0'daki (Available) bir food'un state'i değişince tetiklenir.
        /// OnConveyor'a geçtiyse, o kolonun en üstü tüketildi — veriden
        /// düş, TÜM görünür satırları yeniden kur (kalan her satır bir
        /// üst pozisyona ışınlanmış olur, ve her satır yeniden ortalanır).
        /// </summary>
        private void OnAvailableFoodStateChanged(Food food, FoodState newState)
        {
            if (newState != FoodState.OnConveyor) return;
            if (!availableFoodColumn.TryGetValue(food, out int col)) return;

            food.StateChanged -= OnAvailableFoodStateChanged;
            availableFoodColumn.Remove(food);

            if (columnData.TryGetValue(col, out var list) && list.Count > 0)
                list.RemoveAt(0);

            RebuildAllVisibleRows();
        }

        private GameObject GetPrefab(FoodType food)
        {
            foreach (var entry in foodPrefabs)
                if (entry.food == food) return entry.prefab;
            return null;
        }

        private void ApplyLockedVisual(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r.material.HasProperty("_Color"))
                {
                    var c = r.material.color;
                    c.a = lockedAlpha;
                    r.material.color = c;
                }
            }
        }
    }
}