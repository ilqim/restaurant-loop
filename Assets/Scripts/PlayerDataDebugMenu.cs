#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// SADECE Unity Editor'de görünen debug menüsü — test amaçlı
    /// PlayerData'yı sıfırlamak için. #if UNITY_EDITOR sayesinde bu dosya
    /// cihaz build'ine (Android/iOS/PC build) hiç dahil edilmez, oyuncu
    /// bunu asla göremez.
    ///
    /// Kullanım: Unity üst menüsünden
    /// "RestaurantLoop > Debug > Reset Level Progress" veya
    /// "RestaurantLoop > Debug > Reset ALL Player Data" tıkla.
    /// </summary>
    public static class PlayerDataDebugMenu
    {
        [MenuItem("RestaurantLoop/Debug/Reset Level Progress (Level 1'e dön)")]
        private static void ResetLevelProgress()
        {
            PlayerData.ResetProgress();
            Debug.Log("PlayerDataDebugMenu: Level ilerlemesi sıfırlandı, şimdi Level 1'desin.");
        }

        [MenuItem("RestaurantLoop/Debug/Reset ALL Player Data (coin+can+level)")]
        private static void ResetAllPlayerData()
        {
            PlayerData.ResetAll();
            Debug.Log("PlayerDataDebugMenu: TÜM player data (coin, can, level) sıfırlandı.");
        }

        [MenuItem("RestaurantLoop/Debug/Print Current Player Data")]
        private static void PrintCurrentPlayerData()
        {
            Debug.Log($"PlayerData -> Level: {PlayerData.CurrentLevel}, Coins: {PlayerData.Coins}, Hearts: {PlayerData.Hearts}");
        }
    }
}
#endif