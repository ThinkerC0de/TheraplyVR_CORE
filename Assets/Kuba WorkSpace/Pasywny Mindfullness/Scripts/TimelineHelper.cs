using UnityEngine;

public class TimelineHelper : MonoBehaviour
{
    [SerializeField] private PassiveMindfulnessController pmc;
    private void Start()
    {
        pmc = GetComponentInParent<PassiveMindfulnessController>();
    }

    public void TheEnd()
    {
        FindFirstObjectByType<PassiveMindfulnessCommunication>().OnGameFinished("GameFinished:PM");
        pmc.SaveTheGame();
        Debug.Log("KONIEC!");
        // pmc.CurrentTimelineEnded();
        Destroy(gameObject);
    }

    public void PauseTimeline()
    {
        pmc.Pause();
    }
    
    public void PlayTimeline()
    {
        pmc.Play();
    }
}
