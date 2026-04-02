using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheraplyVR.DateEvents;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using static UnityEngine.EventSystems.EventTrigger;

namespace TheraplyVR.Pairs.Basic
{
    #region Enums

    public enum ExerciseVariant
    {
        TwoObjects,
        CombinedObject
    }

    public enum ObjectType
    {
        christmas,
        arrows,
        square,
        triangles,
        fruits,
        oneSymbol
    }

    #endregion

    #region Data Structures

    [Serializable]
    public class TwoHandsEntry
    {
        [Tooltip("Unikalny identyfikator tego TwoHands (np. 'Images', 'Symbols', 'Shadows')")]
        public string id;

        [Tooltip("Nazwa wyświetlana (opcjonalne)")]
        public string displayName;

        [Tooltip("Referencja do GameObject z komponentem TwoHands")]
        public TwoHands twoHands;

        [Tooltip("Czy ten TwoHands jest domyślnie aktywny")]
        public bool isDefault = false;
    }

    [Serializable]
    public class ThemeToTwoHandsMapping
    {
        [Tooltip("DateEvent ScriptableObject - przeciągnij tutaj event (np. Christmas Event)")]
        public ScriptableObject dateEvent;

        [Tooltip("TwoHands system do użycia gdy ten event jest aktywny - przeciągnij GameObject z komponentem TwoHands")]
        public TwoHands twoHands;
    }

    [Serializable]
    public class BasicSessionConfig
    {
        public string name;
        public string code;

        [Tooltip("Liczba rund w sesji (1-10)")]
        public int repetitions = 3;

        [Tooltip("Liczba wierszy w kolumnie (1-4)")]
        public int rows = 4;

        [Tooltip("Czy włączyć tryb memory")]
        public bool memoryMode = false;

        //[Tooltip("Wariant ćwiczenia: TwoObjects (dwa różne wzorce) lub CombinedObject (jeden wzorzec)")]
        //public ExerciseVariant exerciseVariant = ExerciseVariant.TwoObjects;

        [Tooltip("Typ obiektów (używany do raportowania, nie filtruje prefabów)")]
        public ObjectType objectType = ObjectType.fruits;

        public BasicSessionResult result;

        /// <summary>
        /// Koryguje wartości poza zakresem.
        /// </summary>
        public void Sanitize()
        {
            repetitions = Mathf.Clamp(repetitions, 1, 10);
            rows = Mathf.Clamp(rows, 1, 4);
        }
    }

    [Serializable]
    public class BasicSessionResult
    {
        public string sessionName;
        public int badMovesCount;
        public float averageReactionTime;
    }

    #endregion

    public class BasicSessionManager : MonoBehaviour
    {
        public BasicSessionConfig activeConfig
        {
            get
            {
                return _activeConfig;
            }
            set
            {
                _activeConfig = value;
            }
        }




        #region Serialized Fields
        [SerializeField] private TextMeshProUGUI label;

        [Header("Configuration")]
        [Tooltip("Lista dostępnych systemów TwoHands - każdy reprezentuje inną rozgrywkę")]
        [SerializeField] private List<TwoHandsEntry> availableTwoHands = new List<TwoHandsEntry>();

        [Tooltip("Domyślna konfiguracja sesji")]
        [SerializeField] private BasicSessionConfig defaultConfig;

