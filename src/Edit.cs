using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskDesk
{
    internal sealed class EditWindow : Window
    {
        private readonly Item original;
        private readonly TextBox title = new TextBox();
        private readonly ComboBox priority;
        private readonly CheckBox completed = new CheckBox {Content = "Задача выполнена"};
        private readonly DatePicker date = new DatePicker();
        private readonly CheckBox confirm = new CheckBox {Content = "Подтвердить оценку приоритета"};
        private readonly TextBlock error = Ui.Text("",13,"#953E2D",false);
        public Item Result {get; private set;}

        public EditWindow(Item item, bool adding, string initialText)
        {
            original = item;
            Title = adding ? "Записать выполненную задачу" : "Изменить задачу";
            Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 14; Background = Brushes.White;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
            var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
            panel.Children.Add(Ui.Text("Название задачи",14,"#273F35",true));
            title.Text = initialText; title.TextWrapping = TextWrapping.Wrap;
            title.MinHeight = 46; title.Margin = new Thickness(0,5,0,13);
            panel.Children.Add(title);
            panel.Children.Add(Ui.Text("Приоритет",14,"#273F35",true));
            var options = new List<string> {"A","B","C","Без приоритета"};
            if (item.Priority.Length>0 && !options.Contains(item.Priority)) options.Add(item.Priority);
            priority = Ui.Priorities(options);
            priority.SelectedItem = item.Priority == "" ? "Без приоритета" : item.Priority;
            priority.HorizontalAlignment = HorizontalAlignment.Left; priority.Margin = new Thickness(0,5,0,13);
            panel.Children.Add(priority);
            if (item.Estimated)
            {
                confirm.ToolTip = "При миграции приоритет определён по тексту. Подтвердите его или выберите другой.";
                confirm.Margin = new Thickness(0,0,0,12); panel.Children.Add(confirm);
            }
            completed.IsChecked = item.Completed.HasValue;
            completed.Margin = new Thickness(0,0,0,5); panel.Children.Add(completed);
            var dates = new DockPanel {HorizontalAlignment = HorizontalAlignment.Left};
            Ui.Dock(dates,Ui.Text("Дата выполнения   ",14,"#44584F",false),Dock.Left);
            date.SelectedDate = item.Completed ?? DateTime.Today; date.Width=150;
            dates.Children.Add(date); panel.Children.Add(dates); dates.IsEnabled=item.Completed.HasValue;
            completed.Click += delegate { dates.IsEnabled = completed.IsChecked == true; };
            panel.Children.Add(error); error.Margin = new Thickness(0,7,0,7);
            var buttons = new StackPanel {Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right};
            var cancel = Ui.Button("Отмена",delegate {DialogResult=false;},false); cancel.IsCancel=true;
            var save = Ui.Button("Сохранить",Commit,true); save.IsDefault=true;
            buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
            ContentRendered += delegate { title.Focus(); if(!adding) title.SelectAll(); };
        }
        private void Commit()
        {
            var p = (string)priority.SelectedItem;
            if (p.Length != 1) p = "";
            DateTime? finish = null;
            if (completed.IsChecked == true)
            {
                DateTime typed;
                if (!DateTime.TryParse(date.Text, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
                    System.Globalization.DateTimeStyles.None,out typed))
                { error.Text = "Укажите дату выполнения."; return; }
                finish = typed.Date;
            }
            bool estimated = original.Estimated && p == original.Priority && confirm.IsChecked != true;
            try { Result = original.With(title.Text,p,finish,estimated); }
            catch(ArgumentException ex) { error.Text=ex.Message; return; }
            DialogResult = true;
        }
    }
}
