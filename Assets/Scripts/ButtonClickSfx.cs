using UnityEngine;

namespace RestaurantLoop
{
    /// <summary>
    /// Inspector'daki Button OnClick() event'ine bağlanacak köprü.
    /// AudioManager'a değil, AudioEvents'e referans veriyoruz.
    ///
    /// Kullanım: Bu scripti butonun kendi GameObject'ine ekle, Button
    /// component'indeki On Click () listesine yine aynı GameObject'i
    /// sürükle, fonksiyon olarak iki metottan birini seç:
    ///   - PlayClickSfx()         -> genel/olumlu/nötr butonlar (Restart, Play, onay vb.)
    ///   - PlayNegativeClickSfx() -> iptal/kapat (X)/geri/"Leave" gibi olumsuz-hissi butonlar
    ///
    /// Aynı obje üzerinde ikisi de dursun diye tek script — hangi butonun
    /// hangi sesi çalacağına Inspector'dan OnClick listesinde karar
    /// veriyorsun, iki ayrı component eklemene gerek yok.
    /// </summary>
    public class ButtonSfx : MonoBehaviour
    {
        public void PlayClickSfx() => AudioEvents.PlayButtonClick();

        public void PlayNegativeClickSfx() => AudioEvents.PlayNegativeButtonClick();
    }
}