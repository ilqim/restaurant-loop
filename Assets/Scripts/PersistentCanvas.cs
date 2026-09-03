using UnityEngine;

public class PersistentCanvas : MonoBehaviour
{
    public static PersistentCanvas Instance { get; private set; }

    private void Awake()
    {
        // Enforce a single persistent canvas instance across scenes
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Ensure this object is a root object before calling DontDestroyOnLoad
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }
}