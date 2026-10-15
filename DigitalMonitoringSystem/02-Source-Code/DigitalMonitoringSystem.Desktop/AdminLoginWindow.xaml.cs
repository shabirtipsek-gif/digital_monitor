using System.Windows;

namespace DigitalMonitoringSystem.Desktop;

public partial class AdminLoginWindow : Window
{
    public AdminLoginWindow()
    {
        InitializeComponent();
    }

    public string Username => UsernameInput.Text.Trim();

    public string Password => PasswordInput.Password;

    private void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage.Text = "Enter both username and password.";
            return;
        }

        DialogResult = true;
    }
}