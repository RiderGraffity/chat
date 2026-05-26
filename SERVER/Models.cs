using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ChatServer.Models
{
    enum MessageType
    {
        Register, Login, AuthResponse,
        GlobalMessage,
        PrivateMessage,
        GetHistory, HistoryResponse,
        GetUsers, UsersResponse,
        GetPending, PendingResponse,
        UserJoined,
        VerifyCode         
    }

    class Packet
    {
        public MessageType Type { get; set; }
        public string Payload { get; set; } = "";
    }

    class UserInfo
    {
        public string Username     { get; set; } = "";
        public string Color        { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Email        { get; set; } = "";  
    }

    class ChatMessage
    {
        public string   From      { get; set; } = "";
        public string   To        { get; set; } = "";
        public string   Content   { get; set; } = "";
        public string   Color     { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    class AuthRequest
    {
        public string Username   { get; set; } = "";
        public string Password   { get; set; } = "";
        public string Color      { get; set; } = "";
        public string Email      { get; set; } = "";  
        public bool   IsRegister { get; set; }
    }

    class AuthResponse
    {
        public bool   Success          { get; set; }
        public string Message          { get; set; } = "";
        public string Username         { get; set; } = "";
        public string Color            { get; set; } = "";
        public bool   NeedsVerification { get; set; }  
    }

    class PendingInfo
    {
        public int               TotalCount { get; set; }
        public List<string>      FromUsers  { get; set; } = new();
        public List<ChatMessage> Messages   { get; set; } = new();
    }


    class VerifyCodeRequest
    {
        public string Username { get; set; } = "";
        public string Code     { get; set; } = "";
    }

    class Session
    {
        public TcpClient    Tcp      { get; }
        public NetworkStream Stream  { get; }
        public string?      Username { get; set; }
        public string       Color    { get; set; } = "";
        public bool         Auth     => Username != null;

        public Session(TcpClient tcp)
        {
            Tcp    = tcp;
            Stream = tcp.GetStream();
        }

        public async Task Send(Packet p)
        {
            try
            {
                var json = JsonSerializer.Serialize(p);
                await Stream.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"));
            }
            catch { }
        }
    }
}
