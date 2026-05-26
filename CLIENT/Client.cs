using System.Net.Sockets;
using System.Text;
using ChatClient.Services;
using ChatClient.UI;
using ChatClient.Models;

namespace ChatClient
{
    class Client
    {
        private const int    TCP_PORT = 11001;
        private const int    UDP_PORT = 11000;
        private const string HOST     = "127.0.0.1";

        private TcpClient?    _tcp;
        private NetworkStream? _stream;
        private StreamReader?  _reader;
        private UdpClient?    _udp;

        private string _username  = "";
        private string _myColor   = "";
        private string _view      = "MENU";
        private string _privateWith = "";
        private bool   _running   = true;

        private HashSet<string> _conversations = new();
        private HashSet<string> _seen          = new();


        private TaskCompletionSource<List<ChatMessage>?>? _historyTcs;

        public async Task Run()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.CursorVisible  = false;

            try
            {
                _tcp    = new TcpClient(HOST, TCP_PORT);
                _stream = _tcp.GetStream();
                _reader = new StreamReader(_stream, Encoding.UTF8);
            }
            catch
            {
                UIHelper.PrintError("Не вдалося підключитись до сервера!");
                return;
            }

            if (!await Auth()) return;

            UIHelper.PrintSuccess($"Ласкаво просимо, {_username}!");
            await Task.Delay(1000);

            _ = Task.Run(TcpLoop);
            await SendPacket(new Packet { Type = MessageType.GetPending });

            SetupUdp();
            await Menu();

            _running = false;
            Console.CursorVisible = true;
        }


        private async Task<bool> Auth()
        {
            while (true)
            {
                UIHelper.ClearScreen();
                UIHelper.PrintBanner();

                Console.WriteLine();
                Console.WriteLine("  1) 📝 Реєстрація");
                Console.WriteLine("  2) 🔑 Вхід");
                Console.Write("\n  Вибір: ");

                var choice = Console.ReadLine()?.Trim();
                bool isReg = choice == "1";

                Console.Write("  Нікнейм: ");
                var username = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrEmpty(username))
                {
                    UIHelper.PrintError("Нікнейм не може бути порожнім!");
                    await Task.Delay(1500);
                    continue;
                }

                Console.Write("  Пароль: ");
                var password = UIHelper.ReadPassword();

                string color = "#FFFFFF";
                string email = "";

                if (isReg)
                {
                    Console.Write("  Email: ");
                    email = Console.ReadLine()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                    {
                        UIHelper.PrintError("Вкажіть коректну email-адресу!");
                        await Task.Delay(1500);
                        continue;
                    }
                    color = ColorHelper.PickColor();
                }

                var req = new AuthRequest
                {
                    Username   = username,
                    Password   = password,
                    Color      = color,
                    Email      = email,
                    IsRegister = isReg
                };

                await SendPacket(new Packet
                {
                    Type    = isReg ? MessageType.Register : MessageType.Login,
                    Payload = PacketHelper.Serialize(req)
                });


                var resp = await ReadAuthResponse();
                if (resp == null) return false;

                UIHelper.ClearScreen();

    
                if (resp.NeedsVerification)
                {
                    UIHelper.PrintInfo(resp.Message);


                    bool verified = false;
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        Console.Write($"\n  Введіть 6-значний код ({attempt}/3): ");
                        var code = Console.ReadLine()?.Trim() ?? "";

                        if (code.Length != 6 || !code.All(char.IsDigit))
                        {
                            UIHelper.PrintError("Код має містити рівно 6 цифр.");
                            continue;
                        }

                        await SendPacket(new Packet
                        {
                            Type    = MessageType.VerifyCode,
                            Payload = PacketHelper.Serialize(new VerifyCodeRequest
                            {
                                Username = username,
                                Code     = code
                            })
                        });

                        var vResp = await ReadAuthResponse();
                        if (vResp == null) return false;

                        UIHelper.ClearScreen();
                        if (vResp.Success)
                        {
                            UIHelper.PrintSuccess(vResp.Message);
                            _username = vResp.Username;
                            _myColor  = vResp.Color;
                            await Task.Delay(1000);
                            verified  = true;
                            return true;
                        }
                        else if (vResp.NeedsVerification)
                        {
                            UIHelper.PrintError(vResp.Message);
                        }
                        else
                        {

                            UIHelper.PrintError(vResp.Message);
                            break;
                        }
                    }

                    if (!verified)
                    {
                        UIHelper.PrintError("Перевищено кількість спроб. Почніть реєстрацію знову.");
                        await Task.Delay(2000);
                    }
                    continue;
                }


