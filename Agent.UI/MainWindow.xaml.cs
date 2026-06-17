using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;

namespace Agent.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Collect_Click(object sender, RoutedEventArgs e)
        {
            ResultBox.Text = "Сбор данных...\r\n\r\n";
            
            try
            {
                if (CpuCheck.IsChecked == true)
                {
                    ResultBox.Text += "=== ПРОЦЕССОР ===\r\n";
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor");
                    foreach (var cpu in searcher.Get())
                        ResultBox.Text += $"  {cpu["Name"]} | Ядер: {cpu["NumberOfCores"]}\r\n";
                    ResultBox.Text += "\r\n";
                }

                if (RamCheck.IsChecked == true)
                {
                    ResultBox.Text += "=== ПАМЯТЬ ===\r\n";
                    using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (var mem in searcher.Get())
                    {
                        var total = Convert.ToInt64(mem["TotalVisibleMemorySize"]) * 1024;
                        var free = Convert.ToInt64(mem["FreePhysicalMemory"]) * 1024;
                        ResultBox.Text += $"  Всего: {total / 1024 / 1024 / 1024} ГБ\r\n";
                        ResultBox.Text += $"  Свободно: {free / 1024 / 1024 / 1024} ГБ\r\n";
                    }
                    ResultBox.Text += "\r\n";
                }

                if (DiskCheck.IsChecked == true)
                {
                    ResultBox.Text += "=== ДИСКИ ===\r\n";
                    foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                    {
                        ResultBox.Text += $"  {drive.Name} | Метка: {drive.VolumeLabel} | ФС: {drive.DriveFormat}\r\n";
                        ResultBox.Text += $"  Всего: {drive.TotalSize / 1024 / 1024 / 1024} ГБ | Свободно: {drive.AvailableFreeSpace / 1024 / 1024 / 1024} ГБ\r\n";
                    }
                    ResultBox.Text += "\r\n";
                }

                if (NetCheck.IsChecked == true)
                {
                    ResultBox.Text += "=== СЕТЬ ===\r\n";
                    foreach (var net in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                    {
                        var ip = net.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        ResultBox.Text += $"  {net.Name}\r\n";
                        ResultBox.Text += $"  IP: {ip?.Address} | MAC: {net.GetPhysicalAddress()}\r\n";
                        ResultBox.Text += $"  Скорость: {net.Speed / 1000000} Мбит/с\r\n\r\n";
                    }
                }

                ResultBox.Text += "=== ГОТОВО ===";
            }
            catch (Exception ex)
            {
                ResultBox.Text += $"Ошибка: {ex.Message}";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}