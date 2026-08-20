using System;
using System.Collections;
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

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private static int currentOnConveyorCount;

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;
        private bool queueStatePreset;

        // -1 = sınırsız — QueueManager dışından (örn. test sahnesinde elle
        // sürüklenmiş bir Food) spawn edilirse capacity hiç düşürülmez,
        // eski davranış korunur. QueueManager, PresetCapacity() ile bunu
        // her zaman QueueEntry.capacity'ye eşitliyor (level tasarımında
        // seçtiğin sayı neyse o — 10'a sabit değil).
        private int remainingCapacity = -1;

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

        /// <summary>
        /// QueueManager tarafından, bu food'un temsil ettiği stack'in kaç
        /// teslimat hakkı olduğunu ayarlamak için çağrılır (level
        /// tasarımındaki "Yerleştirilecek Kapasite" değeri — sabit 10
        /// DEĞİL, her hücre için ayrı ayrı seçilebiliyor).
        /// </summary>
        public void PresetCapacity(int capacity)
        {
            remainingCapacity = capacity;
        }

        private void OnDisable()
        {
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

        /// <summary>
        /// Tek global tap yönlendiricisi (QueueManager) bir dokunuşta
        /// raycast'in çarptığı Food için bunu çağırır.
        /// </summary>
        public void ActivateFromTap()
        {
            if (currentState != FoodState.AvailableInQueue && currentState != FoodState.InFoodSlot)
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

        private void MoveToConveyor()
        {
            if (currentState == FoodState.OnConveyor) return;

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

                // TryDeliverAtCell capacity'yi tüketip bu objeyi Destroy
                // etmiş olabilir — coroutine'e devam etmeden önce kontrol et.
                if (this == null) yield break;
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

            // ÖNEMLİ: gereken her şeyi (prefab, rotasyon, süre) ŞİMDİ,
            // parametre olarak yakalıyoruz ve coroutine'i ObjectPool
            // (kalıcı bir obje) üzerinde çalıştırıyoruz — bu Food nesnesi
            // (this) az sonra ConsumeAndDestroy() ile yok edilse bile
            // (kapasite bittiyse), mermi coroutine'i buna bağlı DEĞİL,
            // yoluna devam edip pool'a düzgünce dönüyor. Eskiden bu
            // coroutine Food'un ÜZERİNDE çalışıyordu — Food destroy
            // edilince Unity coroutine'i anında kesiyordu ve mermi
            // olduğu yerde havada donup kalıyordu.
            if (ObjectPool.Instance != null)
            {
                ObjectPool.Instance.StartCoroutine(
                    DeliverCloneRoutine(deliveryPrefab, transform.position, transform.rotation, target, deliveryDuration));
            }

            if (verboseLogging) Debug.Log($"Delivery try {deliveryTryCounter} finished");

            // ---- Capacity düşür — level tasarımında seçilen sayı kadar
            // teslimat yapınca bu food conveyor'dan kalkar. -1 = sınırsız,
            // hiç düşürülmez (QueueManager dışından spawn edilen food'lar
            // için geriye dönük uyumlu varsayılan).
            if (remainingCapacity > 0)
            {
                remainingCapacity--;
                if (remainingCapacity == 0)
                    ConsumeAndDestroy();
            }
        }

        /// <summary>
        /// Atış hakkı (capacity) bitti — conveyor'dan kaldırılıyor.
        /// Şimdilik Destroy ediliyor; ObjectPool entegrasyonu eklenince
        /// Destroy yerine ObjectPool.Instance.Return(gameObject) çağrılacak.
        /// </summary>
        private void ConsumeAndDestroy()
        {
            if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Kapasite tükendi, kaldırılıyor.");

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (currentState == FoodState.OnConveyor)
                currentOnConveyorCount = Mathf.Max(0, currentOnConveyorCount - 1);

            ChangeState(FoodState.Served);

            // TODO: pooling eklenince -> ObjectPool.Instance.Return(gameObject);
            Destroy(gameObject);
        }

        /// <summary>
        /// STATIC — herhangi bir Food instance'ına (this) bağımlı DEĞİL.
        /// ObjectPool.Instance üzerinde çalıştığı için, bu delivery'i
        /// başlatan Food objesi coroutine bitmeden Destroy edilse bile
        /// sorunsuz tamamlanır.
        /// </summary>
        private static IEnumerator DeliverCloneRoutine(
            GameObject prefab, Vector3 launchPosition, Quaternion launchRotation,
            Customer target, float duration)
        {
            if (prefab == null || ObjectPool.Instance == null) yield break;

            GameObject clone = ObjectPool.Instance.Get(prefab, launchPosition, launchRotation);
            if (clone == null) yield break;

            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (clone == null)
                    yield break;

                if (target == null)
                {
                    ObjectPool.Instance.Return(clone);
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                clone.transform.position = Vector3.Lerp(launchPosition, target.transform.position, t);
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
            if (verboseLogging) Debug.Log($"Food [{gameObject.name}] State: {currentState}");
            StateChanged?.Invoke(this, newState);
        }
    }
}