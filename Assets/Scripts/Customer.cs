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

        [Header("Customer Order Bubble")]
        [Tooltip("Prefab içindeki adı 'Bubble' olan child obje otomatik bulunur. Inspector'dan atamana gerek yok.")]
        [SerializeField] private Transform orderBubble;

        [Header("Debug")]
        [SerializeField] private CustomerState currentState = CustomerState.Blocked;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private CustomerManager customerManager;
        private MaterialPropertyBlock mpb;
        private Color[] originalColors;
        private bool initialized;

        // Bu müşteriye şu anda gönderilmekte olan Food.
        private Food incomingFood;

        public int Row { get; private set; }
        public int Col { get; private set; }
        public FoodType DesiredFood { get; private set; }
        public CustomerState CurrentState => currentState;

        public bool IsWaiting =>
            currentState == CustomerState.Idle ||
            currentState == CustomerState.Blocked;

        // Başka bir Food şu anda bu müşteriye gidiyor mu?
        public bool IsReceivingFood => incomingFood != null;


        // ============================================================
        // AWAKE
        // ============================================================

        private void Awake()
        {
            // Renderer'ları otomatik bul.
            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade = GetComponentsInChildren<Renderer>(true);

            // Prefab içindeki "Bubble" child'ını otomatik bul.
            FindOrderBubble();

            // MaterialPropertyBlock hazırla.
            mpb = new MaterialPropertyBlock();

            // Bubble'ı instantiate edilir edilmez kameraya hizala.
            AlignOrderBubbleToCamera();
        }


        // ============================================================
        // BUBBLE BULMA
        // ============================================================

        private void FindOrderBubble()
        {
            // Zaten atanmışsa tekrar arama.
            if (orderBubble != null)
                return;

            Transform[] children =
                GetComponentsInChildren<Transform>(true);

            foreach (Transform child in children)
            {
                if (child.name == "Bubble")
                {
                    orderBubble = child;
                    return;
                }
            }

            Debug.LogWarning(
                $"Customer [{name}]: Child objeler arasında 'Bubble' isimli obje bulunamadı."
            );
        }


        // ============================================================
        // INIT
        // ============================================================

        public void Init(
            int row,
            int col,
            FoodType desiredFood,
            CustomerManager manager)
        {
            Row = row;
            Col = col;
            DesiredFood = desiredFood;
            customerManager = manager;

            currentState = CustomerState.Blocked;

            incomingFood = null;

            // Pool'dan tekrar kullanılıyorsa Bubble referansını
            // garantiye al.
            FindOrderBubble();

            if (renderersToFade == null || renderersToFade.Length == 0)
                renderersToFade =
                    GetComponentsInChildren<Renderer>(true);

            if (mpb == null)
                mpb = new MaterialPropertyBlock();

            CacheOriginalColors();
            ApplyVisual();

            // Init sırasında da tekrar hizala.
            // Böylece pool'dan geri geldiğinde de doğru olur.
            AlignOrderBubbleToCamera();

            initialized = true;

            if (customerManager != null)
            {
                customerManager.RegisterCustomer(this);
            }
            else
            {
                Debug.LogWarning(
                    $"Customer [{name}]: CustomerManager atanmadı, state sistemi çalışmayacak."
                );
            }
        }


        // ============================================================
        // BUBBLE KAMERAYA HİZALAMA
        // ============================================================

        private void AlignOrderBubbleToCamera()
        {
            if (orderBubble == null)
                return;

            Camera cam = Camera.main;

            if (cam == null)
                return;

            /*
             * Bubble'ın yüzeyini kameranın görüş düzlemine paralel yapıyoruz.
             *
             * Kamera sabit olduğu için her frame billboard yapmıyoruz.
             * Sadece oluşturulurken / pool'dan geri alınırken bir kez
             * hizalanıyor.
             */
            orderBubble.rotation =
                Quaternion.LookRotation(
                    cam.transform.forward,
                    cam.transform.up
                );
        }


        // ============================================================
        // FOOD RESERVATION
        // ============================================================

        /// <summary>
        /// Bir Food bu müşteriye gönderilmeden önce müşteriyi rezerve eder.
        /// Aynı müşteriye ikinci bir Food gönderilmesini engeller.
        /// </summary>
        public bool TryReserveForFood(Food food)
        {
            if (food == null)
                return false;

            // Zaten başka bir Food bu müşteriye gidiyor.
            if (incomingFood != null)
                return false;

            // Müşteri Blocked olmamalı.
            if (currentState == CustomerState.Blocked)
                return false;

            // Müşteri Idle olmalı.
            if (currentState != CustomerState.Idle)
                return false;

            // İstenen yemek ile gönderilen yemek eşleşmeli.
            if (DesiredFood != food.FoodTypeValue)
                return false;

            // Müşteriyi bu Food için kilitle.
            incomingFood = food;

            return true;
        }


        /// <summary>
        /// Food müşteriye ulaştığında rezervasyonu kaldırır.
        /// </summary>
        public void ClearFoodReservation(Food food)
        {
            if (incomingFood == food)
                incomingFood = null;
        }


        // ============================================================
        // FOOD RECEIVED
        // ============================================================

        public void ReceiveFood()
        {
            SetState(CustomerState.Eating);

            SetState(CustomerState.HappyJump);

            SetState(CustomerState.Leaving);

            Despawn();
        }


        // ============================================================
        // STATE
        // ============================================================

        public void SetState(CustomerState newState)
        {
            if (currentState == newState)
                return;

            currentState = newState;

            ApplyVisual();
        }


        // ============================================================
        // DESPAWN
        // ============================================================

        private void Despawn()
        {
            incomingFood = null;

            if (initialized && customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
                initialized = false;
            }

            if (ObjectPool.Instance != null)
            {
                ObjectPool.Instance.Return(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }


        // ============================================================
        // VISUAL
        // ============================================================

        private void ApplyVisual()
        {
            if (renderersToFade == null ||
                originalColors == null)
                return;

            float alpha =
                currentState == CustomerState.Blocked
                    ? blockedAlpha
                    : 1f;

            for (int i = 0; i < renderersToFade.Length; i++)
            {
                var r = renderersToFade[i];

                if (r == null)
                    continue;

                r.GetPropertyBlock(mpb);

                Color c = originalColors[i];
                c.a = alpha;

                mpb.SetColor(BaseColorId, c);

                r.SetPropertyBlock(mpb);
            }
        }


        // ============================================================
        // ORIGINAL COLORS
        // ============================================================

        private void CacheOriginalColors()
        {
            originalColors =
                new Color[renderersToFade.Length];

            for (int i = 0;
                i < renderersToFade.Length;
                i++)
            {
                var mat =
                    renderersToFade[i] != null
                        ? renderersToFade[i].sharedMaterial
                        : null;

                originalColors[i] =
                    mat != null &&
                    mat.HasProperty(BaseColorId)
                        ? mat.GetColor(BaseColorId)
                        : Color.white;
            }
        }


        // ============================================================
        // DESTROY
        // ============================================================

        private void OnDestroy()
        {
            if (initialized &&
                customerManager != null)
            {
                customerManager.UnregisterCustomer(this);
            }
        }
    }
}