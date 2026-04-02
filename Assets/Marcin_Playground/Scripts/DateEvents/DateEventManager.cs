using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace TheraplyVR.DateEvents
{
    /// <summary>
    /// Unity event that exposes information about a date event check result.
    /// </summary>
    [Serializable]
    public class DateEventCheckResult
    {
        public DateScriptableObject dateEvent;
        public bool isActive;
        public int daysUntilStart;
        public int daysUntilEnd;
        public int daysSinceEnd;

        public DateEventCheckResult(DateScriptableObject dateEvent, bool isActive, int daysUntilStart, int daysUntilEnd, int daysSinceEnd)
        {
            this.dateEvent = dateEvent;
            this.isActive = isActive;
            this.daysUntilStart = daysUntilStart;
            this.daysUntilEnd = daysUntilEnd;
            this.daysSinceEnd = daysSinceEnd;
        }
    }

    /// <summary>
    /// Unity event that passes DateEventCheckResult.
    /// </summary>
    [Serializable]
    public class DateEventCheckResultEvent : UnityEvent<DateEventCheckResult> { }

    /// <summary>
    /// Manages a list of DateScriptableObject events. On start, checks which events are currently active
    /// (current date is within the event's date range) and calculates days remaining for upcoming events.
    /// </summary>
    public class DateEventManager : MonoBehaviour
    {
        [Header("Date Events")]
        [SerializeField] private List<DateScriptableObject> dateEvents = new List<DateScriptableObject>();

        [Header("Settings")]
        [SerializeField] private bool checkOnStart = true;
        [SerializeField] private bool checkOnEnable = false;
        [SerializeField] private bool logResults = true;
        [Tooltip("If enabled, automatically executes active events when they are found.")]
        [SerializeField] private bool autoExecuteActiveEvents = true;

        [Header("Events")]
        [Tooltip("Invoked for each active event (current date is within event's date range).")]
        public DateEventCheckResultEvent onActiveEventFound = new DateEventCheckResultEvent();

        [Tooltip("Invoked for each upcoming event (current date is before event's start date).")]
        [SerializeField] private DateEventCheckResultEvent onUpcomingEventFound = new DateEventCheckResultEvent();

        [Tooltip("Invoked for each past event (current date is after event's end date).")]
        [SerializeField] private DateEventCheckResultEvent onPastEventFound = new DateEventCheckResultEvent();

        [Tooltip("Invoked when all events have been checked. Passes list of all results.")]
        [SerializeField] private UnityEvent<List<DateEventCheckResult>> onAllEventsChecked = new UnityEvent<List<DateEventCheckResult>>();

        private List<DateEventCheckResult> _lastCheckResults = new List<DateEventCheckResult>();

        /// <summary>
        /// Gets the list of date events.
        /// </summary>
        public List<DateScriptableObject> DateEvents => dateEvents;

        /// <summary>
        /// Gets the last check results.
        /// </summary>
        public List<DateEventCheckResult> LastCheckResults => _lastCheckResults;

        private void Start()
        {
            if (checkOnStart)
            {
                CheckAllEvents();
            }
        }

        private void OnEnable()
        {
            if (checkOnEnable)
            {
                CheckAllEvents();
            }
        }

        /// <summary>
        /// Checks all date events and categorizes them as active, upcoming, or past.
        /// </summary>
        public void CheckAllEvents()
        {
            _lastCheckResults.Clear();
            var today = DateTime.Today;

            foreach (var dateEvent in dateEvents)
            {
                if (dateEvent == null)
                {
                    continue;
                }

                var startDate = dateEvent.StartDate;
                var endDate = dateEvent.EndDate;
                var result = CheckEvent(dateEvent, today, startDate, endDate);
                _lastCheckResults.Add(result);

                // Invoke appropriate event based on result
                if (result.isActive)
                {
                    onActiveEventFound?.Invoke(result);
                    
                    // Automatically execute the event if enabled
                    if (autoExecuteActiveEvents)
                    {
                        dateEvent.Execute();
                    }
                    
                    if (logResults)
                    {
                        Debug.Log($"[DateEventManager] Active event found: {dateEvent.name} (Active until {endDate:yyyy-MM-dd})");
                    }
                }
                else if (result.daysUntilStart > 0)
                {
                    onUpcomingEventFound?.Invoke(result);
                    if (logResults)
                    {
                        Debug.Log($"[DateEventManager] Upcoming event: {dateEvent.name} - {result.daysUntilStart} days until start ({startDate:yyyy-MM-dd})");
                    }
                }
                else
                {
                    onPastEventFound?.Invoke(result);
                    if (logResults)
                    {
                        Debug.Log($"[DateEventManager] Past event: {dateEvent.name} - ended {result.daysSinceEnd} days ago ({endDate:yyyy-MM-dd})");
                    }
                }
            }

            onAllEventsChecked?.Invoke(_lastCheckResults);
        }

        /// <summary>
        /// Checks a single date event and returns the result.
        /// </summary>
        public DateEventCheckResult CheckEvent(DateScriptableObject dateEvent, DateTime currentDate)
        {
            if (dateEvent == null)
            {
                return null;
            }

            var startDate = dateEvent.StartDate;
            var endDate = dateEvent.EndDate;
            return CheckEvent(dateEvent, currentDate, startDate, endDate);
        }

        private DateEventCheckResult CheckEvent(DateScriptableObject dateEvent, DateTime currentDate, DateTime startDate, DateTime endDate)
        {
            bool isActive;
            int daysUntilStart = 0;
            int daysUntilEnd = 0;
            int daysSinceEnd = 0;

            if (dateEvent.IsYearly)
            {
                // For yearly events, compare only month and day, ignore year
                var currentMonth = currentDate.Month;
                var currentDay = currentDate.Day;
                var startMonth = startDate.Month;
                var startDay = startDate.Day;
                var endMonth = endDate.Month;
                var endDay = endDate.Day;

                // Check if current date is within the yearly range
                // Handle cases where range spans across year boundary (e.g., Dec 20 - Jan 5)
                if (startMonth < endMonth || (startMonth == endMonth && startDay <= endDay))
                {
                    // Normal range (e.g., Jan 1 - Dec 31)
                    isActive = (currentMonth > startMonth || (currentMonth == startMonth && currentDay >= startDay)) &&
                               (currentMonth < endMonth || (currentMonth == endMonth && currentDay <= endDay));
                }
                else
                {
                    // Range spans across year boundary (e.g., Dec 20 - Jan 5)
                    isActive = (currentMonth > startMonth || (currentMonth == startMonth && currentDay >= startDay)) ||
                               (currentMonth < endMonth || (currentMonth == endMonth && currentDay <= endDay));
                }

                if (isActive)
                {
                    // Calculate days until end for current year
                    var endDateThisYear = new DateTime(currentDate.Year, endMonth, endDay);
                    if (endDateThisYear < currentDate)
                    {
                        // End date already passed this year, use next year
                        endDateThisYear = new DateTime(currentDate.Year + 1, endMonth, endDay);
                    }
                    daysUntilEnd = (endDateThisYear - currentDate).Days;
                }
                else
                {
                    // Calculate days until start
                    var startDateThisYear = new DateTime(currentDate.Year, startMonth, startDay);
                    if (startDateThisYear < currentDate)
                    {
                        // Start date already passed this year, use next year
                        startDateThisYear = new DateTime(currentDate.Year + 1, startMonth, startDay);
                    }
                    daysUntilStart = (startDateThisYear - currentDate).Days;

                    // If we're past the end date this year, calculate days since end
                    var endDateThisYear = new DateTime(currentDate.Year, endMonth, endDay);
                    if (currentDate > endDateThisYear)
                    {
                        daysSinceEnd = (currentDate - endDateThisYear).Days;
                    }
                }
            }
            else
            {
                // Regular event - compare full dates
                isActive = currentDate >= startDate && currentDate <= endDate;

                if (isActive)
                {
                    // Event is currently active
                    daysUntilEnd = (endDate - currentDate).Days;
                }
                else if (currentDate < startDate)
                {
                    // Event hasn't started yet
                    daysUntilStart = (startDate - currentDate).Days;
                }
                else
                {
                    // Event has ended
                    daysSinceEnd = (currentDate - endDate).Days;
                }
            }

            return new DateEventCheckResult(dateEvent, isActive, daysUntilStart, daysUntilEnd, daysSinceEnd);
        }

        /// <summary>
        /// Gets all currently active events.
        /// </summary>
        public List<DateEventCheckResult> GetActiveEvents()
        {
            return _lastCheckResults.Where(r => r.isActive).ToList();
        }

        /// <summary>
        /// Gets all upcoming events sorted by days until start (ascending).
        /// </summary>
        public List<DateEventCheckResult> GetUpcomingEvents()
        {
            return _lastCheckResults
                .Where(r => r.daysUntilStart > 0)
                .OrderBy(r => r.daysUntilStart)
                .ToList();
        }

        /// <summary>
        /// Gets all past events sorted by days since end (ascending - most recent first).
        /// </summary>
        public List<DateEventCheckResult> GetPastEvents()
        {
            return _lastCheckResults
                .Where(r => r.daysSinceEnd > 0)
                .OrderBy(r => r.daysSinceEnd)
                .ToList();
        }

        /// <summary>
        /// Gets the next upcoming event (closest to current date).
        /// </summary>
        public DateEventCheckResult GetNextUpcomingEvent()
        {
            var upcoming = GetUpcomingEvents();
            return upcoming.FirstOrDefault();
        }

        /// <summary>
        /// Gets the currently active event (if any). Returns first active event if multiple.
        /// </summary>
        public DateEventCheckResult GetActiveEvent()
        {
            var active = GetActiveEvents();
            return active.FirstOrDefault();
        }

        /// <summary>
        /// Adds a date event to the list.
        /// </summary>
        public void AddDateEvent(DateScriptableObject dateEvent)
        {
            if (dateEvent != null && !dateEvents.Contains(dateEvent))
            {
                dateEvents.Add(dateEvent);
            }
        }

        /// <summary>
        /// Removes a date event from the list.
        /// </summary>
        public void RemoveDateEvent(DateScriptableObject dateEvent)
        {
            dateEvents.Remove(dateEvent);
        }

        /// <summary>
        /// Clears all date events from the list.
        /// </summary>
        public void ClearDateEvents()
        {
            dateEvents.Clear();
        }
    }
}

