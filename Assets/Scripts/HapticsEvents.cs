using System;

namespace RestaurantLoop
{
    /// <summary>
    /// AudioEvents ile aynı mantık: diğer scriptler titreşim tetiklemek
    /// istediğinde doğrudan Handheld.Vibrate() çağırmak yerine
    /// HapticsEvents.Vibrate() çağırır. GameSettings.VibrationEnabled
    /// kontrolünü (kapalıysa hiç titretmeme) çağıran kodun bilmesine
    /// gerek yok — bunu HapticsManager hallediyor.
    /// </summary>
    public static class HapticsEvents
    {
        public static event Action VibrateRequested;

        public static void Vibrate() => VibrateRequested?.Invoke();
    }
}