                if (resp.Success)
                {
                    UIHelper.PrintSuccess(resp.Message);
                    _username = resp.Username;
                    _myColor  = resp.Color;
                    await Task.Delay(1000);
                    return true;
                }
                else
                {
                    UIHelper.PrintError(resp.Message);
                    await Task.Delay(1500);
                }
            }
        }


        private async Task<AuthResponse?> ReadAuthResponse()
        {
            var line = await _reader!.ReadLineAsync();
            if (line == null) return null;
            var p = PacketHelper.Deserialize(line);
            if (p?.Type != MessageType.AuthResponse) return null;
            return PacketHelper.Deserialize<AuthResponse>(p.Payload);
        }


        private async Task SendPacket(Packet p)
        {
            try
            {
                var json = PacketHelper.Serialize(p);
                await _stream!.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"));
            }
            catch { }
        }

        private void SetupUdp()
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress, true);
            _udp.ExclusiveAddressUse = false;
            _udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, UDP_PORT));
            _udp.EnableBroadcast = true;
            _ = Task.Run(UdpLoop);
        }

        private async Task UdpLoop()
        {
            while (_running)
            {
                try
                {
                    var result = await _udp!.ReceiveAsync();
                    var msg    = PacketHelper.Deserialize<ChatMessage>(
                        Encoding.UTF8.GetString(result.Buffer));
                    if (msg == null || msg.From == _username) continue;

                    var key = $"{msg.From}:{msg.Timestamp.Ticks}:{msg.Content}";
                    if (!_seen.Add(key)) continue;

                    if (_view == "GLOBAL")
                    {
                        Console.Write("\r");
                        UIHelper.PrintMessage(msg);
                        Console.Write("  > ");
                    }
                }
                catch { }
            }
        }

        private async Task TcpLoop()
        {
            while (_running)
            {
                try
                {
                    var line = await _reader!.ReadLineAsync();
                    if (line == null) break;
                    var p = PacketHelper.Deserialize(line);
                    if (p != null) HandlePacket(p);
                }
                catch { break; }
            }
        }

        private void HandlePacket(Packet p)
        {
            switch (p.Type)
            {
                case MessageType.PrivateMessage:
                    var msg = PacketHelper.Deserialize<ChatMessage>(p.Payload);
                    if (msg != null)
                    {
                        var other = msg.From == _username ? msg.To : msg.From;
                        _conversations.Add(other);
                        if (_view == "PRIVATE" && _privateWith == msg.From)
                        {
                            Console.Write("\r");
                            UIHelper.PrintMessage(msg);
                            Console.Write("  > ");
                        }
                    }
                    break;

                case MessageType.PendingResponse:
                    var info = PacketHelper.Deserialize<PendingInfo>(p.Payload);
                    if (info != null && info.TotalCount > 0)
                    {
                        UIHelper.PrintInfo(
                            $"📬 У вас +{info.TotalCount} повідомлень від: " +
                            string.Join(", ", info.FromUsers));
                        foreach (var m in info.Messages)
                            _conversations.Add(m.From);
                    }
                    break;


                case MessageType.HistoryResponse:
                    var history = PacketHelper.Deserialize<List<ChatMessage>>(p.Payload);

                    if (_historyTcs != null)
                    {
                        _historyTcs.TrySetResult(history);
                    }
                    else
                    {

                        if (history != null)
                            foreach (var m in history)
                                UIHelper.PrintMessage(m);
                        Console.Write("  > ");
                    }
                    break;

                case MessageType.UsersResponse:
                    var data = PacketHelper.DeserializeElement(p.Payload);
                    if (data != null)
                    {
                        var users  = PacketHelper.GetProperty<List<UserInfo>>(data, "Users");
                        var online = PacketHelper.GetProperty<List<string>>(data, "Online");

                        Console.WriteLine("\n┌─ Користувачі ──────────────────────┐");
                        if (users != null)
                        {
                            foreach (var u in users)
                            {
                                Console.Write("│ ");
                                ColorHelper.WriteColor(u.Username, u.Color);
                                if (online != null && online.Contains(u.Username))
                                    UIHelper.PrintOnline(" [онлайн]");
                                else
                                    Console.WriteLine(" [оффлайн]");
                            }
                        }
                        Console.WriteLine("└─────────────────────────────────────┘");
                    }
                    break;

                case MessageType.UserJoined:
                    if (_view != "MENU")
                    {
                        Console.Write("\r");
                        UIHelper.PrintInfo($"*** {p.Payload} ***");
                        Console.Write("  > ");
                    }
                    break;
            }
        }

        private async Task Menu()
        {
            while (true)
            {
                UIHelper.ClearScreen();
                Console.WriteLine();
                Console.WriteLine("┌──────────────────────────────────┐");
                Console.Write("│ ");
                ColorHelper.WriteColor(_username, _myColor);
                Console.WriteLine(" 💬                        │");
                Console.WriteLine("├──────────────────────────────────┤");
                Console.WriteLine("│  1) 🌍 Загальний чат             │");
                Console.WriteLine("│  2) 💬 Діалоги                   │");
                Console.WriteLine("│  3) ✉️  Написати користувачу    │");
                Console.WriteLine("│  4) 👥 Список користувачів       │");
                Console.WriteLine("│  0) ❌ Вихід                     │");
                Console.WriteLine("└──────────────────────────────────┘");
                Console.Write("\n  Вибір: ");

                switch (Console.ReadLine()?.Trim())
                {
                    case "1": await GlobalChat();      break;
                    case "2": await Conversations();   break;
                    case "3": await WriteToUser();     break;
                    case "4":
                        await SendPacket(new Packet { Type = MessageType.GetUsers });
                        await Task.Delay(400);
                        break;
                    case "0": _running = false; return;
                }
            }
        }

        private async Task GlobalChat()
        {
            _view = "GLOBAL";
            UIHelper.ClearScreen();
            Console.WriteLine("\n┌─ Загальний чат ─────────────────────┐");
            Console.WriteLine("│ Команди: /exit  /history            │");
            Console.WriteLine("└─────────────────────────────────────┘\n");


            await LoadAndPrintHistory("GLOBAL");

            while (true)
            {
                Console.Write("  > ");
                var input = Console.ReadLine()?.Trim() ?? "";

                if (input == "/exit") break;
                if (input == "/history")
                {
                    await LoadAndPrintHistory("GLOBAL");
                    continue;
                }
                if (input == "") continue;

                var msg = new ChatMessage
                {
                    From      = _username,
                    Color     = _myColor,
                    Content   = input,
                    Timestamp = DateTime.UtcNow
                };
                var data = Encoding.UTF8.GetBytes(PacketHelper.Serialize(msg));
                await _udp!.SendAsync(data, data.Length,
                    new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, UDP_PORT));
                UIHelper.PrintMessage(msg);
            }
            _view = "MENU";
        }

        private async Task Conversations()
        {
            if (_conversations.Count == 0)
            {
                UIHelper.PrintError("  Діалогів немає.");
                await Task.Delay(1500);
                return;
            }

            UIHelper.ClearScreen();
            Console.WriteLine("\n┌─ Діалоги ──────────────────────────┐");
            var list = _conversations.ToList();
            for (int i = 0; i < list.Count; i++)
                Console.WriteLine($"│  {i + 1}) {list[i]}");
            Console.WriteLine("└─────────────────────────────────────┘");

            Console.Write("\n  Відкрити (номер): ");
            if (int.TryParse(Console.ReadLine(), out int idx)
                && idx >= 1 && idx <= list.Count)
                await PrivateChat(list[idx - 1]);
        }

        private async Task WriteToUser()
        {
            Console.Write("  Нікнейм: ");
            var target = Console.ReadLine()?.Trim() ?? "";
            if (target == "" || target == _username) return;
            _conversations.Add(target);
            await PrivateChat(target);
        }

        private async Task PrivateChat(string target)
        {
            _view       = "PRIVATE";
            _privateWith = target;
            UIHelper.ClearScreen();
            Console.WriteLine($"\n┌─ Діалог з {target} ──────────────────────┐");
            Console.WriteLine("│ Команди: /exit  /history            │");
            Console.WriteLine("└─────────────────────────────────────┘\n");

            // ── Завантажуємо і виводимо історію до першого промпту ──
            await LoadAndPrintHistory($"{_username}|{target}");

            while (true)
            {
                Console.Write("  > ");
                var input = Console.ReadLine()?.Trim() ?? "";

                if (input == "/exit") break;
                if (input == "/history")
                {
                    await LoadAndPrintHistory($"{_username}|{target}");
                    continue;
                }
                if (input == "") continue;

                var msg = new ChatMessage
                {
                    From      = _username,
                    To        = target,
                    Color     = _myColor,
                    Content   = input,
                    Timestamp = DateTime.UtcNow
                };
                await SendPacket(new Packet
                {
                    Type    = MessageType.PrivateMessage,
                    Payload = PacketHelper.Serialize(msg)
                });
            }

            _view        = "MENU";
            _privateWith = "";
        }


        private async Task LoadAndPrintHistory(string payload)
        {

            _historyTcs = new TaskCompletionSource<List<ChatMessage>?>();

            await SendPacket(new Packet
            {
                Type    = MessageType.GetHistory,
                Payload = payload
            });


            var completed = await Task.WhenAny(_historyTcs.Task,
                Task.Delay(TimeSpan.FromSeconds(3)));

            var history = (_historyTcs.Task.IsCompleted)
                ? await _historyTcs.Task
                : null;

            _historyTcs = null;

            if (history != null && history.Count > 0)
            {
                foreach (var m in history)
                    UIHelper.PrintMessage(m);
                Console.WriteLine(); 
            }
            else if (history != null)
            {
                UIHelper.PrintInfo("(Повідомлень ще немає)");
            }
        }
    }

    class Program
    {
        static async Task Main() => await new Client().Run();
    }
}
