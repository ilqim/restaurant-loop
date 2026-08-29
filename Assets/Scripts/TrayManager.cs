using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct TrayVisualConfig
    {
        public FoodType food;

        [Header("Görsel Stack (2x2 kat düzeni sabit — 4 parça/kat)")]
        public GameObject stackPiecePrefab;

        [Tooltip("Yemeğin EN ALT kısmının Tray yüzeyinden ne kadar yukarıda başlayacağı.")]
        public float foodBaseYOffset;

        [Tooltip("Bir kattaki 2x2'nin merkezden ne kadar açık duracağı.")]
        public float pieceSpacing;

        [Tooltip("Katlar arası dikey yükseklik farkı.")]
        public float pieceHeightSpacing;

        [Tooltip("Performans için görsel parça sayısı üst sınırı.")]
        public int maxVisualPieces;

        [Tooltip("Açıksa yemekler her zaman EN ÜST kattan verilir; kapalıysa EN ALT kattan verilir.")]
        public bool removeFromTopFirst;

        [Header("Hız")]
        [Tooltip("Tray'in conveyor üzerindeki SABİT hızı (dünya birimi/saniye) — SÜRE değil.")]
        [UnityEngine.Serialization.FormerlySerializedAs("stepDuration")]
        public float conveyorSpeed;

        [Tooltip("Müşteriye fırlatılan parçanın SABİT hızı (dünya birimi/saniye) — SÜRE değil.")]
        public float deliverySpeed;

        [Header("Fırlatma Rotasyonu (Trail Renderer)")]
        [Tooltip("Yemek müşteriye fırlatılırken saniyede kaç derece döneceği. 0 = dönmez.")]
        public float deliverySpinSpeed;

        [Tooltip("Dönüş ekseni (lokal). Sıfır bırakılırsa otomatik Vector3.up kullanılır.")]
        public Vector3 deliverySpinAxis;

        [Header("Delivery Fırlatma Scale Ayarları")]
        [Tooltip("Fırlatılan yemeğin başlangıç ölçeği (1 = normal boyut), (0.2 = küçük başlayıp büyür)")]
        public float deliveryStartScale;

        [Tooltip("Yemeğin müşteriye varırken ulaşacağı hedef ölçek.")]
        public float deliveryEndScale;

        [Tooltip("Uçuş sırasında scale animasyon eğrisi.")]
        public AnimationCurve deliveryScaleCurve;
    }

    public class TrayManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GridManager gridManager;
        [SerializeField] private CustomerManager customerManager;
        [SerializeField] private SlotManager slotManager;

        [Header("Tray Prefab")]
        [Tooltip("Tray component'i taşıyan prefab.")]
        [SerializeField] private GameObject trayPrefab;

        [Header("Konveyör Kapasitesi")]
        [Tooltip("Aynı anda konveyörde en fazla kaç Tray olabilir.")]
        [SerializeField] private int maxActiveTrays = 5;

        [Header("Tray Base Queue Settings")]
        [Tooltip("Başlangıçta Tray Base alanında yan yana duracak boş tepsi sayısı.")]
        [SerializeField] private int initialBaseTrays = 6;

        [Tooltip("Tepsilerin Base alanında duruş açısı (Z: +90). ÖNEMLİ: Bu rotasyon artık Tray'in KENDİ " +
                 "KÖKÜNE değil, Tray.cs içindeki ModelPivot'a (Visual Model) uygulanır — kök obje asla " +
                 "döndürülmez, sadece pozisyon taşır.")]
        [SerializeField] private Vector3 baseStackRotation = new Vector3(0f, 0f, 90f);

        [Tooltip("Tepsilerin Base'de yan yana dizilme aralığı.")]
        [SerializeField] private float baseStackSpacing = 0.45f;

        [Tooltip("Kuyruk yönünü tersine çevirir (Eğer tepsiler çıkış yönünün tersine diziliyorsa bunu açın/kapatın).")]
        [SerializeField] private bool reverseQueueDirection = false;

        [Tooltip("Öndeki tepsi çıkınca arkadakilerin öne kayma süresi.")]
        [SerializeField] private float queueShiftDuration = 0.2f;

        [Header("Giriş Kapısı (Entry Gate) — Parametrik")]
        [Tooltip("Giriş noktası = GridManager'ın Tray Base merkez noktası (trayBaseWorldCenter) + bu offset. " +
                 "ASLA queue index'ine veya trayBaseQueue.Count'a bağlı DEĞİLDİR, bu yüzden çıkış kapısıyla (index 0) " +
                 "hiçbir zaman çakışmaz. X = sağ/sol, Y = yukarı/aşağı, Z = ileri/geri.")]
        [SerializeField] private Vector3 trayEntryOffset = new Vector3(0f, 0f, -1.5f);

        [Header("Waypoint Y Offset")]
        [Tooltip("Tray'in tüm conveyor waypointlerinde Y ekseninde ne kadar yukarı/aşağı duracağını belirler.")]
        [SerializeField] private float waypointYOffset = 0f;

        [Header("Base Tray Offset")]
        [SerializeField] private Vector3 baseTrayOffset = new Vector3(0.5f, 0f, 0f);

        [Header("Food Type Başına Görsel/Hız Ayarları")]
        [SerializeField] private List<TrayVisualConfig> visualConfigs = new();

        [Header("Exit")]
        [Tooltip("Exit'te slotlar doluysa Tray tekrar conveyor turuna başlasın mı.")]
        [SerializeField] private bool loopIfSlotsFull = false;

        [Header("Merge-back")]
        [Tooltip("Tray'de kalan yemek slot'a dönerken kullanılacak Food prefabları.")]
        [SerializeField]
        private List<FoodTypePrefab> foodPrefabsForMerge = new()
        {
            new FoodTypePrefab { food = FoodType.Hamburger },
            new FoodTypePrefab { food = FoodType.Fries },
            new FoodTypePrefab { food = FoodType.Drink },
            new FoodTypePrefab { food = FoodType.Sushi },
            new FoodTypePrefab { food = FoodType.Steak },
            new FoodTypePrefab { food = FoodType.Donut },
        };

        private int currentActiveTrays;
        private readonly List<Tray> trayBaseQueue = new();
        private Vector3 trayBaseWorldCenter;
        private Coroutine shiftQueueRoutine;

        public GridManager GridManagerRef => gridManager;
        public CustomerManager CustomerManagerRef => customerManager;
        public SlotManager SlotManagerRef => slotManager;

        public bool LoopIfSlotsFull => loopIfSlotsFull;
        public float WaypointYOffset => waypointYOffset;
        public Quaternion BaseStackRotation => Quaternion.Euler(baseStackRotation);

        private void Awake()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();

            if (customerManager == null)
                customerManager = FindFirstObjectByType<CustomerManager>();

            if (slotManager == null)
                slotManager = FindFirstObjectByType<SlotManager>();
        }

        private void Start()
        {
            InitializeBaseTrayQueue();

#if UNITY_EDITOR
            float dist = Vector3.Distance(
                GetBaseStackPosition(0),
                GetEntryGateWorldPosition()
            );

            if (dist < 0.1f)
            {
                Debug.LogWarning(
                    $"TrayManager: Giriş Kapısı, Çıkış Kapısı'na ({dist:F2}u) çok yakın! " +
                    $"trayEntryOffset'i Inspector'dan büyütün.",
                    this
                );
            }
#endif
        }

        private void LateUpdate()
        {
            TrayDeliveryQueue.ProcessAllQueuesOncePerFrame();
        }

        private void InitializeBaseTrayQueue()
        {
            if (gridManager == null || trayPrefab == null)
                return;

            trayBaseWorldCenter = gridManager.GetTrayBaseCenterWorld();

            for (int i = 0; i < initialBaseTrays; i++)
            {
                Vector3 spawnPos = GetBaseStackPosition(i);

                // ÖNEMLİ: Kök obje HER ZAMAN Quaternion.identity ile spawn
                // edilir — BaseStackRotation ARTIK KÖKE DEĞİL, hemen aşağıda
                // çağrılan ParkAtBase() içinde Tray'in kendi ModelPivot'una
                // uygulanıyor. Kökü burada BaseStackRotation ile spawn edip
                // sonra ParkAtBase'in ModelPivot'u AYRICA döndürmesi, kökün
                // gereksiz yere kalıcı bir rotasyon taşımasına (ve buna bağlı
                // CountLabel gibi child'ların tutarsız davranmasına) yol açardı.
                GameObject trayGo = Instantiate(
                    trayPrefab,
                    spawnPos,
                    Quaternion.identity,
                    transform
                );

                Tray tray = trayGo.GetComponent<Tray>();

                if (tray != null)
                {
                    tray.ParkAtBase(this, spawnPos);
                    trayBaseQueue.Add(tray);
                }
            }
        }

        // Index 0 = Çıkış Kapısı (En Ön)
        // Büyük Index = Kuyruğun arkası
        public Vector3 GetBaseStackPosition(int index)
        {
            Vector3 pos = trayBaseWorldCenter;
            pos.y += waypointYOffset;
            pos += baseTrayOffset;

            float directionMultiplier = reverseQueueDirection ? -1f : 1f;
            pos.x += index * baseStackSpacing * directionMultiplier;

            return pos;
        }

        // Giriş her zaman SABİT bir fiziksel noktadır.
        // Kuyruk uzunluğuna (Count) ve initialBaseTrays'e ASLA bağlı değildir.
        // Referans noktası GridManager'dan gelen trayBaseWorldCenter'dır.
        public Vector3 GetEntryGateWorldPosition()
        {
            Vector3 pos = trayBaseWorldCenter + trayEntryOffset;
            pos.y += waypointYOffset;

            return pos;
        }

        public Vector3 GetWaypointPosition(int index)
        {
            if (gridManager == null ||
                gridManager.WaypointWorldPositions == null ||
                index < 0 ||
                index >= gridManager.WaypointWorldPositions.Count)
            {
                return Vector3.zero;
            }

            Vector3 position = gridManager.WaypointWorldPositions[index];
            position.y += waypointYOffset;

            return position;
        }

        // 1. ÇIKIŞ: EN ÖNDEN (INDEX 0 / Çıkış Kapısı) ÇIKAR
        public bool TryLaunchTray(FoodType foodType, int capacity)
        {
            if (currentActiveTrays >= maxActiveTrays)
                return false;

            if (gridManager == null ||
                gridManager.WaypointWorldPositions == null ||
                gridManager.WaypointWorldPositions.Count == 0)
            {
                Debug.LogWarning("TrayManager: Conveyor waypoint listesi boş.");
                return false;
            }

            Tray trayToLaunch = null;

            if (trayBaseQueue.Count > 0)
            {
                trayToLaunch = trayBaseQueue[0];
                trayBaseQueue.RemoveAt(0);

                ShiftTraysForward();
            }
            else
            {
                Vector3 spawnPos = GetWaypointPosition(0);

                GameObject trayGo = Instantiate(
                    trayPrefab,
                    spawnPos,
                    Quaternion.identity,
                    transform
                );

                trayToLaunch = trayGo.GetComponent<Tray>();
            }

            if (trayToLaunch == null)
            {
                Debug.LogError("TrayManager: Tray başlatılamadı.");
                return false;
            }

            currentActiveTrays++;
            trayToLaunch.gameObject.SetActive(true);

            trayToLaunch.Init(
                this,
                foodType,
                capacity
            );

            return true;
        }

        public void ReturnTrayToBase(Tray tray)
        {
            if (tray == null)
                return;

            currentActiveTrays = Mathf.Max(0, currentActiveTrays - 1);

            if (trayBaseQueue.Contains(tray))
                return;

            // Havada uçuşma efektini engellemek için:
            // Tepsiyi önce GİZLE, konumunu sessizce (görünmeden) hedef base
            // slotuna taşı, sonra tekrar görünür yap. Entry-gate üzerinden
            // görünür bir geçiş animasyonu ARTIK YOK.
            tray.gameObject.SetActive(false);

            int targetIndex = trayBaseQueue.Count;
            trayBaseQueue.Add(tray);

            Vector3 finalSlotPos = GetBaseStackPosition(targetIndex);

            tray.transform.position = finalSlotPos;

            // ÖNEMLİ: Kökün rotasyonuna DOKUNMUYORUZ artık — her zaman
            // Quaternion.identity'de sabit kalması gerekiyor. BaseStackRotation
            // (Z=90 vb.), az aşağıda çağrılan ParkAtBase() içinde Tray'in kendi
            // ModelPivot'una uygulanıyor. Burada eskiden "tray.transform.rotation
            // = BaseStackRotation" satırı vardı — kökü gereksiz yere döndürüp
            // CountLabel gibi kökün child'larının tutarsız görünmesine sebep
            // oluyordu. O satır kaldırıldı.
            tray.transform.rotation = Quaternion.identity;

            tray.ParkAtBase(this, finalSlotPos);

            tray.gameObject.SetActive(true);
        }

        private void ShiftTraysForward()
        {
            if (shiftQueueRoutine != null)
                StopCoroutine(shiftQueueRoutine);

            shiftQueueRoutine = StartCoroutine(ShiftTraysForwardRoutine());
        }

        private IEnumerator ShiftTraysForwardRoutine()
        {
            if (trayBaseQueue.Count == 0)
                yield break;

            int count = trayBaseQueue.Count;

            Vector3[] startPositions = new Vector3[count];
            Vector3[] targetPositions = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                if (trayBaseQueue[i] != null)
                {
                    startPositions[i] = trayBaseQueue[i].transform.position;
                    targetPositions[i] = GetBaseStackPosition(i);
                }
            }

            float elapsed = 0f;

            while (elapsed < queueShiftDuration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(elapsed / queueShiftDuration)
                );

                for (int i = 0; i < count; i++)
                {
                    if (trayBaseQueue[i] != null)
                    {
                        trayBaseQueue[i].transform.position =
                            Vector3.Lerp(
                                startPositions[i],
                                targetPositions[i],
                                t
                            );

                        // ÖNEMLİ: Kökün rotasyonuna artık dokunmuyoruz —
                        // her zaman identity kalması gerekiyor. Base'de
                        // beklerken görsel yönelim zaten ParkAtBase()
                        // üzerinden ModelPivot'a uygulanmış durumda,
                        // kaydırma sırasında tekrar ayarlamaya gerek yok.
                    }
                }

                yield return null;
            }

            for (int i = 0; i < count; i++)
            {
                if (trayBaseQueue[i] != null)
                {
                    trayBaseQueue[i].transform.position = targetPositions[i];
                }
            }

            shiftQueueRoutine = null;
        }

        public void ReleaseTraySlot()
        {
            currentActiveTrays = Mathf.Max(0, currentActiveTrays - 1);
        }

        public TrayVisualConfig GetVisualConfig(FoodType food)
        {
            foreach (var config in visualConfigs)
            {
                if (config.food == food)
                    return config;
            }

            return new TrayVisualConfig
            {
                food = food,
                stackPiecePrefab = null,
                foodBaseYOffset = 0f,
                pieceSpacing = 0.3f,
                pieceHeightSpacing = 0.25f,
                maxVisualPieces = 20,
                removeFromTopFirst = false,
                conveyorSpeed = 3f,
                deliverySpeed = 4f,
                deliverySpinSpeed = 360f,
                deliverySpinAxis = Vector3.up,
                deliveryStartScale = 0.35f,
                deliveryEndScale = 1f
            };
        }

        public GameObject GetFoodPrefab(FoodType food)
        {
            foreach (var entry in foodPrefabsForMerge)
            {
                if (entry.food == food)
                    return entry.prefab;
            }

            return null;
        }

        public bool CanLaunchTray()
        {
            if(currentActiveTrays >= maxActiveTrays)
            {
                return false;
            }

            if(gridManager == null ||
             gridManager.WaypointWorldPositions == null ||
              gridManager.WaypointWorldPositions.Count == 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Add Tray Booster: Konveyördeki maksimum aktif tepsi limitini 1 artırır
        /// ve Base kuyruğuna hemen yeni bir boş tepsi ekler.
        /// </summary>
        public void AddExtraTray()
        {
            maxActiveTrays++;

            if (trayPrefab == null)
                return;

            int newIndex = trayBaseQueue.Count;
            Vector3 spawnPos = GetBaseStackPosition(newIndex);

            // ÖNEMLİ: Kök yine identity ile spawn edilir, ParkAtBase
            // ModelPivot'u doğru döndürür.
            GameObject trayGo = Instantiate(
                trayPrefab,
                spawnPos,
                Quaternion.identity,
                transform
            );

            Tray tray = trayGo.GetComponent<Tray>();
            if (tray != null)
            {
                tray.ParkAtBase(this, spawnPos);
                trayBaseQueue.Add(tray);
            }
        }

    public Tray PrepareUpcomingTray()
{
    if (currentActiveTrays >= maxActiveTrays)
        return null;

    if (gridManager == null ||
        gridManager.WaypointWorldPositions == null ||
        gridManager.WaypointWorldPositions.Count == 0)
    {
        return null;
    }

    Tray trayToLaunch = null;

    if (trayBaseQueue.Count > 0)
    {
        trayToLaunch = trayBaseQueue[0];
        trayBaseQueue.RemoveAt(0);
        ShiftTraysForward();
    }
    else
    {
        Vector3 spawnPos = GetWaypointPosition(0);
        GameObject trayGo = Instantiate(
            trayPrefab,
            spawnPos,
            Quaternion.identity,
            transform
        );
        trayToLaunch = trayGo.GetComponent<Tray>();
    }

    if (trayToLaunch == null)
        return null;

    currentActiveTrays++;
    trayToLaunch.gameObject.SetActive(true);

    Vector3 startPos = GetWaypointPosition(0);
    trayToLaunch.transform.position = startPos;

    var facings = gridManager.WaypointFacingDirections;
    if (facings != null && facings.Count > 0 && facings[0].sqrMagnitude > 0.0001f)
    {
        trayToLaunch.ModelTransform.rotation = Quaternion.LookRotation(facings[0], Vector3.up);
    }

    return trayToLaunch;
}

public void FinalizeTrayLaunch(Tray tray, FoodType foodType, int capacity, List<GameObject> spawnedPieces)
{
    if (tray != null)
    {
        tray.Init(this, foodType, capacity, spawnedPieces);
    }
}
    }

    
}