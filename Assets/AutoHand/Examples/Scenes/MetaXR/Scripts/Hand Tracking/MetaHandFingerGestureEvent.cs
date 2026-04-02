using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Autohand {
    public class MetaHandFingerGestureEvent : MonoBehaviour {
        public MetaHandFingerGestureTracker MetaHandFingerGestureTracker;
        public FingerEnum finger1;
        public FingerEnum[] finger2;

        public UnityEvent<MetaHandFingerGestureTracker, FingerEnum, FingerEnum> OnFingerTouchStartEvent;
        public UnityEvent<MetaHandFingerGestureTracker, FingerEnum, FingerEnum> OnFingerTouchStopEvent;

            void OnEnable() {
            MetaHandFingerGestureTracker.OnFingerTouchStart += OnFingerTouchStart;
            MetaHandFingerGestureTracker.OnFingerTouchStop += OnFingerTouchStop;
        }

        void OnDisable() {
            MetaHandFingerGestureTracker.OnFingerTouchStart -= OnFingerTouchStart;
            MetaHandFingerGestureTracker.OnFingerTouchStop -= OnFingerTouchStop;
        }

        void OnFingerTouchStart(MetaXRAutoHandTracking hand, MetaHandFingerGestureTracker gestureTracker, FingerTouchEventArgs e) {
            if (e.finger1 == finger1 && finger2.Contains(e.finger2)) {
                OnFingerTouchStartEvent?.Invoke(gestureTracker, e.finger1, e.finger2);
            }
        }

        void OnFingerTouchStop(MetaXRAutoHandTracking hand, MetaHandFingerGestureTracker gestureTracker, FingerTouchEventArgs e) {
            if (e.finger1 == finger1 && finger2.Contains(e.finger2)) {
                OnFingerTouchStopEvent?.Invoke(MetaHandFingerGestureTracker, e.finger1, e.finger2);
            }
        }
    }
}