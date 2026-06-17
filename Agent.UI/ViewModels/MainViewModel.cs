using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Agent.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private CancellationTokenSource? _cts;
        private readonly HttpClient _httpClient;

        [ObservableProperty] private bool _cpuChecked = true;
        [ObservableProperty] private bool _ramChecked = true;
        [ObservableProperty] private bool _diskChecked = true;
        [ObservableProperty] private bool _networkChecked = true;
        [ObservableProperty] private bool _usbChecked = true;
        [ObservableProperty] private bool _comPortsChecked = true;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _logText = string.Empty;
        [ObservableProperty] private string _reportText = string.Empty;

        public double WindowLeft => SystemParameters.WorkArea.Width - 600;
        public double WindowTop => SystemParameters.WorkArea.Height - 650;

        public IRelayCommand StartCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand ToggleThemeCommand { get; }

        public MainViewModel()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://localhost:5230");
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            StartCommand = new RelayCommand(async () => await StartAsync());
            CancelCommand = new RelayCommand(Cancel);
            CloseCommand = new RelayCommand(Close);
            ToggleThemeCommand = new RelayCommand(() => { });
        }

        private async Task StartAsync()
        {
            _cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            IsRunning = true;
            LogText = string.Empty;
            ReportText = string.Empty;
            Log("Сбор данных...");

            try
            {
                var reportJson = new Dictionary<string, object>();
                var sb = new StringBuilder();

                sb.AppendLine("═══════════════════════════════════");
                sb.AppendLine("  ДИАГНОСТИКА СИСТЕМЫ");
                sb.AppendLine($"  Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
                sb.AppendLine("═══════════════════════════════════");
                sb.AppendLine();

                if (CpuChecked)
                {
                    Log("Сбор CPU...");
                    var cpu = GetCpuInfo();
                    reportJson["cpu"] = cpu;
                    sb.AppendLine("▌ПРОЦЕССОР");
                    sb.AppendLine($"  Название : {cpu.Название}");
                    sb.AppendLine($"  Ядер     : {cpu.Ядер}");
                    sb.AppendLine($"  Частота  : {cpu.Частота_МГц} МГц");
                    sb.AppendLine();
                }

                if (RamChecked)
                {
                    Log("Сбор RAM...");
                    var ram = GetRamInfo();
                    reportJson["ram"] = ram;
                    sb.AppendLine("▌ПАМЯТЬ");
                    sb.AppendLine($"  Всего        : {ram.Всего_ГБ} ГБ");
                    sb.AppendLine($"  Свободно     : {ram.Свободно_ГБ} ГБ");
                    sb.AppendLine($"  Использовано : {ram.Использовано_ГБ} ГБ");
                    sb.AppendLine();
                }

                if (DiskChecked)
                {
                    Log("Сбор дисков...");
                    var disks = GetDiskInfo();
                    reportJson["disks"] = disks;
                    sb.AppendLine("▌ДИСКИ");
                    foreach (var disk in disks)
                    {
                        sb.AppendLine($"  [{disk.Буква}] {disk.Метка}");
                        sb.AppendLine($"      Тип       : {disk.Тип}");
                        sb.AppendLine($"      Файловая  : {disk.ФайловаяСистема}");
                        sb.AppendLine($"      Всего     : {disk.Всего_ГБ} ГБ");
                        sb.AppendLine($"      Свободно  : {disk.Свободно_ГБ} ГБ");
                        sb.AppendLine();
                    }
                }

                if (NetworkChecked)
                {
                    Log("Сбор сети...");
                    var net = GetNetworkInfo();
                    reportJson["network"] = net;
                    sb.AppendLine("▌СЕТЕВЫЕ АДАПТЕРЫ");
                    int i = 1;
                    foreach (var n in net)
                    {
                        sb.AppendLine($"  [{i}] {n.Адаптер}");
                        sb.AppendLine($"      IP-адрес    : {n.IP}");
                        sb.AppendLine($"      Маска       : {n.Маска}");
                        sb.AppendLine($"      Шлюз        : {n.Шлюз}");
                        sb.AppendLine($"      DNS         : {n.DNS}");
                        sb.AppendLine($"      MAC         : {n.MAC}");
                        sb.AppendLine($"      Тип адреса  : {n.ТипАдреса}");
                        sb.AppendLine($"      Скорость    : {n.Скорость} Мбит/с");
                        sb.AppendLine();
                        i++;
                    }
                }

                if (UsbChecked)
                {
                    Log("Сбор USB...");
                    var usb = GetUsbInfo();
                    reportJson["usb"] = usb;
                    sb.AppendLine("▌USB УСТРОЙСТВА");
                    if (usb.Count == 0)
                        sb.AppendLine("  (не найдено)");
                    else
                        foreach (var u in usb)
                            sb.AppendLine($"  • {u.Название} [{u.Производитель}]");
                    sb.AppendLine();
                }

                if (ComPortsChecked)
                {
                    Log("Сбор COM-портов...");
                    var com = GetComPortsInfo();
                    reportJson["com"] = com;
                    sb.AppendLine("▌COM ПОРТЫ");
                    if (com.Count == 0)
                        sb.AppendLine("  (не найдено)");
                    else
                        foreach (var p in com)
                            sb.AppendLine($"  • {p.Порт} — {p.Название}");
                    sb.AppendLine();
                }

                sb.AppendLine("═══════════════════════════════════");

                Log("Данные собраны");
                ReportText = sb.ToString();

                var json = JsonSerializer.Serialize(reportJson);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Log("Отправка отчёта на сервер...");
                var response = await _httpClient.PostAsync("/api/specialist/diagnostic", content, _cts.Token);

                if (response.IsSuccessStatusCode)
                    Log("Отчёт отправлен успешно!");
                else
                    Log($"Ошибка сервера: {response.StatusCode}");
            }
            catch (OperationCanceledException)
            {
                Log("Отменено.");
            }
            catch (HttpRequestException)
            {
                Log("Сервер недоступен.");
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private dynamic GetCpuInfo()
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor");
            var cpu = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            return new
            {
                Название = cpu?["Name"]?.ToString()?.Trim() ?? "Неизвестно",
                Ядер = cpu?["NumberOfCores"] ?? 0,
                Частота_МГц = cpu?["MaxClockSpeed"] ?? 0
            };
        }

        private dynamic GetRamInfo()
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            var mem = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            var total = Convert.ToInt64(mem?["TotalVisibleMemorySize"] ?? 0) * 1024;
            var free = Convert.ToInt64(mem?["FreePhysicalMemory"] ?? 0) * 1024;
            return new
            {
                Всего_ГБ = Math.Round(total / 1024.0 / 1024.0 / 1024.0, 1),
                Свободно_ГБ = Math.Round(free / 1024.0 / 1024.0 / 1024.0, 1),
                Использовано_ГБ = Math.Round((total - free) / 1024.0 / 1024.0 / 1024.0, 1)
            };
        }

        private List<dynamic> GetDiskInfo()
        {
            return System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => (dynamic)new
                {
                    Буква = d.Name.TrimEnd('\\'),
                    Метка = string.IsNullOrEmpty(d.VolumeLabel) ? "—" : d.VolumeLabel,
                    Тип = d.DriveType.ToString(),
                    ФайловаяСистема = d.DriveFormat,
                    Всего_ГБ = Math.Round(d.TotalSize / 1024.0 / 1024.0 / 1024.0, 1),
                    Свободно_ГБ = Math.Round(d.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 1)
                })
                .ToList();
        }

        private List<dynamic> GetNetworkInfo()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n =>
                {
                    try
                    {
                        var ipProps = n.GetIPProperties();
                        var ip = ipProps.UnicastAddresses
                            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        var gateways = ipProps.GatewayAddresses
                            .Select(g => g.Address.ToString());
                        var dns = ipProps.DnsAddresses
                            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                            .Select(d => d.ToString());

                        var addressType = "Неизвестно";
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(
                                $"SELECT DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True AND MACAddress = '{string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))}'");
                            var result = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
                            addressType = result?["DHCPEnabled"]?.ToString() == "True" ? "DHCP" : "Статический";
                        }
                        catch { }

                        return (dynamic)new
                        {
                            Адаптер = n.Name,
                            IP = ip?.Address.ToString() ?? "—",
                            Маска = ip?.IPv4Mask?.ToString() ?? "—",
                            Шлюз = string.Join(", ", gateways.DefaultIfEmpty("—")),
                            DNS = string.Join(", ", dns.DefaultIfEmpty("—")),
                            MAC = string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                            ТипАдреса = addressType,
                            Скорость = n.Speed / 1000000
                        };
                    }
                    catch
                    {
                        return (dynamic)new
                        {
                            Адаптер = n.Name,
                            IP = "—",
                            Маска = "—",
                            Шлюз = "—",
                            DNS = "—",
                            MAC = "—",
                            ТипАдреса = "—",
                            Скорость = n.Speed / 1000000
                        };
                    }
                }).ToList();
        }

        private List<dynamic> GetUsbInfo()
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer FROM Win32_PnPEntity WHERE PNPClass='USB' OR PNPClass='Ports'");
            return searcher.Get()
                .Cast<ManagementBaseObject>()
                .Select(u => (dynamic)new
                {
                    Название = u?["Name"]?.ToString() ?? "Неизвестно",
                    Производитель = u?["Manufacturer"]?.ToString() ?? "Неизвестно"
                })
                .Where(u => u.Название != "Неизвестно")
                .ToList();
        }

        private List<dynamic> GetComPortsInfo()
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID FROM Win32_SerialPort");
            return searcher.Get()
                .Cast<ManagementBaseObject>()
                .Select(p => (dynamic)new
                {
                    Порт = p?["DeviceID"]?.ToString() ?? "?",
                    Название = p?["Name"]?.ToString() ?? "?"
                })
                .ToList();
        }

        private void Cancel() => _cts?.Cancel();
        private void Close() => Application.Current.Shutdown();
        private void Log(string msg) => LogText += $"{DateTime.Now:HH:mm:ss} {msg}\n";
    }
}