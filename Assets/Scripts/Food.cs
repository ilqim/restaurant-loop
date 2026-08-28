using System;
using UnityEngine;
using DG.Tweening;

namespace RestaurantLoop
{
    public enum FoodState
    {
        LockedInQueue,
        AvailableInQueue,
        Launching,
        OnConveyor,
        InFoodSlot,
        Served
    }

    public class Food : MonoBehaviour
    {
        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private TrayManager trayManager;

        [Header("Bu yemeğin türü")]
        [SerializeField] private FoodType foodType;

        [Header("Kapasite")]
        [SerializeField] private int capacity = 10;

        [Header("Görsel Ayrımı (2D/3D)")]
        [Tooltip("Queue ve Slot'tayken aktif olan 2D Sprite/Image objesi.")]
        [SerializeField] private GameObject spriteVisual;
        [Tooltip("Uçuş ve conveyordayken aktif olan 3D objesi.")]
        [SerializeField] private GameObject modelVisual;

        [Header("Zıplama Animasyonu")]
        [SerializeField] private float jumpDuration = 0.35f;
        [SerializeField] private float jumpPower = 1.2f;

        [Header("Kapasite Debug Etiketi")]
        [SerializeField] private bool showCapacityLabel = true;
        [SerializeField] private float labelHeight = 0.6f;
        [SerializeField] private int labelFontSize = 48;
        [SerializeField] private float labelCharacterSize = 0.12f;
        [SerializeField] private Color labelColor = Color.white;

        [Header("State")]
        [SerializeField] private FoodState currentState = FoodState.AvailableInQueue;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Görseller — Queue ve Food-Slot ayrı renk/sprite kullanır")]
        [Tooltip("Bu food QUEUE hücresindeyken uygulanacak sprite. Boş bırakılırsa QueueSlot kendi default sprite'ını korur (sadece renk değişir).")]
        [SerializeField] private Sprite queueSprite;
        public Sprite QueueSprite => queueSprite;
        [Tooltip("Bu food QUEUE hücresindeyken tepsinin rengi.")]
        [SerializeField] private Color queueColor = Color.white;
        public Color QueueColor => queueColor;

        [Header("Food-Slot Rengi (Hex)")]
        [Tooltip("Bu food FOOD-SLOT'a (konveyör sonu, Slot.cs) yerleştiğinde, slotun 'dolu' sprite'ına uygulanacak renk. Hex formatında gir, örn: #FF5733 veya #FF5733FF.")]
        [SerializeField] private string slotColorHex = "#FFFFFF";
        public Color SlotColor
        {
            get
            {
                if (ColorUtility.TryParseHtmlString(slotColorHex, out Color c))
                    return c;

                Debug.LogWarning($"Food [{name}]: slotColorHex ('{slotColorHex}') geçersiz bir hex değeri, beyaz kullanılıyor. Format: #RRGGBB veya #RRGGBBAA.");
                return Color.white;
            }
        }

        private static readonly int BlockedBaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock mpb;

        private bool queueStatePreset;
        private TextMesh capacityLabel;
        private Camera labelFacingCamera;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
            UpdateVisualMode();
        }

        public void PresetCapacity(int value)
        {
            capacity = Mathf.Max(0, value);
            UpdateCapacityLabel();
        }

        private void Awake()
        {
            if(spriteVisual == null)
            {
                var sr = GetComponentInChildren<SpriteRenderer>(true);
                if(sr != null) spriteVisual = sr.gameObject;
            }

            if(modelVisual == null)
            {
                var mr = GetComponentInChildren<MeshRenderer>(true);
                if(mr != null) modelVisual = mr.gameObject;
            }
        }

        private void Start()
        {
            if (trayManager == null) trayManager = FindFirstObjectByType<TrayManager>();
            if (trayManager == null) Debug.LogError("Food: Sahnede bir TrayManager bulunamadı.");

            if (labelFacingCamera == null) labelFacingCamera = Camera.main;

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);

