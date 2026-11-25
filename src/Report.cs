using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskDesk
{
    public sealed class ReportLine
    {
        public string Caption { get; set; }
        public string Badge { get; set; }
        public string Hint { get; set; }
        public Item Task { get; set; }
        public bool IsHeading { get { return Task == null; } }
    }

    public class ReportPane : Grid
    {
        static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(33, 50, 58));
        static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(77, 97, 106));
        static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(22, 97, 93));
        readonly Func<List<Item>> source;
        readonly Action<Item> edit;
        readonly ComboBox period = new ComboBox();
        readonly ComboBox grouping = new ComboBox();
        readonly DatePicker start = new DatePicker();
        readonly DatePicker end = new DatePicker();
        readonly TextBox search = new TextBox();
        readonly Dictionary<string, CheckBox> priorities = new Dictionary<string, CheckBox>();
        readonly TextBlock summary = new TextBlock();
        readonly TextBlock status = new TextBlock();
        readonly ListBox results = new ListBox();
        readonly Button copy = new Button();
        readonly Button editButton = new Button();
        bool updating;
        string clipboardText = "";

        public ReportPane(Func<List<Item>> source, Action<Item> edit)
        {
            this.source = source;
            this.edit = edit;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var timing = new WrapPanel();
            AddAt(timing, 0);
            period.ItemsSource = new[] { "Эта неделя", "Прошлая неделя", "Этот месяц", "Прошлый месяц", "Этот год", "Прошлый год", "Свой период" };
            period.SelectedIndex = 0;
            timing.Children.Add(Field("Период", period, 148));
            timing.Children.Add(Field("С", start, 128));
            timing.Children.Add(Field("По", end, 128));
            grouping.ItemsSource = new[] { "По дням", "По важности" };
            grouping.SelectedIndex = 0;
            timing.Children.Add(Field("Группировка", grouping, 140));
            start.FirstDayOfWeek = end.FirstDayOfWeek = DayOfWeek.Monday;
            ApplyPeriod();

            var filters = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
            AddAt(filters, 1);
            filters.Children.Add(Field("Поиск по названию", search, 236));
            var choices = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string key in new[] { "A", "B", "C", "*", "" })
            {
                var check = new CheckBox {
                    Content = key == "*" ? "Другие" : key == "" ? "Без приоритета" : key,
                    ToolTip = key == "*" ? "Приоритеты кроме A, B и C" : null,
                    Margin = new Thickness(0, 4, 12, 0), VerticalAlignment = VerticalAlignment.Center
                };
                check.Checked += FilterChanged;
                check.Unchecked += FilterChanged;
                priorities.Add(key, check);
                choices.Children.Add(check);
            }
            var clear = new Button { Content = "Сбросить", Padding = new Thickness(7, 2, 7, 2), ToolTip = "Сбросить поиск и фильтр важности" };
            clear.Click += delegate {
                updating = true;
                search.Clear();
                foreach (var check in priorities.Values) check.IsChecked = false;
                updating = false;
                Refresh();
            };
            choices.Children.Add(clear);
            filters.Children.Add(Field("Важность · без выбора показаны все", choices, Double.NaN));

            var head = new DockPanel { Margin = new Thickness(0, 9, 0, 8), LastChildFill = true };
            AddAt(head, 2);
            copy.Content = "Скопировать отчёт";
            copy.Padding = new Thickness(12, 4, 12, 4);
            copy.Click += Copy;
            DockPanel.SetDock(copy, Dock.Right);
            head.Children.Add(copy);
            summary.FontSize = 16;
            summary.FontWeight = FontWeights.SemiBold;
            summary.Foreground = Ink;
            summary.VerticalAlignment = VerticalAlignment.Center;
            summary.TextWrapping = TextWrapping.Wrap;
            head.Children.Add(summary);

            results.BorderBrush = new SolidColorBrush(Color.FromRgb(211, 221, 223));
            results.BorderThickness = new Thickness(1);
            results.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
            results.ItemTemplate = LineTemplate();
            var containers = new Style(typeof(ListBoxItem));
            containers.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            containers.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
            var heading = new DataTrigger { Binding = new Binding("IsHeading"), Value = true };
            heading.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            heading.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
            heading.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(236, 243, 243))));
            containers.Triggers.Add(heading);
            results.ItemContainerStyle = containers;
            AddAt(results, 3);
            results.SelectionChanged += delegate { editButton.IsEnabled = SelectedTask() != null; };
            results.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) {
                var origin = e.OriginalSource as DependencyObject;
                if (origin == null) return;
                var container = ItemsControl.ContainerFromElement(results, origin) as ListBoxItem;
                var line = container == null ? null : container.Content as ReportLine;
                if (line != null && line.Task != null) { e.Handled = true; edit(line.Task); }
            };
            results.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && SelectedTask() != null) { e.Handled = true; edit(SelectedTask()); }
            };

            var foot = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            AddAt(foot, 4);
            editButton.Content = "Изменить задачу";
            editButton.Padding = new Thickness(10, 3, 10, 3);
            editButton.IsEnabled = false;
            editButton.Click += delegate { if (SelectedTask() != null) edit(SelectedTask()); };
            DockPanel.SetDock(editButton, Dock.Right);
            foot.Children.Add(editButton);
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Foreground = Muted;
            status.TextWrapping = TextWrapping.Wrap;
            foot.Children.Add(status);

            period.SelectionChanged += delegate { if (!updating) { ApplyPeriod(); Refresh(); } };
            start.SelectedDateChanged += DateChanged;
            end.SelectedDateChanged += DateChanged;
            grouping.SelectionChanged += delegate { Refresh(); };
            search.TextChanged += delegate { Refresh(); };
            Refresh();
        }

        Item SelectedTask()
        {
            var line = results.SelectedItem as ReportLine;
            return line == null ? null : line.Task;
        }

        static DataTemplate LineTemplate()
        {
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Hint"));
            var badge = new FrameworkElementFactory(typeof(TextBlock));
            badge.Name = "Badge";
            badge.SetValue(DockPanel.DockProperty, Dock.Left);
            badge.SetValue(FrameworkElement.WidthProperty, 97.0);
            badge.SetValue(TextBlock.ForegroundProperty, Muted);
            badge.SetBinding(TextBlock.TextProperty, new Binding("Badge"));
            root.AppendChild(badge);
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.Name = "Caption";
            title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            title.SetValue(TextBlock.ForegroundProperty, Ink);
            title.SetBinding(TextBlock.TextProperty, new Binding("Caption"));
            root.AppendChild(title);
            var template = new DataTemplate { VisualTree = root };
            var heading = new DataTrigger { Binding = new Binding("IsHeading"), Value = true };
            heading.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Badge"));
            heading.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold, "Caption"));
            heading.Setters.Add(new Setter(TextBlock.ForegroundProperty, Accent, "Caption"));
            template.Triggers.Add(heading);
            return template;
        }

        void AddAt(UIElement element, int row)
        {
            Grid.SetRow(element, row);
            Children.Add(element);
        }

        static FrameworkElement Field(string label, FrameworkElement input, double width)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 12, 4), Width = width };
            block.Children.Add(new TextBlock { Text = label, Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
            input.MinHeight = 28;
            block.Children.Add(input);
            return block;
        }

        public static void PeriodBounds(int index, DateTime today, out DateTime first, out DateTime last)
        {
            today = today.Date;
            if (index == 0 || index == 1) {
                first = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - (index == 1 ? 7 : 0));
                last = first.AddDays(6);
            }
            else if (index == 2 || index == 3) {
                first = new DateTime(today.Year, today.Month, 1).AddMonths(index == 3 ? -1 : 0);
                last = first.AddMonths(1).AddDays(-1);
            }
            else {
                first = new DateTime(today.Year - (index == 5 ? 1 : 0), 1, 1);
                last = new DateTime(first.Year, 12, 31);
            }
        }

        void ApplyPeriod()
        {
            if (period.SelectedIndex < 0 || period.SelectedIndex > 5) return;
            DateTime first, last;
            PeriodBounds(period.SelectedIndex, DateTime.Today, out first, out last);
            updating = true;
            start.SelectedDate = first;
            end.SelectedDate = last;
            updating = false;
        }

        void DateChanged(object sender, SelectionChangedEventArgs args)
        {
            if (updating) return;
            updating = true;
            period.SelectedIndex = 6;
            updating = false;
            Refresh();
        }

        void FilterChanged(object sender, RoutedEventArgs e) { Refresh(); }

        static string Category(string p)
        {
            if (String.IsNullOrEmpty(p)) return "";
            return p == "A" || p == "B" || p == "C" ? p : "*";
        }

        static string PriorityName(string p)
        {
            return String.IsNullOrEmpty(p) ? "Без приоритета" : "Приоритет " + p;
        }

        public void Refresh()
        {
            if (updating) return;
            clipboardText = "";
            copy.IsEnabled = false;
            editButton.IsEnabled = false;
            status.Text = "";
            if (!start.SelectedDate.HasValue || !end.SelectedDate.HasValue || end.SelectedDate.Value < start.SelectedDate.Value) {
                results.ItemsSource = null;
                summary.Text = "Проверь даты периода";
                status.Text = "Укажи обе даты. Конечная дата должна быть не раньше начальной.";
                return;
            }

            var first = start.SelectedDate.Value.Date;
            var last = end.SelectedDate.Value.Date;
            var enabled = new HashSet<string>(priorities.Where(p => p.Value.IsChecked == true).Select(p => p.Key));
            string term = search.Text.Trim();
            bool byPriority = grouping.SelectedIndex == 1;
            var filtered = source().Where(t => t.Completed.HasValue && t.Completed.Value.Date >= first && t.Completed.Value.Date <= last
                    && (enabled.Count == 0 || enabled.Contains(Category(t.Priority)))
                    && (term.Length == 0 || Ru.CompareInfo.IndexOf(t.Title, term, CompareOptions.IgnoreCase) >= 0))
                .OrderByDescending(t => t.Completed.Value.Date)
                .ThenBy(t => String.IsNullOrEmpty(t.Priority) ? "~" : t.Priority, StringComparer.Ordinal)
                .ThenBy(t => t.Title, StringComparer.Create(Ru, true)).ToList();

            string span = first.ToString("dd.MM.yyyy", Ru) + " — " + last.ToString("dd.MM.yyyy", Ru);
            summary.Text = "Выполнено задач: " + filtered.Count + "   ·   " + span;
            var lines = new List<ReportLine>();
            var buffer = new StringBuilder().AppendLine(summary.Text);
            var groups = filtered.GroupBy(t => byPriority ? (String.IsNullOrEmpty(t.Priority) ? "~" : t.Priority) : t.Completed.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            groups = byPriority ? groups.OrderBy(g => g.Key, StringComparer.Ordinal) : groups.OrderByDescending(g => g.Key, StringComparer.Ordinal);
            foreach (var group in groups) {
                string groupName = byPriority ? PriorityName(group.First().Priority) : group.First().Completed.Value.ToString("d MMMM yyyy, dddd", Ru);
                string caption = groupName + "  ·  " + group.Count();
                lines.Add(new ReportLine { Caption = caption });
                buffer.AppendLine().AppendLine(caption);
                foreach (var task in group) {
                    string badge = byPriority ? task.Completed.Value.ToString("dd.MM.yyyy", Ru) : PriorityName(task.Priority);
                    lines.Add(new ReportLine { Task = task, Caption = task.Title, Badge = badge, Hint = task.Estimated ? "Важность оценена по описанию при переносе" : null });
                    buffer.Append("• ").Append(badge).Append(" — ").AppendLine(task.Title);
                }
            }
            results.ItemsSource = lines;
            if (filtered.Count == 0) {
                status.Text = "За этот период с выбранными фильтрами выполненных задач нет.";
                return;
            }
            clipboardText = buffer.ToString().TrimEnd('\r', '\n');
            copy.IsEnabled = true;
        }

        void Copy(object sender, RoutedEventArgs args)
        {
            try {
                Clipboard.SetDataObject(clipboardText, true);
                status.Text = "Отчёт скопирован.";
            }
            catch (ExternalException) { status.Text = "Буфер обмена занят. Попробуй скопировать ещё раз."; }
        }
    }
}

