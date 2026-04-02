using System;
using UnityEngine;
using UnityEngine.Events;

namespace TheraplyVR.DateEvents
{
    /// <summary>
    /// Unity event that exposes a date range (start and end dates).
    /// </summary>
    [Serializable]
    public class DateRangeEvent : UnityEvent<DateTime, DateTime> { }

    /// <summary>
    /// ScriptableObject that allows setting start and end dates in the Inspector using the custom date picker
    /// and provides a UnityEvent to attach public functions that accept both dates.
    /// </summary>
    [CreateAssetMenu(fileName = "New Date Range Event", menuName = "TheraplyVR/Date Range Event", order = 1)]
    public class DateScriptableObject : ScriptableObject
    {
        [Header("Event Date")]
        [Tooltip("If enabled, this event will repeat every year. Only day and month are used, year is ignored.")]
        [SerializeField] private bool isYearly = false;
        [SerializeField] private SerializableDate startDate = new SerializableDate();
        [SerializeField] private SerializableDate endDate = new SerializableDate();

        [Header("Theme Association")]
        [Tooltip("Identyfikator tematu assetów (np. 'christmas', 'easter'). Jeśli puste, użyje automatycznego mapowania z nazwy.")]
        [SerializeField] private string themeId = "";

        /// <summary>
        /// Event that is invoked when Execute() is called. Use DateEventBridge to connect to scene objects.
        /// </summary>
        public event Action<DateTime, DateTime> OnExecute;

        /// <summary>
        /// Gets the start date as DateTime.
        /// </summary>
        public DateTime StartDate => startDate.ToDateTime();

        /// <summary>
        /// Gets the end date as DateTime.
        /// </summary>
        public DateTime EndDate => endDate.ToDateTime();

        /// <summary>
        /// Gets the start date SerializableDate for direct access.
        /// </summary>
        public SerializableDate StartDateSerializable => startDate;

        /// <summary>
        /// Gets the end date SerializableDate for direct access.
        /// </summary>
        public SerializableDate EndDateSerializable => endDate;

        /// <summary>
        /// Gets or sets whether this event repeats yearly (only day and month matter, year is ignored).
        /// </summary>
        public bool IsYearly
        {
            get => isYearly;
            set => isYearly = value;
        }

        /// <summary>
        /// Zwraca identyfikator tematu - używa themeId jeśli ustawione, w przeciwnym razie mapuje z nazwy.
        /// </summary>
        public string GetThemeId()
        {
            if (!string.IsNullOrEmpty(themeId))
            {
                return themeId;
            }
            
            // Automatyczne mapowanie z nazwy
            return MapEventNameToTheme(name);
        }

        private static string MapEventNameToTheme(string eventName)
        {
            string lower = eventName.ToLower();
            
            if (lower.Contains("christmas") || lower.Contains("boże") || lower.Contains("xmas") || lower.Contains("świąt"))
                return "christmas";
            if (lower.Contains("easter") || lower.Contains("wielkanoc"))
                return "easter";
            if (lower.Contains("halloween"))
                return "halloween";
            if (lower.Contains("valentine") || lower.Contains("walentynki"))
                return "valentine";
            if (lower.Contains("winter") || lower.Contains("zima"))
                return "winter";
            if (lower.Contains("summer") || lower.Contains("lato"))
                return "summer";
            if (lower.Contains("spring") || lower.Contains("wiosna"))
                return "spring";
            if (lower.Contains("autumn") || lower.Contains("fall") || lower.Contains("jesień"))
                return "autumn";
            
            // Fallback: użyj nazwy jako theme (po konwersji)
            return lower.Replace(" ", "_").Replace("event", "").Trim('_');
        }

        /// <summary>
        /// Sets the date range programmatically.
        /// </summary>
        public void SetDateRange(DateTime start, DateTime end)
        {
            if (end < start)
            {
                end = start;
            }
            startDate.SetDate(start);
            endDate.SetDate(end);
        }

        /// <summary>
        /// Sets the start date programmatically.
        /// </summary>
        public void SetStartDate(DateTime dateTime)
        {
            startDate.SetDate(dateTime);
            EnsureValidRange();
        }

        /// <summary>
        /// Sets the end date programmatically.
        /// </summary>
        public void SetEndDate(DateTime dateTime)
        {
            endDate.SetDate(dateTime);
            EnsureValidRange();
        }

        /// <summary>
        /// Sets the start date programmatically.
        /// </summary>
        public void SetStartDate(int year, int month, int day)
        {
            startDate.Year = year;
            startDate.Month = month;
            startDate.Day = day;
            EnsureValidRange();
        }

        /// <summary>
        /// Sets the end date programmatically.
        /// </summary>
        public void SetEndDate(int year, int month, int day)
        {
            endDate.Year = year;
            endDate.Month = month;
            endDate.Day = day;
            EnsureValidRange();
        }

        /// <summary>
        /// Executes the event with the currently selected date range.
        /// This will invoke all subscribed listeners (typically DateEventBridge components).
        /// </summary>
        public void Execute()
        {
            var start = StartDate;
            var end = EndDate;
            if (end < start)
            {
                end = start;
            }
            OnExecute?.Invoke(start, end);
        }

        /// <summary>
        /// Executes the event with a custom date range (doesn't change the stored dates).
        /// </summary>
        public void ExecuteWithDateRange(DateTime start, DateTime end)
        {
            if (end < start)
            {
                end = start;
            }
            OnExecute?.Invoke(start, end);
        }

        private void OnValidate()
        {
            // Ensure dates are valid when changed in Inspector
            if (startDate != null && endDate != null)
            {
                var start = startDate.ToDateTime();
                var end = endDate.ToDateTime();
                
                startDate.SetDate(start);
                endDate.SetDate(end);
                
                EnsureValidRange();
            }
        }

        private void EnsureValidRange()
        {
            var start = startDate.ToDateTime();
            var end = endDate.ToDateTime();
            
            if (end < start)
            {
                endDate.SetDate(start);
            }
        }
    }
}

