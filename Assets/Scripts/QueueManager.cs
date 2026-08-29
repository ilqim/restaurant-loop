using System;
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

        [Header("Sıra İlerleme Animasyonu")]
        [Tooltip("Öndeki food banda çıkınca, arkadaki food/slot'ların bir üst satıra kayma süresi. Artık anlık 'ışınlanma' YOK.")]
        [SerializeField] private float shiftDuration = 0.25f;
        [SerializeField] private Ease shiftEase = Ease.OutQuad;

        [Header("Select Booster Camera Settings")]
        [Tooltip("Kamera POZİSYONU HİÇ DEĞİŞMEZ — sadece aşağıdaki açıya döner, sonra eski açısına geri döner.")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private float cameraTransitionDuration = 0.5f;
        [SerializeField] private Ease cameraEase = Ease.InOutQuad;
        [SerializeField] private float queueCameraMargin = 1.5f;
        [Tooltip("Select modundayken kameranın döneceği HEDEF açı (world Euler X/Y/Z). Kamera bu açıya döner, mod bitince kendi orijinal açısına geri döner. Pozisyona hiç dokunulmaz.")]
        [SerializeField] private Vector3 selectModeRotationEuler = new Vector3(60f, 0f, 0f);
        [Tooltip("Select modundayken kamera dünya Y ekseninde ne kadar AŞAĞI insin (dünya birimi). 0 = hiç inmez. X/Z pozisyonuna dokunulmaz.")]
        [SerializeField] private float selectModeYOffset = 0f;

        private class ColumnItem
        {
            public GameObject foodGo;
            public GameObject slotGo;
            public Food food;
        }

        private readonly Dictionary<int, List<ColumnItem>> columnItems = new();
        private readonly Dictionary<int, List<QueueEntry>> columnData = new();
        private readonly Dictionary<Food, int> availableFoodColumn = new();
        private readonly Dictionary<Food, (int col, int indexInCol)> activeSelectableFoods = new();

        private bool started;
        private bool isSelectModeActive;

        private Vector3 defaultCamPos;
        private Quaternion defaultCamRot;
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

            CacheCameraDefaults();

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
                    SpawnSelectableItem(col, r, entry);
                }
            }
        }

        private void SpawnSelectableItem(int col, int indexInCol, QueueEntry entry)
        {
            GameObject prefab = GetPrefab(entry.food);
            if (prefab == null) return;

            Vector3 slotPos = ComputeSlotPosition(col, indexInCol);
            Vector3 foodPos = slotPos;
            foodPos.y += foodYOffset;

            var foodGo = Instantiate(prefab, foodPos, prefab.transform.rotation);

            var food = foodGo.GetComponent<Food>();
            food.PresetCapacity(entry.capacity);
            food.PresetQueueState(FoodState.AvailableInQueue);
            food.SetBlockedCrossfade(false);

            activeSelectableFoods[food] = (col, indexInCol);
            food.StateChanged += OnSelectModeFoodStateChanged;

            var slotGo = Instantiate(queueSlotPrefab, slotPos, queueSlotPrefab.transform.rotation);
            var queueSlot = slotGo.GetComponent<QueueSlot>();
            if (queueSlot != null) queueSlot.AssignFood(food);

            AddColumnItem(col, foodGo, slotGo, food);
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
                defaultCamRot = targetCamera.transform.rotation;
                defaultCamOrthoSize = targetCamera.orthographicSize;
                camDefaultsCached = true;
            }
        }

        /// <summary>
        /// POZİSYONA HİÇ DOKUNMUYORUZ — kamera sadece selectModeRotationEuler'a
        /// döner. Orthographic size hesabı, queue'nun (aynı pozisyondan, farklı
        /// açıyla bakıldığında) ekrana sığması için hâlâ gerekli.
        /// </summary>
        private void FocusCameraOnQueue()
        {
            if (targetCamera == null) return;

            int maxRowsInAnyCol = columnData.Values.Count > 0 ? columnData.Values.Max(list => list.Count) : 1;
            float queueWidth = levelData.queueColumns * cellSpacingX;
            float queueHeight = maxRowsInAnyCol * cellSpacingZ;

            float targetOrthoSize = Mathf.Max(defaultCamOrthoSize, (queueHeight * 0.5f) + queueCameraMargin);
            float horizontalRequirement = ((queueWidth * 0.5f) + queueCameraMargin) / targetCamera.aspect;
            targetOrthoSize = Mathf.Max(targetOrthoSize, horizontalRequirement);

            targetCamera.DOKill();
            // ÖNEMLİ: X/Z pozisyonuna hiç dokunulmuyor — sadece Y ekseninde
            // (selectModeYOffset kadar aşağı) ve rotasyonda değişiklik var.
            Vector3 targetPos = defaultCamPos;
            targetPos.y -= selectModeYOffset;
            targetCamera.transform.DOMove(targetPos, cameraTransitionDuration).SetEase(cameraEase);
            targetCamera.transform.DORotate(selectModeRotationEuler, cameraTransitionDuration).SetEase(cameraEase);
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
            // Orijinal açıya (Quaternion olarak, Euler gimbal belirsizliği
            // olmadan) kesin geri dönüş.
            targetCamera.transform.DORotateQuaternion(defaultCamRot, cameraTransitionDuration).SetEase(cameraEase);
            targetCamera.DOOrthoSize(defaultCamOrthoSize, cameraTransitionDuration).SetEase(cameraEase).OnComplete(() =>
            {
                onComplete?.Invoke();
            });
        }

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

                    SpawnAt(col, visualRow, list[visualRow]);
                }
            }
        }

        private Vector3 ComputeSlotPosition(int col, int row)
        {
            float xOffset = (col - (levelData.queueColumns - 1) / 2f) * cellSpacingX;
            float zOffset = -row * cellSpacingZ;
            return originPoint.position + originPoint.right * xOffset + originPoint.forward * zOffset;
        }

        private void SpawnAt(int col, int visualRow, QueueEntry entry)
        {
            GameObject prefab = GetPrefab(entry.food);
            if (prefab == null)
            {
                Debug.LogWarning($"QueueManager: '{entry.food}' için Food Prefabs listesinde prefab yok.");
                return;
            }

            Vector3 slotPos = ComputeSlotPosition(col, visualRow);
            Vector3 foodPos = slotPos;
            foodPos.y += foodYOffset;

            var foodGo = Instantiate(prefab, foodPos, prefab.transform.rotation);

            var food = foodGo.GetComponent<Food>();
            if (food == null)
            {
                Debug.LogWarning($"'{prefab.name}' prefabında Food component'i yok.");
                Destroy(foodGo);
                return;
            }

            food.PresetCapacity(entry.capacity);

            var slotGo = Instantiate(queueSlotPrefab, slotPos, queueSlotPrefab.transform.rotation);

            var queueSlot = slotGo.GetComponent<QueueSlot>();
            if (queueSlot == null)
            {
                Debug.LogWarning($"'{queueSlotPrefab.name}' prefabında QueueSlot component'i yok.");
            }
            else
            {
                queueSlot.AssignFood(food);
            }

            AddColumnItem(col, foodGo, slotGo, food);

            if (visualRow == 0)
            {
                food.PresetQueueState(FoodState.AvailableInQueue);
                availableFoodColumn[food] = col;
                food.StateChanged += OnAvailableFoodStateChanged;
                food.SetBlockedCrossfade(false);
            }
            else
            {
                food.PresetQueueState(FoodState.LockedInQueue);
                food.SetBlockedCrossfade(true);
            }
        }

        private void AddColumnItem(int col, GameObject foodGo, GameObject slotGo, Food food)
        {
            if (!columnItems.TryGetValue(col, out var items))
            {
                items = new List<ColumnItem>();
                columnItems[col] = items;
            }
            items.Add(new ColumnItem { foodGo = foodGo, slotGo = slotGo, food = food });
        }

        private void ClearAllVisuals()
        {
            foreach (var items in columnItems.Values)
            {
                foreach (var item in items)
                {
                    var food = item.food;

                    if (food != null)
                    {
                        if (food.CurrentState == FoodState.OnConveyor ||
                            food.CurrentState == FoodState.InFoodSlot ||
                            food.CurrentState == FoodState.Launching)
                        {
                            if (item.slotGo != null) Destroy(item.slotGo);
                            continue;
                        }

                        if (availableFoodColumn.ContainsKey(food))
                        {
                            food.StateChanged -= OnAvailableFoodStateChanged;
                            food.StateChanged -= OnSelectModeFoodStateChanged;
                            availableFoodColumn.Remove(food);
                        }

                        food.transform.DOKill();
                    }

                    if (item.slotGo != null)
                    {
                        item.slotGo.transform.DOKill();
                        Destroy(item.slotGo);
                    }

                    if (item.foodGo != null)
                        Destroy(item.foodGo);
                }
            }

            columnItems.Clear();
            activeSelectableFoods.Clear();
            availableFoodColumn.Clear();
        }

        private void OnAvailableFoodStateChanged(Food food, FoodState newState)
        {
            if (newState != FoodState.Launching && newState != FoodState.OnConveyor) return;
            if (!availableFoodColumn.TryGetValue(food, out int col)) return;

            food.StateChanged -= OnAvailableFoodStateChanged;
            availableFoodColumn.Remove(food);

            if (columnData.TryGetValue(col, out var list) && list.Count > 0)
                list.RemoveAt(0);

            ShiftColumnForward(col);
        }

        private void ShiftColumnForward(int col)
        {
            if (!columnItems.TryGetValue(col, out var items) || items.Count == 0)
                return;

            var frontItem = items[0];
            items.RemoveAt(0);

            if (frontItem.slotGo != null)
                Destroy(frontItem.slotGo);

            for (int row = 0; row < items.Count; row++)
            {
                var item = items[row];

                Vector3 slotPos = ComputeSlotPosition(col, row);
                Vector3 foodPos = slotPos;
                foodPos.y += foodYOffset;

                if (item.slotGo != null)
                    item.slotGo.transform.DOMove(slotPos, shiftDuration).SetEase(shiftEase);

                if (item.foodGo != null)
                    item.foodGo.transform.DOMove(foodPos, shiftDuration).SetEase(shiftEase);

                if (row == 0 && item.food != null)
                {
                    item.food.PresetQueueState(FoodState.AvailableInQueue);
                    // ÖNEMLİ: duration=shiftDuration — Blocked'tan Available'a
                    // geçiş, TAM OLARAK öne kayma (DOMove) ile aynı sürede,
                    // senkron gerçekleşiyor. Artık anlık bir renk/alfa
                    // sıçraması yok, ikisi birlikte biter.
                    item.food.SetBlockedCrossfade(false, shiftDuration);
                    availableFoodColumn[item.food] = col;
                    item.food.StateChanged += OnAvailableFoodStateChanged;
                }
            }

            if (columnData.TryGetValue(col, out var dataList) &&
                items.Count < visibleRows &&
                items.Count < dataList.Count)
            {
                int newRow = items.Count;
                SpawnAt(col, newRow, dataList[newRow]);
            }
        }

        private GameObject GetPrefab(FoodType food)
        {
            foreach (var entry in foodPrefabs)
                if (entry.food == food) return entry.prefab;
            return null;
        }
    }
}