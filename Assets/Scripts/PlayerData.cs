using System;
using UnityEngine;

namespace RestaurantLoop
{
    public static class PlayerData
    {
        private const string CoinsKey = "PlayerData_Coins";
        private const string HeartsKey = "PlayerData_Hearts";

        public const int MaxHearts = 5;
        public const int DefaultInitialCoins = 100;

        private static int? coins;
        private static int? hearts;

        public static event Action<int> CoinsChanged;
        public static event Action<int> HeartsChanged;

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