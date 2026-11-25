using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TaskDesk
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            Thread.CurrentThread.CurrentUICulture = ru;
            Thread.CurrentThread.CurrentCulture = ru;
            var app = new Application();
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                    app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tasks.txt");
                if (!File.Exists(path))
                {
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                }
                app.Run(new DeskWindow(Store.Open(path)));
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Не удалось открыть журнал", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    internal static class Ui
    {
        public static SolidColorBrush Brush(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
        public static TextBlock Text(string value, double size, string color, bool bold)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center };
        }
        public static Button Button(string text, Action click, bool primary)
        {
            var b = new Button { Content = text, VerticalContentAlignment = VerticalAlignment.Center };
            if (primary) { b.Background = Brush("#146452"); b.Foreground = Brushes.White; }
            b.Click += delegate { click(); };
            return b;
        }
        public static void Dock(DockPanel panel, UIElement element, Dock direction)
        {
            DockPanel.SetDock(element, direction);
            panel.Children.Add(element);
        }
        public static ComboBox Priorities(IEnumerable<string> options)
        {
            var combo = new ComboBox { MinWidth = 68, Margin = new Thickness(0,0,8,0) };
            foreach (var option in options) combo.Items.Add(option);
            combo.SelectedIndex = 0;
            return combo;
        }
    }

    internal sealed class DeskWindow : Window
    {
        private Store store;
        private readonly StackPanel groups = new StackPanel();
        private readonly TextBox quick = new TextBox();
        private readonly TextBox search = new TextBox();
        private readonly ComboBox newPriority = Ui.Priorities(new [] {"A","B","C","Без приоритета"});
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private readonly Dictionary<string, CheckBox> selectedPriorities = new Dictionary<string, CheckBox>();
        private readonly TextBlock count = Ui.Text("",15,"#485D55",false);
        private readonly TextBlock status = Ui.Text("",12,"#55665F",false);
        private Button undo;
        private Item undoOpen, undoDone;
        private ReportPane report;
        private readonly TabControl tabs = new TabControl();

        public DeskWindow(Store opened)
        {
            store = opened;
            Title = "Рабочий журнал · TaskDesk";
            Width = 1060; Height = 740; MinWidth = 830; MinHeight = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
            Language = XmlLanguage.GetLanguage("ru-RU");
            Background = Ui.Brush("#F4F7F5");
            var root = new DockPanel { Margin = new Thickness(22,16,22,12) };
            Content = root;
            var bar = new DockPanel { Margin = new Thickness(0,0,0,15) };
            var refresh = Ui.Button("Обновить", Reload, false);
            refresh.ToolTip = "Перечитать рабочий файл (F5)";
            Ui.Dock(bar, refresh, Dock.Right);
            bar.Children.Add(Ui.Text("Рабочий журнал",28,"#1B3C32",true));
            Ui.Dock(root, bar, Dock.Top);
            var footer = new DockPanel {Margin = new Thickness(0,8,0,0)};
            undo = Ui.Button("Отменить завершение", UndoComplete, false);
            undo.Visibility = Visibility.Collapsed;
            Ui.Dock(footer,undo,Dock.Right);
            status.Text = "Рабочий файл · tasks.txt";
            status.ToolTip = store.PathName; footer.Children.Add(status);
            Ui.Dock(root, footer, Dock.Bottom);
            tabs.Background = Brushes.White; tabs.BorderBrush = Ui.Brush("#D6DFDB");
            root.Children.Add(tabs);
            tabs.Items.Add(Tab("Задачи", TaskPane()));
            report = new ReportPane(delegate {return store.Items;}, Edit) {Margin = new Thickness(16,12,16,10)};
            tabs.Items.Add(Tab("Отчёт", report));
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e) {
                if (e.OriginalSource == tabs && tabs.SelectedIndex == 1) report.Refresh();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.F5) { Reload(); e.Handled = true; }
                if (Keyboard.Modifiers != ModifierKeys.Control) return;
                if (e.Key == Key.N) { tabs.SelectedIndex = 0; quick.Focus(); e.Handled = true; }
                if (e.Key == Key.F && tabs.SelectedIndex == 0) { search.Focus(); search.SelectAll(); e.Handled = true; }
            };
            RenderTasks();
        }
        private static TabItem Tab(string title, UIElement pane)
        {
            return new TabItem { Header = title, Content = pane, FontSize = 14,
                Padding = new Thickness(22,8,22,8) };
        }
        private UIElement TaskPane()
        {
            var panel = new DockPanel { Margin = new Thickness(16,12,16,10) };
            var add = new DockPanel { Margin = new Thickness(0,0,0,12) };
            Ui.Dock(add,Ui.Button("Добавить", AddQuick,true),Dock.Right);
            newPriority.SelectedItem = "B";
            newPriority.ToolTip = "Приоритет новой задачи";
            Ui.Dock(add,newPriority,Dock.Right);
            Ui.Dock(add, Ui.Text("Новая задача   ",14,"#273F35",true),Dock.Left);
            quick.Margin = new Thickness(0,0,8,0); quick.ToolTip = "Введите название и нажмите Enter (Ctrl+N)";
            quick.KeyDown += delegate(object s, KeyEventArgs e) {
                if(e.Key == Key.Enter) { e.Handled = true; AddQuick(); }
            };
            add.Children.Add(quick); Ui.Dock(panel,add,Dock.Top);
            var filter = new DockPanel { Margin = new Thickness(0,0,0,8) };
            var choices = new StackPanel { Orientation = Orientation.Horizontal };
            var filterLabel = Ui.Text("   Важность",13,"#44584F",false);
            filterLabel.ToolTip = "Без выбора показаны все задачи";
            choices.Children.Add(filterLabel);
            foreach (string key in new [] {"A","B","C","Другие"})
            {
                var check = new CheckBox {Content = key, ToolTip = "Без выбора показаны все задачи",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8,0,0,0)};
                selectedPriorities[key] = check; choices.Children.Add(check);
                check.Click += delegate { RenderTasks(); };
            }
            Ui.Dock(filter,choices,Dock.Right);
            Ui.Dock(filter,Ui.Text("Поиск   ",14,"#44584F",false),Dock.Left);
            search.ToolTip = "По названию (Ctrl+F)";
            search.TextChanged += delegate { RenderTasks(); };
            filter.Children.Add(search); Ui.Dock(panel,filter,Dock.Top);
            var meta = new DockPanel { Margin = new Thickness(0,2,0,8) };
            var logged = Ui.Button("Записать выполненную…", AddCompleted, false);
            Ui.Dock(meta,logged,Dock.Right); meta.Children.Add(count); Ui.Dock(panel,meta,Dock.Top);
            panel.Children.Add(new ScrollViewer {Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled});
            return panel;
        }
        private void RenderTasks()
        {
            groups.Children.Clear();
            var open = store.Items.Where(i => !i.Completed.HasValue).ToList();
            bool all = !selectedPriorities.Values.Any(c => c.IsChecked == true);
            var visible = open.Where(i => {
                string p = selectedPriorities.ContainsKey(i.Priority) ? i.Priority : "Другие";
                return (all || selectedPriorities[p].IsChecked == true) &&
                    i.Title.IndexOf(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase) >= 0;
            }).ToList();
            count.Text = visible.Count == open.Count ? "Открытые задачи · " + open.Count :
                "Показано " + visible.Count + " из " + open.Count;
            foreach (var group in visible.GroupBy(i=>i.Priority).OrderBy(g=>g.Key.Length==0 ? "~" : g.Key))
            {
                var rows = new StackPanel();
                foreach (var item in group) rows.Children.Add(TaskRow(item));
                var key = group.Key;
                string color = key == "A" ? "#90472D" : key == "B" ? "#285D7B" : "#496752";
                var header = new Expander {Header = Ui.Text((key.Length==0 ? "Без приоритета" : "Приоритет " + key) +
                    "   ·   " + group.Count(),15,color,true), Content = rows, IsExpanded = !collapsed.Contains(key),
                    Margin = new Thickness(0,3,0,9), Padding = new Thickness(0,4,0,0)};
                header.Collapsed += delegate { collapsed.Add(key); };
                header.Expanded += delegate { collapsed.Remove(key); };
                groups.Children.Add(header);
            }
            if (visible.Count == 0) groups.Children.Add(Ui.Text(open.Count == 0 ? "Все задачи выполнены." :
                "По выбранным условиям задач нет.",15,"#58685F",false));
        }
        private UIElement TaskRow(Item item)
        {
            var wrapper = new Border { BorderBrush = Ui.Brush("#E5EBE8"), BorderThickness = new Thickness(0,0,0,1),
                Padding = new Thickness(7,5,0,5) };
            var row = new DockPanel(); wrapper.Child = row;
            var done = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0),
                ToolTip = "Отметить выполненной сегодня" };
            done.Click += delegate {Complete(item);};
            Ui.Dock(row,done,Dock.Left);
            var edit = Ui.Button("Изменить",delegate {Edit(item);},false);
            edit.FontSize = 12; edit.Padding = new Thickness(7,2,7,2);
            Ui.Dock(row,edit,Dock.Right);
            var label = Ui.Text(item.Title,14,"#263A31",false);
            label.TextWrapping = TextWrapping.Wrap; label.Margin = new Thickness(0,0,8,0);
            label.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {if(e.ClickCount == 2) Edit(item);};
            row.Children.Add(label); return wrapper;
        }
        private void AddQuick()
        {
            if (String.IsNullOrWhiteSpace(quick.Text)) {quick.Focus(); return;}
            var p = (string)newPriority.SelectedItem;
            Item item;
            try { item = Item.New(quick.Text.Trim(),p.Length == 1 ? p : "",null); }
            catch(ArgumentException error) { status.Text = error.Message; quick.Focus(); return; }
            if (Change(delegate {store.Items.Add(item);}, "Задача добавлена.")) {quick.Clear(); quick.Focus();}
        }
        private void AddCompleted()
        {
            var p = (string)newPriority.SelectedItem;
            var draft = Item.New("Новая задача",p.Length == 1 ? p : "",DateTime.Today);
            var editor = new EditWindow(draft,true,quick.Text) {Owner = this};
            if (editor.ShowDialog() != true) return;
            if (Change(delegate {store.Items.Add(editor.Result);}, "Запись добавлена.")) quick.Clear();
        }
        private void Edit(Item item)
        {
            var editor = new EditWindow(item,false,item.Title) {Owner=this};
            if (editor.ShowDialog() != true) return;
            Change(delegate {store.Items[store.Items.IndexOf(item)] = editor.Result;}, "Изменения сохранены.");
        }
        private void Complete(Item item)
        {
            var done = item.With(item.Title,item.Priority,DateTime.Today,false);
            if (Change(delegate {store.Items[store.Items.IndexOf(item)] = done;}, "Выполнено сегодня · " + DateTime.Today.ToString("dd.MM.yyyy")))
            { undoDone = done; undoOpen = item; undo.Visibility = Visibility.Visible; }
        }
        private void UndoComplete()
        {
            int pos = store.Items.IndexOf(undoDone);
            if (pos >= 0) Change(delegate {store.Items[pos] = undoOpen;}, "Задача возвращена в открытые.");
        }
        private bool Change(Action mutate, string message)
        {
            var snapshot = store.Items.ToList();
            try {mutate(); store.Save();}
            catch(Exception error)
            {
                store.Items.Clear(); store.Items.AddRange(snapshot);
                try {store = Store.Open(store.PathName);} catch(IOException) {} catch(UnauthorizedAccessException) {}
                catch(System.Text.DecoderFallbackException) {}
                ResetUndo(); RenderTasks(); report.Refresh();
                MessageBox.Show(this,error.Message,"Изменение не сохранено",MessageBoxButton.OK,MessageBoxImage.Warning);
                return false;
            }
            ResetUndo(); status.Text = message; RenderTasks(); report.Refresh(); return true;
        }
        private void ResetUndo() {undoOpen=null; undoDone=null; undo.Visibility=Visibility.Collapsed;}
        private void Reload()
        {
            try {store = Store.Open(store.PathName); ResetUndo(); RenderTasks(); report.Refresh(); status.Text="Рабочий файл перечитан.";}
            catch(Exception error) {MessageBox.Show(this,error.Message,"Не удалось обновить список",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
    }
}
