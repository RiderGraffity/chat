using System.Text.Json;

namespace ChatClient.Models
{
    // ВАЖЛИВО: числові значення мають точно збігатися з серверним MessageType
    enum MessageType
    {
        Register, Login, AuthResponse,
        GlobalMessage,      
        PrivateMessage,    
        GetHistory,         
        HistoryResponse,    
        GetUsers,         
        UsersResponse,    
        GetPending,         
        PendingResponse,    
        UserJoined,         
        VerifyCode         
    }

    class Packet
    {
        public MessageType Type    { get; set; }
        public string      Payload { get; set; } = "";
    }

    class ChatMessage
    {
        public string   From      { get; set; } = "";
        public string   To        { get; set; } = "";
        public string   Content   { get; set; } = "";
        public string   Color     { get; set; } = "";
        public DateTime Timestamp { get; set; }
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
        public bool   Success           { get; set; }
        public string Message           { get; set; } = "";
        public string Username          { get; set; } = "";
        public string Color             { get; set; } = "";
        public bool   NeedsVerification { get; set; }
    }

    class VerifyCodeRequest
    {
        public string Username { get; set; } = "";
        public string Code     { get; set; } = "";
    }

    class UserInfo
    {
        public string Username { get; set; } = "";
        public string Color    { get; set; } = "";
    }

    class PendingInfo
    {
        public int               TotalCount { get; set; }
        public List<string>      FromUsers  { get; set; } = new();
        public List<ChatMessage> Messages   { get; set; } = new();
    }
}
