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

        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private FoodPool foodPool;

        [Header("Bu yemeğin türü — hangi müşterilere gidebileceğini belirler")]
        [SerializeField] private FoodType foodType;

        [Header("Movement")]
        [Tooltip("Bir waypoint'ten diğerine geçiş süresi.")]
        [SerializeField] private float stepDuration = 0.3f;

        [Tooltip("Konveyörden ayrılıp müşteriye 'fırlatılan' klonun uçuş süresi.")]
        [SerializeField] private float deliveryDuration = 0.25f;

        [Tooltip("Exit'e ulaştığında tekrar Base'e dönsün mü?")]
        [SerializeField] private bool loop = false;

        [Header("Input")]
        [SerializeField] private InputAction tapAction;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        private int currentIndex;
        private Coroutine moveRoutine;

        public FoodState CurrentState => currentState;


        // --------------------------------------------------
        // INPUT
        // --------------------------------------------------

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


        // --------------------------------------------------
        // START
        // --------------------------------------------------

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
                Debug.LogWarning(
                    "Food: Conveyor waypoint listesi boş. " +
                    "Base/Exit ayarlarını kontrol et."
                );

                enabled = false;
                return;
            }

            if (customerManager == null) customerManager = FindFirstObjectByType<CustomerManager>();
            if (foodPool == null) foodPool = FindFirstObjectByType<FoodPool>();

            // Yemek başlangıçta kuyuda.
            ChangeState(FoodState.AvailableInQueue);

            // Henüz konveyöre girmedi.
            currentIndex = 0;
        }


        // --------------------------------------------------
        // TAP
        // --------------------------------------------------

        private void OnTapped(InputAction.CallbackContext context)
        {
            // Sadece kuyudaki yemek alınabilir.
            if (currentState != FoodState.AvailableInQueue)
                return;

            MoveToConveyor();
        }


        // --------------------------------------------------
        // CONVEYOR'A GİR
        // --------------------------------------------------

        private void MoveToConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
                return;

            // Conveyor Base / başlangıç noktası.
            transform.position = waypoints[0];

            currentIndex = 0;

            // Artık yemek konveyörde.
            ChangeState(FoodState.OnConveyor);

            // Eğer eski bir hareket varsa durdur.
            if (moveRoutine != null)
                StopCoroutine(moveRoutine);

            moveRoutine = StartCoroutine(MoveOnConveyor());
        }


        // --------------------------------------------------
        // CONVEYOR HAREKETİ
        // --------------------------------------------------

        private IEnumerator MoveOnConveyor()
        {
            var waypoints = gridManager.WaypointWorldPositions;

            while (true)
            {
                int nextIndex = currentIndex + 1;

                // Exit'e ulaştık.
                if (nextIndex >= waypoints.Count)
                {
                    // Loop kapalıysa burada dur.
                    if (!loop)
                    {
                        Debug.Log(
                            $"Food [{gameObject.name}] Exit'e ulaştı."
                        );

                        moveRoutine = null;

                        // Şimdilik state OnConveyor olarak kalıyor.
                        // Müşteri/slot sistemi yaptığımızda burada
                        // InFoodSlot veya Served yapılabilir.

                        yield break;
                    }

                    // Loop açıksa tekrar Base'e dön.
                    nextIndex = 0;
                }

                // Bir sonraki waypoint'e hareket et.
                yield return StartCoroutine(
                    MoveTo(waypoints[nextIndex])
                );

                currentIndex = nextIndex;

                // Konveyörden AYRILMADAN, sadece hizaya gelinen uygun bir
                // müşteri varsa pool'dan bir klon fırlatılır. Ana yemek
                // (bu obje) konveyörde yoluna devam eder.
                TryDeliverAtCurrentWaypoint();
            }
        }


        // --------------------------------------------------
        // MÜŞTERİYE TESLİMAT (konveyörden ayrılmadan)
        // --------------------------------------------------

        private void TryDeliverAtCurrentWaypoint()
        {
            if (customerManager == null || foodPool == null) return;
            if (gridManager.WaypointBlockOrigins == null) return;
            if (currentIndex < 0 || currentIndex >= gridManager.WaypointBlockOrigins.Count) return;

            Vector2Int blockOrigin = gridManager.WaypointBlockOrigins[currentIndex];

            if (!customerManager.TryFindDeliverableCustomer(
                    foodType, blockOrigin, LevelData.ConveyorBlockSize, out Customer target))
            {
                return;
            }

            // Müşteriyi HEMEN "Eating"e al — klon henüz yolda olsa bile,
            // aynı müşteri başka bir yemek tarafından ikinci kez hedef
            // alınmasın diye (çift teslimat / race condition önlemi).
            target.ReceiveFood();

            StartCoroutine(DeliverClone(target));
        }

        private IEnumerator DeliverClone(Customer target)
        {
            GameObject clone = foodPool.Get(foodType, transform.position, transform.rotation);
            if (clone == null) yield break;

            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < deliveryDuration)
            {
                if (clone == null || target == null)
                {
                    if (clone != null) foodPool.Release(foodType, clone);
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
                foodPool.Release(foodType, clone);
            }
        }


        // --------------------------------------------------
        // WAYPOINT'E HAREKET
        // --------------------------------------------------

        private IEnumerator MoveTo(Vector3 target)
        {
            Vector3 start = transform.position;
            float elapsed = 0f;

            while (elapsed < stepDuration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(
                    elapsed / stepDuration
                );

                transform.position = Vector3.Lerp(
                    start,
                    target,
                    t
                );

                yield return null;
            }

            // Tam waypoint pozisyonuna oturt.
            transform.position = target;
        }


        // --------------------------------------------------
        // STATE
        // --------------------------------------------------

        private void ChangeState(FoodState newState)
        {
            currentState = newState;

            Debug.Log(
                $"Food [{gameObject.name}] State: {currentState}"
            );
        }
    }
}