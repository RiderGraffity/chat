using System.Net;
using System.Net.Sockets;
using System.Text;
using ChatServer.Services;
using ChatServer.Models;

namespace ChatServer
{
    class Server
    {
        private readonly DataStore    _store    = new();
        private readonly List<Session> _sessions = new();
        private readonly object       _lock     = new();
        private const int TCP_PORT = 11001;

        public async Task Run()
        {
            Console.OutputEncoding = Encoding.UTF8;
            PrintBanner();

            var listener = new TcpListener(IPAddress.Any, TCP_PORT);
            listener.Start();

            PrintLog($"✓ Сервер запущен на порту {TCP_PORT}");
            PrintLog($"✓ Ожидание подключений...\n");

            try
            {
                while (true)
                {
                    var tcp = await listener.AcceptTcpClientAsync();
                    var ip  = tcp.Client.RemoteEndPoint;
                    PrintLog($"→ Новое подключение: {ip}");
                    _ = Task.Run(() => Handle(new Session(tcp)));
                }
            }
            catch (Exception ex) { PrintError($"Ошибка сервера: {ex.Message}"); }
        }

        private async Task Handle(Session s)
        {
            var reader = new StreamReader(s.Stream, Encoding.UTF8);
            try
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = PacketHelper.Deserialize(line);
                    if (p != null) await Process(s, p);
                }
            }
            catch { }
            finally
            {
                lock (_lock) _sessions.Remove(s);
                if (s.Username != null)
                {
                    PrintLog($"✗ {s.Username} отключился");
                    await Broadcast(new Packet
                    {
                        Type    = MessageType.UserJoined,
                        Payload = $"{s.Username} покинул чат"
                    }, s.Username);
                }
            }
        }

        private async Task Process(Session s, Packet p)
        {
            switch (p.Type)
            {
                case MessageType.Register:
                case MessageType.Login:
                    await HandleAuth(s, p);
                    break;
                case MessageType.VerifyCode:
                    await HandleVerifyCode(s, p);
                    break;
                case MessageType.GlobalMessage:
                    if (s.Auth) await HandleGlobal(s, p);
                    break;
                case MessageType.PrivateMessage:
                    if (s.Auth) await HandlePrivate(s, p);
                    break;
                case MessageType.GetHistory:
                    if (s.Auth) await HandleHistory(s, p);
                    break;
                case MessageType.GetUsers:
                    if (s.Auth) await HandleUsers(s);
                    break;
                case MessageType.GetPending:
                    if (s.Auth) await HandlePending(s);
                    break;
            }
        }

        private async Task HandleAuth(Session s, Packet p)
        {
            var req = PacketHelper.Deserialize<AuthRequest>(p.Payload);
            if (req == null) return;

            var hash = SecurityHelper.Hash(req.Password);

            if (p.Type == MessageType.Register)
            {
                if (!_store.CanRegister(req.Username))
                {
                    await s.Send(new Packet
                    {
                        Type    = MessageType.AuthResponse,
                        Payload = PacketHelper.Serialize(new AuthResponse
                        {
                            Success = false,
                            Message = "✗ Користувач вже існує!"
                        })
                    });
                    return;
                }

                if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
                {
                    await s.Send(new Packet
                    {
                        Type    = MessageType.AuthResponse,
                        Payload = PacketHelper.Serialize(new AuthResponse
                        {
                            Success = false,
                            Message = "✗ Вкажіть коректну email-адресу!"
                        })
                    });
                    return;
                }

                var code = SecurityHelper.GenerateCode();
                _store.StorePendingReg(req.Username, code, hash, req.Color, req.Email);
                PrintLog($"📧 Надсилаємо код на {req.Email} для {req.Username}");

                bool sent = await EmailHelper.SendVerificationCode(
                    req.Email, req.Username, code);

                await s.Send(new Packet
                {
                    Type    = MessageType.AuthResponse,
                    Payload = PacketHelper.Serialize(new AuthResponse
                    {
                        Success             = false,
                        NeedsVerification   = true,
                        Username            = req.Username,
                        Message             = sent
                            ? $"✉ Код підтвердження надіслано на {req.Email}"
                            : $"✉ Код виведено у консоль сервера (email не налаштовано)"
                    })
                });
            }
            else
            {
                // ── ВХІД ──
                var user = _store.Login(req.Username, hash);
                var resp = user != null
                    ? new AuthResponse
                    {
                        Success  = true,
                        Username = user.Username,
                        Color    = user.Color,
                        Message  = "✓ Ласкаво просимо!"
                    }
                    : new AuthResponse
                    {
                        Success = false,
                        Message = "✗ Невірні облікові дані!"
                    };

                if (resp.Success)
                {
                    s.Username = resp.Username;
                    s.Color    = resp.Color;
                    lock (_lock) _sessions.Add(s);
                    PrintLog($"✓ {s.Username} увійшов у систему");
                    await Broadcast(new Packet
                    {
                        Type    = MessageType.UserJoined,
                        Payload = $"{s.Username} приєднався до чату"
                    }, s.Username);
                }

                await s.Send(new Packet
                {
                    Type    = MessageType.AuthResponse,
                    Payload = PacketHelper.Serialize(resp)
                });
            }
        }

        private async Task HandleVerifyCode(Session s, Packet p)
        {
            var req = PacketHelper.Deserialize<VerifyCodeRequest>(p.Payload);
            if (req == null) return;

            bool ok = _store.ConfirmRegistration(req.Username, req.Code);

            AuthResponse resp;
            if (ok)
            {
                var user = _store.GetUsers()
                    .FirstOrDefault(u =>
                        u.Username.Equals(req.Username, StringComparison.OrdinalIgnoreCase));

                if (user != null)
                {
                    s.Username = user.Username;
                    s.Color    = user.Color;
                    lock (_lock) _sessions.Add(s);
                    PrintLog($"✓ {s.Username} підтвердив email і зареєструвався");
                    await Broadcast(new Packet
                    {
                        Type    = MessageType.UserJoined,
                        Payload = $"{s.Username} приєднався до чату"
                    }, s.Username);
                }

                resp = new AuthResponse
                {
                    Success  = true,
                    Username = req.Username,
                    Color    = user?.Color ?? "#FFFFFF",
                    Message  = "✓ Реєстрацію підтверджено! Ласкаво просимо!"
                };
            }
            else
            {
                resp = new AuthResponse
                {
                    Success             = false,
                    NeedsVerification   = true,
                    Username            = req.Username,
                    Message             = "✗ Невірний або застарілий код. Спробуйте ще раз."
                };
            }

            await s.Send(new Packet
            {
                Type    = MessageType.AuthResponse,
                Payload = PacketHelper.Serialize(resp)
            });
        }

        private async Task HandlePrivate(Session s, Packet p)
        {
            var msg = PacketHelper.Deserialize<ChatMessage>(p.Payload);
            if (msg == null) return;

            msg.From      = s.Username!;
            msg.Color     = s.Color;
            msg.Timestamp = DateTime.UtcNow;

            _store.SavePrivateMessage(msg);
            PrintLog($"[ПМ] {msg.From} → {msg.To}: {msg.Content}");

            Session? recipient;
            lock (_lock) recipient = _sessions.FirstOrDefault(x => x.Username == msg.To);

            if (recipient != null)
            {
                _store.ClearPending(msg.To, msg.From);
                await recipient.Send(new Packet
                {
                    Type    = MessageType.PrivateMessage,
                    Payload = PacketHelper.Serialize(msg)
                });
            }

            await s.Send(new Packet
            {
                Type    = MessageType.PrivateMessage,
                Payload = PacketHelper.Serialize(msg)
            });
        }

        private async Task HandleGlobal(Session s, Packet p)
        {
            var msg = PacketHelper.Deserialize<ChatMessage>(p.Payload);
            if (msg == null) return;

            msg.From      = s.Username!;
            msg.Color     = s.Color;
            msg.Timestamp = DateTime.UtcNow;
            msg.To        = "GLOBAL";

            _store.SaveGlobal(msg);
            PrintLog($"[GLOBAL] {msg.From}: {msg.Content}");

            List<Session> list;
            lock (_lock) list = _sessions.ToList();

            var packet = new Packet
            {
                Type    = MessageType.GlobalMessage,
                Payload = PacketHelper.Serialize(msg)
            };

            foreach (var session in list)
                await session.Send(packet);
        }

        private async Task HandleHistory(Session s, Packet p)
        {
            List<ChatMessage> history;
            if (p.Payload == "GLOBAL")
                history = _store.GetGlobal();
            else
            {
                var parts = p.Payload.Split('|');
                history = parts.Length == 2
                    ? _store.GetPrivate(parts[0], parts[1])
                    : new();
            }

            await s.Send(new Packet
            {
                Type    = MessageType.HistoryResponse,
                Payload = PacketHelper.Serialize(history)
            });
        }

        private async Task HandleUsers(Session s)
        {
            var users  = _store.GetUsers();
            List<string> online;
            lock (_lock) online = _sessions.Where(x => x.Auth).Select(x => x.Username!).ToList();

            await s.Send(new Packet
            {
                Type    = MessageType.UsersResponse,
                Payload = PacketHelper.Serialize(new { Users = users, Online = online })
            });
        }

        private async Task HandlePending(Session s)
        {
            var info = _store.PopPending(s.Username!);
            if (info != null)
                await s.Send(new Packet
                {
                    Type    = MessageType.PendingResponse,
                    Payload = PacketHelper.Serialize(info)
                });
        }

        private async Task Broadcast(Packet p, string except)
        {
            List<Session> list;
            lock (_lock) list = _sessions.ToList();
            foreach (var s in list.Where(x => x.Username != except))
                await s.Send(p);
        }

        private void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n╔═══════════════════════════════════════════╗");
            Console.WriteLine("║       🔧 CHAT SERVER v2.0 🔧            ║");
            Console.WriteLine("║    TCP PORT: 11001  |  Email verify     ║");
            Console.WriteLine("╚═══════════════════════════════════════════╝\n");
            Console.ResetColor();
        }

        private void PrintLog(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  ✓ [{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }

        private void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ✗ [{DateTime.Now:HH:mm:ss}] {message}");
            Console.ResetColor();
        }
    }

    class Program
    {
        static async Task Main() => await new Server().Run();
    }
}
