using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using TheraplyVR.DateEvents;

namespace TheraplyVR.DateEvents
{
    /// <summary>
    /// Defines a scene remap rule: when Flutter requests sourceSceneIndex
    /// and the specified dateEvent is active, load targetSceneIndex instead.
    /// </summary>
    [System.Serializable]
    public class SceneRemapEntry
    {
        [Tooltip("Scene index that Flutter originally requests")]
        public int sourceSceneIndex;
        
        [Tooltip("Date event that must be active for this remap to trigger")]
        public DateScriptableObject dateEvent;
        
        [Tooltip("Scene index to load instead when the event is active")]
        public int targetSceneIndex;
        
        [Tooltip("Optional: descriptive name for this remap rule")]
        public string description = "";
    }

    /// <summary>
    /// Routes scene load requests from Flutter based on active date events.
    /// When Flutter calls LoadScene(index), this component checks if any
    /// date events are active and remaps to alternative scenes accordingly.
    /// </summary>
    public class DateEventSceneRouter : MonoBehaviour
    {
        [Header("Date Event Manager")]
        [Tooltip("Reference to DateEventManager that tracks active events")]
        [SerializeField] private DateEventManager dateEventManager;

        [Header("Scene Remap Rules")]
        [Tooltip("List of remap rules. Each rule defines: if Flutter requests scene X and event Y is active, load scene Z instead")]
        [SerializeField] private List<SceneRemapEntry> remapRules = new List<SceneRemapEntry>();

        [Header("Settings")]
        [Tooltip("If true, logs scene routing decisions to console")]
        [SerializeField] private bool logRouting = true;
        
        [Tooltip("If true, checks events fresh before each scene load. If false, uses cached results from DateEventManager")]
        [SerializeField] private bool alwaysCheckEventsBeforeLoad = true;

        private static DateEventSceneRouter _instance;
        public static DateEventSceneRouter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<DateEventSceneRouter>();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the list of remap rules for editor access.
        /// </summary>
        public List<SceneRemapEntry> RemapRules => remapRules;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Auto-find DateEventManager if not assigned
            if (dateEventManager == null)
            {
                dateEventManager = FindFirstObjectByType<DateEventManager>();
                if (dateEventManager == null)
                {
                    Debug.LogWarning("[DateEventSceneRouter] No DateEventManager found. Scene routing will load original scenes.");
                }
            }
        }

        /// <summary>
        /// Main method called by Flutter. Routes to appropriate scene based on active events.
        /// </summary>
        /// <param name="flutterRequestedIndex">Scene index that Flutter wants to load</param>
        public void LoadScene(int flutterRequestedIndex)
        {
            int targetIndex = GetTargetSceneIndex(flutterRequestedIndex);
            
            if (logRouting)
            {
                if (targetIndex != flutterRequestedIndex)
                {
                    Debug.Log($"[DateEventSceneRouter] Remapping scene {flutterRequestedIndex} → {targetIndex} (active date event)");
                }
                else
                {
                    Debug.Log($"[DateEventSceneRouter] Loading scene {targetIndex} (no remap applied)");
                }
            }

            SceneManager.LoadScene(targetIndex);
        }

        /// <summary>
        /// Async version of LoadScene for smoother transitions.
        /// </summary>
        public AsyncOperation LoadSceneAsync(int flutterRequestedIndex)
        {
            int targetIndex = GetTargetSceneIndex(flutterRequestedIndex);
            
            if (logRouting)
            {
                if (targetIndex != flutterRequestedIndex)
                {
                    Debug.Log($"[DateEventSceneRouter] Async remapping scene {flutterRequestedIndex} → {targetIndex} (active date event)");
                }
                else
                {
                    Debug.Log($"[DateEventSceneRouter] Async loading scene {targetIndex} (no remap applied)");
                }
            }
            
            return SceneManager.LoadSceneAsync(targetIndex);
        }

        /// <summary>
        /// Determines which scene index to actually load based on active events.
        /// </summary>
        /// <param name="requestedIndex">Originally requested scene index</param>
        /// <returns>Target scene index (may be same or different)</returns>
        public int GetTargetSceneIndex(int requestedIndex)
        {
            if (dateEventManager == null)
            {
                return requestedIndex;
            }

            // Refresh event status if configured to do so
            if (alwaysCheckEventsBeforeLoad)
            {
                dateEventManager.CheckAllEvents();
            }

            // Check each remap rule
            foreach (var rule in remapRules)
            {
                if (rule.sourceSceneIndex != requestedIndex)
                {
                    continue;
                }

                if (rule.dateEvent == null)
                {
                    continue;
                }

                // Check if this event is currently active
                var eventResult = dateEventManager.CheckEvent(rule.dateEvent, System.DateTime.Today);
                if (eventResult != null && eventResult.isActive)
                {
                    if (logRouting)
                    {
                        Debug.Log($"[DateEventSceneRouter] Active event found: {rule.dateEvent.name}");
                    }
                    return rule.targetSceneIndex;
                }
            }

            // No matching active event found, return original
            return requestedIndex;
        }

        /// <summary>
        /// Checks if any remap would apply for the given scene index.
        /// </summary>
        public bool HasActiveRemapForScene(int sceneIndex)
        {
            return GetTargetSceneIndex(sceneIndex) != sceneIndex;
        }

        /// <summary>
        /// Gets the active date event that would cause a remap for the given scene, if any.
        /// </summary>
        public DateScriptableObject GetActiveRemapEvent(int sceneIndex)
        {
            if (dateEventManager == null)
            {
                return null;
            }

            foreach (var rule in remapRules)
            {
                if (rule.sourceSceneIndex != sceneIndex || rule.dateEvent == null)
                {
                    continue;
                }

                var eventResult = dateEventManager.CheckEvent(rule.dateEvent, System.DateTime.Today);
                if (eventResult != null && eventResult.isActive)
                {
                    return rule.dateEvent;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a new remap rule programmatically.
        /// </summary>
        public void AddRemapRule(int sourceIndex, DateScriptableObject dateEvent, int targetIndex, string description = "")
        {
            remapRules.Add(new SceneRemapEntry
            {
                sourceSceneIndex = sourceIndex,
                dateEvent = dateEvent,
                targetSceneIndex = targetIndex,
                description = description
            });
        }

        /// <summary>
        /// Removes all remap rules for a specific source scene index.
        /// </summary>
        public void RemoveRemapRulesForSource(int sourceIndex)
        {
            remapRules.RemoveAll(r => r.sourceSceneIndex == sourceIndex);
        }

        /// <summary>
        /// Clears all remap rules.
        /// </summary>
        public void ClearAllRemapRules()
        {
            remapRules.Clear();
        }

        /// <summary>
        /// Test Xmas.
        /// </summary>
        [ContextMenu("Load Xmas Scene")]
        public void LoadXmas()
        {
            LoadScene(8);
        }
    }
}