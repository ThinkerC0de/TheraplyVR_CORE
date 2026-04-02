using UnityEngine;

/// <summary>
/// Makes the [TheraplyCore] GameObject persist across non-additive scene loads.
/// Without this, CORE components (TCPServerService, GameRuntimeService, etc.)
/// are destroyed when MenuManager calls SceneManager.LoadScene().
/// </summary>
public class TheraplyCorePersist : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
