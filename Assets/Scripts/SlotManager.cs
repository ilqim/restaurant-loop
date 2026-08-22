using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public class SlotManager : MonoBehaviour
    {
        [Header("Slots")]
        [SerializeField] private List<Slot> slots = new List<Slot>();

        public bool TryPlaceFood(Food food)
        {
            if (food == null)
                return false;

            // Soldan sağa sırayla kontrol et.
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];

                if (slot == null)
                    continue;

                // Doluysa geç.
                if (!slot.IsEmpty)
                    continue;

                // İlk boş slota yerleştir.
                bool placed = slot.TryPlaceFood(food);

                if (placed)
                    return true;
            }

            // Buraya geldiysek hiçbir slot boş değil.
            Debug.Log("SlotManager: TÜM SLOT'LAR DOLU!");

            // GameManager'a FAIL bildir.
            if (GameManager.Instance != null)
            {
                GameManager.Instance.FailLevel();
            }

            return false;
        }
    }
}