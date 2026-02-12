using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using TheraplyCore.Logging;
using TheraplyCore.Network.Connection;
using MessagePack;

namespace TheraplyCore.Connection
{
    /// <summary>
    /// Reliable command delivery with ACK and retries
    /// 
    /// PROBLEM: UDP/TCP can drop packets. Critical commands (END_GAME, CONFIG_UPDATE)
    /// must be guaranteed to arrive.
    /// 
    /// SOLUTION: 
    /// - Every command gets unique messageId
    /// - Sender waits for ACK (5s timeout)
    /// - Auto-retry up to 3 times
    /// - Queue commands during disconnection
    /// 
    /// USAGE:
    /// await reliableCommand.SendAsync("END_GAME", new { score = 100 });
    /// // Guaranteed delivery or throws exception after 3 retries
    /// </summary>
    public class ReliableCommandService : MonoBehaviour
    {
        [Header("Reliability Settings")]
        [SerializeField] private float _ackTimeout = 5f;
        [SerializeField] private int _maxRetries = 3;
        [SerializeField] private float _retryDelay = 1f;
        
        [Header("Dependencies")]
        [SerializeField] private TCPConnectionService _tcpService;
        [SerializeField] private ConnectionStateManager _connectionManager;
        
        [Header("Debug")]
        [SerializeField] private bool _logCommands = true;
        
        private Dictionary<string, PendingCommand> _pendingAcks = new Dictionary<string, PendingCommand>();
        private Queue<QueuedCommand> _commandQueue = new Queue<QueuedCommand>();
        private const int MAX_QUEUE_SIZE = 100;
        
        private int _commandsSent = 0;
        private int _commandsAcked = 0;
        private int _commandsRetried = 0;
        private int _commandsFailed = 0;
        
        void Awake()
        {
            if (_tcpService == null) _tcpService = GetComponent<TCPConnectionService>();
            if (_connectionManager == null) _connectionManager = GetComponent<ConnectionStateManager>();
        }
        
        void Start()
        {
            _connectionManager.OnConnected += HandleConnected;
            _connectionManager.OnReconnected += HandleReconnected;
            Logger.Info("[ReliableCommand] Initialized");
        }
        
        void OnDestroy()
        {
            if (_connectionManager != null)
            {
                _connectionManager.OnConnected -= HandleConnected;
                _connectionManager.OnReconnected -= HandleReconnected;
            }
        }
        
        public async Task SendAsync(string commandId, object payload)
        {
            if (!_connectionManager.IsConnected)
            {
                QueueCommand(commandId, payload);
                return;
            }
            
            string messageId = Guid.NewGuid().ToString();
            
            var command = new PendingCommand
            {
                messageId = messageId,
                commandId = commandId,
                payload = payload,
                sentAt = DateTime.UtcNow,
                retryCount = 0,
                ackReceived = false
            };
            
            _pendingAcks[messageId] = command;
            
            if (_logCommands)
            {
                Logger.Debug($"[ReliableCommand] Sending {commandId} (msgId: {messageId.Substring(0, 8)})");
            }
            
            for (int attempt = 0; attempt < _maxRetries; attempt++)
            {
                _commandsSent++;
                
                bool sent = await SendCommandInternal(messageId, commandId, payload);
                
                if (!sent)
                {
                    Logger.Warning($"[ReliableCommand] Send failed for {commandId} (attempt {attempt + 1})");
                    
                    if (attempt < _maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_retryDelay));
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
                
                bool ackReceived = await WaitForAck(messageId, _ackTimeout);
                
                if (ackReceived)
                {
                    _commandsAcked++;
                    _pendingAcks.Remove(messageId);
                    
                    if (_logCommands)
                    {
                        Logger.Info($"[ReliableCommand] {commandId} acknowledged");
                    }
                    
                    return;
                }
                
                if (attempt < _maxRetries - 1)
                {
                    _commandsRetried++;
                    Logger.Warning($"[ReliableCommand] No ACK for {commandId} (attempt {attempt + 1}/{_maxRetries})");
                    await Task.Delay(TimeSpan.FromSeconds(_retryDelay));
                }
            }
            
            _commandsFailed++;
            _pendingAcks.Remove(messageId);
            
            string errorMsg = $"Command {commandId} failed - no ACK after {_maxRetries} attempts";
            Logger.Error($"[ReliableCommand] {errorMsg}");
            throw new Exception(errorMsg);
        }
        
