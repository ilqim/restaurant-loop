using System;
using UnityEngine;

namespace RestaurantLoop
{
    public static class PlayerData
    {
        private const string CoinsKey = "PlayerData_Coins";
        private const string HeartsKey = "PlayerData_Hearts";
        private const string CurrentLevelKey = "PlayerData_CurrentLevel";

        public const int MaxHearts = 5;
        public const int DefaultInitialCoins = 100;
        public const int DefaultStartingLevel = 1;

        private static int? coins;
        private static int? hearts;
        private static int? currentLevel;

        public static event Action<int> CoinsChanged;
        public static event Action<int> HeartsChanged;
        public static event Action<int> CurrentLevelChanged;

        public static int Coins
        {
            get
            {
                coins ??= PlayerPrefs.GetInt(CoinsKey, DefaultInitialCoins);
                return coins.Value;
            }
            private set
            {
                if (coins.HasValue && coins.Value == value) return;
                coins = Mathf.Max(0, value);
                PlayerPrefs.SetInt(CoinsKey, coins.Value);
                PlayerPrefs.Save();
                CoinsChanged?.Invoke(coins.Value);
            }
        }

        public static int Hearts
        {
            get
            {
                hearts ??= PlayerPrefs.GetInt(HeartsKey, MaxHearts);
                return hearts.Value;
            }
            private set
            {
                int clamped = Mathf.Clamp(value, 0, MaxHearts);
                if (hearts.HasValue && hearts.Value == clamped) return;
                hearts = clamped;
                PlayerPrefs.SetInt(HeartsKey, hearts.Value);
                PlayerPrefs.Save();
                HeartsChanged?.Invoke(hearts.Value);
            }
        }

        /// <summary>
        /// Oyuncunun şu an sırada olduğu / en son kaldığı level numarası (1'den başlar).
        /// Uygulama kapatılıp açılsa bile PlayerPrefs üzerinden hatırlanır.
        /// </summary>
        public static int CurrentLevel
        {
            get
            {
                currentLevel ??= PlayerPrefs.GetInt(CurrentLevelKey, DefaultStartingLevel);
                return currentLevel.Value;
            }
            set
            {
                int clamped = Mathf.Max(1, value);
                if (currentLevel.HasValue && currentLevel.Value == clamped) return;
                currentLevel = clamped;
                PlayerPrefs.SetInt(CurrentLevelKey, currentLevel.Value);
                PlayerPrefs.Save();
                CurrentLevelChanged?.Invoke(currentLevel.Value);
            }
        }

        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins += amount;
            AudioEvents.PlayCoinEarn();
        }

        public static bool TrySpendCoins(int amount)
        {
            if (amount <= 0 || Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        public static void ConsumeHeart()
        {
            if (Hearts > 0)
                Hearts--;
        }

        public static void AddHearts(int amount)
        {
            if (amount <= 0) return;
            Hearts += amount;
        }

        public static bool HasHearts() => Hearts > 0;
    }
}