using System;

namespace JobAggregator.DataAccess.Entities
{
    public class WebSocketConnection
    {
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
    }
}
