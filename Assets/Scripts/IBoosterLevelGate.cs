namespace RestaurantLoop
{
    /// <summary>
    /// Level'e bağlı olarak kilitli/açık olması gereken booster butonlarının
    /// implement ettiği arayüz. LevelManager, Game sahnesi her yüklendiğinde
    /// sahnedeki TÜM IBoosterLevelGate implementasyonlarını bulup
    /// RefreshLevelGate()'i çağırır — buton kendi BoosterType'ına göre
    /// LevelManager'dan "bu level'de açık mıyım" bilgisini sorup UI'ını
    /// (interactable + soluk görünüm) buna göre günceller.
    /// </summary>
    public interface IBoosterLevelGate
    {
        void RefreshLevelGate();
    }
}