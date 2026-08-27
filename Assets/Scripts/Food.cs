using System;
using System.Collections;
using UnityEngine;

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

        [Header("Zıplama Animasyonu")]
        [SerializeField] private float jumpDuration = 0.35f;
        [SerializeField] private float jumpArcHeight = 1.2f;

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
        [Tooltip("Bu food QUEUE hücresindeyken uygulanacak sprite. Boş bırakılırsa QueueSlot kendi default sprite'ını korur.")]
        [SerializeField] private Sprite queueSprite;
        public Sprite QueueSprite => queueSprite;
        [Tooltip("Bu food QUEUE hücresindeyken tepsinin rengi.")]
        [SerializeField] private Color queueColor = Color.white;
        public Color QueueColor => queueColor;

        [Header("Food-Slot Rengi (Hex)")]
        [Tooltip("Bu food FOOD-SLOT'a yerleştiğinde, slotun 'dolu' sprite'ına uygulanacak renk.")]
        [SerializeField] private string slotColorHex = "#FFFFFF";
        public Color SlotColor
        {
            get
            {
                if (ColorUtility.TryParseHtmlString(slotColorHex, out Color c))
                    return c;

                Debug.LogWarning($"Food [{name}]: slotColorHex ('{slotColorHex}') geçersiz bir hex değeri, beyaz kullanılıyor.");
                return Color.white;
            }
        }

        private bool queueStatePreset;
        private TextMesh capacityLabel;
        private Camera labelFacingCamera;
        private Coroutine jumpCoroutine;

        public FoodState CurrentState => currentState;
        public FoodType FoodTypeValue => foodType;
        public int Capacity => capacity;

        public event Action<Food, FoodState> StateChanged;
        public event Action<Food> ReenterConveyorRequested;

        public void PresetQueueState(FoodState state)
        {
            currentState = state;
            queueStatePreset = true;
        }

        public void PresetCapacity(int value)
        {
            capacity = Mathf.Max(0, value);
            UpdateCapacityLabel();
        }

        private void Start()
        {
            if (trayManager == null) trayManager = FindFirstObjectByType<TrayManager>();
            if (trayManager == null) Debug.LogError("Food: Sahnede bir TrayManager bulunamadı.");

            if (labelFacingCamera == null) labelFacingCamera = Camera.main;

            if (!queueStatePreset)
                ChangeState(FoodState.AvailableInQueue);

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
                AudioEvents.PlayFoodClick();
                TryLaunchWithAnimation();
            }
            else
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");
                ReenterConveyorRequested?.Invoke(this);
            }
        }

        public bool EnterConveyorFromSlot()
        {
            return TryLaunchWithAnimation();
        }

        public void SetInFoodSlot()
        {
            ChangeState(FoodState.InFoodSlot);
        }

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

            if (jumpCoroutine != null)
                StopCoroutine(jumpCoroutine);

            jumpCoroutine = StartCoroutine(JumpToConveyorRoutine());
            return true;
        }

        private IEnumerator JumpToConveyorRoutine()
        {
            ChangeState(FoodState.Launching);

            Vector3 startPos = transform.position;
            Vector3 targetPos = trayManager.GetWaypointPosition(0);

            float elapsed = 0f;

            while (elapsed < jumpDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / jumpDuration);

                // Parabolic Arc
                Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
                float heightOffset = 4f * jumpArcHeight * (t - (t * t));
                currentPos.y += heightOffset;

                transform.position = currentPos;
                yield return null;
            }

            transform.position = targetPos;

            // Finalize launch on the conveyor
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

            jumpCoroutine = null;
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