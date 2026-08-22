using System.Collections;
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
        [Tooltip("ÖNEMLİ: Bu prefabların artık Collider'a ihtiyacı YOK. Tıklanabilir alan QueueSlot prefabında.")]
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

        [Header("Queue Slot Prefab — collider'ı taşıyan, tıklanabilir hücre")]
        [Tooltip("QueueSlot component'i taşıyan prefab. Her görünür hücre için food ile BİRLİKTE bu da spawn edilir.")]
        [SerializeField] private GameObject queueSlotPrefab;

        [Header("Yerleşim — sabit Transform listesi YOK, matematikle hesaplanıyor")]
        [Tooltip("Queue'nun sahnedeki başlangıç noktası — grid'in merkezinin hizalandığı yer.")]
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
        private readonly Dictionary<int, List<GameObject>> columnSlotVisuals = new();
        private readonly Dictionary<Food, int> availableFoodColumn = new();
        private Coroutine pendingRebuild;

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

            if (queueSlotPrefab == null)
            {
                Debug.LogError("QueueManager: Queue Slot Prefab atanmamış.");
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
        /// TÜM görünür satırları (0..visibleRows-1) sıfırdan kurar.
        ///
        /// ÖNEMLİ: Her item'ın X pozisyonu KENDİ SABİT kolon index'ine
        /// göre hesaplanır (levelData.queueColumns'a göre BİR KEZ
        /// ortalanmış grid pozisyonu) — o satırda o anda kaç kolonun
        /// dolu olduğuna göre YENİDEN merkezlenmez. Böylece bir kolon
        /// tükenip boşaldığında sadece o hücre boş kalır, DİĞER kolonlar
        /// asla kaymaz / çapraz durmaz — gerçek bir sabit grid gibi
        /// davranır (örn. 1,2,3 / 4,5,6 düzeninde "1" gidince "4" tam
        /// "1"in eski yerine gelir, "2" ve "3" hiç kıpırdamaz).
        ///
        /// Her hücrede food'un YANINDA bir QueueSlot da spawn edilir —
        /// tıklanabilir collider food'ta değil, bu QueueSlot'ta duruyor.
        /// </summary>
        private void RebuildAllVisibleRows()
        {
            ClearAllVisuals();

            for (int visualRow = 0; visualRow < visibleRows; visualRow++)
            {
                for (int col = 0; col < levelData.queueColumns; col++)
                {
                    if (!columnData.TryGetValue(col, out var list) || visualRow >= list.Count)
                        continue;

                    QueueEntry entry = list[visualRow];

                    float xOffset = (col - (levelData.queueColumns - 1) / 2f) * cellSpacingX;
                    float zOffset = -visualRow * cellSpacingZ;

                    Vector3 basePos = originPoint.position + originPoint.right * xOffset + originPoint.forward * zOffset;
                    Vector3 foodPos = basePos;
                    foodPos.y += foodYOffset;

                    SpawnAt(col, visualRow, entry, basePos, foodPos);
                }
            }
        }

        private void SpawnAt(int col, int visualRow, QueueEntry entry, Vector3 slotPos, Vector3 foodPos)
        {
            GameObject prefab = GetPrefab(entry.food);
            if (prefab == null)
            {
                Debug.LogWarning($"QueueManager: '{entry.food}' için Food Prefabs listesinde prefab yok.");
                return;
            }

            var foodGo = Instantiate(prefab, foodPos, prefab.transform.rotation);

            if (!columnVisuals.TryGetValue(col, out var visuals))
            {
                visuals = new List<GameObject>();
                columnVisuals[col] = visuals;
            }
            visuals.Add(foodGo);

            var food = foodGo.GetComponent<Food>();
            if (food == null)
            {
                Debug.LogWarning($"'{prefab.name}' prefabında Food component'i yok.");
                return;
            }

            // Bu food'un kaç teslimat hakkı olduğu — level tasarımında
            // seçilen "Yerleştirilecek Kapasite" değeri (sabit 10 DEĞİL,
            // her hücre için ayrı seçilebiliyor).
            food.PresetCapacity(entry.capacity);

            // Tıklanabilir hücre — food'un pozisyonuyla AYNI kaynaktan
            // (slotPos) türetiliyor, iki ayrı yerde offset tanımlanmıyor.
            var slotGo = Instantiate(queueSlotPrefab, slotPos, Quaternion.identity);
            if (!columnSlotVisuals.TryGetValue(col, out var slotVisuals))
            {
                slotVisuals = new List<GameObject>();
                columnSlotVisuals[col] = slotVisuals;
            }
            slotVisuals.Add(slotGo);

            var queueSlot = slotGo.GetComponent<QueueSlot>();
            if (queueSlot == null)
            {
                Debug.LogWarning($"'{queueSlotPrefab.name}' prefabında QueueSlot component'i yok.");
            }
            else
            {
                queueSlot.AssignFood(food);
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
                ApplyLockedVisual(foodGo);
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

            // QueueSlot'lar hiçbir gameplay state'i taşımıyor, sadece
            // "hangi food burada" bilgisini tutuyor — food konveyöre
            // geçse bile queue hücresi olarak burası artık geçersiz,
            // o yüzden istisnasız hepsi yok edilip yeniden kuruluyor.
            foreach (var slotVisuals in columnSlotVisuals.Values)
            {
                foreach (var slotGo in slotVisuals)
                {
                    if (slotGo == null) continue;
                    Destroy(slotGo);
                }
            }
            columnSlotVisuals.Clear();
        }

        private void OnAvailableFoodStateChanged(Food food, FoodState newState)
        {
            if (newState != FoodState.OnConveyor) return;
            if (!availableFoodColumn.TryGetValue(food, out int col)) return;

            food.StateChanged -= OnAvailableFoodStateChanged;
            availableFoodColumn.Remove(food);

            if (columnData.TryGetValue(col, out var list) && list.Count > 0)
                list.RemoveAt(0);

            if (pendingRebuild != null) StopCoroutine(pendingRebuild);
            pendingRebuild = StartCoroutine(RebuildNextFrame());
        }

        private IEnumerator RebuildNextFrame()
        {
            yield return null;
            pendingRebuild = null;
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