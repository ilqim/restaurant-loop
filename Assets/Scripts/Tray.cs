using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace RestaurantLoop
{
    public class Tray : MonoBehaviour
    {
        // Tray'in o anki mantıksal durumu.
        // InTrayBase  -> Base'de bekliyor            => TrayIdleAnim
        // InConveyor  -> Konveyörde ilerliyor         => TrayIdleAnim
        // Vanishing   -> Teslimat/merge sonrası kayboluyor => TrayVanishAnim
        public enum TrayState
        {
            InTrayBase,
            InConveyor,
            Vanishing
        }

        [Header("Görsel Model — ÖNEMLİ")]
        [Tooltip("Tray'in GERÇEK 3D mesh'ini taşıyan child obje. Path takibi için gereken TÜM rotasyon " +
                 "artık KÖK objeye (transform) değil, buraya uygulanıyor — böylece kök obje hep sabit " +
                 "(rotasyonsuz) kalır ve CountLabel gibi kökün child'ları path dönüşünden hiç etkilenmez. " +
                 "Boş bırakılırsa (geri uyumluluk için) kökün kendisi kullanılır — YİNE ESKİ DAVRANIŞ, " +
                 "yani bu alanı MUTLAKA ata.")]
        [SerializeField] private Transform visualModel;

        [Header("Ateşleme Animasyonu")]
        [SerializeField] private float shootPunchScale = 0.15f;
        [SerializeField] private float shootPunchDuration = 0.12f;
        [SerializeField] private int shootPunchVibrato = 5;
        [Range(0f,1f)]
        [SerializeField] private float shootPunchElasticity = 0.5f;

        [Header("Vanish Hareketi (DOTween — Geriye ve Yukarı)")]
        [Tooltip("Vanish animasyonu oynarken ModelTransform'un ne kadar geriye (tepsinin yemek fırlattığı " +
                 "yönün TAM TERSİNE, yani -ModelTransform.forward) ilerleyeceği. Bu yön tepsinin o anki " +
                 "rotasyonuna göre HESAPLANIR — dünya ekseni sabit DEĞİL, tepsi hangi yöne bakıyorsa ona göre değişir.")]
        [SerializeField] private float vanishMoveDistance = 1.5f;

        [Tooltip("Aynı anda DÜNYA +Y (yukarı, sabit dünya ekseni) ekseninde ne kadar yükseleceği — " +
                 "'havaya fırlama' hissi için. Bu, geri gitme yönünden bağımsız, her zaman yukarı.")]
        [SerializeField] private float vanishMoveUpDistance = 1f;

        [Tooltip("Geri yönün etrafında rastgele sapma açısı (derece). Her vanish'te -ModelTransform.forward " +
                 "yönü, dünya +Y ekseni etrafında [-bu değer, +bu değer] aralığında rastgele döndürülür — " +
                 "böylece her tepsi tam olarak aynı çizgide değil, hafif farklı açılarda geri gider.")]
        [SerializeField] private float vanishMoveAngleNoise = 20f;

        [Tooltip("Hareketin ease eğrisi.")]
        [SerializeField] private Ease vanishMoveEase = Ease.OutQuad;

        [Tooltip("Vanish hareketinin süresi (saniye). ÖNEMLİ: Hareket animasyonla AYNI ANDA, tetiklendiği " +
                 "an başlaması gerektiği için artık Animator klip uzunluğunu BEKLEMİYORUZ — bu süre doğrudan " +
                 "kullanılıyor. Vanish klibinin süresine yakın bir değer gir ki hareket ile animasyon birlikte bitsin.")]
        [SerializeField] private float vanishMoveDuration = 0.3f;

        [Header("Base'e Giriş Scale Animasyonu (Pop-In)")]
        [Tooltip("Tray, ReturnTrayToBase() ile base'e 'ışınlandıktan' sonra küçük scale'de aktif olup " +
                 "büyüyerek görünsün mü? (Sadece dönüşlerde uygulanır — ilk sahne kurulumunda/booster'da değil.)")]
        [SerializeField] private float baseScaleInStartScale = 0.3f;

        [Tooltip("Küçükten normale büyüme süresi (saniye).")]
        [SerializeField] private float baseScaleInDuration = 0.25f;

        [Tooltip("Büyüme ease eğrisi — 'pop' hissi için OutBack iyi durur.")]
        [SerializeField] private Ease baseScaleInEase = Ease.OutBack;

        [Header("Sayı Etiketi")]
        [Tooltip("Tray'in kalan kapasitesini gösteren 3D etiket (Canvas değil, normal derinlik testine tabi). " +
                 "Kökün (bu objenin) child'ı olmalı, visualModel'in DEĞİL — böylece path dönüşünden etkilenmez.")]
        [SerializeField] private WorldSpaceCountLabel countLabel;

        [Header("Yönelim")]
        [SerializeField] private float rotationSmoothing = 15f;

        // Trayin o anki konveyör segmentinde hangi grid ekseninde
        // hareket ettiği. Her yeni segment başında önceden hesaplanmış listeden çekilir.
        private WaypointMoveAxis currentMoveAxis = WaypointMoveAxis.None;

        [Header("DEBUG — Eksen / Hizalama (canlı izlenir)")]
        [Tooltip("Şu an hangi grid ekseninde ilerliyor. Play modda canlı değişir, elle değiştirmenin etkisi yoktur.")]
        [SerializeField] private WaypointMoveAxis debugMoveAxis;

        [Tooltip("Eksen bu segmentte GÜNCELLENMEDİ mi? True ise currentMoveAxis bir önceki segmentten miras kaldı demektir (ör. yuvarlatılmış köşeler).")]
        [SerializeField] private bool debugAxisUnchangedThisSegment;

        [Tooltip("En son IsAlignedWithCustomer çağrısında kullanılan tray hücresi (checkpoint cell).")]
        [SerializeField] private Vector2Int debugLastTrayCell;

        [Tooltip("En son kontrol edilen müşterinin Row/Col değeri.")]
        [SerializeField] private Vector2Int debugLastTargetRowCol;

        [Tooltip("Son hizalama kontrolünün sonucu ve nedeni, insan tarafından okunabilir.")]
        [SerializeField] private string debugLastAlignmentResult = "-";

        [Header("Tray State Animasyonları")]
        [Tooltip("Trayin scale'ini 1 yapmak için üstüne konan parent'ın altındaki child. " +
                 "Boşsa child'lardan otomatik bulunmaya çalışılır.")]
        [SerializeField] private Animator trayAnimator;

        [Tooltip("SADECE DEBUG: şu anki mantıksal state burada canlı görünür. Elle değiştirmenin bir etkisi yok.")]
        [SerializeField] private TrayState debugCurrentState;

        // Animator state isimleri (GetCurrentAnimatorStateInfo ile kontrol için)
        private static readonly int IdleAnimHash = Animator.StringToHash("TrayIdleAnim");
        private static readonly int VanishAnimHash = Animator.StringToHash("TrayVanishAnim");

        // Animator Controller'da bu isimlerde İKİ Trigger parametresi olmalı.
        private static readonly int IdleTriggerHash = Animator.StringToHash("PlayIdle");
        private static readonly int VanishTriggerHash = Animator.StringToHash("PlayVanish");

        private TrayState currentState;
        private Coroutine vanishRoutine;

        public TrayState CurrentState => currentState;

        private readonly HashSet<Customer> customersReservedByThisTray = new();
        private readonly List<Vector2Int> pendingCheckCells = new();

        private class StackPieceInfo
        {
            public GameObject go;
            public int layerIndex;
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

        /// <summary>
        /// Path takibi / yönelim için kullanılacak GERÇEK transform.
        /// visualModel atanmışsa o, atanmamışsa (geri uyumluluk) kökün kendisi.
        /// </summary>
        public Transform ModelTransform => visualModel != null ? visualModel : transform;

        private void Awake()
        {
            if (trayAnimator == null)
                trayAnimator = GetComponentInChildren<Animator>(true);

            if (countLabel == null)
                Debug.LogWarning($"Tray [{gameObject.name}]: Count Label atanmamış — sayı gösterilemeyecek.", this);

            if (visualModel == null)
                Debug.LogWarning($"Tray [{gameObject.name}]: Visual Model atanmamış — path dönüşü hâlâ kök objeyi (dolayısıyla CountLabel'ı da) etkileyecek.", this);
        }

        public void ParkAtBase(TrayManager manager, Vector3 pos, bool scaleIn = false)
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (vanishRoutine != null)
            {
                StopCoroutine(vanishRoutine);
                vanishRoutine = null;
            }

            trayManager = manager;

            // ÖNEMLİ FIX: ModelTransform'un yanı sıra KÖK transform'u da
            // (transform) DOKill ediyoruz. Sebep: kuyrukta beklerken bu
            // tepsi RefreshBaseQueuePositions() tarafından kendi ROOT
            // transform'una bir DOMove tween'i almış olabilir (sıradaki
            // yerini almak için). Eğer bu tray çok hızlı tekrar konveyöre
            // çıkarılırsa ve bu tween öldürülmezse, konveyör hareketi
            // (transform.position'ı elle set eden MoveSegment) ile bu eski
            // "base slotuna git" tween'i AYNI ANDA aynı transform'u
            // kontrol etmeye çalışır — tepsi konveyörde ilerlerken görünmez
            // bir güçle base'e doğru çekiliyormuş gibi davranır.
            transform.DOKill();
            transform.position = pos;

            // ÖNEMLİ: Rotasyon artık KÖK objeye değil, visualModel'e uygulanıyor.
            // Kök hep identity/sabit kalır, CountLabel (kökün child'ı) bundan etkilenmez.
            // NOT: ModelTransform.DOKill() aşağıda çağrılıyor — bu, VanishThenReturnToBase()
            // içinde başlatılan geri+yukarı DOTween hareketini de burada otomatik temizler,
            // böylece tepsi base'e her döndüğünde ModelTransform'un local pozisyonu
            // sıfırlanmış/temiz bir durumdan devam eder.
            ModelTransform.rotation = manager != null
                ? manager.BaseStackRotation
                : Quaternion.identity;

            ClearStackVisuals();

            countLabel?.Clear();

            SetState(TrayState.InTrayBase);

            // Eski turun eksen hafızasını tamamen temizliyoruz.
            currentMoveAxis = WaypointMoveAxis.None;
            debugMoveAxis = currentMoveAxis;
            debugAxisUnchangedThisSegment = false;

            ModelTransform.DOKill();
            ModelTransform.localPosition = Vector3.zero;

            if (scaleIn)
            {
                // Tepsi base'e ışınlandıktan sonra küçük başlayıp büyüyerek
                // görünür — pop-in efekti. Bu satır, çağıran taraf (TrayManager.
                // ReturnTrayToBase) tray'i gameObject.SetActive(true) yapmadan
                // ÖNCE bu metodu çağırdığı için sorunsuz çalışır: küçük scale
                // obje aktif olmadan set edilmiş oluyor, aktif olduğu an
                // zaten büyümeye başlamış (ya da başlamak üzere) durumda.
                ModelTransform.localScale = Vector3.one * baseScaleInStartScale;
                ModelTransform
                    .DOScale(Vector3.one, baseScaleInDuration)
                    .SetEase(baseScaleInEase);
            }
            else
            {
                ModelTransform.localScale = Vector3.one;
            }

            enabled = false;
        }

        public void Init(
            TrayManager manager,
            FoodType type,
            int startCapacity,
            List<GameObject> preSpawnedPieces = null)
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (vanishRoutine != null)
            {
                StopCoroutine(vanishRoutine);
                vanishRoutine = null;
            }

            enabled = true;
            trayManager = manager;
            foodType = type;
            capacity = startCapacity;

            config = trayManager.GetVisualConfig(foodType);

            currentIndex = 0;
            deliveryTryCounter = 0;
            depleted = false;

            customersReservedByThisTray.Clear();
            pendingCheckCells.Clear();

            // ÖNEMLİ FIX: transform.DOKill() — bu tray hâlâ base kuyruğunda
            // beklerken RefreshBaseQueuePositions() tarafından ROOT transform'a
            // (sıradaki yerine kaymak için) bir DOMove tween'i uygulanmış
            // olabilir. Tray çok hızlı art arda tekrar konveyöre çıkarılırsa
            // (hızlı launch), bu eski tween ölmeden konveyör hareketiyle aynı
            // transform üzerinde YARIŞIR — tepsi konveyörde ilerlerken görünmez
            // bir güçle eski base slotuna doğru çekiliyormuş gibi davranırdı.
            // ModelTransform.DOKill() zaten vardı, şimdi root için de ekliyoruz.
            transform.DOKill();
            ModelTransform.DOKill();
            ModelTransform.localScale = Vector3.one;
            ModelTransform.localPosition = Vector3.zero;

            SetState(TrayState.InConveyor);

            TrayDeliveryQueue.Register(this, foodType);

            var gridManager = trayManager.GridManagerRef;
            var waypoints = gridManager.WaypointWorldPositions;

            if (waypoints == null || waypoints.Count == 0)
                return;

            Vector3 startPos = trayManager.GetWaypointPosition(0);
            transform.position = startPos;

            var facings = gridManager.WaypointFacingDirections;

            if (facings != null &&
                facings.Count > 0 &&
                facings[0].sqrMagnitude > 0.0001f)
            {
                // ÖNEMLİ: Yönelim artık visualModel'e uygulanıyor, köke değil.
                ModelTransform.rotation =
                    Quaternion.LookRotation(
                        facings[0],
                        Vector3.up
                    );
            }

            cumulativeMovementDistance = new float[waypoints.Count];
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

            currentMoveAxis = WaypointMoveAxis.None;
            debugMoveAxis = currentMoveAxis;

            // İlk segmentin eksenini yola çıkmadan uygula
            ApplyMoveAxis(1);

            deliveryCheckpoints = gridManager.DeliveryCheckpoints;
            nextCheckpointIndex = 1;

            if(preSpawnedPieces != null && preSpawnedPieces.Count > 0)
            {
                AdoptPreSpawnedPieces(preSpawnedPieces);
            }
            else
            {
                BuildStackVisuals();
            }

            countLabel?.SetCount(capacity);

            if (deliveryCheckpoints != null &&
                deliveryCheckpoints.Count > 0)
            {
                QueueDeliveryCheck(
                    deliveryCheckpoints[0].cell
                );
            }

            moveRoutine = StartCoroutine(
                MoveOnConveyor()
            );
        }

        private void AdoptPreSpawnedPieces(List<GameObject> pieces)
        {
            ClearStackVisuals();

            currentLayerCount = Mathf.CeilToInt(pieces.Count / (float)PiecesPerLayer);

            for (int i = 0; i < pieces.Count; i++)
            {
                int layer = i / PiecesPerLayer;
                int posInLayer = i % PiecesPerLayer;

                float half = config.pieceSpacing * 0.5f;
                float xOffset = (posInLayer == 0 || posInLayer == 2) ? -half : half;
                float zOffset = (posInLayer == 0 || posInLayer == 1) ? half : -half;

                stackPieceInfos.Add(new StackPieceInfo
                {
                    go = pieces[i],
                    layerIndex = layer,
                    offsetXZ = new Vector2(xOffset, zOffset)
                });
            }
        }

        private void OnDisable()
        {
            ReleaseAllCustomerReservations();
            pendingCheckCells.Clear();

            TrayDeliveryQueue.Unregister(
                this,
                foodType
            );

            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }

            if (vanishRoutine != null)
            {
                StopCoroutine(vanishRoutine);
                vanishRoutine = null;
            }
        }

        // Billboard rotasyonu WorldSpaceCountLabel'ın kendi LateUpdate'inde
        // yapılıyor — Tray'in ayrıca bir şey yapmasına gerek yok.

        // ---------------------------------------------------------------
        // Tray State / Animasyon
        // ---------------------------------------------------------------

        private void SetState(TrayState newState)
        {
            currentState = newState;
            debugCurrentState = newState;

            // Sayı etiketi SADECE konveyördeyken görünür (o an anlamlı olan
            // "kaç porsiyon kaldı" bilgisi budur) — base'de dururken ya da
            // vanish sırasında gizli.
            countLabel?.SetVisible(newState == TrayState.InConveyor);

            if (trayAnimator == null)
                return;

            switch (currentState)
            {
                case TrayState.InTrayBase:
                case TrayState.InConveyor:
                    trayAnimator.ResetTrigger(VanishTriggerHash);
                    trayAnimator.SetTrigger(IdleTriggerHash);
                    break;

                case TrayState.Vanishing:
                    trayAnimator.ResetTrigger(IdleTriggerHash);
                    trayAnimator.SetTrigger(VanishTriggerHash);
                    break;
            }
        }

        /// <summary>
        /// Vanish tetiklendiği AN çağrılır (Animator'ın state'e girdiğini
        /// onaylamasını BEKLEMEZ — bu bekleme, Any State geçişinde bir
        /// crossfade/transition duration varsa hareketin gözle görülür
        /// gecikmeli başlamasına sebep oluyordu).
        ///
        /// Hareket YÖNÜ iki farklı eksenin toplamı:
        /// - GERİ: tepsinin yemek fırlattığı yönün TAM TERSİ, yani
        ///   -ModelTransform.forward. Bu, o anki rotasyona göre hesaplanır
        ///   (dünya ekseni sabit DEĞİL — tepsi nereye bakıyorsa ters yöne gider).
        ///   Ayrıca bu yön, dünya +Y ekseni etrafında [-vanishMoveAngleNoise,
        ///   +vanishMoveAngleNoise] derece aralığında RASTGELE döndürülür —
        ///   her tepsi tam aynı çizgide değil, hafif farklı açılarda gider.
        /// - YUKARI: sabit dünya +Y ekseni (Vector3.up) — yönden bağımsız,
        ///   her zaman yukarı.
        /// </summary>
        private void PlayVanishMoveTween()
        {
            if (ModelTransform == null)
                return;

            if (vanishMoveDistance <= 0f && vanishMoveUpDistance <= 0f)
                return;

            // Önceki (varsa) hareket tween'ini öldür, sıfırdan başlat.
            ModelTransform.DOKill();

            Vector3 startPos = ModelTransform.position;

            // Tepsinin o anki bakış yönünün TAM TERSİ (geri), etrafında
            // rastgele bir açı kadar sapmış.
            Vector3 backwardDir = ModelTransform.forward;

            if (vanishMoveAngleNoise > 0f)
            {
                float randomAngle = Random.Range(-vanishMoveAngleNoise, vanishMoveAngleNoise);
                backwardDir = Quaternion.AngleAxis(randomAngle, Vector3.up) * backwardDir;
            }

            Vector3 targetPos = startPos
                + backwardDir.normalized * vanishMoveDistance
                + Vector3.up * vanishMoveUpDistance;

            ModelTransform
                .DOMove(targetPos, vanishMoveDuration)
                .SetEase(vanishMoveEase);
        }

        private IEnumerator VanishThenReturnToBase()
        {
            SetState(TrayState.Vanishing);

            // ÖNEMLİ: Hareket, Animator'ın state'e GERÇEKTEN girdiğini
            // onaylamasını beklemeden, trigger'ın atıldığı AYNI ANDA başlar.
            // Böylece bir crossfade/transition duration yüzünden hareketin
            // animasyondan geç kalması engellenmiş oluyor.
            PlayVanishMoveTween();

            if (trayAnimator != null)
            {
                float enterTimeout = 1f;

                while (trayAnimator != null &&
                       trayAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash != VanishAnimHash &&
                       enterTimeout > 0f)
                {
                    enterTimeout -= Time.deltaTime;
                    yield return null;
                }

                while (trayAnimator != null &&
                       trayAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == VanishAnimHash &&
                       trayAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
                {
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(vanishMoveDuration);
            }

            vanishRoutine = null;

            if (trayManager != null)
            {
                trayManager.ReturnTrayToBase(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        public void ProcessCheckedDeliveryPlans()
        {
            if (pendingCheckCells.Count == 0)
                return;

            var cellsSnapshot =
                new List<Vector2Int>(
                    pendingCheckCells
                );

            pendingCheckCells.Clear();

            var customerManager =
                trayManager != null
                    ? trayManager.CustomerManagerRef
                    : null;

            if (customerManager == null)
                return;

            foreach (Vector2Int cell in cellsSnapshot)
            {
                if (depleted || capacity <= 0)
                    break;

                deliveryTryCounter++;

                if (!customerManager.TryFindDeliverableCustomer(
                        foodType,
                        cell,
                        1,
                        out Customer target)
                    || target == null)
                {
                    continue;
                }

                if (!IsAlignedWithCustomer(cell, target))
                    continue;

                if (!target.TryReserveForDelivery(
                        this,
                        foodType))
                {
                    continue;
                }

                customersReservedByThisTray.Add(target);
                FireDeliveryAt(target);
            }
        }

        private bool IsAlignedWithCustomer(Vector2Int trayCell, Customer target)
        {
            debugLastTrayCell = trayCell;

            if (target == null)
            {
                debugLastTargetRowCol = new Vector2Int(-1, -1);
                debugLastAlignmentResult = "target NULL -> false";
                return false;
            }

            debugLastTargetRowCol = new Vector2Int(target.Row, target.Col);

            switch (currentMoveAxis)
            {
                case WaypointMoveAxis.Row:
                {
                    // Row sabit, Col değişiyor (Yatay hareket). Dik atış dikey gider.
                    // Bu yüzden müşteri aynı COL'da olmalı.
                    bool match = target.Col == trayCell.y;
                    debugLastAlignmentResult =
                        $"AXIS=Row (Yatay) | target.Col={target.Col} vs trayCell.y={trayCell.y} -> {(match ? "MATCH" : "RED (aynı sütun değil)")}";
                    return match;
                }

                case WaypointMoveAxis.Col:
                {
                    // Col sabit, Row değişiyor (Dikey hareket). Dik atış yatay gider.
                    // Bu yüzden müşteri aynı ROW'da olmalı.
                    bool match = target.Row == trayCell.x;
                    debugLastAlignmentResult =
                        $"AXIS=Col (Dikey) | target.Row={target.Row} vs trayCell.x={trayCell.x} -> {(match ? "MATCH" : "RED (aynı satır değil)")}";
                    return match;
                }

                default:
                    debugLastAlignmentResult = "AXIS=None -> engelleme yok, geçti";
                    return true;
            }
        }

        // Önceden hesaplanmış yolu GridManager üzerinden doğrudan alıp kullanıyoruz.
        // Böylece köşelerde yaşanabilen hücre taşması kaynaklı yanılmalar engelleniyor.
        private void ApplyMoveAxis(int nextWaypointIndex)
        {
            var gridManager = trayManager.GridManagerRef;
            
            // WaypointMoveAxes listesinin var olduğundan ve indeksin sınırlar içinde olduğundan emin olun.
            // (GridManager içinde bu liste doldurulmuş olmalıdır)
            if (gridManager.WaypointMoveAxes != null && nextWaypointIndex < gridManager.WaypointMoveAxes.Count)
            {
                WaypointMoveAxis precalculatedAxis = gridManager.WaypointMoveAxes[nextWaypointIndex];
                
                if (precalculatedAxis != WaypointMoveAxis.None)
                {
                    currentMoveAxis = precalculatedAxis;
                    debugMoveAxis = currentMoveAxis;
                    debugAxisUnchangedThisSegment = false;
                }
                else
                {
                    debugAxisUnchangedThisSegment = true;
                }
            }
        }

        private void FireDeliveryAt(Customer target)
        {
            capacity = Mathf.Max(
                0,
                capacity - 1
            );

            Vector3 dirToCustomer =
                target != null
                    ? target.transform.position - transform.position
                    : ModelTransform.forward;

            RemoveStackPieceTowardCustomer(
                dirToCustomer
            );

            countLabel?.SetCount(capacity);

            PlayShootPunch();

            LaunchDeliveryClone(
                target,
                transform.position
            );

            customersReservedByThisTray.Remove(target);

            if (capacity <= 0 && !depleted)
            {
                depleted = true;

                if (moveRoutine != null)
                {
                    StopCoroutine(moveRoutine);
                    moveRoutine = null;
                }

                Despawn();
            }
        }

        private void PlayShootPunch()
        {
            if(ModelTransform == null || shootPunchScale <= 0f) return;

            ModelTransform.DOKill(true);
            ModelTransform.localScale = Vector3.one;

            ModelTransform.DOPunchScale(Vector3.one * shootPunchScale,
                                    shootPunchDuration,
                                    shootPunchVibrato,
                                    shootPunchElasticity);
        }

        private void BuildStackVisuals()
        {
            ClearStackVisuals();

            if (config.stackPiecePrefab == null)
            {
                currentLayerCount = 0;
                return;
            }

            int count = Mathf.Min(
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
        }

        private void SpawnStackPiece(int index)
        {
            int layer = index / PiecesPerLayer;
            int posInLayer = index % PiecesPerLayer;

            float half =
                config.pieceSpacing * 0.5f;

            float xOffset =
                (posInLayer == 0 || posInLayer == 2)
                    ? -half
                    : half;

            float zOffset =
                (posInLayer == 0 || posInLayer == 1)
                    ? half
                    : -half;

            // ÖNEMLİ: Parent artık ModelTransform — stack parçaları görsel
            // model ile BİRLİKTE dönsün istiyoruz (kökle değil), aksi halde
            // tray köşede dönerken üzerindeki yemek yığını sabit kalır gibi
            // görünürdü.
            GameObject piece =
                ObjectPool.Instance != null
                    ? ObjectPool.Instance.Get(
                        config.stackPiecePrefab,
                        ModelTransform.position,
                        config.stackPiecePrefab.transform.rotation,
                        ModelTransform)
                    : Instantiate(
                        config.stackPiecePrefab,
                        ModelTransform.position,
                        config.stackPiecePrefab.transform.rotation,
                        ModelTransform);

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

        private void RemoveStackPieceTowardCustomer(
            Vector3 dirToCustomerWorld)
        {
            if (stackPieceInfos.Count == 0)
                return;

            int targetLayer =
                config.removeFromTopFirst
                    ? stackPieceInfos.Max(
                        p => p.layerIndex)
                    : stackPieceInfos.Min(
                        p => p.layerIndex);

            List<StackPieceInfo> layerPieces =
                stackPieceInfos
                    .Where(
                        p => p.layerIndex == targetLayer)
                    .ToList();

            if (layerPieces.Count == 0)
                return;

            // ÖNEMLİ: ModelTransform üzerinden — tray'in GERÇEK görsel
            // yönelimine göre hesaplanmalı, sabit kalan kökün rotasyonuna göre DEĞİL.
            Vector3 localDir =
                ModelTransform.InverseTransformDirection(
                    dirToCustomerWorld
                );

            localDir.y = 0f;

            if (localDir.sqrMagnitude < 0.0001f)
                localDir = Vector3.forward;

            localDir.Normalize();

            Vector2 customerDirection =
                new Vector2(
                    localDir.x,
                    localDir.z
                );

            StackPieceInfo chosen = null;
            float bestScore =
                float.NegativeInfinity;

            foreach (StackPieceInfo piece in layerPieces)
            {
                float score =
                    Vector2.Dot(
                        piece.offsetXZ,
                        customerDirection
                    );

                if (chosen == null ||
                    score > bestScore)
                {
                    chosen = piece;
                    bestScore = score;
                }
            }

            if (chosen == null)
                return;

            stackPieceInfos.Remove(chosen);

            if (chosen.go != null)
            {
                if (ObjectPool.Instance != null)
                    ObjectPool.Instance.Return(chosen.go);
                else
                    Destroy(chosen.go);
            }

            currentLayerCount =
                stackPieceInfos.Count > 0
                    ? stackPieceInfos.Max(
                        p => p.layerIndex) + 1
                    : 0;
        }

        private void ClearStackVisuals()
        {
            foreach (var info in stackPieceInfos)
            {
                if (info.go == null)
                    continue;

                if (ObjectPool.Instance != null)
                    ObjectPool.Instance.Return(info.go);
                else
                    Destroy(info.go);
            }

            stackPieceInfos.Clear();
            currentLayerCount = 0;
        }

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

                if (nextIndex >= waypoints.Count)
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
                            moveRoutine = null;
                            Despawn();
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

                Vector3 startPos =
                    trayManager.GetWaypointPosition(
                        currentIndex
                    );

                Vector3 targetPosition =
                    trayManager.GetWaypointPosition(
                        nextIndex
                    );

                // YENİ KOD: Hareket başlamadan önce önceden hesaplanmış ekseni listeye göre çekiyoruz.
                ApplyMoveAxis(nextIndex);

                Vector3 targetFacing =
                    nextIndex < facings.Count
                        ? facings[nextIndex]
                        : Vector3.zero;

                yield return StartCoroutine(
                    MoveSegment(
                        currentIndex,
                        startPos,
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

        private IEnumerator MoveSegment(
            int fromIndex,
            Vector3 start,
            Vector3 target,
            Vector3 targetFacing)
        {
            float distance =
                Vector3.Distance(
                    start,
                    target
                );

            if (distance < 0.001f)
            {
                transform.position = target;
                yield break;
            }

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
                fromIndex < cumulativeMovementDistance.Length
                    ? cumulativeMovementDistance[fromIndex]
                    : 0f;

            // ÖNEMLİ: Hedef rotasyon hesaplanırken "şu anki rotasyon" artık
            // ModelTransform'dan okunuyor (kökten değil).
            Quaternion targetRotation =
                targetFacing.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(
                        targetFacing,
                        Vector3.up)
                    : ModelTransform.rotation;

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / duration
                    );

                // Pozisyon KÖK objede kalmaya devam ediyor — waypoint/teslimat
                // hesaplamalarının hepsi transform.position'a göre yapılıyor,
                // buna dokunmuyoruz.
                transform.position =
                    Vector3.Lerp(
                        start,
                        target,
                        t
                    );

                // ROTASYON artık ModelTransform'a uygulanıyor — kök hep sabit
                // kalıyor, CountLabel (kökün child'ı) bundan etkilenmiyor.
                ModelTransform.rotation =
                    rotationSmoothing > 0f
                        ? Quaternion.Slerp(
                            ModelTransform.rotation,
                            targetRotation,
                            Time.deltaTime *
                            rotationSmoothing)
                        : targetRotation;

                if (totalMovementLength > 0.0001f)
                {
                    float globalT =
                        (prefixDistance +
                         distance * t) /
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
            ModelTransform.rotation = targetRotation;
        }

        private void AdvanceDeliveryCheckpoints(
            float globalT)
        {
            if (deliveryCheckpoints == null)
                return;

            while (
                nextCheckpointIndex <
                    deliveryCheckpoints.Count &&
                deliveryCheckpoints[
                    nextCheckpointIndex].t <= globalT)
            {
                var checkpoint =
                    deliveryCheckpoints[
                        nextCheckpointIndex];

                nextCheckpointIndex++;

                QueueDeliveryCheck(
                    checkpoint.cell
                );

                if (depleted)
                    return;
            }
        }

        private void QueueDeliveryCheck(
            Vector2Int cell)
        {
            if (capacity <= 0 || depleted)
                return;

            pendingCheckCells.Add(cell);
        }

        private void LaunchDeliveryClone(
            Customer target,
            Vector3 launchPosition)
        {
            if (config.stackPiecePrefab == null ||
                ObjectPool.Instance == null)
            {
                if (target != null)
                {
                    target.ReceiveFood();
                    customersReservedByThisTray.Remove(target);
                }

                return;
            }

            float startScale = config.deliveryStartScale > 0.001f ? config.deliveryStartScale : 0.35f;
            float endScale = config.deliveryEndScale > 0.001f ? config.deliveryEndScale : 1f;

            ObjectPool.Instance.StartCoroutine(
                DeliverCloneRoutine(
                    this,
                    config.stackPiecePrefab,
                    launchPosition,
                    ModelTransform.rotation,
                    target,
                    config.deliverySpeed,
                    config.deliverySpinSpeed,
                    config.deliverySpinAxis,
                    startScale,
                    endScale
                )
            );
        }

        private static IEnumerator DeliverCloneRoutine(
            Tray sourceTray,
            GameObject prefab,
            Vector3 launchPosition,
            Quaternion launchRotation,
            Customer target,
            float speed,
            float spinSpeed,
            Vector3 spinAxis,
            float startScaleMultiplier = 0.3f,
            float endScaleMultiplier = 1f)
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
                    sourceTray?.customersReservedByThisTray.Remove(
                        target
                    );
                }

                yield break;
            }

            Vector3 baseScale = clone.GetComponent<PooledObject>() != null 
            ? clone.GetComponent<PooledObject>().OriginalLocalScale 
            : prefab.transform.localScale;

            clone.transform.localScale = baseScale * Mathf.Max(0.01f,startScaleMultiplier);

            TrailRenderer trail =
                clone.GetComponent<TrailRenderer>();

            if (trail != null)
            {
                trail.Clear();
                trail.enabled = true;
                trail.emitting = true;
            }

            Vector3 spinAxisNormalized =
                spinAxis.sqrMagnitude > 0.0001f
                    ? spinAxis.normalized
                    : Vector3.up;

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
                        speed)
                );

            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (clone == null)
                {
                    if (target != null)
                    {
                        target.ReceiveFood();
                        sourceTray?.customersReservedByThisTray.Remove(
                            target
                        );
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

                float scaleProgress = Mathf.SmoothStep(startScaleMultiplier, endScaleMultiplier, t);
                clone.transform.localScale = baseScale * scaleProgress;

                if (spinSpeed != 0f)
                {
                    clone.transform.Rotate(
                        spinAxisNormalized,
                        spinSpeed * Time.deltaTime,
                        Space.Self
                    );
                }

                yield return null;
            }

            if (clone != null)
            {
                clone.transform.position = targetPos;
                clone.transform.localScale = baseScale * endScaleMultiplier;

                if (trail != null)
                {
                    trail.emitting = false;
                    trail.enabled = false;
                    trail.Clear();
                }

                ObjectPool.Instance.Return(clone);
            }

            if (target != null)
            {
                target.ReceiveFood();
                sourceTray?.customersReservedByThisTray.Remove(
                    target
                );
            }
        }

        private void ReleaseAllCustomerReservations()
        {
            if (customersReservedByThisTray.Count == 0)
                return;

            foreach (var customer in customersReservedByThisTray)
            {
                if (customer != null)
                    customer.ReleaseDeliveryReservation(
                        this
                    );
            }

            customersReservedByThisTray.Clear();
        }

        private bool TryMergeIntoSlot()
        {
            GameObject prefab =
                trayManager.GetFoodPrefab(
                    foodType
                );

            if (prefab == null)
                return false;

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

            food.PresetCapacity(capacity);

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

            // ÖNEMLİ: Food zaten slota ışınlandı (TryPlaceFood içinde, yukarıda
            // — o metod food.transform.position'ı doğrudan slot pozisyonuna
            // set ediyor). Ama tray'in kendi üzerindeki stack görselleri
            // (stackPieceInfos — dekoratif yığın parçaları) o ana kadar hâlâ
            // duruyordu ve normalde sadece ParkAtBase()'de temizleniyordu.
            // Yani vanish animasyonu SÜRESİNCE tray'in üzerinde "hayalet" bir
            // yığın görünmeye devam ediyor, food'un slota vardığı an fark
            // edilmiyordu. Burada, vanish animasyonu başlamadan (Despawn()
            // çağrılmadan) ÖNCE temizleyerek "food slota gitti" ile "tray'in
            // üzeri boşaldı" aynı frame'de gerçekleşiyor — animasyon zaten
            // boşalmış bir tray üzerinde oynuyor, kopukluk hissi kalkıyor.
            ClearStackVisuals();
            countLabel?.SetCount(capacity);

            return true;
        }

        private void Despawn()
        {
            ReleaseAllCustomerReservations();
            pendingCheckCells.Clear();

            TrayDeliveryQueue.Unregister(
                this,
                foodType
            );

            if (vanishRoutine != null)
                StopCoroutine(vanishRoutine);

            vanishRoutine = StartCoroutine(VanishThenReturnToBase());
        }
    }
}