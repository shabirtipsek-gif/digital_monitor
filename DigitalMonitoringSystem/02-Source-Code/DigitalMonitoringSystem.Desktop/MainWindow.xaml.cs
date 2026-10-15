using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.AspNetCore.SignalR.Client;

namespace DigitalMonitoringSystem.Desktop;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private readonly HubConnection _hubConnection;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel();
        DataContext = _viewModel;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri("http://localhost:5103/hub/dashboard"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_viewModel.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<DashboardSummary>("ReceiveDashboard", payload =>
        {
            Dispatcher.Invoke(() => _viewModel.UpdateFromSummary(payload));
        });

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _refreshTimer.Tick += async (_, _) => await _viewModel.LoadAsync();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        while (true)
        {
            var loginWindow = new AdminLoginWindow { Owner = this };
            if (loginWindow.ShowDialog() != true)
            {
                Close();
                return;
            }

            if (await _viewModel.AuthenticateAsync(loginWindow.Username, loginWindow.Password))
            {
                break;
            }

            MessageBox.Show("Administrator sign-in failed.", "Sign-in failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        await _viewModel.LoadAsync();
        _refreshTimer.Start();

        try
        {
            await _hubConnection.StartAsync();
        }
        catch
        {
            // Live hub is optional for desktop fallback; polling remains active.
        }
    }

}