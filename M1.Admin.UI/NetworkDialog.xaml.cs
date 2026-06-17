using System.Windows;

namespace M1.Admin.UI
{
    public partial class NetworkDialog : Window
    {
        public string NetworkName => NameBox.Text.Trim();
        public string Username => NameBox.Text.Trim(); // Название = Логин
        public string Password => PasswordBox.Text;
        public string Description => NetworkName;

        public NetworkDialog()
        {
            InitializeComponent();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NetworkName))
            {
                ErrorText.Text = "Введите название";
                return;
            }
            if (Password.Length < 4)
            {
                ErrorText.Text = "Пароль: мин. 4 символа";
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}