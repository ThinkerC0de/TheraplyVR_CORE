using UnityEditor;
using UnityEngine;
using TheraplyVR.DateEvents;

namespace TheraplyVR.DateEvents.Editor
{
    /// <summary>
    /// Custom property drawer for SerializableDate that provides a Windows-style date picker
    /// with dropdowns for day, month, and year selection in the Unity Inspector.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializableDate))]
    public class DatePropertyDrawer : PropertyDrawer
    {
        private const float LineHeight = 18f;
        private const float Spacing = 2f;
        private const int MinYear = 2025;
        private const int MaxYear = 2222;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var yearProp = property.FindPropertyRelative("year");
            var monthProp = property.FindPropertyRelative("month");
            var dayProp = property.FindPropertyRelative("day");

            if (yearProp == null || monthProp == null || dayProp == null)
            {
                EditorGUI.LabelField(position, label.text, "Invalid SerializableDate");
                EditorGUI.EndProperty();
                return;
            }

            // Get current values
            var year = yearProp.intValue;
            var month = monthProp.intValue;
            var day = dayProp.intValue;

            // Validate and clamp values
            year = Mathf.Clamp(year, MinYear, MaxYear);
            month = Mathf.Clamp(month, 1, 12);
            var maxDay = System.DateTime.DaysInMonth(year, month);
            day = Mathf.Clamp(day, 1, maxDay);

            // Draw label
            var labelRect = new Rect(position.x, position.y, position.width, LineHeight);
            EditorGUI.LabelField(labelRect, label);

            // Calculate positions for dropdowns
            var dropdownWidth = (position.width - Spacing * 2) / 3f;
            var startX = position.x;
            var yPos = position.y + LineHeight + Spacing;

            // Day dropdown
            var dayRect = new Rect(startX, yPos, dropdownWidth, LineHeight);
            var dayOptions = new string[maxDay];
            for (int i = 0; i < maxDay; i++)
            {
                dayOptions[i] = (i + 1).ToString("00");
            }
            var newDay = EditorGUI.Popup(dayRect, day - 1, dayOptions) + 1;

            // Month dropdown
            var monthRect = new Rect(startX + dropdownWidth + Spacing, yPos, dropdownWidth, LineHeight);
            var monthNames = new string[12];
            for (int i = 0; i < 12; i++)
            {
                var monthName = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i + 1);
                monthNames[i] = $"{i + 1:00} - {monthName}";
            }
            var newMonth = EditorGUI.Popup(monthRect, month - 1, monthNames) + 1;

            // Year dropdown
            var yearRect = new Rect(startX + (dropdownWidth + Spacing) * 2, yPos, dropdownWidth, LineHeight);
            var yearCount = MaxYear - MinYear + 1;
            var yearOptions = new string[yearCount];
            for (int i = 0; i < yearCount; i++)
            {
                yearOptions[i] = (MinYear + i).ToString();
            }
            var selectedYearIndex = Mathf.Clamp(year - MinYear, 0, yearCount - 1);
            var newYearIndex = EditorGUI.Popup(yearRect, selectedYearIndex, yearOptions);
            var newYear = MinYear + newYearIndex;

            // Update values if changed
            if (newYear != year || newMonth != month || newDay != day)
            {
                yearProp.intValue = newYear;
                monthProp.intValue = newMonth;
                
                // Recalculate max day for new month/year
                var newMaxDay = System.DateTime.DaysInMonth(newYear, newMonth);
                dayProp.intValue = Mathf.Clamp(newDay, 1, newMaxDay);
            }
            else
            {
                // Ensure day is still valid after month/year changes
                var currentMaxDay = System.DateTime.DaysInMonth(year, month);
                if (day > currentMaxDay)
                {
                    dayProp.intValue = currentMaxDay;
                }
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return LineHeight * 2 + Spacing;
        }
    }
}

