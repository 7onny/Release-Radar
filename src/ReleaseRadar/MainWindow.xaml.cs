using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using ReleaseRadar.Core;

namespace ReleaseRadar;

public partial class MainWindow : Window
{
    private readonly CacheStore store;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private CalendarCache? cache;
    private DateOnly month;
    private CancellationTokenSource? refreshCancellation;
    private GameRelease? selected;
    private Button? focusBeforeModal;
    private string? notice;
    private bool closing;
    private int calendarRows = 5;
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-GB");
    private DateOnly Today => Ranking.LondonDate(DateTimeOffset.UtcNow);
    private DateOnly MetricsDate => cache is null ? Today : Ranking.LondonDate(cache.UpdatedAt);
    private string PreferencesPath => Path.Combine(store.DirectoryPath, "window.json");

    public MainWindow(CacheStore store)
    {
        InitializeComponent();
        this.store = store;
        month = new(Today.Year, Today.Month, 1);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReleaseRadar/1.0");
        SourceInitialized += (_, _) => { var enabled = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int)); };
        Loaded += async (_, _) => { PlaceOnRequestedMonitor(); await InitialiseAsync(); };
        Closing += (_, _) => { closing = true; refreshCancellation?.Cancel(); SavePreferences(); };
        Closed += (_, _) => http.Dispose();
        PreviewKeyDown += Window_KeyDown;
        RestorePreferences();
        Render();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    private void PlaceOnRequestedMonitor()
    {
        if (App.NearWindow == IntPtr.Zero) return;
        var target = MonitorFromWindow(App.NearWindow, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var own = new WindowInteropHelper(this).Handle;
        WindowState = WindowState.Normal;
        if (!GetMonitorInfo(target, ref info) || !GetWindowRect(own, out var bounds)) return;
        var width = Math.Min(bounds.Right - bounds.Left, info.Work.Right - info.Work.Left - 60);
        var height = Math.Min(bounds.Bottom - bounds.Top, info.Work.Bottom - info.Work.Top - 60);
        // Move only our own window; the supplied window identifies the desired monitor.
        SetWindowPos(own, IntPtr.Zero, info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2,
            info.Work.Top + (info.Work.Bottom - info.Work.Top - height) / 2, width, height, 0x0014);
    }

    private async Task InitialiseAsync()
    {
        if (App.Demo)
        {
            cache = DemoData.Create(); month = new(2026, 11, 1);
            notice = "DEMO PREVIEW · Historical sample from our planning conversation. Not a live calendar.";
            Render(); return;
        }
        try { cache = await store.LoadAsync(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        { notice = "Could not read the saved calendar. " + ex.Message; }
        Render();
        // Existing caches are never refreshed automatically, regardless of their age.
        if (cache is null) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshCancellation is not null) return;
        if (App.Demo) { notice = "This is an isolated demo. Open Release Radar normally to refresh real Steam data."; Render(); return; }
        refreshCancellation = new();
        RefreshButton.IsEnabled = false;
        PreviousButton.IsEnabled = NextButton.IsEnabled = false;
        CancelButton.Visibility = RefreshProgress.Visibility = Visibility.Visible;
        notice = "Refreshing from Steam. Your current calendar stays available until the new data is ready.";
        Render();
        var token = refreshCancellation.Token;
        var progress = new Progress<string>(message => { if (!closing) StatusText.Text = message; });
        try
        {
            var updated = await new SteamService(http).RefreshAsync(cache, month, progress, token);
            await store.SaveAsync(updated, token);
            cache = updated;
            notice = updated.Warnings.Count > 0 ? string.Join(" ", updated.Warnings) : null;
            if (selected is not null) CloseModal();
        }
        catch (OperationCanceledException)
        { notice = "Refresh cancelled or timed out. Your saved calendar has been kept."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        { notice = "Refresh could not finish. " + ex.Message + " Your saved calendar has been kept."; }
        finally
        {
            refreshCancellation.Dispose(); refreshCancellation = null;
            if (!closing)
            {
                RefreshButton.IsEnabled = PreviousButton.IsEnabled = NextButton.IsEnabled = true;
                CancelButton.Visibility = RefreshProgress.Visibility = Visibility.Collapsed;
                Render();
            }
        }
    }

    private void Render()
    {
        MonthTitle.Text = month.ToString("MMMM yyyy", Culture);
        var all = cache?.Games ?? [];
        var releases = all.Where(g => g.ReleaseDate?.Year == month.Year && g.ReleaseDate?.Month == month.Month).ToList();
        var displayed = releases.GroupBy(g => g.ReleaseDate!.Value).SelectMany(g => Ranking.ForDay(g, g.Key)).ToList();
        var flames = displayed.Count(g => Ranking.GetTrend(g, MetricsDate)?.IsTrending == true);
        MonthSubtitle.Text = "The most anticipated releases. Up to three, every day.";
        StatsText.Text = $"{displayed.Count} releases  ·  {flames} trending";
        UpdatedText.Text = cache is null ? "No saved data" : $"Last updated {TimeZoneInfo.ConvertTime(cache.UpdatedAt, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")).ToString("dd MMM yyyy · HH:mm", Culture)} UK";
        if (refreshCancellation is null) StatusText.Text = App.Demo ? "DEMO DATA · No network access" : cache is null ? "Click Refresh calendar to get started" : "●  Saved on this device  ·  Refresh to check for changes";
        var message = notice;
        if (message is null && cache is not null && displayed.Count == 0)
            message = month < new DateOnly(Today.Year, Today.Month, 1)
                ? "No saved releases for this month. Past months only include releases this app has previously recorded."
                : "No dated releases found in the saved Steam wishlist catalogue for this month. Coverage may be incomplete; dates can still be announced.";
        if (message is null && cache is not null && displayed.Count > 0 && flames == 0)
            message = "Flames appear when a game gains 250+ followers and 10% in seven days. Missing observations are never treated as zero growth.";
        NoticeText.Text = message ?? "";
        NoticeBorder.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        CalendarGrid.Children.Clear();
        var cells = Ranking.MonthCells(month);
        calendarRows = cells.Count / 7; CalendarGrid.Rows = calendarRows;
        foreach (var day in cells) CalendarGrid.Children.Add(BuildDay(day, all));
        ResizeCalendar();
        UnconfirmedPanel.Children.Clear();
        var unconfirmed = Ranking.Order(all.Where(g => g.ReleaseDate is null)).ToList();
        UnconfirmedExpander.Header = $"Date unconfirmed  ·  {unconfirmed.Count:N0} games  ·  showing the 20 most anticipated";
        foreach (var game in unconfirmed.Take(20))
        {
            var button = new Button { Content = game.Name + "  ·  " + game.ReleaseText, Margin = new(0,0,8,8), MaxWidth = 440, ToolTip = game.Name };
            button.Click += (_, _) => OpenGame(game, button);
            UnconfirmedPanel.Children.Add(button);
        }
    }

    private Border BuildDay(DateOnly day, List<GameRelease> games)
    {
        var currentMonth = day.Month == month.Month;
        var today = day == Today;
        var cell = new Border
        {
            Background = Brush(currentMonth ? "#131922" : "#0F141C"), BorderBrush = Brush(today ? "#829FC8" : "#283140"),
            BorderThickness = new(today ? 1.5 : 0.5), CornerRadius = new(7), Margin = new(3), Padding = new(9),
            Opacity = currentMonth ? 1 : 0.45
        };
        var panel = new StackPanel();
        var header = new DockPanel { Margin = new(2,0,2,8) };
        if (today)
        {
            var label = new TextBlock { Text = "TODAY", Foreground = Brush("#BDD4F8"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Right); header.Children.Add(label);
        }
        header.Children.Add(new TextBlock { Text = day.Day.ToString(Culture), Foreground = Brush(today ? "#D6E6FF" : "#A9B5C9"), FontSize = 13, FontWeight = today ? FontWeights.Bold : FontWeights.Normal });
        panel.Children.Add(header);
        if (currentMonth)
        {
            var picks = Ranking.ForDay(games, day);
            foreach (var game in picks) panel.Children.Add(BuildCard(game));
        }
        cell.Child = panel;
        return cell;
    }

    private Button BuildCard(GameRelease game)
    {
        var (background, accent, label) = Colours(game.WishlistRank);
        var trend = Ranking.GetTrend(game, MetricsDate);
        var button = new Button
        {
            Background = Brush(background), BorderBrush = Brush(accent), BorderThickness = new(3,0,0,0),
            Padding = new(8,4,6,4), Margin = new(0,0,0,3), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = $"{game.Name}\n{RankText(game)}\n{TrendText(game)}"
        };
        AutomationProperties.SetName(button, $"{game.Name}, {game.ReleaseDate:dd MMMM yyyy}, {RankText(game)}{(trend?.IsTrending == true ? ", trending" : "")}");
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = game.Name, Foreground = Brush("#F3EFF0"), FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        var row = new DockPanel { Margin = new(0,1,0,0) };
        if (trend?.IsTrending == true)
        {
            var flame = new System.Windows.Shapes.Path { Data = Geometry.Parse("M 7,0 C 9,4 12,5 12,9 C 12,13 9,15 6,15 C 2,15 0,12 0,9 C 0,6 2,4 3,3 C 3,6 4,7 5,7 C 7,6 8,3 7,0 Z"), Fill = Brush("#FFB679"), Width = 10, Height = 12, Stretch = Stretch.Uniform, ToolTip = TrendText(game) };
            AutomationProperties.SetName(flame, "Trending");
            DockPanel.SetDock(flame, Dock.Right); row.Children.Add(flame);
        }
        row.Children.Add(new TextBlock { Text = game.WishlistRank is { } rank ? $"#{rank:N0}  ·  {label}" : "Rank unavailable", Foreground = Brush(accent), FontSize = 9, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(row); button.Content = content;
        button.Click += (_, _) => OpenGame(game, button);
        return button;
    }

    private static (string Background, string Accent, string Label) Colours(int? rank) => Ranking.Band(rank) switch
    {
        RankBand.High => ("#39232C", "#FF929A", "HIGH"),
        RankBand.Middle => ("#382B22", "#F4B57E", "MID"),
        RankBand.Lower => ("#333125", "#E9D989", "LOWER"),
        _ => ("#263140", "#A8B9CF", "UNRANKED")
    };
    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
    private static string RankText(GameRelease game) => game.WishlistRank is { } rank ? $"Wishlist rank #{rank:N0}" : "Wishlist rank unavailable · ordered by known followers";
    private string TrendText(GameRelease game)
    {
        var trend = Ranking.GetTrend(game, MetricsDate);
        var latest = game.Followers.OrderByDescending(f => f.Date).FirstOrDefault();
        var followers = latest is null ? "Follower count unavailable" : $"{latest.Count:N0} followers · observed {latest.Date:dd MMM}";
        return trend is null ? followers + "\nSeven-day trend unavailable · observations needed on both dates" :
            followers + $"\n{trend.Gain:+#,0;-#,0;0} followers · {trend.Percent:+0.0;-0.0;0.0}% in seven days" + (trend.IsTrending ? " · 🔥 Trending" : " · below trending threshold");
    }

    private void OpenGame(GameRelease game, Button origin)
    {
        selected = game; focusBeforeModal = origin;
        ModalEyebrow.Text = "RELEASE DETAILS"; ModalTitle.Text = game.Name;
        ModalDate.Text = game.ReleaseDate?.ToString("dddd, d MMMM yyyy", Culture) ?? game.ReleaseText;
        ModalMetrics.Text = RankText(game) + "\n" + TrendText(game);
        ModalBody.Text = "Save an all-day release reminder in Outlook, or discover the game through trailers on YouTube.";
        OutlookButton.Visibility = LearnMoreButton.Visibility = Visibility.Visible;
        OutlookButton.IsEnabled = game.ReleaseDate.HasValue;
        ActionStatus.Text = game.ReleaseDate.HasValue ? "Dates can change. Imported calendar events do not update automatically." : "A confirmed date is needed to create a calendar event.";
        ModalLayer.Visibility = Visibility.Visible;
        KeyboardNavigation.SetTabNavigation(ModalLayer, KeyboardNavigationMode.Cycle);
        LearnMoreButton.Focus();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        selected = null; focusBeforeModal = sender as Button;
        ModalEyebrow.Text = "A LITTLE LESS NOISE"; ModalTitle.Text = "A calendar for what’s next.";
        ModalDate.Text = "Three releases per day. Ranked by anticipation.";
        ModalMetrics.Text = "RED    #1–500\nORANGE    #501–1,500\nYELLOW    #1,501+\n🔥    250+ followers AND 10% growth in seven days";
        ModalBody.Text = "Wishlist positions come from Steam’s public Top Wishlists order, before filtering to games. Steam’s regional visibility and omissions can affect positions; these are not wishlist counts.\n\nGames only, including Early Access; no demos or DLC. Unranked games follow ranked games, ordered by available follower counts, then title. No minimum popularity cutoff is applied.\n\nRefresh reads the wishlist catalogue and followers for this month’s displayed picks. Seven-day trends require observations on both dates. Other months remain browsable from the cache. Older releases are retained only if previously recorded.\n\nOpening the app and changing months make no network requests. Only Refresh updates an existing cache.";
        OutlookButton.Visibility = LearnMoreButton.Visibility = Visibility.Collapsed; ActionStatus.Text = "";
        ModalLayer.Visibility = Visibility.Visible;
        KeyboardNavigation.SetTabNavigation(ModalLayer, KeyboardNavigationMode.Cycle);
        ModalLayer.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private async void Outlook_Click(object sender, RoutedEventArgs e)
    {
        if (selected?.ReleaseDate is null) return;
        var dialog = new SaveFileDialog { Title = "Save Outlook calendar event", Filter = "Calendar event (*.ics)|*.ics", FileName = $"Steam-{selected.AppId}-{selected.ReleaseDate:yyyy-MM-dd}.ics", DefaultExt = ".ics" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, CalendarExport.Ics(selected, DateTimeOffset.UtcNow), new UTF8Encoding(false));
            try
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                ActionStatus.Text = "Event saved and opened in your calendar app. If prompted, choose Outlook, then review and save the event.";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { ActionStatus.Text = "Event saved. Open the .ics file in Outlook to review and add it to your calendar."; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ActionStatus.Text = "Could not save the event: " + ex.Message; }
    }

    private void LearnMore_Click(object sender, RoutedEventArgs e)
    {
        if (selected is null) return;
        try { Process.Start(new ProcessStartInfo(CalendarExport.YoutubeUrl(selected.Name)) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { ActionStatus.Text = "Could not open your browser. Search YouTube for “" + selected.Name + " trailer”."; }
    }

    private void CloseModal() { ModalLayer.Visibility = Visibility.Collapsed; selected = null; focusBeforeModal?.Focus(); }
    private void CloseModal_Click(object sender, RoutedEventArgs e) => CloseModal();
    private void ModalBackdrop_Click(object sender, MouseButtonEventArgs e) { if (e.OriginalSource == ModalLayer) CloseModal(); }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => refreshCancellation?.Cancel();
    private void Previous_Click(object sender, RoutedEventArgs e) => ChangeMonth(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => ChangeMonth(1);
    private void ChangeMonth(int delta)
    {
        if (refreshCancellation is not null) return;
        if ((delta < 0 && month.Year <= 2000 && month.Month == 1) || (delta > 0 && month.Year >= 2100 && month.Month == 12)) return;
        month = month.AddMonths(delta); CalendarScroll.ScrollToTop(); Render();
    }
    private void Today_Click(object sender, RoutedEventArgs e)
    { if (refreshCancellation is null) { month = new(Today.Year, Today.Month, 1); CalendarScroll.ScrollToTop(); Render(); } }
    private void Calendar_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeCalendar();
    private void ResizeCalendar() { if (CalendarGrid is not null) CalendarGrid.Height = Math.Max(calendarRows * 160, CalendarScroll.ActualHeight - 2); }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseModal(); e.Handled = true; }
        if (ModalLayer.Visibility == Visibility.Visible) return;
        if (e.Key == Key.F5) { _ = RefreshAsync(); e.Handled = true; }
        if (e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) { ChangeMonth(-1); e.Handled = true; }
        if (e.SystemKey == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt) { ChangeMonth(1); e.Handled = true; }
    }

    private sealed record WindowPreferences(double Width, double Height, bool Maximized, double? Left = null, double? Top = null);
    private void RestorePreferences()
    {
        try
        {
            if (!File.Exists(PreferencesPath)) return;
            var p = JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(PreferencesPath));
            if (p is null || !double.IsFinite(p.Width) || !double.IsFinite(p.Height)) return;
            Width = Math.Clamp(p.Width, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
            Height = Math.Clamp(p.Height, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
            if (p.Left is { } left && p.Top is { } top && double.IsFinite(left) && double.IsFinite(top) &&
                left + Width > SystemParameters.VirtualScreenLeft + 100 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                top >= SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
            if (p.Maximized) WindowState = WindowState.Maximized;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }
    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(store.DirectoryPath);
            var bounds = RestoreBounds;
            File.WriteAllText(PreferencesPath, JsonSerializer.Serialize(new WindowPreferences(bounds.Width, bounds.Height, WindowState == WindowState.Maximized, bounds.Left, bounds.Top)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
