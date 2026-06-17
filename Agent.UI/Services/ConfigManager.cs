using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Agent.UI.Services
{
    public class AgentConfig
    {
        public string ServerUrl { get; set; } = "http://192.168.1.85:5230";
        public string NetworkName { get; set; } = string.Empty;
        public string NetworkId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
    }

    public class ConfigManager
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M1Agent");
        private static readonly string ConfigFile = Path.Combine(ConfigFolder, "config.json");
        private static readonly string PasswordFile = Path.Combine(ConfigFolder, "network.dat");

        public static bool ConfigExists()
        {
            return File.Exists(ConfigFile);
        }

        public static AgentConfig? LoadConfig()
        {
            if (!File.Exists(ConfigFile)) return null;
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AgentConfig>(json);
        }

        public static void SaveConfig(AgentConfig config)
        {
            Directory.CreateDirectory(ConfigFolder);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void SavePassword(string password)
        {
            Directory.CreateDirectory(ConfigFolder);
            byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(PasswordFile, encrypted);
        }

        public static string? LoadPassword()
        {
            if (!File.Exists(PasswordFile)) return null;
            byte[] encrypted = File.ReadAllBytes(PasswordFile);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(decrypted);
        }

        public static void DeleteConfig()
        {
            if (File.Exists(ConfigFile)) File.Delete(ConfigFile);
            if (File.Exists(PasswordFile)) File.Delete(PasswordFile);
        }
    }
}