using System;

namespace RestaurantLoop
{
    /// <summary>
    /// Tek seferlik (one-shot) çalınan efektlerin kimliği. Yeni bir ses
    /// eklemek istediğinde: 1) buraya bir değer ekle, 2) AudioManager
    /// Inspector'ındaki listeye o id için bir AudioClip sürükle. Başka
    /// hiçbir yeri değiştirmene gerek yok.
    /// </summary>
    public enum SfxId
    {
        FoodClick,            // Queue'deki food'u banda göndermek için tıklama
        OrderDelivered,       // Banttaki food müşteriyle eşleştiğinde
        TimedCustomerFail,    // Süreli müşterinin süresi bitip fail olduğunda
        LevelComplete,        // Level tamamlandığında
        LevelFail,             // Level fail olduğunda
        ButtonClick,           // Genel buton tıklama
        CoinEarn,              // Ana menüde coin kazanıldığında
        NewFood,               // Yeni food açıldığında
        NewFeature,            // Yeni feature açıldığında
        NewPowerUp,            // Yeni power-up açıldığında
    }

    /// <summary>
    /// Ses olaylarının GEÇTİĞİ merkezi, statik olay hattı (event bus).
    ///
    /// Diğer scriptler AudioManager'a HİÇ referans tutmuyor — sadece bu
    /// sınıftaki statik metodlardan birini çağırıyor, ör:
    ///
    ///     AudioEvents.PlayFoodClick();
    ///
    /// AudioManager, sahnede bulunduğu sürece bu olaylara abone olup
    /// gerçek sesi çalar. AudioManager sahnede yoksa (ör. bir test
    /// sahnesinde) hiçbir şey patlamaz — sadece hiç ses çıkmaz, çünkü
    /// event'i dinleyen olmaz (FindObjectOfType/null-check zincirlerine
    /// gerek kalmıyor).
    ///
    /// TimedCustomerCountdown ayrı tutuluyor çünkü bu bir "one-shot" ses
    /// değil — süreli müşteri aktif olduğu SÜRECE çalması gereken bir
    /// loop, bu yüzden Start/Stop çifti olarak modellendi.
    /// </summary>
    public static class AudioEvents
    {
        public static event Action<SfxId> SfxRequested;
        public static event Action TimedCustomerCountdownStartRequested;
        public static event Action TimedCustomerCountdownStopRequested;

        /// <summary>Genel amaçlı tetikleyici — SfxId enum'ından herhangi birini çalar.</summary>
        public static void Play(SfxId id) => SfxRequested?.Invoke(id);

        // ---- Okunabilirlik için her ses için ayrı, isimli kısayol ----
        // (İstersen doğrudan Play(SfxId.X) da kullanabilirsin, ikisi de
        // aynı şeyi yapar — bunlar sadece çağıran tarafta enum yazıp
        // yanlış değeri seçme riskini azaltıyor.)

        public static void PlayFoodClick() => Play(SfxId.FoodClick);
        public static void PlayOrderDelivered() => Play(SfxId.OrderDelivered);
        public static void PlayTimedCustomerFail() => Play(SfxId.TimedCustomerFail);
        public static void PlayLevelComplete() => Play(SfxId.LevelComplete);
        public static void PlayLevelFail() => Play(SfxId.LevelFail);
        public static void PlayButtonClick() => Play(SfxId.ButtonClick);
        public static void PlayCoinEarn() => Play(SfxId.CoinEarn);
        public static void PlayNewFood() => Play(SfxId.NewFood);
        public static void PlayNewFeature() => Play(SfxId.NewFeature);
        public static void PlayNewPowerUp() => Play(SfxId.NewPowerUp);

        /// <summary>Süreli müşteri aktif olduğunda çağır — geri sayım/saat sesi loop olarak başlar.</summary>
        public static void StartTimedCustomerCountdown() => TimedCustomerCountdownStartRequested?.Invoke();

        /// <summary>Süreli müşteri sonuçlandığında (teslim edildi/fail oldu/despawn) çağır — loop durur.</summary>
        public static void StopTimedCustomerCountdown() => TimedCustomerCountdownStopRequested?.Invoke();
    }
}