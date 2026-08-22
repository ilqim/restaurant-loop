using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public enum FoodState
    {
        LockedInQueue,
        AvailableInQueue,
        OnConveyor,
        InFoodSlot,
        Served
    }

    // Collider artık gerekmiyor — tıklanabilir alan QueueSlot ve Slot'ta.
    // Food sadece kendi state'inden ve hareketinden sorumlu.
    public class Food : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridManager gridManager;

        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private CustomerManager customerManager;

        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private SlotManager slotManager;

        [Header("Bu yemeğin türü — hangi müşterilere gidebileceğini belirler")]
        [SerializeField] private FoodType foodType;

        [Header("Bu yemeğin müşteriye 'fırlatılan' küçük klon prefabı — sadece görsel (mesh/renderer), üzerinde HİÇBİR script olmamalı")]
        [SerializeField] private GameObject deliveryPrefab;

        [Header("Kapasite")]
        [Tooltip("Queue'dan PresetCapacity ile set edilir. Her başarılı teslimatta 1 azalır. 0 olunca conveyor'da (hareket ederken) kaybolur.")]
        [SerializeField] private int capacity = 10;

        [Header("Kapasite Debug Etiketi — food'un üstünde canlı sayı gösterir")]
        [SerializeField] private bool showCapacityLabel = true;
        [SerializeField] private float labelHeight = 0.6f;
        [SerializeField] private int labelFontSize = 48;
        [SerializeField] private float labelCharacterSize = 0.12f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Movement")]
        [SerializeField] private float stepDuration = 0.3f;
        [SerializeField] private float deliveryDuration = 0.25f;

        [Tooltip("Exit'e varınca boş slot yoksa, tekrar Base'e dönüp turu tekrarlasın mı? Kapalıysa Served'a düşer ve durur.")]
        [SerializeField] private bool loop = false;

        [Header("Conveyor Kapasitesi (aynı anda kaç Food)")]
        [SerializeField] private int maxOnConveyor = 5;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private static int currentOnConveyorCount;

        // ============================================================
        // CUSTOMER RESERVATION
        // ============================================================
        //
        // Bir Customer'a aynı anda yalnızca bir Food gönderilebilir.
        // Bir Food müşteriyi bulduğunda önce buradan kontrol eder.
        //
        // Dictionary yerine HashSet kullanıyoruz:
        // Customer -> şu anda başka bir Food tarafından rezerve mi?
        //
        private static readonly HashSet<Customer> reservedCustomers = new();

        // Bu Food'un şu anda rezerve ettiği müşteriler.
        // Normalde bir Food'un aynı anda birden fazla delivery'si
        // olabilir, o yüzden tek Customer değişkeni kullanmıyoruz.
        private readonly HashSet<Customer> customersReservedByThisFood = new();

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;
        private bool queueStatePreset;
        private bool capacityPreset;
        private int pendingDeliveries;
        private bool depleted;

        private TextMesh capacityLabel;
        private Camera labelFacingCamera;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        /// <summary>
        /// QueueManager, Instantiate'in hemen ardından çağırır.
        /// </summary>
        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
        }

        /// <summary>
        /// QueueManager, Instantiate'in hemen ardından çağırır —
        /// food'un kaç müşteriye servis edebileceğini set eder.
        /// </summary>
        public void PresetCapacity(int value)
        {
            capacity = Mathf.Max(0, value);
            capacityPreset = true;
            UpdateCapacityLabel();
        }

        private void OnDisable()
        {
            if (currentState == FoodState.OnConveyor)
                currentOnConveyorCount = Mathf.Max(0, currentOnConveyorCount - 1);

            ReleaseAllCustomerReservations();
        }

        private void Start()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();

            if (gridManager == null)
            {
                Debug.LogError("Food: Sahnede bir GridManager bulunamadı.");
                enabled = false;
                return;
            }

            if (gridManager.WaypointWorldPositions == null ||
                gridManager.WaypointWorldPositions.Count == 0)
            {
                Debug.LogWarning("Food: Conveyor waypoint listesi boş. Base/Exit ayarlarını kontrol et.");
                enabled = false;
                return;
            }

            if (customerManager == null)
                customerManager = FindFirstObjectByType<CustomerManager>();

            if (slotManager == null)
                slotManager = FindFirstObjectByType<SlotManager>();

            if (labelFacingCamera == null)
                labelFacingCamera = Camera.main;

            if (deliveryPrefab == null)
            {
                Debug.LogWarning(
                    $"Food [{gameObject.name}]: Delivery Prefab atanmamış — müşteriye görsel klon fırlatılamayacak."
                );
            }

            if (slotManager == null)
            {
                Debug.LogWarning(
                    "Food: Sahnede bir SlotManager bulunamadı — Exit'e varan yemekler slota yerleşemeyecek."
                );
            }

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);

            currentIndex = 0;
            depleted = false;
            pendingDeliveries = 0;

            CreateCapacityLabel();
        }

        private void LateUpdate()
        {
            if (capacityLabel == null)
                return;

            if (labelFacingCamera == null)
                labelFacingCamera = Camera.main;

            if (labelFacingCamera == null)
                return;

            capacityLabel.transform.rotation =
                Quaternion.LookRotation(
                    capacityLabel.transform.position -
                    labelFacingCamera.transform.position
                );
        }

        /// <summary>
        /// QueueSlot veya Slot tarafından çağrılır.
        /// Food artık kendi input/raycast'ini dinlemiyor.
        /// </summary>
        public void ActivateFromTap()
        {
            if (currentState != FoodState.AvailableInQueue &&
                currentState != FoodState.InFoodSlot)
                return;

            if (currentOnConveyorCount >= maxOnConveyor)
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"Food [{gameObject.name}]: Conveyor dolu " +
                        $"({currentOnConveyorCount}/{maxOnConveyor}), giriş engellendi."
                    );
                }

                return;
            }

            if (currentState == FoodState.AvailableInQueue)
            {
                MoveToConveyor();
            }
            else
            {
                if (verboseLogging)
                    Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");

                ReenterConveyorRequested?.Invoke(this);
            }
        }

        public void EnterConveyorFromSlot()
        {
            MoveToConveyor();
        }

        private void MoveToConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
                return;

            if (capacity <= 0)
                return;

            transform.position = waypoints[0];
            currentIndex = 0;
            depleted = false;

            currentOnConveyorCount++;

            ChangeState(FoodState.OnConveyor);

            TryDeliverAtCell(gridManager.WaypointBlockOrigins[0]);

            if (moveRoutine != null)
                StopCoroutine(moveRoutine);

            if (!depleted)
                moveRoutine = StartCoroutine(MoveOnConveyor());
        }

        private IEnumerator MoveOnConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;
            var pathCells = gridManager.WaypointBlockOrigins;

            while (true)
            {
                int nextIndex = currentIndex + 1;
                bool reachedExitEnd = nextIndex >= waypoints.Count;

                if (reachedExitEnd)
                {
                    if (TryEnterSlot())
                        yield break;

                    if (!loop)
                    {
                        if (verboseLogging)
                        {
                            Debug.Log(
                                $"Food [{gameObject.name}] Exit'e ulaştı ama boş slot yok, duruyor."
                            );
                        }

                        currentOnConveyorCount =
                            Mathf.Max(0, currentOnConveyorCount - 1);

                        moveRoutine = null;
                        yield break;
                    }

                    nextIndex = 0;
                }

                yield return StartCoroutine(
                    MoveTo(waypoints[nextIndex])
                );

                currentIndex = nextIndex;

                TryDeliverAtCurrentWaypoint();
            }
        }

        private bool TryEnterSlot()
        {
            if (customerManager == null) return;
            if (gridManager.WaypointBlockOrigins == null) return;
            if (currentIndex < 0 || currentIndex >= gridManager.WaypointBlockOrigins.Count) return;

            Vector2Int blockOrigin = gridManager.WaypointBlockOrigins[currentIndex];

            if (!customerManager.TryFindDeliverableCustomer(
                    foodType,
                    cell,
                    1,
                    out Customer target))
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"Delivery try {deliveryTryCounter} — no match"
                    );
                }

                return;
            }

            if (target == null)
                return;

            // ========================================================
            // YENİ KOŞUL:
            // Bu müşteri başka bir Food tarafından şu anda rezerve
            // edilmişse BU FOOD müşteriye gönderilmeyecek.
            // ========================================================

            if (IsCustomerReservedByAnotherFood(target))
            {
                if (verboseLogging)
                {
                    Debug.Log(
                        $"Delivery try {deliveryTryCounter} — " +
                        $"Customer [{target.name}] başka bir Food tarafından " +
                        $"rezerve edilmiş, bu Food gönderilmiyor."
                    );
                }

                return;
            }
            Debug.Log($"Found customer {target.gameObject} at {currentIndex}");

            target.ReceiveFood();
            StartCoroutine(DeliverClone(target));
        }

        private IEnumerator DeliverClone(Customer target)
        {
            if (deliveryPrefab == null ||
                ObjectPool.Instance == null)
            {
                if (target != null)
                    target.ReceiveFood();

                // Müşteriye ulaştığı kabul edildi.
                ReleaseCustomerReservation(target);

                OnDeliveryFinished();

                yield break;
            }

            GameObject clone =
                ObjectPool.Instance.Get(
                    deliveryPrefab,
                    launchPosition,
                    transform.rotation
                );

            if (clone == null)
            {
                if (target != null)
                    target.ReceiveFood();

                ReleaseCustomerReservation(target);

                OnDeliveryFinished();

                yield break;
            }

            Vector3 start = launchPosition;
            Vector3 targetPos = target.transform.position;

            float elapsed = 0f;

            while (elapsed < deliveryDuration)
            {
                if (clone == null)
                {
                    ReleaseCustomerReservation(target);

                    OnDeliveryFinished();

                    yield break;
                }

                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / deliveryDuration
                    );

                clone.transform.position =
                    Vector3.Lerp(
                        start,
                        targetPos,
                        t
                    );

                yield return null;
            }

            if (clone != null)
            {
                clone.transform.position = targetPos;

                ObjectPool.Instance.Return(clone);
            }

            // --------------------------------------------------------
            // Yemek müşteriye ulaştı.
            // --------------------------------------------------------

            if (target != null)
                target.ReceiveFood();

            // Artık başka Food bu müşteriyi hedefleyebilir.
            ReleaseCustomerReservation(target);

            OnDeliveryFinished();
        }

        /// <summary>
        /// Bir teslimat (havadaki klon) tamamlandığında çağrılır.
        /// Food tükenmişse ve bekleyen başka teslimat kalmadıysa,
        /// artık gerçekten yok olma zamanı gelmiştir.
        /// </summary>
        private void OnDeliveryFinished()
        {
            pendingDeliveries =
                Mathf.Max(0, pendingDeliveries - 1);

            if (depleted && pendingDeliveries == 0)
                DespawnDepleted();
        }

        private void DespawnDepleted()
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"Food [{gameObject.name}] tükendi, yok oluyor."
                );
            }

            ChangeState(FoodState.Served);

            ReleaseAllCustomerReservations();

            if (ObjectPool.Instance != null)
                ObjectPool.Instance.Return(gameObject);
            else
                gameObject.SetActive(false);
        }

        // ============================================================
        // MOVEMENT
        // ============================================================

        private IEnumerator MoveTo(Vector3 target)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / stepDuration
                    );

                transform.position =
                    Vector3.Lerp(
                        start,
                        target,
                        t
                    );

                yield return null;
            }

            transform.position = target;
        }

        // ============================================================
        // STATE
        // ============================================================

        private void ChangeState(FoodState newState)
        {
            currentState = newState;

            if (verboseLogging)
            {
                Debug.Log(
                    $"Food [{gameObject.name}] State: {currentState}"
                );
            }

            StateChanged?.Invoke(this, newState);
        }

        // ============================================================
        // CAPACITY DEBUG LABEL
        // ============================================================

        private void CreateCapacityLabel()
        {
            if (!showCapacityLabel)
                return;

            if (capacityLabel != null)
                return;

            var labelGO =
                new GameObject("CapacityLabel");

            labelGO.transform.SetParent(
                transform,
                false
            );

            labelGO.transform.localPosition =
                new Vector3(
                    0,
                    labelHeight,
                    0
                );

            capacityLabel =
                labelGO.AddComponent<TextMesh>();

            capacityLabel.anchor =
                TextAnchor.MiddleCenter;

            capacityLabel.alignment =
                TextAlignment.Center;

            capacityLabel.fontSize =
                labelFontSize;

            capacityLabel.characterSize =
                labelCharacterSize;

            capacityLabel.color =
                labelColor;

            UpdateCapacityLabel();
        }

        private void UpdateCapacityLabel()
        {
            if (capacityLabel != null)
                capacityLabel.text =
                    capacity.ToString();
        }
    }
}