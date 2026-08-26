using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace RestaurantLoop
{
    [System.Serializable]
    public struct SfxClip
    {
        public SfxId id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    /// <summary>
    /// AudioEvents'e abone olup gerçek sesi çalan tek merkez. Diğer
    /// scriptler bu sınıfı hiç görmez, sadece AudioEvents.PlayXxx()
    /// çağırır — bkz. AudioEvents.cs.
    ///
    /// Neden tek bir AudioSource yeterli DEĞİL: aynı frame'de birden
    /// fazla food'a aynı anda tıklanabilir ya da iki teslimat üst üste
    /// gelebilir. Tek source kullansaydık, ikinci PlayOneShot çağrısı
    /// birinciyi KESMEZ (PlayOneShot zaten üst üste çalabilir) ama biz
    /// yine de birden fazla source'u round-robin döndürüyoruz ki farklı
    /// sesler birbirini asla "override" etmesin ve volume/pitch gibi
    /// per-clip ayarlar çakışmasın.
    ///
    /// Müzik tamamen ayrı bir AudioSource üzerinden, loop olarak çalar —
    /// SFX one-shot source'larıyla hiç karışmaz, bu yüzden "hangi kanalda
    /// çaldığı" her zaman net: AudioEvents.PlayMusic(...) -> musicSource,
    /// AudioEvents.Play(...) / PlayXxx() -> oneShotSources.
    ///
    /// Ayar (müzik/sfx açık-kapalı) durumu GameSettings'te tutuluyor.
    /// Burada sadece o duruma göre ilgili source'ları susturuyoruz/açıyoruz
    /// — GameSettings sahne geçişinde zaten kalıcı (static), AudioManager
    /// da DontDestroyOnLoad ile tek instance olarak kalıyor.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Tek seferlik (one-shot) efektler")]
        [Tooltip("Her SfxId için bir AudioClip sürükle. Boş bırakılan bir id çağrıldığında Console'a uyarı basılır, oyun çökmez.")]
        [SerializeField]
        private List<SfxClip> clips = new()
        {
            new SfxClip { id = SfxId.ButtonClick, volume = 1f },           // button click.mp3
            new SfxClip { id = SfxId.NegativeButtonClick, volume = 1f },   // negative button click.mp3
            new SfxClip { id = SfxId.FoodClick, volume = 1f },             // food click.mp3
            new SfxClip { id = SfxId.OrderDelivered, volume = 1f },        // sipariş teslim.mp3
            new SfxClip { id = SfxId.New, volume = 1f },                   // new.wav
            new SfxClip { id = SfxId.LevelComplete, volume = 1f },         // level win.mp3
            new SfxClip { id = SfxId.LevelFail, volume = 1f },             // level fail.mp3
            new SfxClip { id = SfxId.CoinEarn, volume = 1f },              // henüz clip yok
            new SfxClip { id = SfxId.TimedCustomerFail, volume = 1f },     // henüz clip yok
        };

        [Header("Süreli Müşteri Geri Sayım Sesi (loop — Start/Stop ile kontrol edilir, SFX kanalı sayılır)")]
        [SerializeField] private AudioClip timedCustomerCountdownClip;
        [Range(0f, 1f)] [SerializeField] private float timedCustomerCountdownVolume = 1f;

        [Header("Müzik (loop — ayrı kanal, şu an sadece ana menüde çalıyor)")]
        [SerializeField] private AudioClip menuMusicClip;
        [Range(0f, 1f)] [SerializeField] private float menuMusicVolume = 1f;

        [Header("Kaynaklar")]
        [Tooltip("Aynı anda üst üste binen one-shot sesler için havuz boyutu. 3-4 genelde yeterli.")]
        [SerializeField] private int oneShotSourceCount = 4;
        [Tooltip("Opsiyonel — Audio Mixer kullanıyorsan SFX grubunu buraya sürükle. Boş bırakılabilir.")]
        [SerializeField] private AudioMixerGroup sfxMixerGroup;
        [Tooltip("Opsiyonel — Audio Mixer kullanıyorsan Müzik grubunu buraya sürükle. Boş bırakılabilir.")]
        [SerializeField] private AudioMixerGroup musicMixerGroup;

        [Header("Ayarlar")]
        [Tooltip("Sahneler arası geçişte AudioManager yok olmasın diye açık tut (genelde açık kalmalı).")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        private readonly Dictionary<SfxId, SfxClip> clipLookup = new();
        private AudioSource[] oneShotSources;
        private int nextSourceIndex;
        private AudioSource countdownSource;
        private AudioSource musicSource;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            clipLookup.Clear();
            foreach (var entry in clips)
                clipLookup[entry.id] = entry;

            BuildAudioSources();
        }

        private void BuildAudioSources()
        {
            oneShotSources = new AudioSource[Mathf.Max(1, oneShotSourceCount)];
            for (int i = 0; i < oneShotSources.Length; i++)
            {
                var go = new GameObject($"OneShotSource_{i}");
                go.transform.SetParent(transform, false);

                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                if (sfxMixerGroup != null) src.outputAudioMixerGroup = sfxMixerGroup;

                oneShotSources[i] = src;
            }

            var countdownGo = new GameObject("CountdownSource");
            countdownGo.transform.SetParent(transform, false);

            countdownSource = countdownGo.AddComponent<AudioSource>();
            countdownSource.playOnAwake = false;
            countdownSource.loop = true;
            countdownSource.mute = !GameSettings.SfxEnabled;
            if (sfxMixerGroup != null) countdownSource.outputAudioMixerGroup = sfxMixerGroup;

            var musicGo = new GameObject("MusicSource");
            musicGo.transform.SetParent(transform, false);

            musicSource = musicGo.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.mute = !GameSettings.MusicEnabled;
            if (musicMixerGroup != null) musicSource.outputAudioMixerGroup = musicMixerGroup;
        }

        private void OnEnable()
        {
            AudioEvents.SfxRequested += HandleSfxRequested;
            AudioEvents.TimedCustomerCountdownStartRequested += HandleCountdownStart;
            AudioEvents.TimedCustomerCountdownStopRequested += HandleCountdownStop;
            AudioEvents.MusicPlayRequested += HandlePlayMusic;
            AudioEvents.MusicStopRequested += HandleMusicStop;

            GameSettings.MusicEnabledChanged += HandleMusicEnabledChanged;
            GameSettings.SfxEnabledChanged += HandleSfxEnabledChanged;
        }

        private void OnDisable()
        {
            AudioEvents.SfxRequested -= HandleSfxRequested;
            AudioEvents.TimedCustomerCountdownStartRequested -= HandleCountdownStart;
            AudioEvents.TimedCustomerCountdownStopRequested -= HandleCountdownStop;
            AudioEvents.MusicPlayRequested -= HandlePlayMusic;
            AudioEvents.MusicStopRequested -= HandleMusicStop;

            GameSettings.MusicEnabledChanged -= HandleMusicEnabledChanged;
            GameSettings.SfxEnabledChanged -= HandleSfxEnabledChanged;
        }

        private void HandleSfxRequested(SfxId id) => PlaySfx(id);

        /// <summary>
        /// Doğrudan da çağrılabilir (AudioManager.Instance.PlaySfx(...)),
        /// ama önerilen yol AudioEvents üzerinden gitmek — diğer
        /// scriptlerin bu sınıfa referans tutmasını gerektirmiyor.
        /// </summary>
        public void PlaySfx(SfxId id)
        {
            if (!GameSettings.SfxEnabled)
                return;

            if (!clipLookup.TryGetValue(id, out var entry) || entry.clip == null)
            {
                Debug.LogWarning($"AudioManager: '{id}' için AudioClip atanmamış (Inspector'dan sürükle).");
                return;
            }

            var source = oneShotSources[nextSourceIndex];
            nextSourceIndex = (nextSourceIndex + 1) % oneShotSources.Length;

            source.PlayOneShot(entry.clip, entry.volume);
        }

        private void HandleCountdownStart()
        {
            if (timedCustomerCountdownClip == null)
            {
                Debug.LogWarning("AudioManager: Timed Customer Countdown Clip atanmamış.");
                return;
            }

            countdownSource.clip = timedCustomerCountdownClip;
            countdownSource.volume = timedCustomerCountdownVolume;
            countdownSource.mute = !GameSettings.SfxEnabled;

            if (!countdownSource.isPlaying)
                countdownSource.Play();
        }

        private void HandleCountdownStop()
        {
            if (countdownSource.isPlaying)
                countdownSource.Stop();
        }

        /// <summary>
        /// Doğrudan da çağrılabilir, ama önerilen yol AudioEvents.PlayMusic().
        /// Zaten çalıyorsa yeniden baştan başlatmaz.
        /// </summary>
        public void HandlePlayMusic()
        {
            if (menuMusicClip == null)
            {
                Debug.LogWarning("AudioManager: Menu Music Clip atanmamış (Inspector'dan sürükle).");
                return;
            }

            if (musicSource.isPlaying && musicSource.clip == menuMusicClip)
                return;

            musicSource.clip = menuMusicClip;
            musicSource.volume = menuMusicVolume;
            musicSource.mute = !GameSettings.MusicEnabled;
            musicSource.Play();
        }

        private void HandleMusicStop()
        {
            if (musicSource.isPlaying)
                musicSource.Stop();
        }

        private void HandleMusicEnabledChanged(bool enabled) => musicSource.mute = !enabled;

        private void HandleSfxEnabledChanged(bool enabled)
        {
            // One-shot'lar zaten anlık çaldığı için PlaySfx içindeki kontrol
            // yeterli; ama countdown loop şu an çalıyor olabilir, onu da
            // anında suspend/resume etmemiz lazım.
            countdownSource.mute = !enabled;
        }
    }
}