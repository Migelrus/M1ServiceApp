using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace M1.Agent.Tray
{
    static class Program
    {
        private static NotifyIcon? _icon;
        private static Process? _agentProcess;
        private static string _networkName = "";
        private static string _deviceId = "";
        private static string _serverUrl = "http://192.168.1.85:5230";

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _deviceId = GenerateDeviceId();

            if (!ConfigExists())
            {
                if (!ShowFirstRunDialog()) { Application.Exit(); return; }
            }

            LoadConfig();
            StartAgentService();

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = true,
                Text = $"M1 Agent - {_deviceId}"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add($"Сеть: {_networkName}").Enabled = false;
            menu.Items.Add($"ID: {_deviceId}").Enabled = false;
            menu.Items.Add("-");
            menu.Items.Add("Диагностика", null, OnDiagnostic);
            menu.Items.Add("Статус", null, OnStatus);
            menu.Items.Add("-");
            menu.Items.Add("Выход", null, OnExit);
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => OnDiagnostic(s, e);

            Application.Run();
        }

        private static string GenerateDeviceId()
        {
            var name = Environment.MachineName;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
            return name + "-" + Convert.ToHexString(hash)[..8].ToUpper();
        }

        private static bool ConfigExists() => File.Exists(GetConfigFile());

        private static bool ShowFirstRunDialog()
        {
            using var form = new Form
            {
                Text = "M1 Agent - Первый запуск",
                Width = 420, Height = 320,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };

            var lblTitle = new Label { Text = "Подключение к сети", Font = new Font("Segoe UI", 14, FontStyle.Bold), Location = new Point(25, 15), AutoSize = true };
            var lblServer = new Label { Text = "Адрес сервера:", Location = new Point(25, 55), AutoSize = true };
            var txtServer = new TextBox { Text = _serverUrl, Location = new Point(25, 75), Width = 350 };
            var lblNetwork = new Label { Text = "Имя сети:", Location = new Point(25, 110), AutoSize = true };
            var txtNetwork = new TextBox { Location = new Point(25, 130), Width = 350 };
            var lblPass = new Label { Text = "Пароль сети:", Location = new Point(25, 165), AutoSize = true };
            var txtPass = new TextBox { Location = new Point(25, 185), Width = 350, PasswordChar = '*' };
            var lblStatus = new Label { Location = new Point(25, 215), AutoSize = true, ForeColor = Color.Gray };
            var btnOk = new Button { Text = "Подключиться", Location = new Point(215, 235), Width = 160, Height = 30, BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnOk.FlatAppearance.BorderSize = 0;

            btnOk.Click += async (s, e) =>
            {
                lblStatus.Text = "Проверка...";
                lblStatus.ForeColor = Color.Gray;
                var (ok, networkId) = await CheckNetworkAsync(txtServer.Text, txtNetwork.Text, txtPass.Text);
                if (ok)
                {
                    SaveConfig(txtServer.Text, txtNetwork.Text, networkId, txtPass.Text, _deviceId);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    lblStatus.Text = "Неверное имя сети или пароль";
                    lblStatus.ForeColor = Color.Red;
                }
            };

            form.Controls.AddRange(new Control[] { lblTitle, lblServer, txtServer, lblNetwork, txtNetwork, lblPass, txtPass, lblStatus, btnOk });
            return form.ShowDialog() == DialogResult.OK;
        }

        private static async Task<(bool success, string networkId)> CheckNetworkAsync(string server, string network, string password)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.PostAsync($"{server}/api/specialist/networks/login-by-name",
                    new StringContent(JsonSerializer.Serialize(new { networkName = network, password }), Encoding.UTF8, "application/json"));
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(json);
                    return (true, result.GetProperty("networkId").GetString()!);
                }
            }
            catch { }
            return (false, "");
        }

        private static void SaveConfig(string server, string network, string networkId, string password, string deviceId)
        {
            try
            {
                var folder = GetConfigFolder();
                Directory.CreateDirectory(folder);
                var config = new AgentConfigData { ServerUrl = server, NetworkName = network, NetworkId = networkId, DeviceId = deviceId };
                File.WriteAllText(GetConfigFile(), JsonSerializer.Serialize(config));
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.LocalMachine);
                File.WriteAllBytes(GetPasswordFile(), bytes);
            }
            catch { }
        }

        private static void LoadConfig()
        {
            try
            {
                if (File.Exists(GetConfigFile()))
                {
                    var config = JsonSerializer.Deserialize<AgentConfigData>(File.ReadAllText(GetConfigFile()))!;
                    _networkName = config.NetworkName;
                    _deviceId = config.DeviceId;
                    _serverUrl = config.ServerUrl;
                }
            }
            catch { }
        }

        private static void StartAgentService()
        {
            try
            {
                var agentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "M1.Agent.Service.exe");
                if (File.Exists(agentPath))
                {
                    _agentProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = agentPath,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                }
            }
            catch { }
        }

        private static void OnDiagnostic(object? sender, EventArgs e)
        {
            var diagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Agent.UI.exe");
            if (File.Exists(diagPath))
            {
                Process.Start(diagPath);
            }
            else
            {
                OnStatus(sender, e);
            }
        }

        private static void OnStatus(object? sender, EventArgs e)
        {
            var running = _agentProcess != null && !_agentProcess.HasExited;
            var info = $"Сеть: {_networkName}\nID: {_deviceId}\nСервер: {_serverUrl}\n\nАгент: {(running ? "Запущен" : "Остановлен")}";
            MessageBox.Show(info, "M1 Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void OnExit(object? sender, EventArgs e)
        {
            try { if (_agentProcess != null && !_agentProcess.HasExited) _agentProcess.Kill(); } catch { }
            _icon?.Dispose();
            Application.Exit();
        }

        private static string GetConfigFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M1Agent");
        private static string GetConfigFile() => Path.Combine(GetConfigFolder(), "config.json");
        private static string GetPasswordFile() => Path.Combine(GetConfigFolder(), "network.dat");

        private class AgentConfigData
        {
            public string ServerUrl { get; set; } = "http://192.168.1.85:5230";
            public string NetworkName { get; set; } = "";
            public string NetworkId { get; set; } = "";
            public string DeviceId { get; set; } = "";
        }
    }
}