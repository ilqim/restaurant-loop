using System;
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

    public class Food : MonoBehaviour
    {
        [Tooltip("Boş bırakılırsa Start'ta otomatik aranır (sadece bir kez).")]
        [SerializeField] private TrayManager trayManager;

        [Header("Bu yemeğin türü")]
        [SerializeField] private FoodType foodType;

        [Header("Kapasite")]
        [SerializeField] private int capacity = 10;

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
                // Queue'deki food'u banda göndermek için tıklama.
                AudioEvents.PlayFoodClick();
                TryLaunchAndDespawn();
            }
            else
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Slottan çıkış isteniyor.");
                ReenterConveyorRequested?.Invoke(this);
            }
        }

        public void EnterConveyorFromSlot()
        {
            TryLaunchAndDespawn();
        }

        public void SetInFoodSlot()
        {
            ChangeState(FoodState.InFoodSlot);
        }

        private void TryLaunchAndDespawn()
        {
            if (trayManager == null)
            {
                Debug.LogError("Food: TrayManager yok, tray başlatılamıyor.");
                return;
            }

            bool launched = trayManager.TryLaunchTray(foodType, capacity);
            if (!launched)
            {
                if (verboseLogging) Debug.Log($"Food [{gameObject.name}]: Konveyör dolu, tray başlatılamadı.");
                return;
            }
            //Debug.Log($"Found customer {target.gameObject} at {currentIndex}");

            ChangeState(FoodState.OnConveyor);
            DespawnSelf();
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