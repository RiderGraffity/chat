using System.Text.Json;
using ChatServer.Models;

namespace ChatServer.Services
{
    // Временная запись для email-верификации
    record PendingReg(
        string Code,
        string Username,
        string Hash,
        string Color,
        string Email,
        DateTime Expires);

    class DataStore
    {
        private readonly string _dataDir    = "data";
        private readonly string _usersFile;
        private readonly string _globalFile;
        private readonly string _privateDir;
        private readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private List<UserInfo>                        _users   = new();
        private List<ChatMessage>                     _global  = new();
        private Dictionary<string, List<ChatMessage>> _private = new();
        private Dictionary<string, List<ChatMessage>> _pending = new();

        // username → данные ожидающей верификации
        private readonly Dictionary<string, PendingReg> _pendingReg = new();

        private readonly object _lock = new();

        public DataStore()
        {
            _usersFile  = Path.Combine(_dataDir, "users.json");
            _globalFile = Path.Combine(_dataDir, "global.json");
            _privateDir = Path.Combine(_dataDir, "private");
            Directory.CreateDirectory(_dataDir);
            Directory.CreateDirectory(_privateDir);
            Load();
        }

        private void Load()
        {
            if (File.Exists(_usersFile))
            {
                var t = File.ReadAllText(_usersFile).Trim();
                if (t.Length > 0)
                    _users = JsonSerializer.Deserialize<List<UserInfo>>(t) ?? new();
            }
            if (File.Exists(_globalFile))
            {
                var t = File.ReadAllText(_globalFile).Trim();
                if (t.Length > 0)
                    _global = JsonSerializer.Deserialize<List<ChatMessage>>(t) ?? new();
            }
        }

        private string Key(string a, string b) =>
            string.Join("|", new[] { a, b }.OrderBy(x => x));

        private List<ChatMessage> LoadPrivate(string key)
        {
            if (_private.ContainsKey(key)) return _private[key];
            var f    = Path.Combine(_privateDir, key.Replace("|", "_") + ".json");
            var msgs = File.Exists(f)
                ? JsonSerializer.Deserialize<List<ChatMessage>>(File.ReadAllText(f)) ?? new()
                : new List<ChatMessage>();
            _private[key] = msgs;
            return msgs;
        }

        private void SavePrivate(string key)
        {
            var f = Path.Combine(_privateDir, key.Replace("|", "_") + ".json");
            File.WriteAllText(f, JsonSerializer.Serialize(_private[key], _opts));
        }

        // ── Верификация email ─────────────────────────────────────────

        /// Проверяет, что username не занят. Не регистрирует пользователя.
        public bool CanRegister(string username)
        {
            lock (_lock)
                return !_users.Any(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        /// Сохраняет ожидающую верификацию (заменяет предыдущую).
        public void StorePendingReg(string username, string code,
                                    string hash, string color, string email)
        {
            lock (_lock)
                _pendingReg[username] = new PendingReg(
                    code, username, hash, color, email,
                    DateTime.UtcNow.AddMinutes(10));
        }

        /// Проверяет код и регистрирует пользователя.
        /// Возвращает: true = успех, false = неверный/истёкший код.
        public bool ConfirmRegistration(string username, string code)
        {
            lock (_lock)
            {
                if (!_pendingReg.TryGetValue(username, out var pr)) return false;
                if (pr.Code != code)                                  return false;
                if (DateTime.UtcNow > pr.Expires)
                {
                    _pendingReg.Remove(username);
                    return false;
                }

                // повторная проверка – вдруг кто-то успел зарегать тот же ник
                if (_users.Any(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    _pendingReg.Remove(username);
                    return false;
                }

                _users.Add(new UserInfo
                {
                    Username     = pr.Username,
                    PasswordHash = pr.Hash,
                    Color        = pr.Color,
                    Email        = pr.Email
                });
                File.WriteAllText(_usersFile, JsonSerializer.Serialize(_users, _opts));
                _pendingReg.Remove(username);
                return true;
            }
        }

        // ── Стандартные методы ────────────────────────────────────────

        public bool Register(string username, string hash, string color)
        {
            lock (_lock)
            {
                if (_users.Any(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                    return false;

                _users.Add(new UserInfo
                {
                    Username     = username,
                    PasswordHash = hash,
                    Color        = color
                });
                File.WriteAllText(_usersFile, JsonSerializer.Serialize(_users, _opts));
                return true;
            }
        }

        public UserInfo? Login(string username, string hash)
        {
            lock (_lock)
                return _users.FirstOrDefault(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                    && u.PasswordHash == hash);
        }

        public List<UserInfo> GetUsers()
        {
            lock (_lock) return _users.ToList();
        }

        public void SaveGlobal(ChatMessage msg)
        {
            lock (_lock)
            {
                _global.Add(msg);
                if (_global.Count > 200)
                    _global = _global.TakeLast(200).ToList();
                File.WriteAllText(_globalFile, JsonSerializer.Serialize(_global, _opts));
            }
        }

        public List<ChatMessage> GetGlobal()
        {
            lock (_lock) return _global.TakeLast(50).ToList();
        }

        public void SavePrivateMessage(ChatMessage msg)
        {
            lock (_lock)
            {
                var key  = Key(msg.From, msg.To);
                var list = LoadPrivate(key);
                list.Add(msg);
                SavePrivate(key);

                if (!_pending.ContainsKey(msg.To))
                    _pending[msg.To] = new();
                _pending[msg.To].Add(msg);
            }
        }

        public List<ChatMessage> GetPrivate(string a, string b)
        {
            lock (_lock) return LoadPrivate(Key(a, b)).TakeLast(50).ToList();
        }

        public PendingInfo? PopPending(string username)
        {
            lock (_lock)
            {
                if (!_pending.TryGetValue(username, out var msgs) || msgs.Count == 0)
                    return null;

                var info = new PendingInfo
                {
                    TotalCount = msgs.Count,
                    FromUsers  = msgs.Select(m => m.From).Distinct().ToList(),
                    Messages   = msgs.ToList()
                };
                _pending[username] = new();
                return info;
            }
        }

        public void ClearPending(string to, string from)
        {
            lock (_lock)
            {
                if (_pending.ContainsKey(to))
                    _pending[to].RemoveAll(m => m.From == from);
            }
        }
    }
}
