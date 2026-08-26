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
        ButtonClick,           // Genel (olumlu/nötr) buton tıklama
        NegativeButtonClick,   // İptal/kapat/geri gibi olumsuz-hissi buton tıklama
        FoodClick,             // Queue'deki food'u banda göndermek için tıklama
        OrderDelivered,        // Banttaki food müşteriyle eşleştiğinde ("sipariş teslim")
        New,                   // Yeni food/feature/power-up açıldığında (ortak "ding" sesi)
        LevelComplete,         // Level tamamlandığında ("level win")
        LevelFail,             // Level fail olduğunda
        CoinEarn,              // Ana menüde coin kazanıldığında — henüz clip yok, ileride eklenecek
        TimedCustomerFail,     // Süreli müşterinin süresi bitip fail olduğunda — henüz clip yok, ileride eklenecek
    }

    /// <summary>
    /// Ses olaylarının GEÇTİĞİ merkezi, statik olay hattı (event bus).
    ///
    /// Diğer scriptler AudioManager'a HİÇ referans tutmuyor — sadece bu
    /// sınıftaki statik metodlardan birini çağırıyor, ör:
    ///
    ///     AudioEvents.PlayFoodClick();
    ///     AudioEvents.PlayMusic();
    ///
    /// AudioManager, sahnede bulunduğu sürece bu olaylara abone olup
    /// gerçek sesi çalar. AudioManager sahnede yoksa (ör. bir test
    /// sahnesinde) hiçbir şey patlamaz — sadece hiç ses çıkmaz, çünkü
    /// event'i dinleyen olmaz (FindObjectOfType/null-check zincirlerine
    /// gerek kalmıyor).
    ///
    /// Müzik ve SFX bilerek İKİ AYRI event grubu: hangi sesin hangi
    /// kanalda (müzik source'u mu, sfx source'ları mı) çalacağı burada,
    /// çağıran koda hiç bakmadan netleşiyor. TimedCustomerCountdown da
    /// SFX kanalında ama loop olduğu için ayrı bir Start/Stop çifti.
    /// </summary>
    public static class AudioEvents
    {
        // ---- SFX ----
        public static event Action<SfxId> SfxRequested;
        public static event Action TimedCustomerCountdownStartRequested;
        public static event Action TimedCustomerCountdownStopRequested;

        /// <summary>Genel amaçlı tetikleyici — SfxId enum'ından herhangi birini çalar.</summary>
        public static void Play(SfxId id) => SfxRequested?.Invoke(id);

        // ---- Okunabilirlik için her ses için ayrı, isimli kısayol ----
        // (İstersen doğrudan Play(SfxId.X) da kullanabilirsin, ikisi de
        // aynı şeyi yapar — bunlar sadece çağıran tarafta enum yazıp
        // yanlış değeri seçme riskini azaltıyor.)

        public static void PlayButtonClick() => Play(SfxId.ButtonClick);
        public static void PlayNegativeButtonClick() => Play(SfxId.NegativeButtonClick);
        public static void PlayFoodClick() => Play(SfxId.FoodClick);
        public static void PlayOrderDelivered() => Play(SfxId.OrderDelivered);
        public static void PlayNew() => Play(SfxId.New);
        public static void PlayLevelComplete() => Play(SfxId.LevelComplete);
        public static void PlayLevelFail() => Play(SfxId.LevelFail);
        public static void PlayCoinEarn() => Play(SfxId.CoinEarn);
        public static void PlayTimedCustomerFail() => Play(SfxId.TimedCustomerFail);

        /// <summary>Süreli müşteri aktif olduğunda çağır — geri sayım/saat sesi loop olarak başlar.</summary>
        public static void StartTimedCustomerCountdown() => TimedCustomerCountdownStartRequested?.Invoke();

        /// <summary>Süreli müşteri sonuçlandığında (teslim edildi/fail oldu/despawn) çağır — loop durur.</summary>
        public static void StopTimedCustomerCountdown() => TimedCustomerCountdownStopRequested?.Invoke();

        // ---- Müzik (ayrı kanal — şu an tek parça: sadece ana menüde çalıyor) ----
        public static event Action MusicPlayRequested;
        public static event Action MusicStopRequested;

        /// <summary>Ana menü müziğini loop olarak başlatır. Zaten çalıyorsa yeniden başlatmaz.</summary>
        public static void PlayMusic() => MusicPlayRequested?.Invoke();

        /// <summary>Müzik kanalını durdurur (ör. oyun sahnesine geçince).</summary>
        public static void StopMusic() => MusicStopRequested?.Invoke();
    }
}