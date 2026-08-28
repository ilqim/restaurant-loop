using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct FoodTypePrefab
    {
        public FoodType food;
        public GameObject prefab;
    }

    public class QueueManager : MonoBehaviour, ILevelDataReceiver
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
            new FoodTypePrefab { food = FoodType.Donut },
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

        [Header("Select Booster Camera Settings")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float cameraTransitionDuration = 0.5f;
        [SerializeField] private Ease cameraEase = Ease.InOutQuad;
        [SerializeField] private float queueCameraMargin = 1.5f;
        [SerializeField] private float cameraOffset = 12f; 

        private readonly Dictionary<int, List<QueueEntry>> columnData = new();
        private readonly Dictionary<int, List<GameObject>> columnVisuals = new();
        private readonly Dictionary<int, List<GameObject>> columnSlotVisuals = new();
        private readonly Dictionary<Food, int> availableFoodColumn = new();
        private readonly Dictionary<Food, (int col, int indexInCol)> activeSelectableFoods = new();

        private Coroutine pendingRebuild;
        private bool started;
        private bool isSelectModeActive;

        private Vector3 defaultCamPos;
        private float defaultCamOrthoSize;
        private bool camDefaultsCached;

        public bool IsSelectModeActive => isSelectModeActive;
        public event Action SelectModeEnded;

        public void SetLevelData(LevelData data)
        {
            levelData = data;

            if (started)
            {
                BuildColumnData();
                RebuildAllVisibleRows();
            }
        }

        private void Start()
        {
            started = true;
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;
            if (levelData == null) gridManager = FindFirstObjectByType<GridManager>();
            if (levelData == null && gridManager != null) levelData = gridManager.LevelDataRef;

            if(targetCamera == null) targetCamera = Camera.main;
            if(targetCamera != null && !camDefaultsCached)
            {
                defaultCamPos = targetCamera.transform.position;
                defaultCamOrthoSize = targetCamera.orthographicSize;
                camDefaultsCached = true;
            }

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

        public void EnterSelectBoosterMode()
        {
            if(isSelectModeActive) return;

            isSelectModeActive = true;

            CacheCameraDefaults();

            RebuildEntireQueueForSelection();

            FocusCameraOnQueue();
        }

        public void ExitSelectBoosterMode()
        {
            if(!isSelectModeActive) return;
            isSelectModeActive = false;

            ResetCamera(() => 
            {
                RebuildAllVisibleRows();
                SelectModeEnded?.Invoke();
            });
        }

        private void RebuildEntireQueueForSelection()
        {
            ClearAllVisuals();
            activeSelectableFoods.Clear();

            int maxRowsInAnyCol = columnData.Values.Count > 0 ? columnData.Values.Max(list => list.Count) : 0;

            for (int r = 0; r < maxRowsInAnyCol; r++)
            {
                for (int col = 0; col < levelData.queueColumns; col++)
                {
                    if (!columnData.TryGetValue(col, out var list) || r >= list.Count)
                        continue;

                    QueueEntry entry = list[r];

                    float xOffset = (col - (levelData.queueColumns - 1) / 2f) * cellSpacingX;
                    float zOffset = -r * cellSpacingZ;

                    Vector3 basePos = originPoint.position + originPoint.right * xOffset + originPoint.forward * zOffset;
                    Vector3 foodPos = basePos;
                    foodPos.y += foodYOffset;

                    SpawnSelectableItem(col, r, entry, basePos, foodPos);
                }
            }
        }

        private void SpawnSelectableItem(int col, int indexInCol, QueueEntry entry, Vector3 slotPos, Vector3 foodPos)
        {
            GameObject prefab = GetPrefab(entry.food);
            if (prefab == null) return;

            var foodGo = Instantiate(prefab, foodPos, prefab.transform.rotation);
            if (!columnVisuals.TryGetValue(col, out var visuals))
            {
                visuals = new List<GameObject>();
                columnVisuals[col] = visuals;
            }
            visuals.Add(foodGo);

            var food = foodGo.GetComponent<Food>();
            food.PresetCapacity(entry.capacity);
            food.PresetQueueState(FoodState.AvailableInQueue); // All fully active & bright
            // Select modunda TÜM item'lar tam parlak/aktif görünmeli — burada
            // locked görünümü hiç uygulanmıyor, garanti için resetliyoruz.
            food.ApplyBlockedVisual(false);

            activeSelectableFoods[food] = (col, indexInCol);
            food.StateChanged += OnSelectModeFoodStateChanged;

            var slotGo = Instantiate(queueSlotPrefab, slotPos, queueSlotPrefab.transform.rotation);
            if (!columnSlotVisuals.TryGetValue(col, out var slotVisuals))
            {
                slotVisuals = new List<GameObject>();
                columnSlotVisuals[col] = slotVisuals;
            }
            slotVisuals.Add(slotGo);

            var queueSlot = slotGo.GetComponent<QueueSlot>();
            if (queueSlot != null) queueSlot.AssignFood(food);
        }

        private void OnSelectModeFoodStateChanged(Food food, FoodState newState)
        {
            if (newState != FoodState.Launching && newState != FoodState.OnConveyor) return;
            if (!activeSelectableFoods.TryGetValue(food, out var info)) return;

            food.StateChanged -= OnSelectModeFoodStateChanged;
            activeSelectableFoods.Remove(food);

            if (columnData.TryGetValue(info.col, out var list) && info.indexInCol < list.Count)
            {
                list.RemoveAt(info.indexInCol);
            }

            ExitSelectBoosterMode();
        }

        private void CacheCameraDefaults()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null && !camDefaultsCached)
            {
                defaultCamPos = targetCamera.transform.position;
                defaultCamOrthoSize = targetCamera.orthographicSize;
                camDefaultsCached = true;
            }
        }

        private void FocusCameraOnQueue()
        {
            if (targetCamera == null) return;

            int maxRowsInAnyCol = columnData.Values.Count > 0 ? columnData.Values.Max(list => list.Count) : 1;
            float queueWidth = levelData.queueColumns * cellSpacingX;
            float queueHeight = maxRowsInAnyCol * cellSpacingZ;

            Vector3 centerPos = originPoint.position + (originPoint.forward * (-queueHeight * 0.5f));

            Vector3 targetCamPos = new Vector3(centerPos.x, defaultCamPos.y - cameraOffset, centerPos.z);

            float targetOrthoSize = Mathf.Max(defaultCamOrthoSize, (queueHeight * 0.5f) + queueCameraMargin);
            float horizontalRequirement = ((queueWidth * 0.5f) + queueCameraMargin) / targetCamera.aspect;
            targetOrthoSize = Mathf.Max(targetOrthoSize, horizontalRequirement);

            targetCamera.DOKill();
            targetCamera.transform.DOMove(targetCamPos, cameraTransitionDuration).SetEase(cameraEase);
            targetCamera.DOOrthoSize(targetOrthoSize, cameraTransitionDuration).SetEase(cameraEase);
        }

        private void ResetCamera(Action onComplete)
        {
            if (targetCamera == null)
            {
                onComplete?.Invoke();
                return;
            }

            targetCamera.DOKill();
            targetCamera.transform.DOMove(defaultCamPos, cameraTransitionDuration).SetEase(cameraEase);
            targetCamera.DOOrthoSize(defaultCamOrthoSize, cameraTransitionDuration).SetEase(cameraEase).OnComplete(() =>
            {
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// SHUFFLE BOOSTER: Tüm kolonlardaki kalan yemekleri (sadece görünen
        /// 3 satır değil, sıradaki TÜM gizli yemekler dahil) tek bir listede
        /// toplayıp karıştırır, sonra her kolonun eleman SAYISINI koruyarak
        /// geri dağıtır.
        /// </summary>
        public void ShuffleQueue()
        {
            if (levelData == null)
            {
                Debug.LogWarning("QueueManager: ShuffleQueue çağrıldı ama levelData yok.");
                return;
            }

            var allEntries = new List<QueueEntry>();
            var countPerColumn = new int[levelData.queueColumns];

            for (int col = 0; col < levelData.queueColumns; col++)
            {
                if (columnData.TryGetValue(col, out var list))
                {
                    countPerColumn[col] = list.Count;
                    allEntries.AddRange(list);
                }
            }

            for (int i = allEntries.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (allEntries[i], allEntries[j]) = (allEntries[j], allEntries[i]);
            }

            int cursor = 0;
            for (int col = 0; col < levelData.queueColumns; col++)
            {
                int count = countPerColumn[col];
                var newList = new List<QueueEntry>(count);

                for (int row = 0; row < count; row++)
                {
                    QueueEntry entry = allEntries[cursor++];
                    entry.col = col;
                    entry.row = row;
                    newList.Add(entry);
                }

                columnData[col] = newList;
            }

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

            food.PresetCapacity(entry.capacity);

            var slotGo = Instantiate(queueSlotPrefab, slotPos, queueSlotPrefab.transform.rotation);
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
                // Görünür/available satır — orijinal renge dön (rebuild sonrası
                // eski locked haliyle kalmasın).
                food.ApplyBlockedVisual(false);
            }
            else
            {
                food.PresetQueueState(FoodState.LockedInQueue);
                // ÖNEMLİ: Saydamlık (alpha) DEĞİL, RGB karartma — Food.cs
                // içindeki ApplyBlockedVisual bunu garantiliyor, materyal
                // Opaque kalmaya devam ediyor, altındaki 2D queue slot
                // sprite'ıyla derinlik/sıralama çakışması olmuyor.
                food.ApplyBlockedVisual(true, lockedAlpha);
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
                            food.CurrentState == FoodState.InFoodSlot ||
                            food.CurrentState == FoodState.Launching)
                        {
                            continue;
                        }

                        if (availableFoodColumn.ContainsKey(food))
                        {
                            food.StateChanged -= OnAvailableFoodStateChanged;
                            food.StateChanged -= OnSelectModeFoodStateChanged;
                            availableFoodColumn.Remove(food);
                        }
                    }

                    Destroy(go);
                }
            }
            columnVisuals.Clear();
            activeSelectableFoods.Clear();
            availableFoodColumn.Clear();

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
            if (newState != FoodState.Launching && newState != FoodState.OnConveyor) return;
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
    }
}