using System.Windows;

namespace SpokysProjectVercel.Views
{
    public partial class PasswordDialog : Window
    {
        public string Password { get; private set; } = "";

        public PasswordDialog(string title, string message, string defaultPassword)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            PasswordBox.Password = defaultPassword;
            PasswordBox.Focus();
        }

        private void OkBtn_Click(object sender, RoutedEventArgs e)
        {
            Password = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
