using System.Text;
using ChatClient.Models;

namespace ChatClient.UI
{
    static class UIHelper
    {
        public static void ClearScreen()
        {
            Console.Clear();
        }

        public static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔════════════════════════════════════┐");
            Console.WriteLine("║        💬 CHAT CLIENT v1.0         ║");
            Console.WriteLine("╚════════════════════════════════════╝");
            Console.ResetColor();
        }

        public static void PrintSuccess(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n  ✓ {message}");
            Console.ResetColor();
        }

        public static void PrintError(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  ✗ {message}");
            Console.ResetColor();
        }

        public static void PrintInfo(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  ℹ {message}");
            Console.ResetColor();
        }

        public static void PrintOnline(string text)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(text);
            Console.ResetColor();
        }

        public static void PrintMessage(ChatMessage msg)
        {
            Console.Write("  ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{msg.Timestamp.ToLocalTime():HH:mm:ss}] ");
            Console.ResetColor();
            
            ColorHelper.WriteColor(msg.From, msg.Color);
            
            if (!string.IsNullOrEmpty(msg.To) && msg.To != "GLOBAL")
            {
                Console.Write(" → ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(msg.To);
                Console.ResetColor();
            }
            
            Console.Write(": ");
            Console.WriteLine(msg.Content);
        }

        public static string ReadPassword()
        {
            var sb = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) 
                    break;
                if (k.Key == ConsoleKey.Backspace && sb.Length > 0) 
                { 
                    sb.Remove(sb.Length - 1, 1); 
                    Console.Write("\b \b"); 
                }
                else if (k.Key != ConsoleKey.Backspace) 
                { 
                    sb.Append(k.KeyChar); 
                    Console.Write('*'); 
                }
            }
            Console.WriteLine();
            return sb.ToString();
        }
    }

    static class ColorHelper
    {
        private static readonly Dictionary<string, ConsoleColor> Colors = new()
        {
            { "#FF0000", ConsoleColor.Red },
            { "#00FF00", ConsoleColor.Green },
            { "#0000FF", ConsoleColor.Blue },
            { "#FFFF00", ConsoleColor.Yellow },
            { "#FF00FF", ConsoleColor.Magenta },
            { "#00FFFF", ConsoleColor.Cyan },
            { "#FFFFFF", ConsoleColor.White },
            { "#FFA500", ConsoleColor.DarkYellow },
        };

        public static void WriteColor(string text, string hex)
        {
            Console.ForegroundColor = Colors.TryGetValue(hex.ToUpper(), out var c) ? c : ConsoleColor.Gray;
            Console.Write(text);
            Console.ResetColor();
        }

        public static string PickColor()
        {
            var list = Colors.ToList();
            Console.WriteLine("\n  Выберите цвет никнейма:");
            for (int i = 0; i < list.Count; i++)
            {
                Console.Write("  ");
                Console.ForegroundColor = list[i].Value;
                Console.WriteLine($"{i + 1}) {list[i].Key}");
                Console.ResetColor();
            }
            Console.Write("\n  Выбор: ");
            if (int.TryParse(Console.ReadLine(), out int idx) && idx >= 1 && idx <= list.Count)
                return list[idx - 1].Key;
            return "#FFFFFF";
        }
    }
}