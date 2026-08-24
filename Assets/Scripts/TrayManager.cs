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
        [Tooltip("Tray'in conveyor üzerindeki SABİT hızı (dünya birimi/saniye) — SÜRE değil. " +
                 "Köşeler yuvarlatıldığında waypoint'ler arası mesafe artık eşit olmadığı için " +
                 "(köşedeki kısa yay adımları, düz kısımdaki uzun adımlar) süre-bazlı hareket " +
                 "köşelerde yavaşlama/hızlanma gibi görünürdü. Hız sabit, süre = mesafe / hız.")]
        [UnityEngine.Serialization.FormerlySerializedAs("stepDuration")]
        public float conveyorSpeed;

        [Tooltip("Müşteriye fırlatılan parçanın SABİT hızı (dünya birimi/saniye) — SÜRE değil. " +
                 "Böylece yakın müşteriye giden parça da uzak müşteriye giden parça da AYNI HIZDA gider; " +
                 "sadece varış süresi mesafeye göre değişir (mesafe/hız). Eskiden bu 'deliveryDuration' " +
                 "(sabit SÜRE) idi — o zaman yakın müşteriye giden parça yavaş, uzağa giden hızlı görünüyordu.")]
        public float deliverySpeed;
    }

    public class TrayManager : MonoBehaviour
    {
        [Header("Tray Base Stack Settings")]
        [Tooltip("Başlangıçta Tray Base alanında üst üste duracak boş tepsi sayısı.")]
        [SerializeField] private int initialBaseTrays = 6;
        [Tooltip("Tepsiler üst üste stacklendiğinde aralarındaki dikey mesafe.")]
        [SerializeField] private float baseStackYOffset = 0.08f;
        [Tooltip("Tepsinin işi bittiğinde Base'e geri dönüş hızı.")]
        [SerializeField] private float returnSpeed = 6f;

        private readonly List<Tray> trayBaseStack = new();
        private static readonly Quaternion BaseTrayFacingRotation = Quaternion.Euler(0f, 90f, 0f);
        private Vector3 trayBaseWorldCenter;

        public float ReturnSpeed => returnSpeed;

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

        [Header("Waypoint Y Offset")]
        [Tooltip("Tray'in tüm conveyor waypointlerinde Y ekseninde ne kadar yukarı/aşağı duracağını belirler.")]
        [SerializeField] private float waypointYOffset = 0f;

        [Header("Food Type Başına Görsel/Hız Ayarları")]
        [SerializeField] private List<TrayVisualConfig> visualConfigs = new();

        [Header("Exit")]
        [Tooltip("Exit'te slotlar doluysa Tray tekrar conveyor turuna başlasın mı?")]
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

        public GridManager GridManagerRef => gridManager;
        public CustomerManager CustomerManagerRef => customerManager;
        public SlotManager SlotManagerRef => slotManager;

        public bool LoopIfSlotsFull => loopIfSlotsFull;

        public float WaypointYOffset => waypointYOffset;

        private void Awake()
        {
            if (gridManager == null)
                gridManager = FindFirstObjectByType<GridManager>();

            if (customerManager == null)
                customerManager = FindFirstObjectByType<CustomerManager>();

            if (slotManager == null)
                slotManager = FindFirstObjectByType<SlotManager>();
        }

        /// <summary>
        /// Öncelik kuyruğu artık burada DEĞİL — TrayDeliveryQueue statik
        /// sınıfında, food type başına GLOBAL olarak tutuluyor. Bunun
        /// sebebi: sahnede satır/sütun gibi birden fazla TrayManager
        /// olsa bile, hangi tepsinin önce ateş edeceği TÜM sahne
        /// genelinde tutarlı olmalı. Bu yüzden burada sadece o global
        /// koordinatörü tetikliyoruz; birden fazla TrayManager aynı
        /// frame'de bunu çağırsa bile TrayDeliveryQueue işi frame
        /// başına sadece bir kez yapar.
        /// </summary>
        private void LateUpdate()
        {
            TrayDeliveryQueue.ProcessAllQueuesOncePerFrame();
        }

        private void Start()
{
    InitializeBaseTrayStack();
}

private void InitializeBaseTrayStack()
{
    if (gridManager == null || trayPrefab == null)
        return;

    trayBaseWorldCenter = gridManager.GetTrayBaseCenterWorld();

    for (int i = 0; i < initialBaseTrays; i++)
    {
        Vector3 spawnPos = GetBaseStackPosition(i);
        // Instantiated facing right:
        GameObject trayGo = Instantiate(trayPrefab, spawnPos, BaseTrayFacingRotation, transform);
        Tray tray = trayGo.GetComponent<Tray>();
        if (tray != null)
        {
            tray.ParkAtBase(this, spawnPos);
            trayBaseStack.Add(tray);
        }
    }
}

public Vector3 GetBaseStackPosition(int index)
{
    Vector3 pos = trayBaseWorldCenter;
    pos.y += waypointYOffset + (index * baseStackYOffset);
    return pos;
}

        /// <summary>
        /// Tray'in belirli waypoint'teki gerçek pozisyonunu verir.
        /// Waypoint Y değerine WaypointYOffset eklenir.
        /// </summary>
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

        /// <summary>
        /// Yeni bir Tray oluşturur ve conveyor'a gönderir.
        /// </summary>
        public bool TryLaunchTray(FoodType foodType, int capacity)
        {
            // CHANGED INSIDE TryLaunchTray:
Tray trayToLaunch;

if (trayBaseStack.Count > 0)
{
    trayToLaunch = trayBaseStack[^1];
    trayBaseStack.RemoveAt(trayBaseStack.Count - 1);
}
else
{
    Vector3 spawnPos = GetWaypointPosition(0);
    GameObject trayGo = Instantiate(trayPrefab, spawnPos, trayPrefab.transform.rotation, transform);
    trayToLaunch = trayGo.GetComponent<Tray>();
}

if (trayToLaunch == null)
{
    Debug.LogError("TrayManager: Tray başlatılamadı.");
    return false;
}

currentActiveTrays++;
trayToLaunch.transform.position = GetWaypointPosition(0);
trayToLaunch.Init(this, foodType, capacity);

return true;
        }

public void ReturnTrayToBase(Tray tray)
{
    currentActiveTrays = Mathf.Max(0, currentActiveTrays - 1);

    if (!trayBaseStack.Contains(tray))
    {
        int targetIndex = trayBaseStack.Count;
        trayBaseStack.Add(tray);
        Vector3 targetPos = GetBaseStackPosition(targetIndex);
        
        // Directly park/teleport back to the stack:
        tray.ParkAtBase(this, targetPos);
    }
}

        /// <summary>
        /// Bir Tray conveyor'dan çıktığında kapasiteyi serbest bırakır.
        /// </summary>
        public void ReleaseTraySlot()
        {
            currentActiveTrays =
                Mathf.Max(0, currentActiveTrays - 1);
        }

        

        /// <summary>
        /// FoodType'a göre görsel/hız ayarlarını getirir.
        /// </summary>
        public TrayVisualConfig GetVisualConfig(FoodType food)
        {
            foreach (var config in visualConfigs)
            {
                if (config.food == food)
                    return config;
            }

            Debug.LogWarning(
                $"TrayManager: '{food}' için Visual Config yok, varsayılan kullanılıyor."
            );

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

                deliverySpeed = 4f
            };
        }

        /// <summary>
        /// Tray exit'e geldiğinde kalan Food'u oluşturmak için prefab getirir.
        /// </summary>
        public GameObject GetFoodPrefab(FoodType food)
        {
            foreach (var entry in foodPrefabsForMerge)
            {
                if (entry.food == food)
                    return entry.prefab;
            }

            return null;
        }
    }
}