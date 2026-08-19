using UnityEngine;

namespace RestaurantLoop
{
    public enum CustomerState
    {
        Blocked,
        Idle,
        Eating,
        HappyJump,
        Leaving,
        Angry
    }

    public class Customer : MonoBehaviour
    {
        [Header("Görsel — Blocked iken saydamlaşacak renderer'lar")]
        [Tooltip("Boş bırakılırsa Awake'te GetComponentsInChildren<Renderer> ile otomatik doldurulur.")]
        [SerializeField] private Renderer[] renderersToFade;

        [Range(0f, 1f)]
        [SerializeField] private float blockedAlpha = 0.35f;

        [Header("Debug")]
        [SerializeField] private CustomerState currentState = CustomerState.Blocked;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private CustomerManager customerManager;
        private MaterialPropertyBlock mpb;
        private Color[] originalColors;
        private bool initialized;

        public int Row { get; private set; }
        public int Col { get; private set; }
        public FoodType DesiredFood { get; private set; }
        public CustomerState CurrentState => currentState;

        public bool IsWaiting => currentState == CustomerState.Idle || currentState == CustomerState.Blocked;

        public void Init(int row, int col, FoodType desiredFood, CustomerManager manager)
        {
            Row = row;
            Col = col;
            DesiredFood = desiredFood;
            customerManager = manager;
            currentState = CustomerState.Blocked;

            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = GetComponentsInChildren<Renderer>();

            mpb = new MaterialPropertyBlock();
            CacheOriginalColors();
            ApplyVisual();

            initialized = true;

            if (customerManager != null)
                customerManager.RegisterCustomer(this);
            else
                Debug.LogWarning($"Customer [{name}]: CustomerManager atanmadı, state sistemi çalışmayacak.");
        }

        public void ReceiveFood()
        {
            SetState(CustomerState.Eating);
            SetState(CustomerState.HappyJump);
            SetState(CustomerState.Leaving);
            Despawn();
        }

        public void SetState(CustomerState newState)
        {
            if (currentState == newState) return;
            currentState = newState;
            ApplyVisual();
        }

        private void Despawn()
        {
            if (initialized && customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
                initialized = false;
            }

            if (ObjectPool.Instance != null)
                ObjectPool.Instance.Return(gameObject);
            else
                gameObject.SetActive(false);
        }

        private void ApplyVisual()
        {
            if (renderersToFade == null || originalColors == null) return;

            float alpha = currentState == CustomerState.Blocked ? blockedAlpha : 1f;

            for (int i = 0; i < renderersToFade.Length; i++)
            {
                var r = renderersToFade[i];
                if (r == null) continue;

                r.GetPropertyBlock(mpb);
                Color c = originalColors[i];
                c.a = alpha;
                mpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(mpb);
            }
        }

        private void CacheOriginalColors()
        {
            originalColors = new Color[renderersToFade.Length];
            for (int i = 0; i < renderersToFade.Length; i++)
            {
                var mat = renderersToFade[i] != null ? renderersToFade[i].sharedMaterial : null;
                originalColors[i] = (mat != null && mat.HasProperty(BaseColorId))
                    ? mat.GetColor(BaseColorId)
                    : Color.white;
            }
        }

        private void OnDestroy()
        {
            if (initialized && customerManager != null)
                customerManager.UnregisterCustomer(this);
        }
    }
}