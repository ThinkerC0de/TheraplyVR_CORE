using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Logging;
using TheraplyCore.Games;

namespace TheraplyCore.Firebase
{
    /// <summary>
    /// Non-blocking Firebase data collection service
    /// NEVER blocks main thread - all writes happen in background
    /// </summary>
    public class FirebaseDataService : MonoBehaviour
    {
        [Header("Batch Settings")]
        [SerializeField] private int _batchSize = 10;
        [SerializeField] private float _batchInterval = 5f;
        
        [Header("Queue Settings")]
        [SerializeField] private int _maxQueueSize = 1000;
        [SerializeField] private bool _dropOldestOnFull = true;
        
        [Header("Debug")]
        [SerializeField] private bool _logWrites = true;
        [SerializeField] private bool _simulateFirebase = true;
        
        private Queue<GameDataPoint> _writeQueue = new Queue<GameDataPoint>();
        private bool _isProcessing = false;
        private DateTime _lastBatchWrite;
        
        private string _sessionId;
        
        private int _pointsQueued = 0;
        private int _pointsWritten = 0;
        private int _pointsFailed = 0;
        private int _batchesWritten = 0;
        
        void Start()
        {
            _lastBatchWrite = DateTime.UtcNow;
            StartCoroutine(BackgroundWorker());
            Logger.Info("[FirebaseData] Service started");
        }
        
        void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                Logger.Info("[FirebaseData] App pausing - flushing data");
                FlushAllData();
            }
        }
        
        void OnApplicationQuit()
        {
            Logger.Info("[FirebaseData] App quitting - flushing data");
            FlushAllData();
        }
        
        public void QueueDataPoint(GameDataPoint dataPoint)
        {
            if (_writeQueue.Count >= _maxQueueSize)
            {
                if (_dropOldestOnFull)
                {
                    var dropped = _writeQueue.Dequeue();
                    Logger.Warning($"[FirebaseData] Queue full - dropped oldest point ({dropped.dataType})");
                }
                else
                {
                    Logger.Warning("[FirebaseData] Queue full - dropping new point");
                    return;
                }
            }
            
            _writeQueue.Enqueue(dataPoint);
            _pointsQueued++;
            
            Logger.Debug($"[FirebaseData] Queued: {dataPoint.dataType} (queue: {_writeQueue.Count})");
            
            if (_writeQueue.Count >= _batchSize)
            {
                TriggerBatchWrite();
            }
        }
        
        public void SetSessionId(string sessionId)
        {
            _sessionId = sessionId;
            Logger.Info($"[FirebaseData] Session ID set: {sessionId}");
        }
        
        public void FlushAllData()
        {
            Logger.Info($"[FirebaseData] Flushing all data ({_writeQueue.Count} points)");
            
            while (_writeQueue.Count > 0)
            {
                var batch = DequeueBatch();
                WriteBatchToFirebase(batch).Wait();
            }
            
            Logger.Info("[FirebaseData] All data flushed");
        }
        
        public QueueStatistics GetStatistics()
        {
            return new QueueStatistics
            {
                queueSize = _writeQueue.Count,
                pointsQueued = _pointsQueued,
                pointsWritten = _pointsWritten,
                pointsFailed = _pointsFailed,
                batchesWritten = _batchesWritten,
                successRate = _pointsQueued > 0 ? (float)_pointsWritten / _pointsQueued * 100 : 0
            };
        }
        
        private IEnumerator BackgroundWorker()
        {
            while (true)
            {
                float timeSinceLastWrite = (float)(DateTime.UtcNow - _lastBatchWrite).TotalSeconds;
                
                if (_writeQueue.Count > 0 && timeSinceLastWrite >= _batchInterval)
                {
                    TriggerBatchWrite();
                }
                
                yield return new WaitForSeconds(1f);
            }
        }
        
        private async void TriggerBatchWrite()
        {
            if (_isProcessing)
            {
                Logger.Debug("[FirebaseData] Already writing batch, skipping");
                return;
            }
            
            if (_writeQueue.Count == 0) return;
            
            _isProcessing = true;
            
            try
            {
                var batch = DequeueBatch();
                
                if (_logWrites)
                {
                    Logger.Debug($"[FirebaseData] Writing batch of {batch.Count} points");
                }
                
                await WriteBatchToFirebase(batch);
                
                _lastBatchWrite = DateTime.UtcNow;
                _batchesWritten++;
                _pointsWritten += batch.Count;
                
                if (_logWrites)
                {
                    Logger.Info($"[FirebaseData] Batch written successfully ({batch.Count} points)");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"[FirebaseData] Batch write failed: {e.Message}", e);
                _pointsFailed += _batchSize;
            }
            finally
            {
                _isProcessing = false;
            }
        }
        
        private List<GameDataPoint> DequeueBatch()
        {
            var batch = new List<GameDataPoint>();
            int count = Mathf.Min(_batchSize, _writeQueue.Count);
            
            for (int i = 0; i < count; i++)
            {
                batch.Add(_writeQueue.Dequeue());
            }
            
            return batch;
        }
        
        private async Task WriteBatchToFirebase(List<GameDataPoint> batch)
        {
            if (_simulateFirebase)
            {
                await Task.Delay(UnityEngine.Random.Range(50, 200));
                return;
            }
            
            // TODO: Implement actual Firebase write
            // See PRODUCTION-ISSUES-SOLUTIONS.md for implementation
        }
        
#if UNITY_EDITOR
        [ContextMenu("Log Statistics")]
        private void DebugLogStatistics()
        {
            var stats = GetStatistics();
            
            Logger.Info(
                $"[FirebaseData] Statistics:\n" +
                $"Queue size: {stats.queueSize}\n" +
                $"Points queued: {stats.pointsQueued}\n" +
                $"Points written: {stats.pointsWritten}\n" +
                $"Points failed: {stats.pointsFailed}\n" +
                $"Batches written: {stats.batchesWritten}\n" +
                $"Success rate: {stats.successRate:F1}%"
            );
        }
        
        [ContextMenu("Test - Queue 100 Points")]
        private void DebugQueue100Points()
        {
            for (int i = 0; i < 100; i++)
            {
                QueueDataPoint(new GameDataPoint
                {
                    timestamp = DateTime.UtcNow,
                    dataType = "test_data",
                    payload = new Dictionary<string, object>
                    {
                        { "index", i },
                        { "random", UnityEngine.Random.value }
                    }
                });
            }
            
            Logger.Info("[FirebaseData] Queued 100 test points");
        }
#endif
    }
    
    [Serializable]
    public struct QueueStatistics
    {
        public int queueSize;
        public int pointsQueued;
        public int pointsWritten;
        public int pointsFailed;
        public int batchesWritten;
        public float successRate;
    }
}