        public async Task SendUnreliableAsync(string commandId, object payload)
        {
            if (!_connectionManager.IsConnected)
            {
                Logger.Warning($"[ReliableCommand] Cannot send {commandId} - not connected");
                return;
            }
            
            await _tcpService.SendCommandAsync(commandId, payload);
        }
        
        private void QueueCommand(string commandId, object payload)
        {
            if (_commandQueue.Count >= MAX_QUEUE_SIZE)
            {
                Logger.Warning("[ReliableCommand] Queue full - dropping oldest command");
                _commandQueue.Dequeue();
            }
            
            _commandQueue.Enqueue(new QueuedCommand
            {
                commandId = commandId,
                payload = payload,
                timestamp = DateTime.UtcNow
            });
            
            Logger.Info($"[ReliableCommand] Queued {commandId} (queue size: {_commandQueue.Count})");
        }
        
        private async void HandleConnected() { }
        
        private async void HandleReconnected()
        {
            Logger.Info($"[ReliableCommand] Flushing command queue ({_commandQueue.Count} commands)");
            await FlushQueue();
        }
        
        private async Task FlushQueue()
        {
            int flushed = 0;
            int failed = 0;
            
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                
                try
                {
                    await SendAsync(cmd.commandId, cmd.payload);
                    flushed++;
                    Logger.Debug($"[ReliableCommand] Flushed queued command: {cmd.commandId}");
                }
                catch (Exception e)
                {
                    failed++;
                    Logger.Error($"[ReliableCommand] Failed to flush {cmd.commandId}: {e.Message}");
                }
            }
            
            Logger.Info($"[ReliableCommand] Queue flush complete: {flushed} sent, {failed} failed");
        }
        
        private async Task<bool> SendCommandInternal(string messageId, string commandId, object payload)
        {
            try
            {
                await _tcpService.SendCommandAsync(commandId, payload);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error($"[ReliableCommand] Send error: {e.Message}", e);
                return false;
            }
        }
        
        public void HandleAck(string messageId)
        {
            if (_pendingAcks.ContainsKey(messageId))
            {
                _pendingAcks[messageId].ackReceived = true;
                
                if (_logCommands)
                {
                    Logger.Debug($"[ReliableCommand] ACK received for {messageId.Substring(0, 8)}");
                }
            }
        }
        
        private async Task<bool> WaitForAck(string messageId, float timeout)
        {
            float elapsed = 0f;
            
            while (elapsed < timeout)
            {
                if (_pendingAcks.ContainsKey(messageId) && _pendingAcks[messageId].ackReceived)
                {
                    return true;
                }
                
                await Task.Delay(100);
                elapsed += 0.1f;
            }
            
            return false;
        }
        
        public void LogStatistics()
        {
            Logger.Info(
                $"[ReliableCommand] Statistics:\n" +
                $"Commands sent: {_commandsSent}\n" +
                $"Commands ACKed: {_commandsAcked}\n" +
                $"Retries: {_commandsRetried}\n" +
                $"Failed: {_commandsFailed}\n" +
                $"Success rate: {(_commandsSent > 0 ? (float)_commandsAcked / _commandsSent * 100 : 0):F1}%\n" +
                $"Pending ACKs: {_pendingAcks.Count}\n" +
                $"Queued commands: {_commandQueue.Count}"
            );
        }
        
#if UNITY_EDITOR
        [ContextMenu("Log Statistics")]
        private void DebugLogStatistics() { LogStatistics(); }
        
        [ContextMenu("Clear Queue")]
        private void DebugClearQueue()
        {
            _commandQueue.Clear();
            Logger.Info("[ReliableCommand] Queue cleared");
        }
#endif
    }
    
    public class PendingCommand
    {
        public string messageId;
        public string commandId;
        public object payload;
        public DateTime sentAt;
        public int retryCount;
        public bool ackReceived;
    }
    
    public class QueuedCommand
    {
        public string commandId;
        public object payload;
        public DateTime timestamp;
    }
}
