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

    public class Food : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridManager gridManager;

        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private CustomerManager customerManager;

        [Header("Bu yemeğin türü — hangi müşterilere gidebileceğini belirler")]
        [SerializeField] private FoodType foodType;

        [Header("Bu yemeğin müşteriye 'fırlatılan' küçük klon prefabı — sadece görsel (mesh/renderer), üzerinde HİÇBİR script olmamalı")]
        [SerializeField] private GameObject deliveryPrefab;

        [Header("Movement")]
        [SerializeField] private float stepDuration = 0.3f;
        [SerializeField] private float deliveryDuration = 0.25f;
        [SerializeField] private bool loop = false;

        [Header("Input")]
        [SerializeField] private InputAction tapAction;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;

        public FoodState CurrentState => currentState;

        private void OnEnable()
        {
            tapAction.Enable();
            tapAction.performed += OnTapped;
        }

        private void OnDisable()
        {
            tapAction.performed -= OnTapped;
            tapAction.Disable();
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

            if (deliveryPrefab == null)
                Debug.LogWarning($"Food [{gameObject.name}]: Delivery Prefab atanmamış — müşteriye görsel klon fırlatılamayacak.");

            ChangeState(FoodState.AvailableInQueue);
            currentIndex = 0;
        }

        private void OnTapped(InputAction.CallbackContext context)
        {
            if (currentState != FoodState.AvailableInQueue)
                return;

            MoveToConveyor();
        }

        private void MoveToConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;
            if (waypoints == null || waypoints.Count == 0) return;

            transform.position = waypoints[0];
            currentIndex = 0;

            ChangeState(FoodState.OnConveyor);

            // Base hücresi (path[0]) de tekil bir hücre — girer girmez orada
            // teslimat mümkünse tek seferlik kontrol.
            TryDeliverAtCell(gridManager.WaypointBlockOrigins[0]);

            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(MoveOnConveyor());
        }

        private IEnumerator MoveOnConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;
            var pathCells = gridManager.WaypointBlockOrigins; // artık path'teki HER eleman zaten tekil bir (row,col)

            while (true)
            {
                int nextIndex = currentIndex + 1;

                if (nextIndex >= waypoints.Count)
                {
                    if (!loop)
                    {
                        if (verboseLogging) Debug.Log($"Food [{gameObject.name}] Exit'e ulaştı.");
                        moveRoutine = null;
                        yield break;
                    }
                    nextIndex = 0;
                }

                yield return StartCoroutine(MoveTo(waypoints[nextIndex]));
                currentIndex = nextIndex;

                // Path artık native 1-hücre çözünürlüğünde — her adımda
                // TEK bir hücre kontrol ediliyor, aynı anda 2 müşteriye
                // birden ateş etme ihtimali yapısal olarak ortadan kalktı.
                TryDeliverAtCell(pathCells[currentIndex]);
            }
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
        }
    }
}