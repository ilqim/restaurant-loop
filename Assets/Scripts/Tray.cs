using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RestaurantLoop
{
    public class Tray : MonoBehaviour
    {
        [Header("Kapasite Debug Etiketi")]
        [SerializeField] private bool showCapacityLabel = true;
        [SerializeField] private float labelMarginAboveStack = 0.4f;
        [SerializeField] private int labelFontSize = 48;
        [SerializeField] private float labelCharacterSize = 0.12f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("Yönelim")]
        [SerializeField] private float rotationSmoothing = 10f;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        // Bu tray'in şu an rezerve ettiği (yemek yola çıkmış ama henüz
        // ulaşmamış) müşteriler. Rezervasyonun asıl kaydı burada DEĞİL,
        // Customer.incomingDeliverySource'ta tutuluyor (Food ile ORTAK) —
        // bu set sadece "bu Tray beklenmedik şekilde disable olursa hangi
        // rezervasyonları serbest bırakmam gerekiyor" bilgisini tutan
        // yerel bir defter.
        //
        // ÖNEMLİ (bug fix notu): Bir müşteri ateşlendiği (FireDeliveryAt)
        // ANDA bu setten HEMEN çıkarılıyor — çünkü o andan itibaren
        // rezervasyonun kaderi artık asenkron DeliverCloneRoutine'in
        // elinde (klon ulaşınca ya da kaybolsa bile ReceiveFood() onu
        // çağırıp müşteriyi despawn edecek, Customer kendi
        // incomingDeliverySource'unu kendi Despawn'ında temizleyecek).
        // Bu setin ARTIK bu müşteriyi tutmaması gerekiyor, çünkü bu Tray
        // kapasitesi bittiği için genelde AYNI FRAME'DE senkron olarak
        // Despawn() çağırıyor -> OnDisable() -> ReleaseAllCustomerReservations().
        // Eğer müşteri o anda hâlâ bu sette duruyorsa, klon havadayken
        // rezervasyon ERKEN serbest bırakılır, müşteri "Serving"den anında
        // "Idle"a döner ve başka bir Tray/Food onu klon daha ulaşmadan
        // tekrar bulup ateş edebilir. Asıl bug buydu.
        private readonly HashSet<Customer> customersReservedByThisTray = new();

        // ============================================================
        // TEK ATOMİK ADIM + GLOBAL, FOOD-TYPE BAZLI ÖNCELİK KUYRUĞU
        //
        //   Checkpoint'e ulaşılınca -> hücre pendingCheckCells'e eklenir
        //                     (henüz arama/rezervasyon YAPILMAZ).
        //
        //   ProcessCheckedDeliveryPlans() -> her hücre için ARAMA VE
        //                     REZERVASYON aynı fonksiyon içinde, arada
        //                     hiçbir başka kod çalışmadan art arda
        //                     yapılır, hemen ardından ateşlenir. Bu
        //                     tepsinin KENDİ LateUpdate()'inden DEĞİL,
        //                     TrayDeliveryQueue (statik, GLOBAL, food
        //                     type başına ayrı FIFO kuyruk) tarafından,
        //                     konveyöre GİRİŞ SIRASINA göre çağrılır.
        //                     Sahnede kaç tane TrayManager / konveyör
        //                     hattı olursa olsun (satır, sütun, vb.) bu
        //                     kuyruk GLOBAL olduğu için öncelik tüm
        //                     sahnede tutarlıdır — hangi hatta olduğu
        //                     fark etmez.
        //
        // Bu sayede: iki tepsi aynı müşteriyi hedeflese bile önce giren
        // tepsi HER ZAMAN önce dener; kaybeden otomatik pas geçer.
        // Çakışma yoksa hiçbir tepsi "bekletilmez" — herkes normal
        // şekilde aynı frame'de ateş eder.
        // ============================================================

        private readonly List<Vector2Int> pendingCheckCells = new();

        private class StackPieceInfo
        {
            public GameObject go;
            public int layerIndex;

            // Parçanın tepsiye göre LOCAL X/Z konumu.
            public Vector2 offsetXZ;
        }

        private const int PiecesPerLayer = 4;

        private readonly List<StackPieceInfo> stackPieceInfos = new();

        private int currentLayerCount;

        private TrayManager trayManager;
        private FoodType foodType;
        private int capacity;
        private TrayVisualConfig config;

        private int currentIndex;
        private Coroutine moveRoutine;
        private int deliveryTryCounter;
        private bool depleted;

        private List<(float t, Vector2Int cell)> deliveryCheckpoints;
        private int nextCheckpointIndex;

        private float[] cumulativeMovementDistance;
        private float totalMovementLength;

        private TextMesh capacityLabel;
        private Transform capacityLabelTransform;
        private Camera labelFacingCamera;


        // ============================================================
        // INIT
        // ============================================================

        public void Init(TrayManager manager, FoodType type, int startCapacity)
        {
            trayManager = manager;
            foodType = type;
            capacity = startCapacity;

            config = trayManager.GetVisualConfig(foodType);

            currentIndex = 0;
            deliveryTryCounter = 0;
            depleted = false;

            customersReservedByThisTray.Clear();
            pendingCheckCells.Clear();

            // Global, food-type bazlı öncelik kuyruğuna kaydol. İlk giren
            // tepsi kendi food type'ının kuyruğunda başta kalır ve
            // ateşleme önceliğine sahip olur (bkz. TrayDeliveryQueue).
            TrayDeliveryQueue.Register(this, foodType);

            var gridManager = trayManager.GridManagerRef;
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
            {
                Debug.LogWarning(
                    $"Tray [{gameObject.name}] waypoint bulunamadı."
                );
                return;
            }

            transform.position = trayManager.GetWaypointPosition(0);

            var facings = gridManager.WaypointFacingDirections;

            if (facings != null &&
                facings.Count > 0 &&
                facings[0].sqrMagnitude > 0.0001f)
            {
                transform.rotation =
                    Quaternion.LookRotation(
                        facings[0],
                        Vector3.up
                    );
            }

            cumulativeMovementDistance =
                new float[waypoints.Count];

            cumulativeMovementDistance[0] = 0f;

            for (int i = 1; i < waypoints.Count; i++)
            {
                cumulativeMovementDistance[i] =
                    cumulativeMovementDistance[i - 1] +
                    Vector3.Distance(
                        waypoints[i - 1],
                        waypoints[i]
                    );
            }

            totalMovementLength =
                waypoints.Count > 0
                    ? cumulativeMovementDistance[^1]
                    : 0f;

            deliveryCheckpoints =
                gridManager.DeliveryCheckpoints;

            nextCheckpointIndex = 1;

            BuildStackVisuals();

            CreateCapacityLabel();
            UpdateCapacityLabel();

            if (deliveryCheckpoints.Count > 0)
            {
                // Direkt ateş edilmiyor — ilk checkpoint kontrol
                // kuyruğuna eklenir; bu frame'in Update()'i kontrol
                // edecek, TrayDeliveryQueue (öncelik sırasına göre)
                // ateşleyecek.
                QueueDeliveryCheck(
                    deliveryCheckpoints[0].cell
                );
            }

            moveRoutine =
                StartCoroutine(
                    MoveOnConveyor()
                );
        }


        private void OnDisable()
        {
            ReleaseAllCustomerReservations();

            pendingCheckCells.Clear();

            TrayDeliveryQueue.Unregister(this, foodType);

            trayManager?.ReleaseTraySlot();
        }


        // ============================================================
        // ETİKET YÖNLENDİRME — bu tepsiye özel, sıraya girmesine gerek
        // yok, kendi LateUpdate()'inde kalabilir.
        // ============================================================

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


        // ============================================================
        // ARAMA + REZERVASYON + ATEŞLEME — ARTIK BU TEPSİNİN KENDİSİ
        // ÇAĞIRMIYOR. TrayDeliveryQueue (global, food-type bazlı FIFO
        // kuyruk), bu metodu konveyöre GİRİŞ SIRASINA göre (önce giren
        // önce) tek tek çağırır.
        //
        // Her hücre için arama VE rezervasyon AYNI ADIMDA, aralarında
        // hiçbir başka kodun çalışmasına izin vermeden yapılır — bu
        // yüzden "arama anında müsaitti ama rezervasyon anında değildi"
        // senaryosu yapısal olarak imkânsızdır (bkz. sınıf başındaki not).
        // ============================================================

        public void ProcessCheckedDeliveryPlans()
        {
            if (pendingCheckCells.Count == 0)
                return;

            // Anlık kopya: FireDeliveryAt() kapasiteyi tüketip Despawn()
            // çağırabilir, bu da OnDisable() üzerinden pendingCheckCells'i
            // TEMİZLER. Aynı listeyi enumerate ederken temizlemek
            // "Collection was modified" hatasına yol açıyordu — bu yüzden
            // önce kopya alınıp orijinal liste hemen boşaltılıyor.
            var cellsSnapshot = new List<Vector2Int>(pendingCheckCells);
            pendingCheckCells.Clear();

            var customerManager = trayManager != null
                ? trayManager.CustomerManagerRef
                : null;

            if (customerManager == null)
                return;

            foreach (Vector2Int cell in cellsSnapshot)
            {
                if (depleted || capacity <= 0)
                    break;

                deliveryTryCounter++;

                // --------------------------------------------------
                // ATOMİK ADIM: arama ve rezervasyon art arda, aynı
                // fonksiyon çağrısı içinde yapılır. Bu iki satır
                // arasına Unity'nin tek thread'li yapısı gereği HİÇBİR
                // başka Tray'in kodu giremez.
                // --------------------------------------------------
                if (!customerManager.TryFindDeliverableCustomer(
                        foodType,
                        cell,
                        1,
                        out Customer target) ||
                    target == null)
                {
                    continue;
                }

                if (!target.TryReserveForDelivery(this, foodType))
                {
                    // Teorik olarak burada HİÇ düşmemeli — arama zaten
                    // "müsait" dedi ve hemen ardından rezervasyon
                    // deneniyor. Yine de savunma amaçlı bırakıyoruz:
                    // eğer buraya düşerse, arama/rezervasyon mantığında
                    // hâlâ bir tutarsızlık var demektir ve loglanmalı.
                    if (verboseLogging)
                    {
                        Debug.LogWarning(
                            $"Tray [{gameObject.name}] " +
                            $"BEKLENMEDİK SKIP (arama müsait dedi ama " +
                            $"rezervasyon reddetti) -> {target.name} " +
                            $"(ID={target.GetInstanceID()}, " +
                            $"Session={target.OrderSessionId}, " +
                            $"state={target.CurrentState}) " +
                            $"cell ({cell.x},{cell.y})"
                        );
                    }

                    continue;
                }

                customersReservedByThisTray.Add(target);

                if (verboseLogging)
                {
                    Debug.Log(
                        $"Tray [{gameObject.name}] " +
                        $"delivery FIRE -> {target.name} " +
                        $"(ID={target.GetInstanceID()}, " +
                        $"Session={target.OrderSessionId}) " +
                        $"cell ({cell.x},{cell.y})"
                    );
                }

                FireDeliveryAt(target);
            }
        }


        /// <summary>
        /// Rezervasyonu zaten alınmış bir müşteriye gerçek teslimatı
        /// gerçekleştirir: kapasiteyi düşürür, görsel parçayı eksiltir,
        /// klonu fırlatır, gerekiyorsa tepsiyi tüketir.
        /// </summary>
        private void FireDeliveryAt(Customer target)
        {
            capacity =
                Mathf.Max(
                    0,
                    capacity - 1
                );

            // Parça seçimi müşterinin gerçek konumuna göre değil,
            // tepsinin kendi ön yönüne (transform.forward) göre yapılıyor.
            // Tepsi her zaman "önü nereyi gösteriyorsa" oradaki gruptan
            // ateş eder.
            RemoveStackPieceTowardCustomer(
                transform.forward
            );

            UpdateCapacityLabel();

            LaunchDeliveryClone(
                target,
                transform.position
            );

            // ------------------------------------------------------
            // *** BUG FİX ***
            // Teslimat artık ASENKRON DeliverCloneRoutine'e devredildi
            // — o rutin, klon gerçekten müşteriye ulaşana (ya da yolda
            // kaybolsa bile) kadar ReceiveFood()'u kendisi çağıracak;
            // Customer da kendi Despawn'ında incomingDeliverySource'unu
            // kendisi temizleyecek. Bu yüzden bu rezervasyonu ARTIK bu
            // Tray'in yerel "disable olursam serbest bırak" defterinde
            // (customersReservedByThisTray) TUTMUYORUZ — sorumluluk
            // devredildi.
            //
            // NEDEN KRİTİK: Kapasite tam bu teslimatta bittiyse, hemen
            // aşağıda depleted=true olup Despawn() SENKRON olarak
            // çağrılacak (aynı çağrı zinciri, aynı frame). Despawn ->
            // OnDisable -> ReleaseAllCustomerReservations çalışır; bu
            // müşteri o anda hâlâ sette duruyor olsaydı, KLON HAVADAYKEN
            // rezervasyon ERKEN serbest bırakılırdı — müşteri "Serving"
            // yerine anında "Idle"a döner, başka bir Tray/Food onu klon
            // daha ulaşmadan tekrar bulup ateş edebilirdi. Şimdi bu
            // satır, o pencereyi tamamen kapatıyor: müşteri ateşlendiği
            // anda bu tepsinin "disable olursa serbest bırakacakları"
            // listesinden çıkıyor, artık SADECE DeliverCloneRoutine'in
            // (ya da Customer'ın kendi Despawn'ının) sorumluluğunda.
            // ------------------------------------------------------
            customersReservedByThisTray.Remove(target);

            if (capacity <= 0 &&
                !depleted)
            {
                depleted = true;

                if (moveRoutine != null)
                {
                    StopCoroutine(
                        moveRoutine
                    );

                    moveRoutine = null;
                }

                Despawn();
            }
        }


        // ============================================================
        // STACK GÖRSELİ
        // ============================================================

        private void BuildStackVisuals()
        {
            ClearStackVisuals();

            if (config.stackPiecePrefab == null)
            {
                currentLayerCount = 0;
                PositionLabelAboveStack();
                return;
            }

            int count =
                Mathf.Min(
                    capacity,
                    Mathf.Max(
                        0,
                        config.maxVisualPieces
                    )
                );

            currentLayerCount =
                Mathf.CeilToInt(
                    count / (float)PiecesPerLayer
                );

            for (int i = 0; i < count; i++)
            {
                SpawnStackPiece(i);
            }

            PositionLabelAboveStack();
        }


        private void SpawnStackPiece(int index)
        {
            int layer =
                index / PiecesPerLayer;

            int posInLayer =
                index % PiecesPerLayer;

            float half =
                config.pieceSpacing * 0.5f;

            /*
             * Layer içindeki dizilim:
             *
             *       +Z
             *
             *       0   1
             *
             *       2   3
             *
             *       -Z
             *
             * X:
             *       -     +
             *
             * Z:
             *       +     +
             *       -     -
             */

            float xOffset =
                (posInLayer == 0 ||
                 posInLayer == 2)
                    ? -half
                    : half;

            float zOffset =
                (posInLayer == 0 ||
                 posInLayer == 1)
                    ? half
                    : -half;

            GameObject piece =
                ObjectPool.Instance != null
                    ? ObjectPool.Instance.Get(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform
                    )
                    : Instantiate(
                        config.stackPiecePrefab,
                        transform.position,
                        config.stackPiecePrefab.transform.rotation,
                        transform
                    );

            float yOffset =
                config.foodBaseYOffset +
                layer * config.pieceHeightSpacing;

            piece.transform.localPosition =
                new Vector3(
                    xOffset,
                    yOffset,
                    zOffset
                );

            stackPieceInfos.Add(
                new StackPieceInfo
                {
                    go = piece,
                    layerIndex = layer,
                    offsetXZ = new Vector2(
                        xOffset,
                        zOffset
                    )
                }
            );
        }


        // ============================================================
        // YENİ PARÇA SEÇME SİSTEMİ
        // ============================================================

        /// <summary>
        /// Müşteriye gönderilecek yemek parçasını seçer.
        ///
        /// NOT: Seçim müşterinin gerçek pozisyonuna göre DEĞİL,
        /// tepsinin kendi ÖN yönüne (transform.forward) göre yapılır.
        /// Yani tepsi her zaman "önü nereyi gösteriyorsa" o taraftaki
        /// parça grubundan eksiltmeye başlar; müşteri fiilen sağda,
        /// solda ya da arkada olsa bile bu seçim değişmez.
        ///
        /// Tepsinin önü +Z ise:
        ///
        ///      0   1    ← ÖNCE (ön grup tamamen bitmeden
        ///      2   3    ← SONRA    arka gruba geçilmez)
        ///
        /// </summary>
        private void RemoveStackPieceTowardCustomer(
            Vector3 dirToCustomerWorld
        )
        {
            if (stackPieceInfos.Count == 0)
                return;


            // --------------------------------------------------------
            // 1. Önce hangi layer'dan yiyecek çıkaracağımızı belirle.
            // --------------------------------------------------------

            int targetLayer;

            if (config.removeFromTopFirst)
            {
                targetLayer =
                    stackPieceInfos.Max(
                        p => p.layerIndex
                    );
            }
            else
            {
                targetLayer =
                    stackPieceInfos.Min(
                        p => p.layerIndex
                    );
            }


            // Sadece aktif layer'daki parçalarla ilgileniyoruz.

            List<StackPieceInfo> layerPieces =
                stackPieceInfos
                    .Where(
                        p => p.layerIndex == targetLayer
                    )
                    .ToList();

            if (layerPieces.Count == 0)
                return;


            // --------------------------------------------------------
            // 2. Yönü WORLD -> LOCAL çevir.
            //    (dirToCustomerWorld artık her zaman transform.forward
            //    olarak gönderiliyor, bu yüzden localDir sabit biçimde
            //    tepsinin "ön"ünü (local +Z) verir.)
            // --------------------------------------------------------

            Vector3 localDir =
                transform.InverseTransformDirection(
                    dirToCustomerWorld
                );

            localDir.y = 0f;

            if (localDir.sqrMagnitude < 0.0001f)
            {
                localDir = Vector3.forward;
            }

            localDir.Normalize();


            Vector2 customerDirection =
                new Vector2(
                    localDir.x,
                    localDir.z
                );


            // --------------------------------------------------------
            // 3. Her parçanın "ön yöne" yakınlık skorunu hesapla.
            // --------------------------------------------------------
            //
            // Dot product sayesinde:
            //
            // Ön yöndeki parçalar -> yüksek skor
            // Arka yöndeki parçalar -> düşük skor
            //
            // Örneğin ön +Z ise:
            //
            // 0 = +Z
            // 1 = +Z
            // 2 = -Z
            // 3 = -Z
            //
            // Sonuç:
            //
            // 0 / 1 önce
            // 2 / 3 sonra
            // --------------------------------------------------------

            StackPieceInfo chosen = null;

            float bestScore =
                float.NegativeInfinity;


            foreach (StackPieceInfo piece in layerPieces)
            {
                Vector2 piecePosition =
                    piece.offsetXZ;

                float score =
                    Vector2.Dot(
                        piecePosition,
                        customerDirection
                    );


                // Aynı mesafedeki parçalar için
                // deterministik bir seçim yap.
                //
                // Böylece her frame rastgele değişmez.

                if (chosen == null ||
                    score > bestScore)
                {
                    chosen = piece;
                    bestScore = score;
                }
                else if (
                    Mathf.Approximately(
                        score,
                        bestScore
                    ))
                {
                    // Eşit uzaklıktaki iki parçada
                    // önce küçük X, sonra küçük Z.

                    if (piece.offsetXZ.x <
                        chosen.offsetXZ.x)
                    {
                        chosen = piece;
                    }
                    else if (
                        Mathf.Approximately(
                            piece.offsetXZ.x,
                            chosen.offsetXZ.x
                        ) &&
                        piece.offsetXZ.y <
                        chosen.offsetXZ.y)
                    {
                        chosen = piece;
                    }
                }
            }


            if (chosen == null)
                return;


            // --------------------------------------------------------
            // 4. Parçayı listeden çıkar.
            // --------------------------------------------------------

            stackPieceInfos.Remove(chosen);


            // --------------------------------------------------------
            // 5. Görsel parçayı pool'a geri gönder.
            // --------------------------------------------------------

            if (chosen.go != null)
            {
                if (ObjectPool.Instance != null)
                {
                    ObjectPool.Instance.Return(
                        chosen.go
                    );
                }
                else
                {
                    Destroy(chosen.go);
                }
            }


            // --------------------------------------------------------
            // 6. Stack yüksekliğini güncelle.
            // --------------------------------------------------------

            currentLayerCount =
                stackPieceInfos.Count > 0
                    ? stackPieceInfos.Max(
                        p => p.layerIndex
                    ) + 1
                    : 0;

            PositionLabelAboveStack();


            if (verboseLogging)
            {
                Debug.Log(
                    $"Tray [{gameObject.name}] " +
                    $"stack piece removed. " +
                    $"Layer={targetLayer}, " +
                    $"LocalOffset={chosen.offsetXZ}, " +
                    $"FrontDir={customerDirection}"
                );
            }
        }


        private void ClearStackVisuals()
        {
            foreach (var info in stackPieceInfos)
            {
                if (info.go == null)
                    continue;

                if (ObjectPool.Instance != null)
                {
                    ObjectPool.Instance.Return(
                        info.go
                    );
                }
                else
                {
                    Destroy(info.go);
                }
            }

            stackPieceInfos.Clear();
            currentLayerCount = 0;
        }


        // ============================================================
        // HAREKET
        // ============================================================

        private IEnumerator MoveOnConveyor()
        {
            var gridManager =
                trayManager.GridManagerRef;

            var waypoints =
                gridManager.WaypointWorldPositions;

            var facings =
                gridManager.WaypointFacingDirections;

            while (true)
            {
                int nextIndex =
                    currentIndex + 1;

                bool reachedExitEnd =
                    nextIndex >= waypoints.Count;


                if (reachedExitEnd)
                {
                    AdvanceDeliveryCheckpoints(1f);

                    if (depleted)
                    {
                        moveRoutine = null;
                        yield break;
                    }


                    if (capacity > 0)
                    {
                        if (TryMergeIntoSlot())
                        {
                            moveRoutine = null;
                            Despawn();
                            yield break;
                        }


                        if (trayManager.LoopIfSlotsFull)
                        {
                            nextIndex = 0;
                        }
                        else
                        {
                            if (verboseLogging)
                            {
                                Debug.Log(
                                    $"Tray [{gameObject.name}] " +
                                    $"Exit'te boş slot yok, parkediyor."
                                );
                            }

                            moveRoutine = null;
                            yield break;
                        }
                    }
                    else
                    {
                        moveRoutine = null;
                        Despawn();
                        yield break;
                    }
                }


                Vector3 targetPosition =
                    trayManager.GetWaypointPosition(
                        nextIndex
                    );

                Vector3 targetFacing =
                    nextIndex < facings.Count
                        ? facings[nextIndex]
                        : Vector3.zero;


                yield return StartCoroutine(
                    MoveTo(
                        currentIndex,
                        targetPosition,
                        targetFacing
                    )
                );


                currentIndex = nextIndex;


                if (depleted)
                {
                    moveRoutine = null;
                    yield break;
                }
            }
        }


        private IEnumerator MoveTo(
            int fromIndex,
            Vector3 target,
            Vector3 targetFacing
        )
        {
            Vector3 start =
                transform.position;

            float distance =
                Vector3.Distance(
                    start,
                    target
                );

            float speed =
                Mathf.Max(
                    0.01f,
                    config.conveyorSpeed
                );

            float duration =
                Mathf.Max(
                    0.01f,
                    distance / speed
                );


            float prefixDistance =
                cumulativeMovementDistance != null &&
                fromIndex <
                cumulativeMovementDistance.Length
                    ? cumulativeMovementDistance[fromIndex]
                    : 0f;


            Quaternion targetRotation =
                targetFacing.sqrMagnitude >
                0.0001f
                    ? Quaternion.LookRotation(
                        targetFacing,
                        Vector3.up
                    )
                    : transform.rotation;


            float elapsed = 0f;


            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / duration
                    );


                transform.position =
                    Vector3.Lerp(
                        start,
                        target,
                        t
                    );


                transform.rotation =
                    rotationSmoothing > 0f
                        ? Quaternion.Slerp(
                            transform.rotation,
                            targetRotation,
                            Time.deltaTime *
                            rotationSmoothing
                        )
                        : targetRotation;


                if (totalMovementLength >
                    0.0001f)
                {
                    float globalT =
                        (
                            prefixDistance +
                            distance * t
                        ) /
                        totalMovementLength;


                    AdvanceDeliveryCheckpoints(
                        globalT
                    );


                    if (depleted)
                        yield break;
                }


                yield return null;
            }


            transform.position = target;
            transform.rotation = targetRotation;
        }


        private void AdvanceDeliveryCheckpoints(
            float globalT
        )
        {
            if (deliveryCheckpoints == null)
                return;


            while (
                nextCheckpointIndex <
                deliveryCheckpoints.Count &&
                deliveryCheckpoints[
                    nextCheckpointIndex
                ].t <= globalT
            )
            {
                var checkpoint =
                    deliveryCheckpoints[
                        nextCheckpointIndex
                    ];

                nextCheckpointIndex++;

                // Direkt ateş edilmiyor — checkpoint sadece kontrol
                // kuyruğuna eklenir. Kontrol Update()'te, ateşleme
                // TrayDeliveryQueue'nun (global, food-type bazlı öncelik
                // sırasına göre) tetiklediği ProcessCheckedDeliveryPlans
                // içinde yapılır.
                QueueDeliveryCheck(
                    checkpoint.cell
                );


                if (depleted)
                    return;
            }
        }


        // ============================================================
        // TESLİMAT — KUYRUĞA EKLEME (gerçek arama/ateşleme burada
        // yapılmaz, bkz. Update() / ProcessCheckedDeliveryPlans())
        // ============================================================

        private void QueueDeliveryCheck(
            Vector2Int cell
        )
        {
            if (capacity <= 0 || depleted)
                return;

            pendingCheckCells.Add(cell);
        }


        private void LaunchDeliveryClone(
            Customer target,
            Vector3 launchPosition
        )
        {
            if (config.stackPiecePrefab == null)
            {
                if (target != null)
                {
                    target.ReceiveFood();
                    customersReservedByThisTray.Remove(target);
                }

                return;
            }


            if (ObjectPool.Instance == null)
            {
                if (target != null)
                {
                    target.ReceiveFood();
                    customersReservedByThisTray.Remove(target);
                }

                return;
            }


            ObjectPool.Instance.StartCoroutine(
                DeliverCloneRoutine(
                    this,
                    config.stackPiecePrefab,
                    launchPosition,
                    transform.rotation,
                    target,
                    config.deliverySpeed
                )
            );
        }


        private static IEnumerator DeliverCloneRoutine(
            Tray sourceTray,
            GameObject prefab,
            Vector3 launchPosition,
            Quaternion launchRotation,
            Customer target,
            float speed
        )
        {
            GameObject clone =
                ObjectPool.Instance.Get(
                    prefab,
                    launchPosition,
                    launchRotation
                );


            if (clone == null)
            {
                if (target != null)
                {
                    target.ReceiveFood();
                    sourceTray?.customersReservedByThisTray.Remove(target);
                }

                yield break;
            }


            Vector3 targetPos =
                target != null
                    ? target.transform.position
                    : launchPosition;


            float distance =
                Vector3.Distance(
                    launchPosition,
                    targetPos
                );


            float duration =
                Mathf.Max(
                    0.01f,
                    distance /
                    Mathf.Max(
                        0.01f,
                        speed
                    )
                );


            float elapsed = 0f;


            while (elapsed < duration)
            {
                if (clone == null)
                {
                    // Klon yolculuk sırasında kayboldu (ör. pool geri
                    // alındı). Rezervasyon SERBEST BIRAKILMIYOR — teslimat
                    // görselsiz ama KESİN olarak tamamlanıyor. Bkz. Tray
                    // sınıfının başındaki not: rezervasyonun kaderi artık
                    // bu Tray'in disable/despawn akışından bağımsız,
                    // sadece bu rutinin ve Customer'ın kendi Despawn'ının
                    // elinde.
                    if (sourceTray != null && sourceTray.verboseLogging)
                    {
                        Debug.LogWarning(
                            $"Tray [{(sourceTray != null ? sourceTray.gameObject.name : "?")}] " +
                            $"klon yolculuk sırasında kayboldu -> " +
                            $"{(target != null ? target.name : "null")} " +
                            $"(Session={(target != null ? target.OrderSessionId : -1)}) " +
                            $"teslimat GÖRSELSİZ olarak tamamlanıyor " +
                            $"(rezervasyon SERBEST BIRAKILMIYOR)."
                        );
                    }

                    if (target != null)
                    {
                        target.ReceiveFood();
                        sourceTray?.customersReservedByThisTray.Remove(target);
                    }

                    yield break;
                }


                elapsed += Time.deltaTime;


                float t =
                    Mathf.Clamp01(
                        elapsed / duration
                    );


                clone.transform.position =
                    Vector3.Lerp(
                        launchPosition,
                        targetPos,
                        t
                    );


                yield return null;
            }


            if (clone != null)
            {
                clone.transform.position =
                    targetPos;

                ObjectPool.Instance.Return(
                    clone
                );
            }


            if (target != null)
            {
                target.ReceiveFood();
                sourceTray?.customersReservedByThisTray.Remove(target);
            }
        }


        // ============================================================
        // CUSTOMER RESERVATION (yerel defter — asıl kayıt Customer'da)
        // ============================================================

        private void ReleaseAllCustomerReservations()
        {
            if (customersReservedByThisTray.Count == 0)
                return;


            foreach (
                var customer
                in customersReservedByThisTray)
            {
                if (customer != null)
                    customer.ReleaseDeliveryReservation(this);
            }


            customersReservedByThisTray.Clear();
        }


        // ============================================================
        // MERGE
        // ============================================================

        private bool TryMergeIntoSlot()
        {
            GameObject prefab =
                trayManager.GetFoodPrefab(
                    foodType
                );


            if (prefab == null)
            {
                Debug.LogWarning(
                    $"Tray: '{foodType}' " +
                    $"için merge-back Food prefabı yok."
                );

                return false;
            }


            GameObject foodGo =
                Instantiate(
                    prefab,
                    transform.position,
                    prefab.transform.rotation
                );


            Food food =
                foodGo.GetComponent<Food>();


            if (food == null)
            {
                Destroy(foodGo);
                return false;
            }


            food.PresetCapacity(
                capacity
            );


            bool placed =
                trayManager.SlotManagerRef != null &&
                trayManager.SlotManagerRef.TryPlaceFood(
                    food
                );


            if (!placed)
            {
                Destroy(foodGo);
                return false;
            }


            capacity = 0;

            return true;
        }


        // ============================================================
        // DESPAWN
        // ============================================================

        private void Despawn()
        {
            if (verboseLogging)
            {
                Debug.Log(
                    $"Tray [{gameObject.name}] despawn."
                );
            }


            ClearStackVisuals();


            if (ObjectPool.Instance != null)
            {
                ObjectPool.Instance.Return(
                    gameObject
                );
            }
            else
            {
                gameObject.SetActive(false);
            }
        }


        // ============================================================
        // CAPACITY LABEL
        // ============================================================

        private void CreateCapacityLabel()
        {
            if (!showCapacityLabel)
                return;


            if (capacityLabel != null)
            {
                UpdateCapacityLabel();
                PositionLabelAboveStack();
                return;
            }


            GameObject labelGO =
                new GameObject(
                    "CapacityLabel"
                );


            labelGO.transform.SetParent(
                transform,
                false
            );


            capacityLabelTransform =
                labelGO.transform;


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


            PositionLabelAboveStack();
        }


        private void PositionLabelAboveStack()
        {
            if (capacityLabelTransform == null)
                return;


            float stackTopHeight =
                config.foodBaseYOffset +
                Mathf.Max(
                    0,
                    currentLayerCount - 1
                ) *
                config.pieceHeightSpacing;


            capacityLabelTransform.localPosition =
                new Vector3(
                    0,
                    stackTopHeight +
                    labelMarginAboveStack,
                    0
                );
        }


        private void UpdateCapacityLabel()
        {
            if (capacityLabel != null)
            {
                capacityLabel.text =
                    capacity.ToString();
            }
        }
    }
}