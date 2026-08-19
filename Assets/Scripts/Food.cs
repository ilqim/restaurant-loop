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

        private int currentIndex;
        private Coroutine moveRoutine;

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

            if (moveRoutine != null) StopCoroutine(moveRoutine);
            moveRoutine = StartCoroutine(MoveOnConveyor());
        }

        private IEnumerator MoveOnConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;

            while (true)
            {
                int nextIndex = currentIndex + 1;

                if (nextIndex >= waypoints.Count)
                {
                    if (!loop)
                    {
                        Debug.Log($"Food [{gameObject.name}] Exit'e ulaştı.");
                        moveRoutine = null;
                        yield break;
                    }
                    nextIndex = 0;
                }

                yield return StartCoroutine(MoveTo(waypoints[nextIndex]));
                currentIndex = nextIndex;

                TryDeliverAtCurrentWaypoint();
            }
        }

        private void TryDeliverAtCurrentWaypoint()
        {
            if (customerManager == null) return;
            if (gridManager.WaypointBlockOrigins == null) return;
            if (currentIndex < 0 || currentIndex >= gridManager.WaypointBlockOrigins.Count) return;

            Vector2Int blockOrigin = gridManager.WaypointBlockOrigins[currentIndex];

            if (!customerManager.TryFindDeliverableCustomer(
                    foodType, blockOrigin, LevelData.ConveyorBlockSize, out Customer target))
            {
                return;
            }

            target.ReceiveFood();
            StartCoroutine(DeliverClone(target));
        }

        private IEnumerator DeliverClone(Customer target)
        {
            if (deliveryPrefab == null || ObjectPool.Instance == null) yield break;

            GameObject clone = ObjectPool.Instance.Get(deliveryPrefab, transform.position, transform.rotation);
            if (clone == null) yield break;

            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < deliveryDuration)
            {
                if (clone == null || target == null)
                {
                    if (clone != null) ObjectPool.Instance.Return(clone);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / deliveryDuration);
                clone.transform.position = Vector3.Lerp(start, target.transform.position, t);
                yield return null;
            }

            if (clone != null)
            {
                if (target != null) clone.transform.position = target.transform.position;
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
            Debug.Log($"Food [{gameObject.name}] State: {currentState}");
        }
    }
}