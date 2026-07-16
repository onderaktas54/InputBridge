using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using InputBridge.Linux.Native;

namespace InputBridge.Linux.Desktop;

internal sealed class MainWindow : Window
{
    private static readonly IBrush WindowBrush = MakeBrush("#070B14");
    private static readonly IBrush CardBrush = MakeBrush("#0D1524");
    private static readonly IBrush CardAltBrush = MakeBrush("#111C2E");
    private static readonly IBrush CardBorderBrush = MakeBrush("#23314A");
    private static readonly IBrush PrimaryBrush = MakeBrush("#12B8D6");
    private static readonly IBrush VioletBrush = MakeBrush("#8B5CF6");
    private static readonly IBrush TextBrush = MakeBrush("#F5F9FF");
    private static readonly IBrush MutedBrush = MakeBrush("#91A2BA");
    private static readonly IBrush SuccessBrush = MakeBrush("#22C55E");
    private static readonly IBrush WarningBrush = MakeBrush("#F59E0B");
    private static readonly IBrush ErrorBrush = MakeBrush("#EF4444");

    private readonly ConnectionController _controller = new();
    private readonly DesktopSettings _settings;
    private readonly List<string> _logLines = [];

    private readonly ComboBox _roleBox;
    private readonly TextBox _secretBox;
    private readonly TextBox _hostBox;
    private readonly TextBox _portBox;
    private readonly CheckBox _rememberBox;
    private readonly TextBlock _hostLabel;
    private readonly TextBlock _roleHelp;
    private readonly Button _connectButton;
    private readonly Button _stopButton;
    private readonly TextBlock _statusTitle;
    private readonly TextBlock _statusDetail;
    private readonly Border _statusDot;
    private readonly TextBox _logBox;
    private readonly TextBlock _permissionText;

    public MainWindow()
    {
        _settings = DesktopSettingsStore.Load();

        Title = "InputBridge · Linux";
        Width = 980;
        Height = 720;
        MinWidth = 820;
        MinHeight = 620;
        Background = WindowBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        TrySetIcon();

        _roleBox = new ComboBox
        {
            ItemsSource = new[]
            {
                "Client · Bu cihaz kontrol edilir",
                "Host · Bu cihaz kontrol eder",
            },
            SelectedIndex = _settings.Role == DesktopRole.Client ? 0 : 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        _secretBox = CreateInput("En az 16 karakter");
        _secretBox.PasswordChar = '●';
        _secretBox.Text = _settings.RememberSecret ? _settings.Secret : "";

        _hostBox = CreateInput("Boş bırak: otomatik keşif");
        _hostBox.Text = _settings.HostAddress;

        _portBox = CreateInput("7201");
        _portBox.Text = _settings.Port.ToString();

        _rememberBox = new CheckBox
        {
            Content = "Secret Key'i bu cihazda hatırla",
            IsChecked = _settings.RememberSecret,
            Foreground = MutedBrush,
        };

        _hostLabel = CreateLabel("HOST IP · İSTEĞE BAĞLI");
        _roleHelp = new TextBlock
        {
            Foreground = MutedBrush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        _connectButton = CreateButton("Bağlan", true);
        _connectButton.Click += async (_, _) => await StartAsync();

        _stopButton = CreateButton("Bağlantıyı kes", false);
        _stopButton.IsEnabled = false;
        _stopButton.Click += async (_, _) => await StopAsync();

        _statusTitle = new TextBlock
        {
            Text = "Bağlantı kapalı",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextBrush,
        };
        _statusDetail = new TextBlock
        {
            Text = "Ayarlarını girip Bağlan'a bas.",
            FontSize = 13,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        _statusDot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = MakeBrush("#08101D"),
            BorderBrush = CardBorderBrush,
            Foreground = MakeBrush("#BED2EA"),
            FontSize = 12,
            MinHeight = 250,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);

        _permissionText = new TextBlock
        {
            FontSize = 12,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = BuildLayout();
        UpdateRoleUi();
        UpdatePermissionState();

        _roleBox.SelectionChanged += (_, _) => UpdateRoleUi();
        _controller.StatusChanged += OnStatusChanged;
        UiLogSink.MessageReceived += OnLogMessage;
        Closed += async (_, _) =>
        {
            UiLogSink.MessageReceived -= OnLogMessage;
            await _controller.DisposeAsync();
            await Serilog.Log.CloseAndFlushAsync();
        };
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
                new RowDefinition(GridLength.Auto),
            },
        };

        Control header = BuildHeader();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new Grid
        {
            Margin = new Thickness(24, 20, 24, 20),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1.05, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(20)),
                new ColumnDefinition(new GridLength(0.95, GridUnitType.Star)),
            },
        };

        Control form = BuildConnectionCard();
        Grid.SetColumn(form, 0);
        content.Children.Add(form);

        Control activity = BuildActivityColumn();
        Grid.SetColumn(activity, 2);
        content.Children.Add(activity);

        Grid.SetRow(content, 1);
        root.Children.Add(content);

