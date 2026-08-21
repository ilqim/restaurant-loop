using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Slot'un tıklanabilir collider'ını taşır. Slot'un kendisi (Slot.cs)
    /// hâlâ Food referansını tutuyor — bu component sadece "tıklandım"
    /// bilgisini oraya iletiyor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SlotClickTarget : MonoBehaviour
    {
        [SerializeField] private Slot slot;

        private void Awake()
        {
            if (slot == null) slot = GetComponent<Slot>();
            if (slot == null) slot = GetComponentInParent<Slot>();
        }

        public void HandleClick()
        {
            if (slot == null || slot.IsEmpty) return;
            slot.CurrentFood?.ActivateFromTap();
        }
    }
}