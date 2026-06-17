using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace M1.Admin.UI
{
    public partial class LoginWindow : Window
    {
        private static readonly string CredentialsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "M1Admin", "credentials.dat");

        public string ServerUrl { get; private set; } = "";
        public string Token { get; private set; } = "";

        public LoginWindow()
        {
            InitializeComponent();
            LoadSavedCredentials();
        }

        private void LoadSavedCredentials()
        {
            try
            {
                if (File.Exists(CredentialsPath))
                {
                    var encrypted = File.ReadAllBytes(CredentialsPath);
                    var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    var json = Encoding.UTF8.GetString(decrypted);
                    var creds = JsonSerializer.Deserialize<SavedCredentials>(json);
                    if (creds != null)
                    {
                        ServerBox.Text = creds.ServerUrl;
                        LoginBox.Text = creds.Login;
                        PasswordBox.Password = creds.Password;
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
                    var creds = new SavedCredentials
                    {
                        ServerUrl = ServerBox.Text,
                        Login = LoginBox.Text,
                        Password = PasswordBox.Password
                    };
                    var json = JsonSerializer.Serialize(creds);
                    var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                    Directory.CreateDirectory(Path.GetDirectoryName(CredentialsPath)!);
                    File.WriteAllBytes(CredentialsPath, encrypted);
                }
                else
                {
                    if (File.Exists(CredentialsPath)) File.Delete(CredentialsPath);
                }
            }
            catch { }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var server = ServerBox.Text.Trim();
            var login = LoginBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
            {
                ErrorText.Text = "Заполните все поля";
                return;
            }

            ErrorText.Text = "Подключение...";
            ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212));

            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(server), Timeout = TimeSpan.FromSeconds(10) };
                var resp = await http.PostAsync("/api/specialist/admin-login",
                    new StringContent(JsonSerializer.Serialize(new { login, password }), Encoding.UTF8, "application/json"));

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(json);
                    Token = result.GetProperty("token").GetString()!;
                    ServerUrl = server;

                    SaveCredentials();

                    DialogResult = true;
                    Close();
                }
                else
                {
                    ErrorText.Text = "Неверный логин или пароль";
                    ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 47, 47));
                }
            }
            catch
            {
                ErrorText.Text = "Сервер недоступен";
                ErrorText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(211, 47, 47));
            }
        }

        private class SavedCredentials
        {
            public string ServerUrl { get; set; } = "";
            public string Login { get; set; } = "";
            public string Password { get; set; } = "";
        }
    }
}