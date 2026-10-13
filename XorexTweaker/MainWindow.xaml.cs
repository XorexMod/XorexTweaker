using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using XorexTweaker.Models;
using XorexTweaker.Services;

namespace XorexTweaker;

public partial class MainWindow : Window
{
    private readonly TweakEngine _engine = new();
    private readonly WingetService _winget = new();
    private readonly BypassService _bypass = new();
    private readonly ProfileService _profiles = new();
    private readonly RestorePointService _restore = new();
    private readonly ZapretDownloader _zapretDownloader = new();
    private readonly UpdateService _updates = new();
    private readonly SettingsService _settings = new();

    private TweakConfig _tweaks = new();
    private AppConfig _apps = new();
    private DebloatConfig _debloat = new();

    private string _currentView = "dashboard";
    private bool _wasMinimized = false;
    private readonly Dictionary<string, bool> _categoryExpanded = new();

    private UpdateInfo? _pendingUpdate = null;
    private bool _updateChecked = false;

    public MainWindow()
    {
        InitializeComponent();
        LoadData();
        WireNav();
        ShowDashboard();
        StateChanged += OnWindowStateChanged;
        Closing += (_, _) => _bypass.Stop();

        if (_settings.Current.AutoCheckUpdates)
            _ = CheckUpdatesInBackgroundAsync();
    }

    // ============================================================
    // WM_GETMINMAXINFO — фикс разворота с кастомным title bar
    // ============================================================
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            GetMonitorInfo(monitor, monitorInfo);

            var rcWorkArea = monitorInfo.rcWork;
            var rcMonitorArea = monitorInfo.rcMonitor;

            mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
            mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
            mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
            mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            mmi.ptMinTrackSize.x = 800;
            mmi.ptMinTrackSize.y = 500;
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    // ============================================================
    // TITLE BAR + АНИМАЦИИ
    // ============================================================
    private void TitleBar_Minimize(object sender, RoutedEventArgs e)
    {
        if (_settings.Current.DisableAnimations)
        {
            WindowState = WindowState.Minimized;
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => { WindowState = WindowState.Minimized; };
        RootBorder.BeginAnimation(OpacityProperty, fade);
    }

    private void TitleBar_Maximize(object sender, RoutedEventArgs e)
    {
        var target = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        if (!_settings.Current.DisableAnimations)
        {
            if (RootBorder.RenderTransform is not ScaleTransform)
                RootBorder.RenderTransform = new ScaleTransform(1, 1);
            RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            var scale = new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var st = (ScaleTransform)RootBorder.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        WindowState = target;
        UpdateMaximizeState();
    }

    private void TitleBar_Close(object sender, RoutedEventArgs e)
    {
        if (_settings.Current.DisableAnimations)
        {
            Close();
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Close();
        RootBorder.BeginAnimation(OpacityProperty, fade);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            TitleBar_Maximize(sender, e);
        }
        else if (e.ClickCount == 1 && WindowState == WindowState.Maximized)
        {
            var mousePos = e.GetPosition(this);
            var percentHorizontal = mousePos.X / ActualWidth;
            var percentVertical = mousePos.Y / ActualHeight;

            WindowState = WindowState.Normal;

            var screenPos = PointToScreen(mousePos);
            Left = screenPos.X - (RestoreBounds.Width * percentHorizontal);
            Top = screenPos.Y - (RestoreBounds.Height * percentVertical);

            UpdateMaximizeState();
        }
        else
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _wasMinimized = true;
        }
        else if (WindowState == WindowState.Normal && _wasMinimized)
        {
            _wasMinimized = false;
            RootBorder.BeginAnimation(OpacityProperty, null);
            RootBorder.Opacity = 1;

            if (!_settings.Current.DisableAnimations)
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                RootBorder.BeginAnimation(OpacityProperty, fade);
            }
        }
    }

    private void UpdateMaximizeState()
    {
        BtnMax.Content = WindowState == WindowState.Maximized
            ? "\uE923"
            : "\uE922";
    }

