using System;
using UnityEngine;
using UnityEngine.Events;

namespace TheraplyVR.DateEvents
{
    /// <summary>
    /// Bridge component that allows DateScriptableObject to call methods on scene objects.
    /// Place this component on a GameObject in the scene and assign scene objects to its events.
    /// </summary>
    public class DateEventBridge : MonoBehaviour
    {
        [Header("Date Event")]
        [Tooltip("The DateScriptableObject that will trigger this bridge's events.")]
        [SerializeField] private DateScriptableObject dateEvent;

        [Header("Scene Events")]
        [Tooltip("This event will be invoked when the date event executes. You can attach scene objects here.")]
        [SerializeField] private DateRangeEvent onDateEventExecute = new DateRangeEvent();

        private void OnEnable()
        {
            if (dateEvent != null)
            {
                dateEvent.OnExecute += HandleDateEvent;
            }
        }

        private void OnDisable()
        {
            if (dateEvent != null)
            {
                dateEvent.OnExecute -= HandleDateEvent;
            }
        }

        private void HandleDateEvent(DateTime start, DateTime end)
        {
            onDateEventExecute?.Invoke(start, end);
        }

        /// <summary>
        /// Sets the date event to listen to.
        /// </summary>
        public void SetDateEvent(DateScriptableObject dateEvent)
        {
            if (this.dateEvent != null)
            {
                this.dateEvent.OnExecute -= HandleDateEvent;
            }

            this.dateEvent = dateEvent;

            if (this.dateEvent != null)
            {
                this.dateEvent.OnExecute += HandleDateEvent;
            }
        }

        /// <summary>
        /// Gets the currently assigned date event.
        /// </summary>
        public DateScriptableObject GetDateEvent()
        {
            return dateEvent;
        }
    }
}

