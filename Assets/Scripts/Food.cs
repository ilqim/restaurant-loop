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

        [Header("Movement")]
        [Tooltip("Bir waypoint'ten diğerine geçiş süresi.")]
        [SerializeField] private float stepDuration = 0.3f;

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
            if (gridManager == null)
            {
                Debug.LogError("Food: GridManager atanmamış.");
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