        Control footer = BuildFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return root;
    }

    private Control BuildHeader()
    {
        var panel = new Grid
        {
            Margin = new Thickness(24, 18),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(14)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };

        try
        {
            var logo = new Image
            {
                Source = new Bitmap(
                    AssetLoader.Open(new Uri("avares://InputBridge.Linux/Assets/logo.png"))),
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
            };
            Grid.SetColumn(logo, 0);
            panel.Children.Add(logo);
        }
        catch
        {
            var fallback = new TextBlock
            {
                Text = "⌁",
                FontSize = 40,
                Foreground = PrimaryBrush,
            };
            Grid.SetColumn(fallback, 0);
            panel.Children.Add(fallback);
        }

        var titles = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "InputBridge",
                    FontSize = 25,
                    FontWeight = FontWeight.Bold,
                    Foreground = TextBrush,
                },
                new TextBlock
                {
                    Text = "LINUX DESKTOP · SECURE NETWORK KVM",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = PrimaryBrush,
                    LetterSpacing = 1.2,
                },
            },
        };
        Grid.SetColumn(titles, 2);
        panel.Children.Add(titles);

        var secure = new Border
        {
            Background = MakeBrush("#112B2A"),
            BorderBrush = MakeBrush("#1F6B60"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 7),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "●  AES-256-GCM",
                Foreground = MakeBrush("#6EE7C7"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
            },
        };
        Grid.SetColumn(secure, 3);
        panel.Children.Add(secure);

        return new Border
        {
            Background = MakeBrush("#0A111E"),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel,
        };
    }

    private Control BuildConnectionCard()
    {
        var stack = new StackPanel { Spacing = 13 };
        stack.Children.Add(CreateSectionTitle("BAĞLANTI"));
        stack.Children.Add(CreateLabel("ROL"));
        stack.Children.Add(_roleBox);
        stack.Children.Add(_roleHelp);
        stack.Children.Add(CreateLabel("SECRET KEY"));

        var secretRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(8)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        secretRow.Children.Add(_secretBox);
        var reveal = CreateButton("Göster", false);
        reveal.MinWidth = 76;
        reveal.Click += (_, _) =>
        {
            bool hidden = _secretBox.PasswordChar != '\0';
            _secretBox.PasswordChar = hidden ? '\0' : '●';
            reveal.Content = hidden ? "Gizle" : "Göster";
        };
        Grid.SetColumn(reveal, 2);
        secretRow.Children.Add(reveal);
        stack.Children.Add(secretRow);
        stack.Children.Add(_rememberBox);
        stack.Children.Add(_hostLabel);
        stack.Children.Add(_hostBox);
        stack.Children.Add(CreateLabel("TCP PORT"));
        stack.Children.Add(_portBox);

        var actions = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(10)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        actions.Children.Add(_connectButton);
        Grid.SetColumn(_stopButton, 2);
        actions.Children.Add(_stopButton);
        stack.Children.Add(actions);

        var safety = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Background = MakeBrush("#101B2D"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13),
            Child = new TextBlock
            {
                Text = "Client modunda Windows Host bu cihazı kontrol eder. " +
                       "Host modunda Ctrl+Alt+S yönlendirmeyi açar; Ctrl+Alt+Esc acil bırakır.",
                Foreground = MutedBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        stack.Children.Add(safety);

        return CreateCard(stack);
    }

    private Control BuildActivityColumn()
    {
        var stack = new StackPanel { Spacing = 16 };

        var statusPanel = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(12)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        Grid.SetColumn(_statusDot, 0);
        statusPanel.Children.Add(_statusDot);

        var statusText = new StackPanel
        {
            Spacing = 4,
            Children = { _statusTitle, _statusDetail },
        };
        Grid.SetColumn(statusText, 2);
        statusPanel.Children.Add(statusText);

        var statusStack = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CreateSectionTitle("DURUM"),
                statusPanel,
            },
        };
        stack.Children.Add(CreateCard(statusStack));

        var logStack = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                CreateSectionTitle("CANLI AKIŞ"),
                _logBox,
            },
        };
        stack.Children.Add(CreateCard(logStack));

        return stack;
    }

    private Control BuildFooter()
    {
        var panel = new Grid
        {
            Margin = new Thickness(24, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        panel.Children.Add(_permissionText);

        var version = new TextBlock
        {
            Text = "InputBridge Linux · v1.2",
            FontSize = 11,
            Foreground = MutedBrush,
        };
        Grid.SetColumn(version, 1);
        panel.Children.Add(version);

        return new Border
        {
            Background = MakeBrush("#0A111E"),
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = panel,
        };
    }

    private async Task StartAsync()
    {
        string secret = _secretBox.Text ?? "";
        if (secret.Length < 16)
        {
            ShowValidation("Secret Key en az 16 karakter olmalı.");
            return;
        }

        if (!int.TryParse(_portBox.Text, out int port) || port is < 2 or > 65535)
        {
            ShowValidation("Port 2–65535 arasında geçerli bir sayı olmalı.");
            return;
        }

        DesktopRole role = _roleBox.SelectedIndex == 1
            ? DesktopRole.Host
            : DesktopRole.Client;
        string? host = string.IsNullOrWhiteSpace(_hostBox.Text)
            ? null
            : _hostBox.Text.Trim();

        _settings.Role = role;
        _settings.HostAddress = host ?? "";
        _settings.Port = port;
        _settings.RememberSecret = _rememberBox.IsChecked == true;
        _settings.Secret = secret;
        DesktopSettingsStore.Save(_settings);

        SetRunningUi(true);
        AddLog("[UI] Bağlantı başlatılıyor…");
        await _controller.StartAsync(new ConnectionRequest(role, secret, host, port));
    }

    private async Task StopAsync()
    {
        _stopButton.IsEnabled = false;
        await _controller.StopAsync();
        SetRunningUi(false);
    }

    private void OnStatusChanged(LinuxConnectionStatus status, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            (string title, IBrush color) = status switch
            {
                LinuxConnectionStatus.Discovering => ("Host aranıyor", PrimaryBrush),
                LinuxConnectionStatus.Waiting => ("Client bekleniyor", WarningBrush),
                LinuxConnectionStatus.Connecting => ("Bağlanıyor", VioletBrush),
                LinuxConnectionStatus.Connected => ("Bağlandı", SuccessBrush),
                LinuxConnectionStatus.Reconnecting => ("Yeniden bağlanıyor", WarningBrush),
                LinuxConnectionStatus.Error => ("Bağlantı hatası", ErrorBrush),
                _ => ("Bağlantı kapalı", MutedBrush),
            };

            _statusTitle.Text = title;
            _statusDetail.Text = message;
            _statusDot.Background = color;

            bool running = status != LinuxConnectionStatus.Stopped;
            SetRunningUi(running);
        });
    }

    private void OnLogMessage(string message) =>
        Dispatcher.UIThread.Post(() => AddLog(message));

    private void AddLog(string message)
    {
        _logLines.Add(message);
        if (_logLines.Count > 250) _logLines.RemoveRange(0, 50);
        _logBox.Text = string.Join(Environment.NewLine, _logLines);
        _logBox.CaretIndex = _logBox.Text?.Length ?? 0;
    }

    private void UpdateRoleUi()
    {
        bool client = _roleBox.SelectedIndex != 1;
        _hostLabel.IsVisible = client;
        _hostBox.IsVisible = client;
        _roleHelp.Text = client
            ? "Windows masaüstünün klavye ve faresini bu Linux cihazda kullan."
            : "Bu Linux cihazın klavye ve faresini başka bir Client'a yönlendir.";
        _connectButton.Content = client ? "Host'u bul ve bağlan" : "Host'u başlat";
    }

    private void SetRunningUi(bool running)
    {
        _connectButton.IsEnabled = !running;
        _stopButton.IsEnabled = running;
        _roleBox.IsEnabled = !running;
        _secretBox.IsEnabled = !running;
        _hostBox.IsEnabled = !running;
        _portBox.IsEnabled = !running;
        _rememberBox.IsEnabled = !running;
    }

    private void UpdatePermissionState()
    {
        int fd = NativeMethods.open(
            "/dev/uinput",
            NativeMethods.O_WRONLY | NativeMethods.O_NONBLOCK);
        bool available = fd >= 0;
        if (available) NativeMethods.close(fd);

        _permissionText.Text = available
            ? "●  uinput hazır · X11 ve Wayland uyumlu"
            : "●  /dev/uinput erişimi yok · udev kuralını kur";
        _permissionText.Foreground = available ? SuccessBrush : ErrorBrush;
    }

    private void ShowValidation(string message)
    {
        _statusTitle.Text = "Ayarları kontrol et";
        _statusDetail.Text = message;
        _statusDot.Background = ErrorBrush;
    }

    private void TrySetIcon()
    {
        try
        {
            using Stream stream =
                AssetLoader.Open(new Uri("avares://InputBridge.Linux/Assets/logo.png"));
            Icon = new WindowIcon(stream);
        }
        catch
        {
            // Non-critical.
        }
    }

    private static TextBlock CreateSectionTitle(string text) => new()
    {
        Text = text,
        Foreground = PrimaryBrush,
        FontWeight = FontWeight.Bold,
        FontSize = 12,
        LetterSpacing = 1.1,
    };

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        Foreground = MutedBrush,
        FontWeight = FontWeight.SemiBold,
        FontSize = 11,
    };

    private static TextBox CreateInput(string watermark) => new()
    {
        PlaceholderText = watermark,
        Background = MakeBrush("#08101D"),
        BorderBrush = CardBorderBrush,
        Foreground = TextBrush,
        Padding = new Thickness(11, 9),
        CornerRadius = new CornerRadius(7),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static Button CreateButton(string text, bool primary) => new()
    {
        Content = text,
        Background = primary ? PrimaryBrush : CardAltBrush,
        Foreground = primary ? MakeBrush("#031116") : TextBrush,
        BorderBrush = primary ? PrimaryBrush : CardBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 10),
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private static Border CreateCard(Control child) => new()
    {
        Background = CardBrush,
        BorderBrush = CardBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(20),
        Child = child,
    };

    private static IBrush MakeBrush(string hex) =>
        new SolidColorBrush(Color.Parse(hex));
}
