namespace TheraplyCore.Network.Connection
{
    /// <summary>
    /// Wire format for all TCP messages between Unity and Flutter.
    /// Serialized as JSON (JsonUtility).
    /// </summary>
    public struct NetworkMessage
    {
        public string messageId;
        public long timestamp;
        public string commandId;
        public byte[] payload;

        public string payloadString
        {
            get => payload != null ? System.Text.Encoding.UTF8.GetString(payload) : null;
            set => payload = value != null ? System.Text.Encoding.UTF8.GetBytes(value) : null;
        }
    }
}