        [Header("Audio Feedback")]
        [Tooltip("Źródło dźwięku dla feedbacku")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Dźwięk sukcesu (miękki, przyjemny)")]
        [SerializeField] private AudioClip successSound;

        [Tooltip("Dźwięk błędu (krótkie kliknięcie)")]
        [SerializeField] private AudioClip errorSound;

        [Header("Date Events Integration")]
        [Tooltip("DateEventManager do automatycznego wyboru TwoHands na podstawie aktywnych eventów. Jeśli ustawione, aktywny event automatycznie wybierze twoHandsId (chyba że ustawione ręcznie w konfiguracji).")]
        [SerializeField] private DateEventManager dateEventManager;

        [Tooltip("Mapowanie DateEvent na TwoHands system. Jeśli aktywny event jest w mapie, automatycznie wybierze odpowiadający TwoHands.")]
        [SerializeField] private List<ThemeToTwoHandsMapping> themeToTwoHandsMap = new List<ThemeToTwoHandsMapping>();

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        [SerializeField] private Marker markerL;
        [SerializeField] private Marker markerR;
        [SerializeField] private Animator bookAnimator;

        [SerializeField] private bool offlineMode;
        #endregion

        #region Unity Events

        [Serializable] public class SessionResultEvent : UnityEvent<BasicSessionResult> { }

        [Header("Lifecycle Events")]
        public UnityEvent OnSessionStarted;
        public SessionResultEvent OnSessionCompleted;
        public UnityEvent OnSessionStopped;

        #endregion

        #region Private State

        private BasicSessionConfig _activeConfig;
        private BasicSessionResult _gameResult;
        private bool _sessionRunning;
        private TwoHands _activeTwoHandsSystem;       

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (defaultConfig == null)
            {
                defaultConfig = new BasicSessionConfig();
            }

            defaultConfig.Sanitize();

            // Walidacja dostępnych TwoHands
            if (availableTwoHands == null || availableTwoHands.Count == 0)
            {
                Debug.LogWarning("BasicSessionManager: No TwoHands systems configured! Add at least one in Inspector.");
            }
            else
            {
                // Upewnij się że wszystkie są nieaktywne na start
                foreach (var entry in availableTwoHands)
                {
                    if (entry.twoHands != null && entry.twoHands.gameObject != null)
                    {
                        entry.twoHands.gameObject.SetActive(false);
                    }
                }
            }

            // Upewnij się że AudioSource istnieje
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        private void Start()
        {
            _gameResult = new BasicSessionResult();

            //StartSession();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Konfiguruje sesję z podaną konfiguracją.
        /// </summary>
        public void ConfigureSession(BasicSessionConfig config)
        {
            _activeConfig = CloneConfig(config ?? defaultConfig);
            _activeConfig.Sanitize();

            // Automatyczny wybór TwoHands na podstawie DateEvent (zawsze sprawdzaj jeśli dateEventManager jest przypisany)
            if (dateEventManager != null)
            {
                TwoHands mappedTwoHands = GetTwoHandsForActiveEvent();
                if (mappedTwoHands != null)
                {
                    // Znajdź ID tego TwoHands w availableTwoHands
                    var entry = availableTwoHands.Find(e => e.twoHands == mappedTwoHands);
                    if (entry != null && !string.IsNullOrEmpty(entry.id))
                    {
                        string previousTwoHandsId = _activeConfig.objectType.ToString();
                        //_activeConfig.twoHandsId = entry.id;
                        //if (debugLogs)
                        {
                            string eventName = GetActiveDateEventName();
                            //label.text += previousTwoHandsId + " " + entry.id;
                            if (previousTwoHandsId != entry.id)
                            {
                                //label.text += $"BasicSessionManager: Auto-selected twoHandsId '{entry.id}' from active date event '{eventName}' (overrode '{previousTwoHandsId}')";
                                Debug.Log($"BasicSessionManager: Auto-selected twoHandsId '{entry.id}' from active date event '{eventName}' (overrode '{previousTwoHandsId}')");
                            }
                            else
                            {
                                //label.text += $"BasicSessionManager: Auto-selected twoHandsId '{entry.id}' from active date event '{eventName}'";
                                Debug.Log($"BasicSessionManager: Auto-selected twoHandsId '{entry.id}' from active date event '{eventName}'");
                            }
                        }
                    }
                    else //if (debugLogs)
                    {
                        //label.text += $"BasicSessionManager: Found TwoHands for active event, but it's not in availableTwoHands list!";
                        Debug.LogWarning($"BasicSessionManager: Found TwoHands for active event, but it's not in availableTwoHands list!");
                    }
                }
                else //if (debugLogs)
                {
                    //label.text += $"BasicSessionManager: No mapping found for active date event '{GetActiveDateEventName()}', using configured twoHandsId '{_activeConfig.objectType}'";
                    Debug.Log($"BasicSessionManager: No mapping found for active date event '{GetActiveDateEventName()}', using configured twoHandsId '{_activeConfig.objectType}'");
                }
            }

            //if (debugLogs)
            {
                //label.text += $"BasicSessionManager: Session configured - rounds={_activeConfig.repetitions}, rows={_activeConfig.rows}, memory={_activeConfig.memoryMode}, twoHandsId={_activeConfig.objectType}";
                Debug.Log($"BasicSessionManager: Session configured - rounds={_activeConfig.repetitions}, rows={_activeConfig.rows}, memory={_activeConfig.memoryMode}, twoHandsId={_activeConfig.objectType}");
            }
        }

[ContextMenu("Start Session")]
        /// <summary>
        /// Rozpoczyna sesję.
        /// </summary>
        public void StartSession()
        {
            //label.text = "StartSession: ";
            if (_sessionRunning)
            {
                StopSession();
            }

            // Zawsze konfiguruj sesję przed startem (aby sprawdzić DateEvent)
            ConfigureSession(_activeConfig ?? defaultConfig);
            BuildRound();
            OnSessionStarted?.Invoke();
        }

        /// <summary>
        /// Zatrzymuje sesję.
        /// </summary>
        public void StopSession()
        {
            // Deaktywuj aktywny system TwoHands
            if (_activeTwoHandsSystem != null && _activeTwoHandsSystem.gameObject != null)
            {
                _activeTwoHandsSystem.gameObject.SetActive(false);
            }

            _sessionRunning = false;
            _activeTwoHandsSystem = null;
            OnSessionStopped?.Invoke();
        }
 
        #endregion

        #region Session Flow

        public void GameFinished(BasicSessionResult result)
        {
            _gameResult.averageReactionTime = result.averageReactionTime;
            _gameResult.badMovesCount = result.badMovesCount;

            OnSessionCompleted?.Invoke(result);

            Debug.Log($"Session completed. Avarge reaction time = '{_gameResult.averageReactionTime}'");

            if (debugLogs)
            {
                LogSessionStatistics(result);
            } 
        }

        private void BuildRound()
        {
            // Wybierz odpowiedni TwoHands na podstawie konfiguracji
            _activeTwoHandsSystem = SelectTwoHandsSystem(_activeConfig.objectType.ToString());

            if (_activeTwoHandsSystem == null)
            {
                Debug.LogError("BasicSessionManager: No active TwoHands system available!");
                return;
            }

            // Walidacja: sprawdź czy placeL i placeR mają wystarczająco dużo elementów
            int requiredPlaces = _activeConfig.rows * 4;
            int availablePlaces = GetAvailablePlacesCount(_activeTwoHandsSystem);

            if (availablePlaces < requiredPlaces)
            {
                Debug.LogWarning($"BasicSessionManager: Not enough place positions! Required: {requiredPlaces}, Available: {availablePlaces}. Adjusting rows from {_activeConfig.rows} to {availablePlaces / 4}.");
                // Dostosuj rows aby pasowało do dostępnych pozycji
                int adjustedRows = Mathf.Max(1, availablePlaces / 4);
                _activeConfig.rows = adjustedRows;
            }

            // Walidacja: sprawdź czy objectSets nie jest puste
            if (!HasObjectSets(_activeTwoHandsSystem))
            {
                Debug.LogError("BasicSessionManager: TwoHands system has no objectSets configured! Cannot start session.");
                return;
            }

            // Wywołaj TwoHands.StartSession() z parametrami z konfiguracji
            _activeTwoHandsSystem.StartSession(_activeConfig);

            //Show markers object
            markerL.gameObject.SetActive(true);
            markerR.gameObject.SetActive(true);

            StartCoroutine(VirtualFriend.Instance.FriendTalking("twoHands_markers"));


            if (debugLogs)
            {
                Debug.Log($"BasicSessionManager: Round {_activeConfig.repetitions} prepared.");
            }
        }

        private int GetAvailablePlacesCount(TwoHands twoHands)
        {
            if (twoHands == null) return 0;

            try
            {
                // Użyj refleksji aby dostać się do prywatnych pól placeL i placeR
                var fieldL = typeof(TwoHands).GetField("placeL",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var fieldR = typeof(TwoHands).GetField("placeR",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                int countL = 0;
                int countR = 0;

                if (fieldL != null)
                {
                    Transform[] placeL = fieldL.GetValue(twoHands) as Transform[];
                    if (placeL != null)
                    {
                        countL = placeL.Length;
                    }
                }

                if (fieldR != null)
                {
                    Transform[] placeR = fieldR.GetValue(twoHands) as Transform[];
                    if (placeR != null)
                    {
                        countR = placeR.Length;
                    }
                }

                // Zwróć minimum z dwóch (bo potrzebujemy tyle samo po lewej i prawej)
                return Mathf.Min(countL, countR);
            }
            catch (System.Exception e)
            {
                if (debugLogs)
                {
                    Debug.LogWarning($"BasicSessionManager: Could not access TwoHands place arrays: {e.Message}");
                }
                return 0;
            }
        }

        private bool HasObjectSets(TwoHands twoHands)
        {
            if (twoHands == null) return false;

            try
            {
                // Użyj refleksji aby dostać się do prywatnego pola objectSets
                var field = typeof(TwoHands).GetField("objectSets",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    List<GameObject> objectSets = field.GetValue(twoHands) as List<GameObject>;
                    return objectSets != null && objectSets.Count > 0;
                }
            }
            catch (System.Exception e)
            {
                if (debugLogs)
                {
                    Debug.LogWarning($"BasicSessionManager: Could not access TwoHands.objectSets: {e.Message}");
                }
            }

            return false;
        }

        private TwoHands SelectTwoHandsSystem(string twoHandsId)
        {
            if (debugLogs)
            {
                Debug.Log($"BasicSessionManager: SelectTwoHandsSystem called with twoHandsId='{twoHandsId}'");
            }

            // Najpierw deaktywuj wszystkie
            foreach (var entry in availableTwoHands)
            {
                if (entry.twoHands != null && entry.twoHands.gameObject != null)
                {
                    entry.twoHands.gameObject.SetActive(false);
                }
            }

            // Znajdź odpowiedni system
            TwoHandsEntry selected = null;

            // Szukaj po ID
            if (!string.IsNullOrEmpty(twoHandsId) && twoHandsId != "default")
            {
                selected = availableTwoHands.Find(e => e.id == twoHandsId);
                if (debugLogs)
                {
                    if (selected != null)
                    {
                        Debug.Log($"BasicSessionManager: Found TwoHands by ID '{twoHandsId}': '{selected.id}'");
                    }
                    else
                    {
                        Debug.LogWarning($"BasicSessionManager: TwoHands with ID '{twoHandsId}' not found in availableTwoHands. Available IDs: [{string.Join(", ", availableTwoHands.Select(e => e.id))}]");
                    }
                }
            }

            // Fallback: użyj domyślnego (tylko jeśli nie znaleziono po ID)
            if (selected == null)
            {
                selected = availableTwoHands.Find(e => e.isDefault);
                if (debugLogs && selected != null)
                {
                    Debug.Log($"BasicSessionManager: Using default TwoHands: '{selected.id}' (isDefault=true)");
                }
            }

            // Fallback: pierwszy dostępny
            if (selected == null && availableTwoHands.Count > 0)
            {
                selected = availableTwoHands[0];
                if (debugLogs)
                {
                    Debug.LogWarning($"BasicSessionManager: No default TwoHands found, using first available: '{selected.id}'");
                }
            }

            if (selected != null && selected.twoHands != null)
            {
                selected.twoHands.gameObject.SetActive(true);

                if (debugLogs)
                {
                    string displayName = !string.IsNullOrEmpty(selected.displayName) ? selected.displayName : selected.id;
                    Debug.Log($"BasicSessionManager: Selected TwoHands system '{selected.id}' ({displayName})");
                }
                _gameResult.sessionName = selected.displayName;
                return selected.twoHands;
            }

            Debug.LogError($"BasicSessionManager: No TwoHands system found for ID '{twoHandsId}'!");
            return null;
        }

        public void ActivateMarkers(bool state)
        {
            if (state)
            {
                label.text = "Start";
                _activeTwoHandsSystem.ResetTimer(); //Update time after tutorial
            }
            markerL.SetEnable(state);
            markerR.SetEnable(state);

            OnSessionStarted?.Invoke();
        }

        #endregion

        #region Audio Feedback

        public void PlayFeedbackSound(bool isCorrect)
        {
            if (audioSource == null) return;

            AudioClip clip = isCorrect ? successSound : errorSound;
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        #endregion

        #region Statistics
        
        private BasicSessionResult CalculateSessionResult()
        {

            var sessionResult = new BasicSessionResult();
        /*
            {
                config = _activeConfig,
                //totalRounds = _activeConfig?.numberOfExercises ?? 0,
                completedRounds = _sessionRoundResults.Count,
                roundResults = new List<BasicRoundResult>(_sessionRoundResults),
                sessionStartTime = _sessionStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                sessionEndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            sessionResult.sessionDurationSeconds = (float)(DateTime.Now - _sessionStartTime).TotalSeconds;
            sessionResult.timedOutRounds = _sessionRoundResults.Count(r => r.timedOut);

            if (_sessionRoundResults.Count > 0)
            {
                // Średnie
                sessionResult.averageReactionTime = (float)_sessionRoundResults.Average(r => r.reactionTime);
                sessionResult.averageAccuracyPercent = (float)_sessionRoundResults.Average(r => r.accuracyPercent);
                sessionResult.averageBilateralSyncRatio = (float)_sessionRoundResults.Average(r => r.bilateralSyncRatio);
                sessionResult.averageBadMovesPerRound = (float)_sessionRoundResults.Average(r => r.badMovesCount);

                // Najlepsze/najgorsze
                sessionResult.bestAccuracyPercent = (float)_sessionRoundResults.Max(r => r.accuracyPercent);
                sessionResult.worstAccuracyPercent = (float)_sessionRoundResults.Min(r => r.accuracyPercent);
                sessionResult.bestReactionTime = (float)_sessionRoundResults.Min(r => r.reactionTime);
                sessionResult.worstReactionTime = (float)_sessionRoundResults.Max(r => r.reactionTime);

                // Sumy z TwoHands
                sessionResult.totalBadMoves = _sessionRoundResults.Sum(r => r.badMovesCount);
                sessionResult.totalGoodSelections = _sessionRoundResults.Sum(r => r.goodSelectedCount);
            }
        */
            return sessionResult;
        
        }
        
        private void LogSessionStatistics(BasicSessionResult result)
        {
            if (result == null) return;
            /*
            Debug.Log("=== BASIC SESSION STATISTICS ===");
            Debug.Log($"Session Duration: {result.sessionDurationSeconds:F2} seconds");
            Debug.Log($"Start Time: {result.sessionStartTime}");
            Debug.Log($"End Time: {result.sessionEndTime}");
            Debug.Log($"");
            Debug.Log($"Configuration:");
            //Debug.Log($"  - Mode: {result.config.mode}");
            //Debug.Log($"  - Level: {result.config.level}");
            Debug.Log($"  - Rows: {result.config.rows}");
            Debug.Log($"  - Repetitions: {result.config.repetitions}");
            //Debug.Log($"  - Exercise Variant: {result.config.exerciseVariant}");
            Debug.Log($"  - Object Type: {result.config.objectType}");
            //Debug.Log($"  - Timed Mode: {result.config.timedMode}");
            //if (result.config.timedMode)
            //{
            //    Debug.Log($"  - Time Limit: {result.config.timeLimitSeconds:F1}s");
            //}
            Debug.Log($"");
            Debug.Log($"Rounds Summary:");
            Debug.Log($"  - Total Rounds: {result.totalRounds}");
            Debug.Log($"  - Completed Rounds: {result.completedRounds}");
            Debug.Log($"  - Timed Out Rounds: {result.timedOutRounds}");
            Debug.Log($"  - Completion Rate: {(result.totalRounds > 0 ? (result.completedRounds / (float)result.totalRounds * 100f) : 0f):F1}%");
            Debug.Log($"");
            Debug.Log($"Average Statistics:");
            Debug.Log($"  - Average Reaction Time: {result.averageReactionTime:F3}s");
            Debug.Log($"  - Average Accuracy: {result.averageAccuracyPercent:F2}%");
            Debug.Log($"  - Average Bilateral Sync Ratio: {result.averageBilateralSyncRatio:F3}");
            Debug.Log($"  - Average Bad Moves per Round: {result.averageBadMovesPerRound:F2}");
            Debug.Log($"");
            Debug.Log($"Best/Worst Performance:");
            Debug.Log($"  - Best Accuracy: {result.bestAccuracyPercent:F2}%");
            Debug.Log($"  - Worst Accuracy: {result.worstAccuracyPercent:F2}%");
            Debug.Log($"  - Best Reaction Time: {result.bestReactionTime:F3}s");
            Debug.Log($"  - Worst Reaction Time: {result.worstReactionTime:F3}s");
            Debug.Log($"");
            Debug.Log($"Total Counts (from TwoHands):");
            Debug.Log($"  - Total Bad Moves: {result.totalBadMoves}");
            Debug.Log($"  - Total Good Selections: {result.totalGoodSelections}");
            Debug.Log($"");
            Debug.Log($"Round-by-Round Results:");
            for (int i = 0; i < result.roundResults.Count; i++)
            {
                var round = result.roundResults[i];
                Debug.Log($"  Round {round.roundIndex}: " +
                          $"Accuracy={round.accuracyPercent:F1}%, " +
                          $"Reaction={round.reactionTime:F3}s, " +
                          $"BilateralSync={round.bilateralSyncRatio:F3}, " +
                          $"BadMoves={round.badMovesCount}, " +
                          $"GoodSelections={round.goodSelectedCount}, " +
                          $"TimedOut={round.timedOut}");
            }
            Debug.Log("=== END SESSION STATISTICS ===");
            */
        }
        
        #endregion

        #region Helpers

        private BasicSessionConfig CloneConfig(BasicSessionConfig source)
        {
            return new BasicSessionConfig
            {
                repetitions = source.repetitions,
                rows = source.rows,
                memoryMode = source.memoryMode,
                objectType = source.objectType,
            };
        }

        private TwoHands GetTwoHandsForActiveEvent()
        {
            if (dateEventManager == null) return null;

            // Upewnij się że eventy są sprawdzone
            if (dateEventManager.LastCheckResults.Count == 0)
            {
                dateEventManager.CheckAllEvents();
            }

            var activeEvent = dateEventManager.GetActiveEvent();
            if (activeEvent == null || activeEvent.dateEvent == null)
            {
                if (debugLogs)
                {
                    Debug.Log("BasicSessionManager: No active date event found.");
                }
                return null;
            }

            if (debugLogs)
            {
                Debug.Log($"BasicSessionManager: Active date event found: '{activeEvent.dateEvent.name}' (type: {activeEvent.dateEvent.GetType().Name})");
            }

            // Znajdź mapping dla tego konkretnego DateScriptableObject
            if (debugLogs)
            {
                Debug.Log($"BasicSessionManager: Searching in themeToTwoHandsMap ({themeToTwoHandsMap.Count} entries)...");
                for (int i = 0; i < themeToTwoHandsMap.Count; i++)
                {
                    var m = themeToTwoHandsMap[i];
                    string mapEventName = m.dateEvent != null ? m.dateEvent.name : "NULL";
                    string mapTwoHandsName = m.twoHands != null ? m.twoHands.name : "NULL";
                    bool isMatch = m.dateEvent == activeEvent.dateEvent;
                    Debug.Log($"BasicSessionManager: Map[{i}]: dateEvent='{mapEventName}' (type: {m.dateEvent?.GetType().Name ?? "NULL"}), twoHands='{mapTwoHandsName}', match={isMatch}");
                }
            }

            var mapping = themeToTwoHandsMap.Find(m => m.dateEvent == activeEvent.dateEvent);
            if (mapping != null && mapping.twoHands != null)
            {
                if (debugLogs)
                {
                    Debug.Log($"BasicSessionManager: Found mapping for '{activeEvent.dateEvent.name}' -> TwoHands '{mapping.twoHands.name}'");
                }
                return mapping.twoHands;
            }

            if (debugLogs)
            {
                Debug.LogWarning($"BasicSessionManager: No mapping found for active date event '{activeEvent.dateEvent.name}' in themeToTwoHandsMap (map has {themeToTwoHandsMap.Count} entries). Active event object: {activeEvent.dateEvent}, type: {activeEvent.dateEvent.GetType().FullName}");
            }

            return null;
        }

        private string GetActiveDateEventName()
        {
            if (dateEventManager == null) return "unknown";

            var activeEvent = dateEventManager.GetActiveEvent();
            if (activeEvent != null && activeEvent.dateEvent != null)
            {
                return activeEvent.dateEvent.name;
            }

            return "unknown";
        }

        public void MarkerGrab(bool state)
        {
            _sessionRunning = true;


            if (offlineMode && _activeTwoHandsSystem == null)
            {
                Debug.Log("ActiveTwoHandsSystem is null - setup defaultConfig");
                StartSession();
            }

            if (_activeTwoHandsSystem != null)
            {
                _activeTwoHandsSystem.MarkerGrab(state);
            }
        }

        public void ChangePage()
        {
            bookAnimator.SetTrigger("sheet_move");
        }

        #endregion

    }
}

