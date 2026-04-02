using UnityEditor;
using UnityEngine;
using TheraplyVR.DateEvents;

namespace TheraplyVR.DateEvents.Editor
{
    /// <summary>
    /// Custom editor for DateScriptableObject that disables year dropdowns when isYearly is true.
    /// </summary>
    [CustomEditor(typeof(DateScriptableObject))]
    public class DateScriptableObjectEditor : UnityEditor.Editor
    {
        private SerializedProperty isYearlyProp;
        private SerializedProperty startDateProp;
        private SerializedProperty endDateProp;

        private void OnEnable()
        {
            isYearlyProp = serializedObject.FindProperty("isYearly");
            startDateProp = serializedObject.FindProperty("startDate");
            endDateProp = serializedObject.FindProperty("endDate");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(isYearlyProp);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Event Date", EditorStyles.boldLabel);

            // Store original GUI enabled state
            var originalEnabled = GUI.enabled;

            // Disable year fields if isYearly is true
            var isYearly = isYearlyProp.boolValue;
            if (isYearly)
            {
                EditorGUILayout.HelpBox("Yearly event: Only day and month are used. Event will repeat every year.", MessageType.Info);
            }

            // Draw start date
            EditorGUILayout.LabelField("Start Date");
            EditorGUI.indentLevel++;
            DrawDateField(startDateProp, isYearly);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // Draw end date
            EditorGUILayout.LabelField("End Date");
            EditorGUI.indentLevel++;
            DrawDateField(endDateProp, isYearly);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("To connect this event to scene objects, use DateEventBridge component on a GameObject in the scene.", MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDateField(SerializedProperty dateProp, bool disableYear)
        {
            var yearProp = dateProp.FindPropertyRelative("year");
            var monthProp = dateProp.FindPropertyRelative("month");
            var dayProp = dateProp.FindPropertyRelative("day");

            if (yearProp == null || monthProp == null || dayProp == null)
            {
                EditorGUILayout.PropertyField(dateProp);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            // Day
            var dayValue = dayProp.intValue;
            var maxDay = System.DateTime.DaysInMonth(yearProp.intValue, monthProp.intValue);
            dayValue = Mathf.Clamp(dayValue, 1, maxDay);
            var dayOptions = new string[maxDay];
            for (int i = 0; i < maxDay; i++)
            {
                dayOptions[i] = (i + 1).ToString("00");
            }
            var newDayIndex = EditorGUILayout.Popup(dayValue - 1, dayOptions, GUILayout.Width(60));
            dayProp.intValue = newDayIndex + 1;

            // Month
            var monthValue = monthProp.intValue;
            var monthNames = new string[12];
            for (int i = 0; i < 12; i++)
            {
                var monthName = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i + 1);
                monthNames[i] = $"{i + 1:00} - {monthName}";
            }
            var newMonthIndex = EditorGUILayout.Popup(monthValue - 1, monthNames, GUILayout.Width(150));
            monthProp.intValue = newMonthIndex + 1;

            // Year - disabled if yearly
            GUI.enabled = !disableYear;
            var yearValue = yearProp.intValue;
            const int MinYear = 2025;
            const int MaxYear = 2222;
            var yearCount = MaxYear - MinYear + 1;
            var yearOptions = new string[yearCount];
            for (int i = 0; i < yearCount; i++)
            {
                yearOptions[i] = (MinYear + i).ToString();
            }
            var selectedYearIndex = Mathf.Clamp(yearValue - MinYear, 0, yearCount - 1);
            var newYearIndex = EditorGUILayout.Popup(selectedYearIndex, yearOptions, GUILayout.Width(80));
            if (!disableYear)
            {
                yearProp.intValue = MinYear + newYearIndex;
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // Update day if month/year changed
            var newMaxDay = System.DateTime.DaysInMonth(yearProp.intValue, monthProp.intValue);
            if (dayProp.intValue > newMaxDay)
            {
                dayProp.intValue = newMaxDay;
            }
        }
    }
}

