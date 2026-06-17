using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;

namespace M1.Admin.UI
{
    public partial class MainWindow : Window
    {
        private HttpClient? _http;
        private HubConnection? _hub;
        private List<NetworkViewModel> _networks = new();
        private List<DeviceViewModel> _devices = new();
        private string _selectedNetworkId = "";
        private string? _currentToken;
        private static readonly string CredPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M1Admin", "creds.dat");

        public MainWindow()
        {
            InitializeComponent();
            LoadSavedCredentials();
        }

        private void LoadSavedCredentials()
        {
            try
            {
                if (File.Exists(CredPath))
                {
                    var bytes = File.ReadAllBytes(CredPath);
                    var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
                    var c = JsonSerializer.Deserialize<SavedCreds>(json);
                    if (c != null)
                    {
                        ServerBox.Text = c.Url;
                        LoginBox.Text = c.Login;
                        PasswordBox.Password = c.Password;
                        RememberCheck.IsChecked = true;
                    }
                }
            }
            catch { }
        }

        private void SaveCredentials()
        {
            try
            {
                if (RememberCheck.IsChecked == true)
                {
                    var c = new SavedCreds { Url = ServerBox.Text, Login = LoginBox.Text, Password = PasswordBox.Password };
                    var json = JsonSerializer.Serialize(c);
                    var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                    Directory.CreateDirectory(Path.GetDirectoryName(CredPath)!);
                    File.WriteAllBytes(CredPath, bytes);
                }
                else { if (File.Exists(CredPath)) File.Delete(CredPath); }
            }
            catch { }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var url = ServerBox.Text.Trim();
            var login = LoginBox.Text.Trim();
            var pass = PasswordBox.Password;
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(login) || string.IsNullOrEmpty(pass))
            { LoginError.Text = "Заполните все поля"; LoginError.Visibility = Visibility.Visible; return; }
            LoginError.Visibility = Visibility.Collapsed;
            try
            {
                var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.PostAsync("/api/specialist/admin-login",
                    new StringContent(JsonSerializer.Serialize(new { login, password = pass }), Encoding.UTF8, "application/json"));
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    _currentToken = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("token").GetString()!;
                    SaveCredentials();
                    _http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) };
                    _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken);
                    await ConnectSignalRAsync(url);
                    LoginPanel.Visibility = Visibility.Collapsed;
                    AdminPanel.Visibility = Visibility.Visible;
                    await LoadNetworks();
                }
                else { LoginError.Text = "Неверный логин или пароль"; LoginError.Visibility = Visibility.Visible; }
            }
            catch { LoginError.Text = "Сервер недоступен"; LoginError.Visibility = Visibility.Visible; }
        }

        private async Task ConnectSignalRAsync(string serverUrl)
        {
            try
            {
                _hub = new HubConnectionBuilder()
                    .WithUrl($"{serverUrl}/hubs/signaling?sessionId=admin", o => o.AccessTokenProvider = () => Task.FromResult(_currentToken)!)
                    .Build();
                _hub.On<byte[]>("ReceiveScreenshot", imageData => Dispatcher.Invoke(() => ShowScreenshot(imageData)));
                _hub.On<string>("ReceiveDiagnosticResult", report => Dispatcher.Invoke(() => MessageBox.Show(report, "Результат", MessageBoxButton.OK, MessageBoxImage.Information)));
                await _hub.StartAsync();
            }
            catch { }
        }

        private void ShowScreenshot(byte[] imageData)
        {
            try
            {
                using var ms = new MemoryStream(imageData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.StreamSource = ms; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                var window = new Window { Title = "Скриншот", Width = bitmap.Width + 20, Height = bitmap.Height + 40, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brushes.Black };
                window.Content = new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.None };
                window.Show();
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка скриншота: {ex.Message}"); }
        }

        private async Task LoadNetworks()
        {
            try
            {
                var resp = await _http!.GetAsync("/api/specialist/networks");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    _networks = JsonSerializer.Deserialize<List<NetworkViewModel>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    NetworksList.ItemsSource = _networks;
                    ServerStatusText.Text = "Подключено";
                    ServerStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
            }
            catch { ServerStatusText.Text = "Недоступен"; ServerStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(211, 47, 47)); }
        }

        private async Task LoadDevices()
        {
            try
            {
                var resp = await _http!.GetAsync("/api/specialist/devices");
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var all = JsonSerializer.Deserialize<List<DeviceViewModel>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                    _devices = string.IsNullOrEmpty(_selectedNetworkId) ? all : all.Where(d => d.NetworkId == _selectedNetworkId).ToList();
                    foreach (var d in _devices)
                    {
                        d.StatusBrush = d.IsOnline ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(200, 200, 200));
                        if (string.IsNullOrEmpty(d.PcName)) d.PcName = d.Id;
                    }
                    DevicesList.ItemsSource = _devices;
                    DeviceCountText.Text = $"{_devices.Count} устройств";
                }
            }
            catch { }
        }

        private async void NetworksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NetworksList.SelectedItem is NetworkViewModel n) { _selectedNetworkId = n.Id; SelectedNetworkText.Text = n.Name; await LoadDevices(); }
        }

        private async void AddNetwork_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NetworkDialog();
            if (dlg.ShowDialog() == true)
            {
                await _http!.PostAsync("/api/specialist/networks", new StringContent(
                    JsonSerializer.Serialize(new { Name = dlg.NetworkName, Description = dlg.Description, Username = dlg.Username, Password = dlg.Password }),
                    Encoding.UTF8, "application/json"));
                await LoadNetworks();
            }
        }

        private async void RefreshDevices_Click(object sender, RoutedEventArgs e) => await LoadDevices();

        private async void RustDeskDevice_Click(object sender, RoutedEventArgs e)
        {
            var d = GetSelectedDevice();
            if (d == null || string.IsNullOrEmpty(d.RustDeskId))
            {
                MessageBox.Show("RustDesk ID не найден", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var password = Guid.NewGuid().ToString("N")[..8];
            await Send(d.Id, "START_RUSTDESK", new { AppType = "RustDesk", AppPath = "", Arguments = "", Password = password });

            var rustDeskPath = @"C:\Program Files\RustDesk\rustdesk.exe";
            if (!File.Exists(rustDeskPath)) rustDeskPath = @"C:\M1Agent\rustdesk.exe";

            if (File.Exists(rustDeskPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = rustDeskPath,
                    Arguments = $"--connect {d.RustDeskId} --password {password}",
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"RustDesk не найден.\nПодключитесь вручную:\nID: {d.RustDeskId}\nПароль: {password}",
                    "RustDesk", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void DiagnosticDevice_Click(object sender, RoutedEventArgs e) => await Send(GetSelectedDevice()?.Id, "RUN_DIAGNOSTIC", new { });
        private async void ScreenshotDevice_Click(object sender, RoutedEventArgs e) => await Send(GetSelectedDevice()?.Id, "SCREENSHOT", new { });
        private async void TaskMgrDevice_Click(object sender, RoutedEventArgs e) => await Send(GetSelectedDevice()?.Id, "CTRL_ALT_DEL", new { });
        private async void RestartExplorerDevice_Click(object sender, RoutedEventArgs e) => await Send(GetSelectedDevice()?.Id, "RESTART_EXPLORER", new { });
        private async void CompMgmtDevice_Click(object sender, RoutedEventArgs e) => await Send(GetSelectedDevice()?.Id, "LAUNCH_APP", new { AppType = "CompMgmt", AppPath = "compmgmt.msc", Arguments = "", ConnectionId = "" });

        private async void SendCustomCmd_Click(object sender, RoutedEventArgs e)
        {
            var d = GetSelectedDevice();
            if (d == null) return;
            var cmd = Microsoft.VisualBasic.Interaction.InputBox("Введите команду для запуска на клиенте:", "Отправить команду", "");
            if (!string.IsNullOrEmpty(cmd))
                await Send(d.Id, "RUN_CMD", new { AppType = "Cmd", AppPath = "cmd.exe", Arguments = cmd });
        }

        private void CopyId_Click(object sender, RoutedEventArgs e)
        {
            var d = GetSelectedDevice();
            if (d != null) { Clipboard.SetText(d.Id); MessageBox.Show("Скопировано: " + d.Id); }
        }

        private DeviceViewModel? GetSelectedDevice() => DevicesList.SelectedItem as DeviceViewModel;

        private async Task Send(string? deviceId, string type, object payload)
        {
            if (deviceId == null) return;
            try
            {
                var resp = await _http!.PostAsync("/api/specialist/command",
                    new StringContent(JsonSerializer.Serialize(new { deviceId, type, payload = JsonSerializer.Serialize(payload) }), Encoding.UTF8, "application/json"));
                if (!resp.IsSuccessStatusCode) MessageBox.Show($"Ошибка: {resp.StatusCode}");
            }
            catch (Exception ex) { MessageBox.Show($"Ошибка: {ex.Message}"); }
        }

        private class SavedCreds { public string Url { get; set; } = ""; public string Login { get; set; } = ""; public string Password { get; set; } = ""; }
    }

    public class NetworkViewModel { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Devices { get; set; } }
    public class DeviceViewModel
    {
        public string Id { get; set; } = ""; public string? NetworkId { get; set; } public string NetworkName { get; set; } = "";
        public string? RustDeskId { get; set; } public string OsVersion { get; set; } = ""; public DateTime LastSeen { get; set; }
        public bool IsOnline { get; set; } public string PcName { get; set; } = ""; public string LastSeenText => LastSeen.ToString("dd.MM HH:mm");
        public SolidColorBrush? StatusBrush { get; set; }
    }
}