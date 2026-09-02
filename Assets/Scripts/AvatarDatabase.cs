using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Tüm avatar sprite'larının TEK kaynağı. Bir asset olarak oluşturulup
    /// hem Edit Profile grid'ine (ProfilePanel) hem de ana menü ikonuna
    /// (HomeAvatarIcon) atanmalı — böylece ikisi de AYNI listeyi, AYNI
    /// index sırasıyla okur.
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarDatabase", menuName = "RestaurantLoop/Avatar Database")]
    public class AvatarDatabase : ScriptableObject
    {
        [Tooltip("Sırası, Edit Profile ekranındaki 3x3 grid'in sırasıyla (soldan sağa, yukarıdan aşağı) BİREBİR AYNI olmalı — index 0 = grid'deki ilk (sol üst) avatar. PlayerData.PlayerAvatarIndex bu listenin index'ini tutar.")]
        [SerializeField] private Sprite[] avatarSprites;

        public int Count => avatarSprites != null ? avatarSprites.Length : 0;

        public Sprite GetSprite(int index)
        {
            if (avatarSprites == null || avatarSprites.Length == 0) return null;
            index = Mathf.Clamp(index, 0, avatarSprites.Length - 1);
            return avatarSprites[index];
        }
    }
}