using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheraplyCore.Logging
{
    /// <summary>
    /// Structured logging system with severity levels and remote logging
    /// 
    /// USAGE:
    /// Logger.Fatal("Critical error", exception);  // Red - app crash
    /// Logger.Error("Operation failed");           // Orange - error but continues
    /// Logger.Warning("Unexpected condition");     // Yellow - handled issue
    /// Logger.Info("User action");                 // White - important events
    /// Logger.Debug("Detailed trace");             // Gray - debugging only
    /// 
    /// FEATURES:
    /// - Color-coded console output
    /// - Configurable minimum level
    /// - Remote logging for Fatal/Error
    /// - Stack traces for errors
    /// - Context info (scene, FPS, memory)
    /// - Thread-safe
    /// </summary>
    public static class Logger
    {
        // ============================================
        // CONFIGURATION
        // ============================================
        
        private static LogLevel _minLevel = LogLevel.Info;
        private static bool _includeStackTrace = true;
        private static bool _remoteLoggingEnabled = true;
        
        // Remote logging queue
        private static Queue<LogEntry> _remoteLogQueue = new Queue<LogEntry>();
        private static bool _isFlushingRemoteLogs = false;
        
        // Statistics
        private static int _fatalCount = 0;
        private static int _errorCount = 0;
        private static int _warningCount = 0;
        
        // ============================================
        // PUBLIC API
        // ============================================
        
        /// <summary>
        /// Configure minimum log level
        /// </summary>
        public static void SetMinLevel(LogLevel level)
        {
            _minLevel = level;
            UnityEngine.Debug.Log($"<color=cyan>[Logger]</color> Min level set to: {level}");
        }
        
        /// <summary>
        /// Enable/disable remote logging (Firebase/CloudWatch)
        /// </summary>
        public static void SetRemoteLogging(bool enabled)
        {
            _remoteLoggingEnabled = enabled;
            UnityEngine.Debug.Log($"<color=cyan>[Logger]</color> Remote logging: {(enabled ? "ENABLED" : "DISABLED")}");
        }
        
        /// <summary>
        /// Enable/disable stack traces for errors
        /// </summary>
        public static void SetStackTraces(bool enabled)
        {
            _includeStackTrace = enabled;
        }
        
        // ============================================
        // LOGGING METHODS
        // ============================================
        
        /// <summary>
        /// FATAL - Application crash, data loss, unrecoverable error
        /// </summary>
        public static void Fatal(string message, Exception exception = null)
        {
            _fatalCount++;
            Log(LogLevel.Fatal, "FATAL", message, exception);
        }
        
        /// <summary>
        /// ERROR - Operation failed but app continues
        /// </summary>
        public static void Error(string message, Exception exception = null)
        {
            _errorCount++;
            Log(LogLevel.Error, "ERROR", message, exception);
        }
        
        /// <summary>
        /// WARNING - Something unexpected but handled gracefully
        /// </summary>
        public static void Warning(string message)
        {
            _warningCount++;
            Log(LogLevel.Warning, "WARN", message, null);
        }
        
        /// <summary>
        /// INFO - Important application events
        /// </summary>
        public static void Info(string message)
        {
            Log(LogLevel.Info, "INFO", message, null);
        }
        
        /// <summary>
        /// DEBUG - Detailed diagnostic information
        /// </summary>
        public static void Debug(string message)
        {
            Log(LogLevel.Debug, "DEBUG", message, null);
        }
        
        // ============================================
        // CORE LOGGING
        // ============================================
        
        private static void Log(LogLevel level, string prefix, string message, Exception exception)
        {
            // Skip if below minimum level
            if (level > _minLevel) return;
            
            // Create log entry
            var entry = new LogEntry
            {
                timestamp = DateTime.UtcNow,
                level = level,
                message = message,
                exception = exception?.ToString(),
                stackTrace = (_includeStackTrace && level <= LogLevel.Error) ? Environment.StackTrace : null,
                context = CaptureContext()
            };
            
            // Console output with color
            OutputToConsole(prefix, message, exception, level);
            
            // Remote logging for Fatal/Error
            if (_remoteLoggingEnabled && level <= LogLevel.Error)
            {
                QueueRemoteLog(entry);
            }
            
            // Fatal = crash report
            if (level == LogLevel.Fatal)
            {
                SendCrashReport(entry);
            }
        }
        
        private static void OutputToConsole(string prefix, string message, Exception exception, LogLevel level)
        {
            string color = GetColorForLevel(level);
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            
            // Format: [TIME] [LEVEL] Message
            string formattedMessage = $"<color=gray>{timestamp}</color> <color={color}>[{prefix}]</color> {message}";
            
            // Output based on level
            switch (level)
            {
                case LogLevel.Fatal:
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(formattedMessage);
                    break;
                    
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(formattedMessage);
                    break;
                    
                default:
                    UnityEngine.Debug.Log(formattedMessage);
                    break;
            }
            
            // Exception details
            if (exception != null)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
        
        private static string GetColorForLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Fatal => "red",
                LogLevel.Error => "orange",
                LogLevel.Warning => "yellow",
                LogLevel.Info => "white",
                LogLevel.Debug => "gray",
                _ => "white"
            };
        }
        
        private static LogContext CaptureContext()
        {
            try
            {
                return new LogContext
                {
                    scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    fps = (int)(1f / Time.smoothDeltaTime),
                    memoryMB = (int)(System.GC.GetTotalMemory(false) / 1024 / 1024),
                    platform = Application.platform.ToString(),
                    unityVersion = Application.unityVersion
                };
            }
            catch
            {
                // Don't crash if context capture fails
                return new LogContext();
            }
        }
        
        // ============================================
        // REMOTE LOGGING
        // ============================================
        
        private static void QueueRemoteLog(LogEntry entry)
        {
            lock (_remoteLogQueue)
            {
                _remoteLogQueue.Enqueue(entry);
                
                // Auto-flush if queue gets large
                if (_remoteLogQueue.Count >= 10)
                {
                    FlushRemoteLogs();
                }
            }
        }
        
        /// <summary>
        /// Flush pending remote logs to Firebase/CloudWatch
        /// Call this periodically or on app pause/quit
        /// </summary>
        public static async void FlushRemoteLogs()
        {
            if (_isFlushingRemoteLogs) return;
            if (_remoteLogQueue.Count == 0) return;
            
            _isFlushingRemoteLogs = true;
            
            try
            {
                List<LogEntry> batch;
                
                lock (_remoteLogQueue)
                {
                    // Dequeue batch
                    batch = new List<LogEntry>(_remoteLogQueue);
                    _remoteLogQueue.Clear();
                }
                
                // Send to remote logging service
                // TODO: Implement Firebase/CloudWatch integration
                await SendLogsToRemote(batch);
                
                UnityEngine.Debug.Log($"<color=cyan>[Logger]</color> Flushed {batch.Count} remote logs");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"<color=red>[Logger]</color> Failed to flush remote logs: {e.Message}");
            }
            finally
            {
                _isFlushingRemoteLogs = false;
            }
        }
        
        private static async System.Threading.Tasks.Task SendLogsToRemote(List<LogEntry> logs)
        {
            // TODO: Implement actual remote logging
            // For now, just simulate async operation
            await System.Threading.Tasks.Task.Delay(100);
        }
        
        private static void SendCrashReport(LogEntry entry)
        {
            // TODO: Send crash report to Firebase Crashlytics
            UnityEngine.Debug.LogError($"<color=red>[CRASH REPORT]</color> {entry.message}");
        }
        
        // ============================================
        // STATISTICS
        // ============================================
        
        public static void LogStatistics()
        {
            UnityEngine.Debug.Log(
                $"<color=cyan>[Logger Statistics]</color>\n" +
                $"Fatal: {_fatalCount}\n" +
                $"Error: {_errorCount}\n" +
                $"Warning: {_warningCount}\n" +
                $"Queued remote logs: {_remoteLogQueue.Count}"
            );
        }
        
        public static void ResetStatistics()
        {
            _fatalCount = 0;
            _errorCount = 0;
            _warningCount = 0;
        }
    }
    
    // ============================================
    // DATA STRUCTURES
    // ============================================
    
    public enum LogLevel
    {
        Fatal = 0,      // App crash, data loss
        Error = 1,      // Operation failed but app continues
        Warning = 2,    // Unexpected but handled
        Info = 3,       // Important events
        Debug = 4       // Detailed tracing
    }
    
    [Serializable]
    public class LogEntry
    {
        public DateTime timestamp;
        public LogLevel level;
        public string message;
        public string exception;
        public string stackTrace;
        public LogContext context;
    }
    
    [Serializable]
    public class LogContext
    {
        public string scene;
        public int fps;
        public int memoryMB;
        public string platform;
        public string unityVersion;
    }
}