            UpdateVisualMode();
            CreateCapacityLabel();
        }

        private void LateUpdate()
        {
            if (capacityLabel == null) return;
            if (labelFacingCamera == null) labelFacingCamera = Camera.main;
            if (labelFacingCamera == null) return;
            capacityLabel.transform.rotation = Quaternion.LookRotation(capacityLabel.transform.position - labelFacingCamera.transform.position);
        }

        public void ActivateFromTap()
        {
            if (currentState != FoodState.AvailableInQueue && currentState != FoodState.InFoodSlot)
                return;

            if (currentState == FoodState.AvailableInQueue)
            {
                // Queue'deki food'u banda göndermek için tıklama. Ses SADECE
                // konveyörde gerçekten yer varsa (yemek fiilen çıkabildiyse)
                // çalar — her tıklamada değil.
                bool launched = TryLaunchWithAnimation();
                if (launched)
                    AudioEvents.PlayFoodClick();
            }
            else
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");
                ReenterConveyorRequested?.Invoke(this);
            }
        }

        /// <summary>
        /// Slot, food'u konveyöre geri göndermek istediğinde çağırır.
        /// Dönüş değeri: food GERÇEKTEN konveyöre çıkabildi mi (true) yoksa
        /// konveyör dolu olduğu için olduğu yerde mi kaldı (false).
        /// Slot, bu sonuca göre kendini boşaltıp boşaltmayacağına karar verir.
        /// </summary>
        public bool EnterConveyorFromSlot()
        {
            return TryLaunchWithAnimation();
        }

        public void SetInFoodSlot()
        {
            ChangeState(FoodState.InFoodSlot);
        }

        private void UpdateVisualMode()
        {
            bool isStaticInSlotOrQueue = (currentState == FoodState.AvailableInQueue ||
                                          currentState == FoodState.LockedInQueue ||
                                          currentState == FoodState.InFoodSlot);

            // Kuyrukta ve slotta 2D Sprite göster, fırlatma/uçuş anında ve konveyörde 3D modeli göster
            if (spriteVisual != null)
                spriteVisual.SetActive(isStaticInSlotOrQueue);

            if (modelVisual != null)
                modelVisual.SetActive(!isStaticInSlotOrQueue);
        }

        /// <summary>
        /// Queue'da locked (henüz sırası gelmemiş) durumdaki görünüm.
        /// ÖNEMLİ: Saydamlık (alpha) DEĞİL, RGB karartma kullanıyor —
        /// materyal Opaque kalmaya devam ediyor, bu yüzden altındaki 2D
        /// queue slot sprite'ıyla derinlik/sıralama çakışması olmuyor.
        /// isBlocked=false ile çağırırsan orijinal renge geri döner.
        /// </summary>
        public void ApplyBlockedVisual(bool isBlocked, float darkenFactor = 0.35f)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>(true);
            float factor = isBlocked ? Mathf.Clamp01(darkenFactor) : 1f;

            foreach (var r in renderers)
            {
                if (r == null) continue;

                // SpriteRenderer varsa (Customer.cs'deki Bubble fix'iyle
                // aynı mantık) direkt .color kullan — _BaseColor sprite
                // shader'ında yok.
                if (r is SpriteRenderer sr)
                {
                    Color c = sr.color;
                    c.r *= factor; c.g *= factor; c.b *= factor;
                    c.a = 1f; // ALFAYA DOKUNMA — Opaque kalmalı
                    sr.color = c;
                    continue;
                }

                var mat = r.sharedMaterial;
                if (mat == null || !mat.HasProperty(BlockedBaseColorId)) continue;

                r.GetPropertyBlock(mpb);
                Color baseColor = mat.GetColor(BlockedBaseColorId);
                baseColor.r *= factor; baseColor.g *= factor; baseColor.b *= factor;
                baseColor.a = 1f; // ALFAYA DOKUNMA — Opaque kalmalı
                mpb.SetColor(BlockedBaseColorId, baseColor);
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>
        /// Konveyöre (tray olarak) çıkışı dener. Başarılıysa food'u despawn eder
        /// ve true döner. Konveyör doluysa hiçbir şey değiştirmeden false döner.
        /// </summary>
        private bool TryLaunchWithAnimation()
        {
            if (trayManager == null)
            {
                Debug.LogError("Food: TrayManager yok, tray başlatılamıyor.");
                return false;
            }

            if (!trayManager.CanLaunchTray())
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Konveyör dolu, tray başlatılamadı.");
                return false;
            }

            Vector3 targetPos = trayManager.GetWaypointPosition(0);

            ChangeState(FoodState.Launching);

            UpdateVisualMode();

            // Smooth DOTween jump to the conveyor starting position
            transform.DOJump(targetPos, jumpPower, 1, jumpDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() =>
                {
                    bool launched = trayManager.TryLaunchTray(foodType, capacity);
                    if (launched)
                    {
                        ChangeState(FoodState.OnConveyor);
                        DespawnSelf();
                    }
                    else
                    {
                        ChangeState(FoodState.AvailableInQueue);
                    }
                });

            return true;
        }

        private void DespawnSelf()
        {
            var pooled = GetComponent<PooledObject>();
            if (pooled != null && pooled.SourcePrefab != null && ObjectPool.Instance != null)
                ObjectPool.Instance.Return(gameObject);
            else
                Destroy(gameObject);
        }

        private void ChangeState(FoodState newState)
        {
            currentState = newState;
            UpdateVisualMode();
            if (verboseLogging) Debug.Log($"Food [{gameObject.name}] State: {currentState}");
            StateChanged?.Invoke(this, newState);
        }

        private void CreateCapacityLabel()
        {
            if (!showCapacityLabel) return;
            if (capacityLabel != null) return;

            var labelGO = new GameObject("CapacityLabel");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0, labelHeight, 0);

            capacityLabel = labelGO.AddComponent<TextMesh>();
            capacityLabel.anchor = TextAnchor.MiddleCenter;
            capacityLabel.alignment = TextAlignment.Center;
            capacityLabel.fontSize = labelFontSize;
            capacityLabel.characterSize = labelCharacterSize;
            capacityLabel.color = labelColor;

            UpdateCapacityLabel();
        }

        private void UpdateCapacityLabel()
        {
            if (capacityLabel != null) capacityLabel.text = capacity.ToString();
        }
    }
}