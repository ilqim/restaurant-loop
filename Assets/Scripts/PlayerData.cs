using System;
using System.Collections.Generic;
using UnityEngine;

namespace RestaurantLoop
{
    public enum BoosterType
    {
        Shuffle,
        AddTray,
        Select
    }

    public static class PlayerData
    {
        private const string CoinsKey = "PlayerData_Coins";
        private const string HeartsKey = "PlayerData_Hearts";
        private const string CurrentLevelKey = "PlayerData_CurrentLevel";
        private const string BoosterKeyPrefix = "PlayerData_Booster_";

        public const int MaxHearts = 5;
        public const int DefaultInitialCoins = 100;
        public const int DefaultStartingLevel = 1;
        public const int DefaultBoosterCount = 99;

        private static int? coins;
        private static int? hearts;
        private static int? currentLevel;
        private static readonly Dictionary<BoosterType, int> boosterCounts = new();

        public static event Action<int> CoinsChanged;
        public static event Action<int> HeartsChanged;
        public static event Action<int> CurrentLevelChanged;
        public static event Action<BoosterType, int> BoosterCountChanged;

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
            // İSTEK: Coin sesi 3 kez ARDIŞIK çalsın (üst üste binmeden) —
            // gerçek "biri bitince diğeri başlar" mantığı AudioManager'da
            // (coroutine ile) yönetiliyor, buradan sadece istek gönderiliyor.
            AudioEvents.PlayCoinEarnSequence(3);
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

        // ==========================================
        // BOOSTERLAR — Shuffle, Add Tray, Select
        // ==========================================

        private static string BoosterKey(BoosterType type) => BoosterKeyPrefix + type;

        /// <summary>Belirtilen booster tipinden kaç tane var. İlk hiç kaydedilmemişse varsayılan 99'dur.</summary>
        public static int GetBoosterCount(BoosterType type)
        {
            if (!boosterCounts.TryGetValue(type, out int value))
            {
                value = PlayerPrefs.GetInt(BoosterKey(type), DefaultBoosterCount);
                boosterCounts[type] = value;
            }

            return value;
        }

        private static void SetBoosterCount(BoosterType type, int value)
        {
            int clamped = Mathf.Max(0, value);

            if (boosterCounts.TryGetValue(type, out int current) && current == clamped)
                return;

            boosterCounts[type] = clamped;
            PlayerPrefs.SetInt(BoosterKey(type), clamped);
            PlayerPrefs.Save();
            BoosterCountChanged?.Invoke(type, clamped);
        }

        /// <summary>Booster'ı kullanmayı dener — yeterli hak yoksa false döner, hiçbir şey değişmez.</summary>
        public static bool TrySpendBooster(BoosterType type, int amount = 1)
        {
            if (amount <= 0) return false;

            int current = GetBoosterCount(type);
            if (current < amount) return false;

            SetBoosterCount(type, current - amount);
            return true;
        }

        public static void AddBoosterCount(BoosterType type, int amount)
        {
            if (amount <= 0) return;
            SetBoosterCount(type, GetBoosterCount(type) + amount);
        }

        /// <summary>
        /// SADECE "her level başında 99'a sıfırlansın" istersen kullan —
        /// GameManager'da level başlarken (örn. Start()'ta) çağır.
        /// Kalıcı bir kaynak istiyorsan (coin gibi, level değişince
        /// sıfırlanmasın) bu metodu HİÇ çağırma, boosterlar zaten
        /// TrySpendBooster/AddBoosterCount ile normal şekilde kalıcı kalır.
        /// </summary>
        public static void ResetAllBoostersToDefault()
        {
            foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
            {
                SetBoosterCount(type, DefaultBoosterCount);
            }
        }

        // Kolaylık property'leri — okunabilirlik için (Coins/Hearts ile aynı üslup).
        public static int ShuffleBoosterCount => GetBoosterCount(BoosterType.Shuffle);
        public static int AddTrayBoosterCount => GetBoosterCount(BoosterType.AddTray);
        public static int SelectBoosterCount => GetBoosterCount(BoosterType.Select);

        /// <summary>
        /// SADECE TEST/DEBUG amaçlı — level ilerlemesini 1'e sıfırlar.
        /// Coin ve can'a dokunmaz. Gerçek cihazda oyuncuya asla otomatik
        /// çağrılmamalı; Editor'de test ederken elle tetiklemek için var.
        /// </summary>
        public static void ResetProgress()
        {
            CurrentLevel = DefaultStartingLevel;
        }

        /// <summary>
        /// SADECE TEST/DEBUG amaçlı — coin, can ve level ilerlemesinin
        /// TÜMÜNÜ varsayılana sıfırlar (yeni oyuncu gibi baştan başlar).
        /// </summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(CoinsKey);
            PlayerPrefs.DeleteKey(HeartsKey);
            PlayerPrefs.DeleteKey(CurrentLevelKey);
            PlayerPrefs.Save();

            coins = null;
            hearts = null;
            currentLevel = null;

            CoinsChanged?.Invoke(Coins);
            HeartsChanged?.Invoke(Hearts);
            CurrentLevelChanged?.Invoke(CurrentLevel);
        }
    }
}