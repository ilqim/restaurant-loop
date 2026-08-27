namespace RestaurantLoop
{
    /// <summary>
    /// Game sahnesi yüklendiğinde LevelManager'ın, o anki level'in
    /// LevelData'sını vermesi gereken her component bu arayüzü implement eder.
    /// GridManager, QueueManager, LevelConservationChecker bunlara örnek.
    ///
    /// LevelManager, sahne yüklendiğinde sahnedeki TÜM
    /// ILevelDataReceiver'ları otomatik bulup SetLevelData çağırır —
    /// yeni bir component daha eklemek istersen sadece bu arayüzü
    /// implement etmen yeterli, LevelManager'a dokunmana gerek yok.
    /// </summary>
    public interface ILevelDataReceiver
    {
        void SetLevelData(LevelData data);
    }
}