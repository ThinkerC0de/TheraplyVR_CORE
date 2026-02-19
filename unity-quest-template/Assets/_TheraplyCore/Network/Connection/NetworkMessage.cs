using MessagePack;

namespace TheraplyCore.Network.Connection
{
    /// <summary>
    /// Wire format for all TCP messages between Unity and Flutter.
    /// Serialized as JSON (JsonUtility). MessagePack attributes are legacy remnants kept for compatibility.
    /// </summary>
    public struct NetworkMessage
    {
        [Key(0)] public string messageId;
        [Key(1)] public long timestamp;
        [Key(2)] public string commandId;
        [Key(3)] public byte[] payload;

        [IgnoreMember]
        public string payloadString
        {
            get => payload != null ? System.Text.Encoding.UTF8.GetString(payload) : null;
            set => payload = value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null;
        }
    }
}
