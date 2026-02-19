using System;
using System.Collections.Generic;

namespace TheraplyCore.Games
{
    /// <summary>
    /// Single data point collected during gameplay.
    /// Used for telemetry and ML analysis.
    /// Bridged from ITelemetryService.Track() to FirebaseDataService.QueueDataPoint().
    /// </summary>
    [Serializable]
    public class GameDataPoint
    {
        public DateTime timestamp;
        public string dataType;
        public Dictionary<string, object> payload;
    }
}