    // ============================================================
    // RESTART EXPLORER
    // ============================================================
    private void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
            System.Threading.Thread.Sleep(500);
            Process.Start("explorer.exe");
            SetStatus("✓ Проводник перезапущен");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка: " + ex.Message);
            SetStatus("✗ Не удалось перезапустить проводник");
        }
    }

    // ============================================================
    // DATA
    // ============================================================

    private void LoadData()
{
    var opt = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    try
    {
        _tweaks = JsonSerializer.Deserialize<TweakConfig>(
            ResourceReader.ReadText(Path.Combine("Data", "tweaks.json")), opt) ?? new();
    }
    catch (Exception ex)
    {
        MessageBox.Show("Ошибка загрузки tweaks.json:\n" + ex.Message, "XorexTweaker");
    }

    try
    {
        _apps = JsonSerializer.Deserialize<AppConfig>(
            ResourceReader.ReadText(Path.Combine("Data", "apps.json")), opt) ?? new();
    }
    catch { }

    try
    {
        _debloat = JsonSerializer.Deserialize<DebloatConfig>(
            ResourceReader.ReadText(Path.Combine("Data", "debloat.json")), opt) ?? new();
    }
    catch { }
}

    private void WireNav()
    {
        NavDashboard.Checked += (_, _) => ShowDashboard();
        NavTweaks.Checked    += (_, _) => ShowTweaks();
        NavDebloat.Checked   += (_, _) => ShowDebloat();
        NavInstall.Checked   += (_, _) => ShowInstall();
        NavBypass.Checked    += (_, _) => ShowBypass();
        NavSettings.Checked  += (_, _) => ShowSettings();
        NavAbout.Checked     += (_, _) => ShowAbout();
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void SetContent(UIElement content)
    {
        MainContent.Content = content;
        MainContent.BeginAnimation(OpacityProperty, null);

        if (_settings.Current.DisableAnimations)
        {
            MainContent.Opacity = 1;
            return;
        }

        MainContent.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        MainContent.BeginAnimation(OpacityProperty, fade);
    }

    // ============================================================
    // UPDATES
    // ============================================================
    private async Task CheckUpdatesInBackgroundAsync()
    {
        var info = await _updates.CheckAsync();
        _updateChecked = true;

        if (info == null || !info.IsNewer)
        {
            _pendingUpdate = null;
            return;
        }

        _pendingUpdate = info;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_currentView == "dashboard") ShowDashboard();
        });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    // ============================================================
    // DASHBOARD
    // ============================================================
    private void ShowDashboard()
    {
        _currentView = "dashboard";

        var sp = new StackPanel();
        sp.Children.Add(Header("Добро пожаловать 👋", "Быстрые действия и информация о системе"));

        if (_pendingUpdate != null)
        {
            var updRow = new StackPanel { Orientation = Orientation.Horizontal };
            updRow.Children.Add(Button($"🎉 Скачать v{_pendingUpdate.LatestVersion}",
                () => OpenUrl(_pendingUpdate.HtmlUrl)));
            updRow.Children.Add(Button("Что нового?",
                () => MessageBox.Show(
                    string.IsNullOrWhiteSpace(_pendingUpdate.Body)
                        ? "Список изменений пуст."
                        : _pendingUpdate.Body,
                    $"Релиз v{_pendingUpdate.LatestVersion}")));

            sp.Children.Add(Card(
                $"🎉 Доступна новая версия: v{_pendingUpdate.LatestVersion}",
                Text($"Ваша текущая: v{_pendingUpdate.CurrentVersion}"),
                Text($"Дата релиза: {_pendingUpdate.PublishedAt:yyyy-MM-dd}"),
                updRow));
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var card1 = Card("⚡ Быстрые действия",
            Button("Открыть твики", () => NavTweaks.IsChecked = true),
            Button("Установка программ", () => NavInstall.IsChecked = true),
            Button("Обход блокировок", () => NavBypass.IsChecked = true));

        var card2 = Card("💾 Резервные копии",
            Text($"Папка: {_engine.Backup.BackupDir}"),
            Button("Создать точку восстановления Windows", async () =>
            {
                SetStatus("Создаю точку восстановления...");
                var (ok, msg) = await _restore.CreateAsync("XorexTweaker: перед применением твиков");
                MessageBox.Show(msg);
                SetStatus(ok ? "✓ Точка восстановления создана" : "✗ Не удалось создать точку");
            }),
            Button("Восстановить всё из бэкапов реестра", () =>
            {
                var n = _engine.Backup.RestoreAll();
                MessageBox.Show($"Восстановлено записей: {n}");
                SetStatus($"Восстановлено: {n}");
            }));

        Grid.SetColumn(card1, 0); Grid.SetColumn(card2, 1);
        grid.Children.Add(card1); grid.Children.Add(card2);
        sp.Children.Add(grid);

        SetContent(sp);
    }

    // ============================================================
    // TWEAKS + ПОИСК + ПРОФИЛИ + АККОРДЕОН
    // ============================================================
    private void ShowTweaks()
    {
        _currentView = "tweaks";

        var sp = new StackPanel();
        sp.Children.Add(Header("Твики системы",
            "Переключатели применяются мгновенно. Бэкап реестра делается автоматически."));

        // ===== Панель профилей =====
        var profilePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        profilePanel.Children.Add(Button("💾 Экспорт профиля", () =>
        {
            var enabled = _tweaks.Categories
                .SelectMany(c => c.Tweaks)
                .Where(t => _engine.IsTweakEnabled(t))
                .Select(t => t.Id)
                .ToList();

            if (enabled.Count == 0)
            {
                MessageBox.Show("Нет включённых твиков — нечего экспортировать.");
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Профиль XorexTweaker (*.xtweak)|*.xtweak|JSON (*.json)|*.json",
                FileName = $"xorex-profile-{DateTime.Now:yyyyMMdd-HHmm}.xtweak"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                _profiles.Export(sfd.FileName, Path.GetFileNameWithoutExtension(sfd.FileName), enabled);
                SetStatus($"✓ Профиль сохранён ({enabled.Count} твиков)");
                MessageBox.Show($"Профиль сохранён:\n{sfd.FileName}\n\nТвиков: {enabled.Count}");
            }
            catch (Exception ex) { MessageBox.Show("Ошибка экспорта: " + ex.Message); }
        }));
        profilePanel.Children.Add(Button("📂 Импорт профиля", async () =>
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Профиль XorexTweaker (*.xtweak)|*.xtweak|JSON (*.json)|*.json|Все файлы (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true) return;

            TweakProfile profile;
            try { profile = _profiles.Import(ofd.FileName); }
            catch (Exception ex) { MessageBox.Show("Ошибка импорта: " + ex.Message); return; }

            var confirm = MessageBox.Show(
                $"Профиль «{profile.Name}» от {profile.CreatedAt:yyyy-MM-dd HH:mm}\n" +
                $"Содержит твиков: {profile.EnabledTweaks.Count}\n\n" +
                "Применить эти твики?",
                "Импорт профиля", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            int ok = 0, fail = 0;
            var allTweaks = _tweaks.Categories.SelectMany(c => c.Tweaks).ToList();
            foreach (var id in profile.EnabledTweaks)
            {
                var t = allTweaks.FirstOrDefault(x => x.Id == id);
                if (t == null) { fail++; continue; }
                if (_engine.IsTweakEnabled(t)) { ok++; continue; }
                if (await _engine.ApplyAsync(t, true)) ok++; else fail++;
            }
            SetStatus($"Профиль применён: {ok} успешно, {fail} ошибок");
            MessageBox.Show($"Профиль применён.\nУспешно: {ok}\nОшибок: {fail}");
            ShowTweaks();
        }));

        // ===== Поиск =====
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var searchBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Height = 40,
            Padding = new Thickness(12, 0, 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var placeholder = new TextBlock
        {
            Text = "🔍   Поиск по названию или описанию...",
            Foreground = (Brush)FindResource("MutedBrush"),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            FontSize = 13
        };
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(placeholder);

        // ===== Панель «Развернуть/Свернуть все» =====
        var expandPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var tweaksContainer = new StackPanel();

        expandPanel.Children.Add(Button("▼ Развернуть все", () =>
        {
            foreach (var cat in _tweaks.Categories)
                _categoryExpanded[cat.Id] = true;
            PopulateTweaks(tweaksContainer, null);
            SetStatus("Все категории развёрнуты");
        }));
        expandPanel.Children.Add(Button("▶ Свернуть все", () =>
        {
            foreach (var cat in _tweaks.Categories)
                _categoryExpanded[cat.Id] = false;
            PopulateTweaks(tweaksContainer, null);
            SetStatus("Все категории свёрнуты");
        }));

        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            var q = searchBox.Text?.Trim() ?? "";
            PopulateTweaks(tweaksContainer, q.Length == 0 ? null : q);
        };

        sp.Children.Add(profilePanel);
        sp.Children.Add(searchGrid);
        sp.Children.Add(expandPanel);
        sp.Children.Add(tweaksContainer);

        PopulateTweaks(tweaksContainer, null);
        SetContent(sp);
    }

    private void PopulateTweaks(StackPanel container, string? query)
    {
        container.Children.Clear();
        int totalShown = 0;
        bool isSearching = query != null;

        foreach (var cat in _tweaks.Categories)
        {
            var matching = cat.Tweaks
                .Where(t => query == null ||
                            t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0) continue;

            bool expanded;
            if (isSearching)
                expanded = true;
            else if (!_categoryExpanded.TryGetValue(cat.Id, out expanded))
                expanded = false;

            // ===== Заголовок категории (кликабельный) =====
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var arrow = new TextBlock
            {
                Text = expanded ? "▼" : "▶",
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var title = new TextBlock
            {
                Text = $"{cat.Icon}  {cat.Name}",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var countBadge = new Border
            {
                Background = (Brush)FindResource("TrackBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = matching.Count.ToString(),
                    FontSize = 11,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    FontWeight = FontWeights.SemiBold
                }
            };

            Grid.SetColumn(arrow, 0);
            Grid.SetColumn(title, 1);
            Grid.SetColumn(countBadge, 2);
            headerGrid.Children.Add(arrow);
            headerGrid.Children.Add(title);
            headerGrid.Children.Add(countBadge);

            var header = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 12, 0, 4),
                Cursor = Cursors.Hand,
                Child = headerGrid
            };

            // ===== Контейнер с твиками =====
            var contentPanel = new StackPanel
            {
                Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
            };

            foreach (var tweak in matching)
            {
                tweak.IsEnabled = _engine.IsTweakEnabled(tweak);

                var row = new Border { Style = (Style)FindResource("CardBorder") };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new StackPanel();
                txt.Children.Add(new TextBlock
                {
                    Text = tweak.Name, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush")
                });
                txt.Children.Add(new TextBlock
                {
                    Text = tweak.Description,
                    Foreground = (Brush)FindResource("MutedBrush"),
                    TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                if (tweak.Risky)
                    txt.Children.Add(new TextBlock { Text = "⚠ Осторожно", Foreground = Brushes.Orange, FontSize = 11 });

                var toggle = new CheckBox
                {
                    IsChecked = tweak.IsEnabled,
                    Style = (Style)FindResource("ToggleSwitch")
                };
                toggle.Checked   += async (_, _) => await ApplyTweak(tweak, true, toggle);
                toggle.Unchecked += async (_, _) => await ApplyTweak(tweak, false, toggle);

                g.Children.Add(txt);
                Grid.SetColumn(toggle, 1);
                g.Children.Add(toggle);
                row.Child = g;
                contentPanel.Children.Add(row);
            }

            var capturedCat = cat;
            var capturedArrow = arrow;
            var capturedContent = contentPanel;

            header.MouseLeftButtonDown += (_, _) =>
            {
                bool isVisible = capturedContent.Visibility == Visibility.Visible;
                capturedContent.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
                capturedArrow.Text = isVisible ? "▶" : "▼";
                _categoryExpanded[capturedCat.Id] = !isVisible;
            };

            header.MouseEnter += (_, _) => header.Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1F, 0x2A));
            header.MouseLeave += (_, _) => header.Background = Brushes.Transparent;

            container.Children.Add(header);
            container.Children.Add(contentPanel);
            totalShown += matching.Count;
        }

        if (totalShown == 0)
        {
            container.Children.Add(new TextBlock
            {
                Text = "Ничего не найдено 🤷",
                Foreground = (Brush)FindResource("MutedBrush"),
                FontSize = 14,
                Margin = new Thickness(0, 32, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            SetStatus("Поиск: 0 результатов");
        }
        else if (query != null)
        {
            SetStatus($"Поиск: найдено {totalShown}");
        }
        else
        {
            SetStatus($"Твиков загружено: {totalShown}");
        }
    }

    private async Task ApplyTweak(Tweak t, bool enable, CheckBox src)
    {
        if (enable && t.Risky && _settings.Current.ConfirmRiskyTweaks)
        {
            var res = MessageBox.Show(
                $"Твик «{t.Name}» помечен как ОПАСНЫЙ.\n\n" +
                t.Description + "\n\n" +
                "Включить его?",
                "⚠ Осторожно",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes)
            {
                src.IsChecked = false;
                return;
            }
        }

        if (enable && _settings.Current.AutoCreateRestorePoint)
        {
            SetStatus("Создаю точку восстановления...");
            var (ok, _) = await _restore.CreateAsync($"XorexTweaker: {t.Name}");
            if (!ok) SetStatus("Не удалось создать точку, продолжаю...");
        }

        SetStatus($"Применяю: {t.Name}...");
        src.IsEnabled = false;
        var success = await _engine.ApplyAsync(t, enable);
        src.IsEnabled = true;
        SetStatus(success ? $"✓ {t.Name}" : $"✗ Ошибка: {t.Name}");
        if (!success) src.IsChecked = !enable;
        if (success && t.RequiresReboot)
            MessageBox.Show($"«{t.Name}» требует перезагрузки.");
    }

    // ============================================================
    // DEBLOAT
    // ============================================================
    private async void ShowDebloat()
    {
        _currentView = "debloat";

        var sp = new StackPanel();
        sp.Children.Add(Header("Debloat",
            "Удаление встроенных UWP-приложений. Показаны только те, что реально установлены."));

        var loading = new TextBlock
        {
            Text = "Проверяю установленные приложения...",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 8)
        };
        sp.Children.Add(loading);
        SetContent(sp);

        var semaphore = new SemaphoreSlim(4);
        var checkTasks = _debloat.Packages.Select(async p =>
        {
            await semaphore.WaitAsync();
            try { return (p, ok: await IsUwpInstalledAsync(p.PackageName)); }
            finally { semaphore.Release(); }
        });

        var results = await Task.WhenAll(checkTasks);
        if (_currentView != "debloat") return;

        var installed = results.Where(r => r.ok).Select(r => r.p).ToList();
        sp.Children.Remove(loading);

        if (installed.Count == 0)
        {
            sp.Children.Add(Card("✨ Чисто!",
                Text("Все встроенные UWP-приложения, которые твикер знает, уже удалены.")));
            SetStatus("Debloat: нечего удалять.");
            return;
        }

        foreach (var p in installed)
        {
            var row = new Border { Style = (Style)FindResource("CardBorder") };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var txt = new StackPanel();
            txt.Children.Add(new TextBlock
            {
                Text = p.FriendlyName, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush")
            });
            txt.Children.Add(new TextBlock
            {
                Text = $"{p.Description}  ·  {p.PackageName}",
                Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12
            });

            var btn = new Button
            {
                Content = "Удалить",
                Style = (Style)FindResource("PrimaryButton"),
                MinWidth = 120
            };
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                btn.Content = "Удаление...";
                SetStatus($"Удаляю {p.FriendlyName}...");

                var cmd = $"Get-AppxPackage -AllUsers *{p.PackageName}* | Remove-AppxPackage -ErrorAction SilentlyContinue; " +
                          $"Get-AppxProvisionedPackage -Online | Where-Object DisplayName -like '*{p.PackageName}*' | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{cmd}\"",
                    CreateNoWindow = true, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var proc = Process.Start(psi)!;
                await proc.WaitForExitAsync();

                SetStatus($"✓ {p.FriendlyName} удалено");
                row.Visibility = Visibility.Collapsed;
            };

            g.Children.Add(txt);
            Grid.SetColumn(btn, 1);
            g.Children.Add(btn);
            row.Child = g;
            sp.Children.Add(row);
        }

        SetStatus($"Debloat: найдено {installed.Count} приложений.");
    }

    private static async Task<bool> IsUwpInstalledAsync(string packageName)
    {
        try
        {
            var cmd = $"if (Get-AppxPackage -Name '*{packageName}*' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                CreateNoWindow = true, UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // ============================================================
    // INSTALL
    // ============================================================
    private async void ShowInstall()
    {
        _currentView = "install";

        var sp = new StackPanel();
        sp.Children.Add(Header("Установка программ",
            "Используется winget. Уже установленные программы помечены и неактивны."));

        var loading = new TextBlock
        {
            Text = "Проверяю установленные программы...",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12, Margin = new Thickness(0, 8, 0, 8)
        };
        sp.Children.Add(loading);
        SetContent(sp);

        var installedState = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        var total = _apps.Apps.Count;
        var done = 0;
        var semaphore = new SemaphoreSlim(5);

        var checkTasks = _apps.Apps.Select(async app =>
        {
            await semaphore.WaitAsync();
            try
            {
                var ok = await _winget.IsInstalledAsync(app.Id);
                installedState[app.Id] = ok;
                var current = Interlocked.Increment(ref done);
                Application.Current.Dispatcher.Invoke(() =>
                    SetStatus($"Проверяю программы... {current}/{total}"));
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(checkTasks);
        if (_currentView != "install") return;

        sp.Children.Remove(loading);

        var grouped = _apps.Apps.GroupBy(a => a.Category).OrderBy(g => g.Key);
        foreach (var grp in grouped)
        {
            sp.Children.Add(new TextBlock
            {
                Text = grp.Key, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 14, 0, 6)
            });

            foreach (var app in grp)
            {
                var row = new Border { Style = (Style)FindResource("CardBorder") };
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new StackPanel();
                txt.Children.Add(new TextBlock
                {
                    Text = app.Name, FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextBrush")
                });
                txt.Children.Add(new TextBlock
                {
                    Text = $"{app.Description}  ·  {app.Id}",
                    FontSize = 12, Foreground = (Brush)FindResource("MutedBrush")
                });

                var isInstalled = installedState.TryGetValue(app.Id, out var i) && i;
                var btn = new Button
                {
                    Content = isInstalled ? "✓ Установлено" : "Установить",
                    Style = (Style)FindResource("PrimaryButton"),
                    MinWidth = 140,
                    IsEnabled = !isInstalled
                };

                if (!isInstalled)
                {
                    btn.Click += async (_, _) =>
                    {
                        btn.IsEnabled = false;
                        btn.Content = "Установка...";
                        SetStatus($"Устанавливаю {app.Name}...");
                        var ok = await _winget.InstallAsync(app.Id, new Progress<string>(SetStatus));
                        if (ok) { btn.Content = "✓ Установлено"; SetStatus($"✓ {app.Name}"); }
                        else { btn.Content = "Ошибка"; btn.IsEnabled = true; SetStatus($"✗ {app.Name}"); }
                    };
                }

                g.Children.Add(txt);
                Grid.SetColumn(btn, 1);
                g.Children.Add(btn);
                row.Child = g;
                sp.Children.Add(row);
            }
        }

        SetStatus($"Список загружен. Всего: {total} программ.");
    }

    // ============================================================
    // BYPASS
    // ============================================================
    private void ShowBypass()
    {
        _currentView = "bypass";

        var sp = new StackPanel();
        sp.Children.Add(Header("Обход блокировок (РКН)",
            "Использует Zapret. Можно скачать автоматически или положить вручную в Tools/zapret/."));

        sp.Children.Add(Card("Статус",
            Text($"Установлен: {(_bypass.IsInstalled ? "Да" : "Нет")}"),
            Text($"Запущен: {(_bypass.IsRunning ? "Да" : "Нет")}"),
            Text($"Папка: {_bypass.ToolsDir}")
        ));

        var downloadRow = new StackPanel { Orientation = Orientation.Horizontal };
        downloadRow.Children.Add(Button("⬇ Скачать последнюю версию с GitHub", async () =>
        {
            var confirm = MessageBox.Show(
                "Скачать последний релиз Zapret с GitHub и распаковать в Tools/zapret/?\n\n" +
                "Существующая папка Tools/zapret/ будет удалена и заменена.",
                "Скачать Zapret", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var progress = new Progress<string>(SetStatus);
                await _zapretDownloader.DownloadLatestAsync(_bypass.ToolsDir, progress);
                MessageBox.Show("✓ Zapret скачан и распакован.");
                ShowBypass();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка скачивания: " + ex.Message);
                SetStatus("✗ Не удалось скачать Zapret");
            }
        }));
        sp.Children.Add(Card("Автоматическая установка", downloadRow));

        var presets = _bypass.GetPresets().ToList();
        var presetBox = new ComboBox
        {
            Width = 420,
            Margin = new Thickness(0, 8, 0, 8),
            Style = (Style)FindResource("DarkComboBox")
        };
        foreach (var p in presets) presetBox.Items.Add(p);
        if (presets.Count > 0) presetBox.SelectedIndex = 0;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Button("▶ Запустить", () =>
        {
            if (presetBox.SelectedItem is not string file)
            {
                MessageBox.Show("Не найден .bat-пресет. Скачай Zapret выше.");
                return;
            }
            try { _bypass.Start(file); SetStatus($"Zapret запущен: {file}"); }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }));
        row.Children.Add(Button("■ Остановить", () =>
        {
            _bypass.Stop();
            SetStatus("Zapret остановлен.");
        }));

        sp.Children.Add(Card("Управление", presetBox, row));
        SetContent(sp);
    }

    // ============================================================
    // SETTINGS
    // ============================================================
    private void ShowSettings()
    {
        _currentView = "settings";

        var sp = new StackPanel();
        sp.Children.Add(Header("Настройки",
            "Настройки сохраняются в settings.json и применяются мгновенно."));

        sp.Children.Add(new TextBlock
        {
            Text = "🎨  Интерфейс",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 12, 0, 6)
        });

        sp.Children.Add(MakeToggleRow(
            "Отключить анимации интерфейса",
            "Убирает плавные переходы и fade-in. Рекомендуется для слабых ПК и старых видеокарт.",
            _settings.Current.DisableAnimations,
            v =>
            {
                _settings.Current.DisableAnimations = v;
                _settings.Save();
                SetStatus(v ? "Анимации отключены" : "Анимации включены");
            }));

        sp.Children.Add(new TextBlock
        {
            Text = "🔄  Обновления",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 16, 0, 6)
        });

        sp.Children.Add(MakeToggleRow(
            "Проверять обновления твикера при запуске",
            "Тихий запрос к GitHub API. Займёт ~1 секунду, ни на что не влияет.",
            _settings.Current.AutoCheckUpdates,
            v =>
            {
                _settings.Current.AutoCheckUpdates = v;
                _settings.Save();
                SetStatus(v ? "Автопроверка обновлений включена" : "Автопроверка обновлений отключена");
            }));

        sp.Children.Add(new TextBlock
        {
            Text = "🛡️  Безопасность",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 16, 0, 6)
        });

        sp.Children.Add(MakeToggleRow(
            "Подтверждать применение «⚠ Осторожно»-твиков",
            "Перед включением опасных твиков будет запрос. Крайне рекомендуется оставить включённым.",
            _settings.Current.ConfirmRiskyTweaks,
            v =>
            {
                _settings.Current.ConfirmRiskyTweaks = v;
                _settings.Save();
                SetStatus(v ? "Подтверждение включено" : "Подтверждение отключено");
            }));

        sp.Children.Add(MakeToggleRow(
            "Автосоздание точки восстановления перед каждым твиком",
            "Может замедлить работу твикера — Windows тратит 5-30 сек на точку.",
            _settings.Current.AutoCreateRestorePoint,
            v =>
            {
                _settings.Current.AutoCreateRestorePoint = v;
                _settings.Save();
                SetStatus(v ? "Автосоздание точки включено" : "Автосоздание точки отключено");
            }));

        sp.Children.Add(new TextBlock
        {
            Text = "📁  Файлы и папки",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 16, 0, 6)
        });

        var backupCard = Card("💾 Бэкапы реестра",
            Text($"Папка: {_engine.Backup.BackupDir}"),
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    Button("📂 Открыть папку", () =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(_engine.Backup.BackupDir)
                            { UseShellExecute = true });
                        }
                        catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
                    }),
                    Button("🗑 Очистить все бэкапы", () =>
                    {
                        var res = MessageBox.Show(
                            "Удалить все сохранённые бэкапы реестра?\n\n" +
                            "Отменить это действие будет НЕВОЗМОЖНО.",
                            "Очистка", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res != MessageBoxResult.Yes) return;
                        try
                        {
                            int deleted = 0;
                            foreach (var f in Directory.GetFiles(_engine.Backup.BackupDir, "*.json"))
                            {
                                File.Delete(f); deleted++;
                            }
                            SetStatus($"Удалено бэкапов: {deleted}");
                            MessageBox.Show($"Удалено бэкапов: {deleted}");
                        }
                        catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message); }
                    })
                }
            });
        sp.Children.Add(backupCard);

        sp.Children.Add(new TextBlock
        {
            Text = "⚠️  Сброс",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Orange,
            Margin = new Thickness(0, 16, 0, 6)
        });

        sp.Children.Add(Card("Сброс настроек",
            Text("Вернуть все настройки к заводским значениям. Бэкапы реестра НЕ удаляются."),
            Button("🔄 Сбросить настройки", () =>
            {
                var res = MessageBox.Show(
                    "Сбросить все настройки к заводским значениям?",
                    "Сброс", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                _settings.Reset();
                SetStatus("Настройки сброшены");
                ShowSettings();
            })));

        SetContent(sp);
    }

    private Border MakeToggleRow(string title, string description, bool initialValue, Action<bool> onChanged)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var txt = new StackPanel();
        txt.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        txt.Children.Add(new TextBlock
        {
            Text = description,
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var toggle = new CheckBox
        {
            IsChecked = initialValue,
            Style = (Style)FindResource("ToggleSwitch")
        };
        toggle.Checked   += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);

        g.Children.Add(txt);
        Grid.SetColumn(toggle, 1);
        g.Children.Add(toggle);

        return new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Child = g
        };
    }

    // ============================================================
    // ABOUT + ДОНАТ
    // ============================================================
    private void ShowAbout()
    {
        _currentView = "about";

        var sp = new StackPanel();
        sp.Children.Add(Header("О программе", "XorexTweaker · MIT License"));

        var versionText = new TextBlock
        {
            Text = $"Версия: v{_updates.GetCurrentVersion()}",
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        sp.Children.Add(versionText);

        var authorText = new TextBlock
        {
            Text = "Идея и разработка: XorexMod",
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        sp.Children.Add(authorText);

        var statusText = new TextBlock
        {
            Text = _updateChecked
                ? (_pendingUpdate != null
                    ? $"🎉 Доступна новая версия v{_pendingUpdate.LatestVersion}"
                    : "✓ У вас последняя версия")
                : "Нажмите «Проверить обновления», чтобы узнать о новой версии",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
        sp.Children.Add(statusText);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Button("🔄 Проверить обновления", async () =>
        {
            SetStatus("Проверяю обновления...");
            var info = await _updates.CheckAsync();
            _updateChecked = true;

            if (info == null)
            {
                statusText.Text = "✗ Не удалось проверить обновления (нет сети или репозиторий недоступен)";
                SetStatus("Проверка обновлений не удалась");
                return;
            }

            if (info.IsNewer)
            {
                _pendingUpdate = info;
                statusText.Text = $"🎉 Доступна новая версия v{info.LatestVersion} (у вас v{info.CurrentVersion})";
                SetStatus($"Доступна новая версия v{info.LatestVersion}");

                var res = MessageBox.Show(
                    $"Доступна новая версия: v{info.LatestVersion}\n" +
                    $"У вас: v{info.CurrentVersion}\n\n" +
                    "Открыть страницу релиза в браузере?",
                    "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes) OpenUrl(info.HtmlUrl);
            }
            else
            {
                _pendingUpdate = null;
                statusText.Text = $"✓ У вас последняя версия (v{info.CurrentVersion})";
                SetStatus("Обновлений нет");
            }
        }));

        if (_pendingUpdate != null)
        {
            row.Children.Add(Button("⬇ Открыть страницу релиза",
                () => OpenUrl(_pendingUpdate.HtmlUrl)));
        }

        sp.Children.Add(row);

        sp.Children.Add(new TextBlock
        {
            Text = "💛  Поддержать автора",
            FontSize = 16, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 20, 0, 6)
        });

        var donateCard = Card("Спасибо за использование!",
            Text("XorexTweaker — бесплатный и открытый. Если он сэкономил тебе время — можешь кинуть на печеньки автору. Если не можешь или не хочешь — просто пользуйся, мне приятно 💜"));

        var donateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        donateRow.Children.Add(Button("🎁 Boosty",         () => OpenUrl("https://boosty.to/xorexhome/donate")));

        var donateSp = (StackPanel)donateCard.Child;
        donateSp.Children.Add(donateRow);
        sp.Children.Add(donateCard);

        var socialRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        socialRow.Children.Add(Button("🐙 GitHub",    () => OpenUrl("https://github.com/XorexMod/XorexTweaker")));
        sp.Children.Add(socialRow);

        sp.Children.Add(Text(""));
        sp.Children.Add(Text("Это open-source твикер для Windows 10/11. Автор не несёт ответственности за последствия использования."));
        sp.Children.Add(Text("Лицензия MIT позволяет свободно использовать, изменять и распространять код при условии сохранения уведомления об авторских правах."));
        sp.Children.Add(Text("Список сторонних проектов см. в THIRD-PARTY-NOTICES.txt"));

        SetContent(sp);
    }

    // ============================================================
    // HELPERS
    // ============================================================
    private static StackPanel Header(string title, string subtitle)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 26, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEF))
        });
        sp.Children.Add(new TextBlock
        {
            Text = subtitle, FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0xA6)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return sp;
    }

    private Border Card(string title, params UIElement[] children)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var c in children) sp.Children.Add(c);
        return new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Child = sp,
            Margin = new Thickness(0, 6, 8, 6)
        };
    }

    private TextBlock Text(string s) => new()
    {
        Text = s,
        Foreground = (Brush)FindResource("MutedBrush"),
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Margin = new Thickness(0, 2, 0, 2)
    };

    private Button Button(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 6, 8, 0)
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button Button(string text, Func<Task> onClickAsync)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)FindResource("PrimaryButton"),
            Margin = new Thickness(0, 6, 8, 0)
        };
        b.Click += async (_, _) => await onClickAsync();
        return b;
    }
}