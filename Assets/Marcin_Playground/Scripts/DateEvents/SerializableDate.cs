using System;
using UnityEngine;

namespace TheraplyVR.DateEvents
{
    /// <summary>
    /// Serializable wrapper for DateTime that allows easy editing in Unity Inspector
    /// with a custom property drawer that provides a Windows-style date picker interface.
    /// </summary>
    [Serializable]
    public class SerializableDate
    {
        private const int MinYear = 2025;
        private const int MaxYear = 2222;

        [SerializeField] private int year = DateTime.Today.Year;
        [SerializeField] private int month = DateTime.Today.Month;
        [SerializeField] private int day = DateTime.Today.Day;

        public SerializableDate()
        {
            var today = DateTime.Today;
            year = Mathf.Clamp(today.Year, MinYear, MaxYear);
            month = today.Month;
            day = today.Day;
        }

        public SerializableDate(int year, int month, int day)
        {
            this.year = Mathf.Clamp(year, MinYear, MaxYear);
            this.month = month;
            this.day = day;
        }

        public SerializableDate(DateTime dateTime)
        {
            year = Mathf.Clamp(dateTime.Year, MinYear, MaxYear);
            month = dateTime.Month;
            day = dateTime.Day;
        }

        public DateTime ToDateTime()
        {
            // Ensure valid date
            var maxDay = DateTime.DaysInMonth(year, month);
            var validDay = Mathf.Clamp(day, 1, maxDay);
            var validMonth = Mathf.Clamp(month, 1, 12);
            var validYear = Mathf.Clamp(year, MinYear, MaxYear);
            
            return new DateTime(validYear, validMonth, validDay);
        }

        public void SetDate(DateTime dateTime)
        {
            year = Mathf.Clamp(dateTime.Year, MinYear, MaxYear);
            month = dateTime.Month;
            day = dateTime.Day;
        }

        public int Year
        {
            get => year;
            set => year = Mathf.Clamp(value, MinYear, MaxYear);
        }

        public int Month
        {
            get => month;
            set => month = Mathf.Clamp(value, 1, 12);
        }

        public int Day
        {
            get => day;
            set
            {
                var maxDay = DateTime.DaysInMonth(year, month);
                day = Mathf.Clamp(value, 1, maxDay);
            }
        }

        public static implicit operator DateTime(SerializableDate serializableDate)
        {
            return serializableDate?.ToDateTime() ?? DateTime.Today;
        }

        public static implicit operator SerializableDate(DateTime dateTime)
        {
            return new SerializableDate(dateTime);
        }
    }
}

