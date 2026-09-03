using System;

namespace RestaurantLoop
{
    public enum SfxId
    {
        ButtonClick,           
        NegativeButtonClick,   
        FoodClick,             
        OrderDelivered,        
        New,                   
        LevelComplete,         
        LevelFail,             
        CoinEarn,              
        TimedCustomerFail,     
        SurpriseFood,          // YENİ: Sürpriz yemek açılma sesi
        SurpriseCustomer,      // YENİ: Sürpriz müşteri açılma sesi
        Shuffle                // YENİ: Shuffle booster kullanılınca çalan ses
    }

    public static class AudioEvents
    {
        // ---- SFX ----
        public static event Action<SfxId> SfxRequested;
        public static event Action TimedCustomerCountdownStartRequested;
        public static event Action TimedCustomerCountdownStopRequested;

        public static void Play(SfxId id) => SfxRequested?.Invoke(id);

        public static void PlayButtonClick() => Play(SfxId.ButtonClick);
        public static void PlayNegativeButtonClick() => Play(SfxId.NegativeButtonClick);
        public static void PlayFoodClick() => Play(SfxId.FoodClick);
        public static void PlayOrderDelivered() => Play(SfxId.OrderDelivered);
        public static void PlayNew() => Play(SfxId.New);
        public static void PlayLevelComplete() => Play(SfxId.LevelComplete);
        public static void PlayLevelFail() => Play(SfxId.LevelFail);
        public static void PlayCoinEarn() => Play(SfxId.CoinEarn);
        public static void PlayTimedCustomerFail() => Play(SfxId.TimedCustomerFail);
        
        // YENİ ÇAĞRILAR
        public static void PlaySurpriseFood() => Play(SfxId.SurpriseFood);
        public static void PlaySurpriseCustomer() => Play(SfxId.SurpriseCustomer);
        public static void PlayShuffle() => Play(SfxId.Shuffle);

        // ---- Coin Sesi ----
        public static event Action<int> CoinEarnSequenceRequested;
        public static void PlayCoinEarnSequence(int times = 3) => CoinEarnSequenceRequested?.Invoke(times);

        public static void StartTimedCustomerCountdown() => TimedCustomerCountdownStartRequested?.Invoke();
        public static void StopTimedCustomerCountdown() => TimedCustomerCountdownStopRequested?.Invoke();

        // ---- Müzik ----
        public static event Action MusicPlayRequested;
        public static event Action MusicStopRequested;

        public static void PlayMusic() => MusicPlayRequested?.Invoke();
        public static void StopMusic() => MusicStopRequested?.Invoke();

        public static event Action<LevelDifficulty> MusicForDifficultyRequested;
        public static void PlayMusicForDifficulty(LevelDifficulty difficulty) => MusicForDifficultyRequested?.Invoke(difficulty);

        public static event Action FailMusicRequested;
        public static void PlayFailMusic() => FailMusicRequested?.Invoke();
    }
}