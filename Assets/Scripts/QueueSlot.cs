using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Raycast ile tıklanabilen her şeyin implement ettiği ortak arayüz.
    /// InputManager sadece bu arayüzü arıyor — QueueSlot, Slot ya da
    /// ileride eklenecek başka bir tıklanabilir tip fark etmiyor,
    /// InputManager hiçbirine özel olarak bağlı değil.
    /// </summary>
    public interface IQueueClickable
    {
        void HandleClick();
    }

    /// <summary>
    /// Bir queue hücresinin tıklanabilir collider'ını taşır. Food'un
    /// KENDİSİNDE collider YOK — tıklanabilir alan burada, sabit bir
    /// grid hücresinde duruyor. QueueManager bir food'u bu hücreye
    /// "ışınladığında" AssignFood ile hangi food'un burada durduğunu
    /// bildiriyor.
    ///
    /// GÖRSEL: Ayrı bir prefab instantiate ETMİYORUZ. Bu objenin ÜZERİNDE
    /// zaten duran SpriteRenderer'ın sprite/color'ını, atanan food'a göre
    /// değiştiriyoruz. Obje sayısı hiç artmıyor, extra collider/script
    /// oluşmuyor, GC allocation yok — en ucuz yöntem.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(SpriteRenderer))]
    public class QueueSlot : MonoBehaviour, IQueueClickable
    {
        private Food assignedFood;
        private SpriteRenderer spriteRenderer;
        private Sprite defaultSprite;
        private Color defaultColor;

        public Food AssignedFood => assignedFood;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            // Prefab/sahnede baştan atanmış olan sprite+renk "boş hücre"
            // görünümü kabul ediliyor — food gelmediğinde / ClearFood
            // sonrası buna geri dönülüyor.
            defaultSprite = spriteRenderer.sprite;
            defaultColor = spriteRenderer.color;
        }

        public void AssignFood(Food food)
        {
            assignedFood = food;

            if (food != null)
            {
                spriteRenderer.sprite = food.QueueSprite != null ? food.QueueSprite : defaultSprite;
                spriteRenderer.color = food.QueueColor;
            }
            else
            {
                ClearFood();
            }
        }

        public void ClearFood()
        {
            assignedFood = null;
            spriteRenderer.sprite = defaultSprite;
            spriteRenderer.color = defaultColor;
        }

        public void HandleClick()
        {
            if (assignedFood == null) return;
            assignedFood.ActivateFromTap();
        }
    }
}