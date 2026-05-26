using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatServer.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ChatServer.Services
{
    static class PacketHelper
    {
        public static Packet? Deserialize(string json)
        {
            try { return JsonSerializer.Deserialize<Packet>(json); }
            catch { return null; }
        }

        public static T? Deserialize<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json); }
            catch { return default; }
        }

        public static string Serialize<T>(T obj) => JsonSerializer.Serialize(obj);
    }

    static class SecurityHelper
    {
        public static string Hash(string s)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        public static string GenerateCode()
        {
            return Random.Shared.Next(100000, 999999).ToString();
        }
    }


    class EmailConfig
    {
        public string SmtpHost { get; set; } = "";
        public int    SmtpPort { get; set; } = 587;
        public bool   UseTls   { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string From     { get; set; } = "";
    }

    static class EmailHelper
    {
        private const string ConfigPath = "data/email_config.json";
        private static EmailConfig? _cfg;

        static EmailHelper()
        {
            if (!File.Exists(ConfigPath))
            {

                var template = new EmailConfig
                {
                    SmtpHost = "smtp.gmail.com",
                    SmtpPort = 587,
                    UseTls   = true,
                    Username = "your-email@gmail.com",
                    Password = "your-app-password",
                    From     = "your-email@gmail.com"
                };
                Directory.CreateDirectory("data");
                File.WriteAllText(ConfigPath,
                    JsonSerializer.Serialize(template,
                        new JsonSerializerOptions { WriteIndented = true }));
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  ⚠  Email не налаштований.");
                Console.WriteLine($"     Заповніть {ConfigPath} і перезапустіть сервер.");
                Console.WriteLine($"     Поки що код буде виводитись у консоль.\n");
                Console.ResetColor();
                return;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                _cfg = JsonSerializer.Deserialize<EmailConfig>(json);
            }
            catch
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  ✗ Помилка читання email_config.json");
                Console.ResetColor();
            }
        }

        public static async Task<bool> SendVerificationCode(
            string toEmail, string username, string code)
        {

            if (_cfg == null || string.IsNullOrWhiteSpace(_cfg.SmtpHost)
                            || _cfg.Username == "your-email@gmail.com")
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n  📧 [MOCK EMAIL]  {username} <{toEmail}>");
                Console.WriteLine($"     Код підтвердження: {code}\n");
                Console.ResetColor();
                return true;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(_cfg.From));
                message.To.Add(MailboxAddress.Parse(toEmail));
                message.Subject = "Підтвердження реєстрації у ChatApp";
                message.Body = new TextPart("plain")
                {
                    Text = $"Привіт, {username}!\n\n" +
                           $"Ваш 6-значний код підтвердження: {code}\n\n" +
                           $"Код дійсний 10 хвилин.\n\n" +
                           $"Якщо ви не реєструвались – проігноруйте цей лист."
                };

                using var client = new SmtpClient();
                var secOpt = _cfg.UseTls
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.None;

                await client.ConnectAsync(_cfg.SmtpHost, _cfg.SmtpPort, secOpt);
                await client.AuthenticateAsync(_cfg.Username, _cfg.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  ✗ Помилка відправки email: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }
    }
}
