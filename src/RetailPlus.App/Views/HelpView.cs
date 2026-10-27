using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using RetailPlus.App.Help;
using RetailPlus.App.Infrastructure;

namespace RetailPlus.App.Views;

/// <summary>Help: every topic of the desktop app, searchable, with a button that opens the screen a topic explains.</summary>
public sealed class HelpView : UserControl, IRefreshable
{
    private readonly TextBox _search = new() { Width = 420, Margin = new Thickness(0, 0, 12, 6), FontSize = 18 };
    private readonly ListBox _list = new() { Width = 360, Margin = new Thickness(0, 0, 16, 0), DisplayMemberPath = nameof(HelpTopic.Title) };
    private readonly StackPanel _article = new() { Margin = new Thickness(6, 0, 24, 24), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _found = new() { Foreground = Look.Brush("Muted"), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public HelpView()
    {
        var reader = new ScrollViewer { Content = _article, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        body.Children.Add(new Border { Style = Layout.S("CardBorder"), Padding = new Thickness(24, 18, 12, 12), Child = reader });
        Content = Layout.Page("Help", out var bar, body);
        Look.SetPlaceholder(_search, "Search the help: \"end of day\", \"pin\", \"commission\"…");
        bar.Children.Add(_search);
        bar.Children.Add(_found);
        _timer.Tick += (_, _) => { _timer.Stop(); Filter(); };
        _search.TextChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
        _search.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Down && _list.Items.Count > 0) { _list.Focus(); _list.SelectedIndex = Math.Max(0, _list.SelectedIndex); } };
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as HelpTopic);
        // Shown again once in the window, so the topic's "Open" button can find the screens the user may open.
        Loaded += (_, _) => Show(_list.SelectedItem as HelpTopic);
        Filter();
    }

    public Task RefreshAsync()
    {
        Dispatcher.BeginInvoke(() => _search.Focus(), DispatcherPriority.Input);
        return Task.CompletedTask;
    }

    /// <summary>Opens one topic (from the Ctrl+K list or a screen).</summary>
    public void ShowTopic(string key)
    {
        _search.Text = "";
        Filter();
        _list.SelectedItem = HelpTopics.All.FirstOrDefault(t => t.Key == key) ?? _list.SelectedItem;
    }

    /// <summary>Shows the topics that match the search (all of them when it is empty).</summary>
    public void Search(string text)
    {
        _search.Text = text;
        Filter();
    }

    private void Filter()
    {
        var topics = HelpTopics.Search(_search.Text);
        var selected = _list.SelectedItem as HelpTopic;
        _list.ItemsSource = topics;
        _found.Text = string.IsNullOrWhiteSpace(_search.Text) ? $"{topics.Count} topics" : topics.Count == 0 ? "Nothing found. Try another word." : $"{topics.Count} found";
        _list.SelectedItem = selected is not null && topics.Contains(selected) ? selected : topics.Count > 0 ? topics[0] : null;
        if (_list.SelectedItem is null) Show(null);
    }

    private void Show(HelpTopic? topic)
    {
        _article.Children.Clear();
        if (topic is null) return;
        _article.Children.Add(new TextBlock { Text = topic.Title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 30, TextWrapping = TextWrapping.Wrap });
        _article.Children.Add(new TextBlock { Text = topic.Summary, Foreground = Look.Brush("Muted"), FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 6) });
        if (topic.Screen is { } screen && Window.GetWindow(this) is MainWindow main && main.AllowedEntries.FirstOrDefault(e => e.Key == screen) is { } entry)
        {
            var open = Layout.Button("Open " + entry.Title, (_, _) => main.Navigate(screen));
            open.HorizontalAlignment = HorizontalAlignment.Left;
            open.Margin = new Thickness(0, 6, 0, 4);
            _article.Children.Add(open);
        }
        foreach (var section in topic.Sections)
        {
            _article.Children.Add(new TextBlock { Text = section.Heading, Style = Layout.S("SectionTitle"), Margin = new Thickness(0, 18, 0, 6) });
            foreach (var paragraph in section.Paragraphs ?? [])
                _article.Children.Add(new TextBlock { Text = paragraph, TextWrapping = TextWrapping.Wrap, FontSize = 17, LineHeight = 25, Margin = new Thickness(0, 0, 0, 8) });
            int n = 0;
            foreach (var step in section.Steps ?? [])
                _article.Children.Add(Numbered(++n, step));
            foreach (var (term, meaning) in section.Definitions ?? [])
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 17, LineHeight = 25, Margin = new Thickness(0, 0, 0, 6) };
                text.Inlines.Add(new Run(term) { FontWeight = FontWeights.SemiBold });
                text.Inlines.Add(new Run("  " + meaning));
                _article.Children.Add(text);
            }
        }
    }

    private static DockPanel Numbered(int n, string text)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Look.Brush("Sun"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.CurrentCulture), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 17, LineHeight = 25, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
