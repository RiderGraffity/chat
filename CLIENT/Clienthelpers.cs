using System.Text.Json;
using ChatClient.Models;

namespace ChatClient.Services
{
    static class PacketHelper
    {
        public static Packet? Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Packet>(json);
            }
            catch
            {
                return null;
            }
        }

        public static T? Deserialize<T>(string json) where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return null;
            }
        }

        public static JsonElement? DeserializeElement(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<JsonElement>(json);
            }
            catch
            {
                return null;
            }
        }

        public static T? GetProperty<T>(JsonElement? element, string propertyName) where T : class
        {
            if (element == null) return null;
            
            try
            {
                if (element.Value.TryGetProperty(propertyName, out var prop))
                    return JsonSerializer.Deserialize<T>(prop.GetRawText());
            }
            catch { }
            
            return null;
        }

        public static string Serialize<T>(T obj)
        {
            return JsonSerializer.Serialize(obj);
        }
    }
}