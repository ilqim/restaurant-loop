using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [RequireComponent(typeof(Collider))]
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

        [Header("Movement")]
        [SerializeField] private float stepDuration = 0.3f;
        [SerializeField] private float deliveryDuration = 0.25f;
        [Tooltip("Exit'e varınca boş slot yoksa, tekrar Base'e dönüp turu tekrarlasın mı? Kapalıysa Served'a düşer ve durur.")]
        [SerializeField] private bool loop = false;

        [Header("Conveyor Kapasitesi")]
        [Tooltip("Aynı anda conveyor'da (OnConveyor state'inde) en fazla kaç Food olabilir.")]
        [SerializeField] private int maxOnConveyor = 5;

        [Header("Input")]
        [SerializeField] private InputAction tapAction;
        [Tooltip("Tap noktasından bu objeye raycast atarken kullanılacak kamera. Boşsa Camera.main kullanılır.")]
        [SerializeField] private Camera raycastCamera;
        [Tooltip("Raycast'in hangi layer'ları görmezden geleceği (opsiyonel).")]
        [SerializeField] private LayerMask raycastMask = ~0;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private static int currentOnConveyorCount;

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;
        private bool queueStatePreset;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        /// <summary>
        /// QueueManager tarafından, Instantiate'in HEMEN ardından (Start
        /// çalışmadan önce) çağrılır. Start() bu preset varsa kendi
        /// varsayılan AvailableInQueue atamasını ATLAR — böylece
        /// QueueManager bir food'u LockedInQueue olarak spawn edebilir.
        /// </summary>
        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
        }

        private void OnEnable()
        {
            tapAction.Enable();
            tapAction.performed += OnTapped;
        }

        private void OnDisable()
        {
            tapAction.performed -= OnTapped;
            tapAction.Disable();

            if (currentState == FoodState.OnConveyor)
            {
                currentOnConveyorCount = Mathf.Max(0, currentOnConveyorCount - 1);
            }
        }

        private void Start()
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();

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

            if (customerManager == null) customerManager = FindFirstObjectByType<CustomerManager>();
            if (slotManager == null) slotManager = FindFirstObjectByType<SlotManager>();
            if (raycastCamera == null) raycastCamera = Camera.main;

            if (deliveryPrefab == null)
                Debug.LogWarning($"Food [{gameObject.name}]: Delivery Prefab atanmamış — müşteriye görsel klon fırlatılamayacak.");

            if (slotManager == null)
                Debug.LogWarning("Food: Sahnede bir SlotManager bulunamadı — Exit'e varan yemekler slota yerleşemeyecek.");

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);
            else if (verboseLogging)
                Debug.Log($"Food [{gameObject.name}] preset state ile başladı: {currentState}");

            currentIndex = 0;
        }

        private void OnTapped(InputAction.CallbackContext context)
        {
            if (currentState != FoodState.AvailableInQueue && currentState != FoodState.InFoodSlot)
                return;

            if (!IsPointerOverThisObject())
                return;

            if (currentOnConveyorCount >= maxOnConveyor)
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Conveyor dolu ({currentOnConveyorCount}/{maxOnConveyor}), giriş engellendi.");
                return;
            }

            if (currentState == FoodState.AvailableInQueue)
            {
                MoveToConveyor();
            }
            else
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");
                ReenterConveyorRequested?.Invoke(this);
            }
        }

        public void EnterConveyorFromSlot()
        {
            MoveToConveyor();
        }

        private bool IsPointerOverThisObject()
        {
            if (raycastCamera == null) raycastCamera = Camera.main;
            if (raycastCamera == null) return false;

            Vector2 screenPos = Pointer.current != null
                ? Pointer.current.position.ReadValue()
                : (Vector2)Mouse.current.position.ReadValue();

            Ray ray = raycastCamera.ScreenPointToRay(screenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, raycastMask))
            {
                return hit.transform == transform || hit.transform.IsChildOf(transform);
            }

            return false;
        }

        private void MoveToConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;
            if (waypoints == null || waypoints.Count == 0) return;

            transform.position = waypoints[0];
            currentIndex = 0;

            currentOnConveyorCount++;

            ChangeState(FoodState.OnConveyor);

            TryDeliverAtCell(gridManager.WaypointBlockOrigins[0]);

            if (moveRoutine != null) StopCoroutine(moveRoutine);
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
                        if (verboseLogging) Debug.Log($"Food [{gameObject.name}] Exit'e ulaştı ama boş slot yok, duruyor.");
                        currentOnConveyorCount = Mathf.Max(0, currentOnConveyorCount - 1);
                        moveRoutine = null;
                        yield break;
                    }

                    nextIndex = 0;
                }

                yield return StartCoroutine(MoveTo(waypoints[nextIndex]));
                currentIndex = nextIndex;

                TryDeliverAtCell(pathCells[currentIndex]);
            }
        }

        private bool TryEnterSlot()
        {
            if (slotManager == null) return false;

            bool placed = slotManager.TryPlaceFood(this);
            if (placed)
                currentOnConveyorCount = Mathf.Max(0, currentOnConveyorCount - 1);

            return placed;
        }

        public void SetInFoodSlot()
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            ChangeState(FoodState.InFoodSlot);
        }

        private void TryDeliverAtCell(Vector2Int cell)
        {
            if (customerManager == null) return;

            deliveryTryCounter++;
            if (verboseLogging)
            {
                Debug.Log($"Delivery try {deliveryTryCounter} started");
                Debug.Log($"Cell: ({cell.x}, {cell.y})");
            }

            if (!customerManager.TryFindDeliverableCustomer(foodType, cell, 1, out Customer target))
            {
                if (verboseLogging) Debug.Log($"Delivery try {deliveryTryCounter} — no match");
                return;
            }

            if (verboseLogging) Debug.Log($"Found customer {target.name} at ({cell.x},{cell.y})");

            target.ReceiveFood();
            StartCoroutine(DeliverClone(target, transform.position));

            if (verboseLogging) Debug.Log($"Delivery try {deliveryTryCounter} finished");
        }

        private IEnumerator DeliverClone(Customer target, Vector3 launchPosition)
        {
            if (deliveryPrefab == null || ObjectPool.Instance == null) yield break;

            GameObject clone = ObjectPool.Instance.Get(deliveryPrefab, launchPosition, transform.rotation);
            if (clone == null) yield break;

            Vector3 start = launchPosition;
            Vector3 targetPos = target.transform.position;
            float elapsed = 0f;

            while (elapsed < deliveryDuration)
            {
                if (clone == null) yield break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / deliveryDuration);
                clone.transform.position = Vector3.Lerp(start, targetPos, t);
                yield return null;
            }

            if (clone != null)
            {
                clone.transform.position = targetPos;
                ObjectPool.Instance.Return(clone);
            }
        }

        private IEnumerator MoveTo(Vector3 target)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / stepDuration);
                transform.position = Vector3.Lerp(start, target, t);
                yield return null;
            }

            transform.position = target;
        }

        private void ChangeState(FoodState newState)
        {
            currentState = newState;
            if (verboseLogging) Debug.Log($"Food [{gameObject.name}] State: {currentState}");
            StateChanged?.Invoke(this, newState);
        }
    }
}