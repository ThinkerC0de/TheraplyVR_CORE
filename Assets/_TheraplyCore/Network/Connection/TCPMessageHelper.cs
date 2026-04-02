using System;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using UnityEngine;

namespace TheraplyCore.Network.Connection
{
    /// <summary>
    /// TCP Message Helper - Ensures consistent format between Unity and Flutter
    /// 
    /// FORMAT: [4-byte length (big-endian)][JSON message]
    /// 
    /// Example:
    ///   Length: 100 bytes
    ///   Bytes: [00 00 00 64] + JSON data
    /// </summary>
    public static class TCPMessageHelper
    {
        /// <summary>
        /// Send NetworkMessage with proper length prefix
        /// </summary>
        public static async Task<bool> SendNetworkMessageAsync(NetworkStream stream, NetworkMessage message, bool logVerbose = false)
        {
            try
            {
                // 1. Serialize to JSON
                string jsonString = JsonUtility.ToJson(message);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
                
                // 2. Create length prefix (4 bytes, BIG-ENDIAN)
                byte[] lengthPrefix = BitConverter.GetBytes(jsonBytes.Length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthPrefix); // Convert to big-endian
                }
                
                if (logVerbose)
                {
                    Debug.Log($"[TCPMessageHelper] Sending: {message.commandId}");
                    Debug.Log($"  Length: {jsonBytes.Length} bytes");
                    Debug.Log($"  Length prefix: [{lengthPrefix[0]:X2} {lengthPrefix[1]:X2} {lengthPrefix[2]:X2} {lengthPrefix[3]:X2}]");
                    Debug.Log($"  JSON preview: {jsonString.Substring(0, Math.Min(100, jsonString.Length))}...");
                }
                
                // 3. Send: length prefix + JSON
                await stream.WriteAsync(lengthPrefix, 0, 4);
                await stream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
                await stream.FlushAsync();
                
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPMessageHelper] Send error: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Receive NetworkMessage with length prefix
        /// </summary>
        public static async Task<NetworkMessage?> ReceiveNetworkMessageAsync(NetworkStream stream, bool logVerbose = false)
        {
            try
            {
                // 1. Read length prefix (4 bytes, BIG-ENDIAN)
                byte[] lengthBuffer = new byte[4];
                int bytesRead = await ReadExactAsync(stream, lengthBuffer, 0, 4);
                
                if (bytesRead != 4)
                {
                    Debug.LogWarning("[TCPMessageHelper] Incomplete length prefix");
                    return null;
                }
                
                // 2. Parse length (big-endian)
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBuffer); // Convert from big-endian
                }
                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                
                // 3. Validate length
                if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // Max 10MB
                {
                    Debug.LogError($"[TCPMessageHelper] Invalid message length: {messageLength}");
                    return null;
                }
                
                if (logVerbose)
                {
                    Debug.Log($"[TCPMessageHelper] Receiving message: {messageLength} bytes");
                }
                
                // 4. Read JSON body
                byte[] jsonBuffer = new byte[messageLength];
                bytesRead = await ReadExactAsync(stream, jsonBuffer, 0, messageLength);
                
                if (bytesRead != messageLength)
                {
                    Debug.LogWarning("[TCPMessageHelper] Incomplete message body");
                    return null;
                }
                
                // 5. Deserialize JSON
                string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                NetworkMessage message = JsonUtility.FromJson<NetworkMessage>(jsonString);
                
                if (logVerbose)
                {
                    Debug.Log($"[TCPMessageHelper] Received: {message.commandId}");
                }
                
                return message;
            }
            catch (Exception e)
            {
                Debug.LogError($"[TCPMessageHelper] Receive error: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Read exact number of bytes (helper)
        /// </summary>
        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            
            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead);
                
                if (bytesRead == 0)
                {
                    // Connection closed
                    return totalRead;
                }
                
                totalRead += bytesRead;
            }
            
            return totalRead;
        }
        
        /// <summary>
        /// Create NetworkMessage helper
        /// </summary>
        public static NetworkMessage CreateMessage(string commandId, object payloadObject = null)
        {
            return new NetworkMessage
            {
                messageId = Guid.NewGuid().ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = commandId,
                payload = payloadObject != null ? Encoding.UTF8.GetBytes(JsonUtility.ToJson(payloadObject)) : null
            };
        }
        
        /// <summary>
        /// Create NetworkMessage with string payload
        /// </summary>
        public static NetworkMessage CreateMessageWithString(string commandId, string payloadString)
        {
            return new NetworkMessage
            {
                messageId = Guid.NewGuid().ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                commandId = commandId,
                payloadString = payloadString // Uses helper property
            };
        }
    }
}