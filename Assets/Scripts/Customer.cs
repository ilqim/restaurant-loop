using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Müşterinin olası tüm durumları.
    /// Şu an sadece Blocked / Idle gameplay olarak aktif;
    /// diğerleri (Eating, HappyJump, Leaving, Angry) ileride
    /// başka sistemler tarafından set edilecek — bu script
    /// onların davranışını implement etmiyor, sadece state'i
    /// tutuyor ve enum'da tanımlı olmalarını sağlıyor.
    /// </summary>
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

        /// <summary>
        /// CustomerManager'ın otomatik Idle/Blocked ataması bu state'lerdeki
        /// müşterilere dokunur. Eating/HappyJump/Leaving/Angry state'indeki
        /// bir müşteri hâlâ hücresinde "oturuyor" sayılır (komşuları bloklamaya
        /// devam eder) ama kendi state'i bu sistem tarafından değiştirilmez.
        /// </summary>
        public bool IsWaiting => currentState == CustomerState.Idle || currentState == CustomerState.Blocked;

        /// <summary>
        /// GridManager tarafından instantiate edildikten hemen sonra çağrılır.
        /// </summary>
        public void Init(int row, int col, FoodType desiredFood, CustomerManager manager)
        {
            Row = row;
            Col = col;
            DesiredFood = desiredFood;
            customerManager = manager;

            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = GetComponentsInChildren<Renderer>();

            mpb = new MaterialPropertyBlock();
            CacheOriginalColors();

            initialized = true;

            if (customerManager != null)
                customerManager.RegisterCustomer(this);
            else
                Debug.LogWarning($"Customer [{name}]: CustomerManager atanmadı, state sistemi çalışmayacak.");
        }

        /// <summary>
        /// Food sistemi tarafından, uygun yemek bu müşteriye teslim edilmeye
        /// karar verildiği anda çağrılır (yemek fiziksel olarak varmadan ÖNCE —
        /// böylece aynı müşteri başka bir yemek tarafından ikinci kez hedef
        /// alınmaz). Eating'in gerçek gameplay'i (animasyon, süre, vs.) henüz
        /// implement edilmedi; şimdilik sadece state geçişini yapıyor.
        /// </summary>
        public void ReceiveFood()
        {
            SetState(CustomerState.Eating);
        }

        /// <summary>
        /// CustomerManager veya ileride Eating/Leaving gibi başka sistemler
        /// tarafından çağrılacak tek giriş noktası.
        /// </summary>
        public void SetState(CustomerState newState)
        {
            if (currentState == newState) return;
            currentState = newState;
            ApplyVisual();
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