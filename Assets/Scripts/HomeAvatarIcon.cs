using UnityEngine;
using UnityEngine.UI;

namespace RestaurantLoop
{
    /// <summary>
    /// Ana menünün sol üstündeki avatar ikonu. PlayerData.PlayerAvatarIndex'e
    /// göre kendini gösterir; ProfilePanel'de Save'e basılıp avatar
    /// değiştiğinde PlayerAvatarIndexChanged event'i üzerinden ANINDA
    /// (sahne geçişine gerek kalmadan) günceller.
    /// </summary>
    public class HomeAvatarIcon : MonoBehaviour
    {
        [Tooltip("Tüm avatar sprite'larının kaynağı — ProfilePanel'deki AvatarDatabase ile AYNI asset olmalı.")]
        [SerializeField] private AvatarDatabase avatarDatabase;
        [Tooltip("İkonun kendi Image component'i. Boş bırakılırsa bu objenin üzerindeki Image kullanılır.")]
        [SerializeField] private Image iconImage;

        private void Awake()
        {
            if (iconImage == null) iconImage = GetComponent<Image>();
        }

        private void OnEnable()
        {
            Refresh(PlayerData.PlayerAvatarIndex);
            PlayerData.PlayerAvatarIndexChanged += OnPlayerAvatarIndexChanged;
        }

        private void OnDisable()
        {
            PlayerData.PlayerAvatarIndexChanged -= OnPlayerAvatarIndexChanged;
        }

        private void OnPlayerAvatarIndexChanged(int newIndex)
        {
            Refresh(newIndex);
        }

        private void Refresh(int index)
        {
            if (iconImage == null || avatarDatabase == null) return;

            Sprite sprite = avatarDatabase.GetSprite(index);
            if (sprite != null)
                iconImage.sprite = sprite;
        }
    }
}