using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BarkReminderApp
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ReminderTask> _tasks = new();
        private System.Windows.Threading.DispatcherTimer _timer = new();
        private string _lastTickMinute = "";
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string ConfigPath = "bark_config_v3.json";
        private bool _isLoaded = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadData();
            DgReminders.ItemsSource = _tasks;
            _isLoaded = true;

            // 每30秒扫描一次
            _timer.Interval = TimeSpan.FromSeconds(30);
            _timer.Tick += async (s, e) => await OnTimerTick();
            _timer.Start();
        }

        private async Task OnTimerTick()
        {
            string currentMin = DateTime.Now.ToString("HH:mm");
            if (currentMin == _lastTickMinute) return;

            // 这里直接读取 _tasks 里的最新 Time
            var activeTasks = _tasks.Where(t => t.Time == currentMin).ToList();
            if (activeTasks.Any())
            {
                _lastTickMinute = currentMin;
                foreach (var task in activeTasks)
                {
                    if (await IsConditionMet(task))
                    {
                        await SendBark(task.Message);
                    }
                }
            }
        }

        private async Task<bool> IsConditionMet(ReminderTask task)
        {
            DateTime now = DateTime.Now;
            if (task.ScheduleMode == 0 || task.ScheduleMode == 1)
            {
                int dayType = await GetChineseDayType(now);
                if (task.ScheduleMode == 0) return (dayType == 0 || dayType == 3);
                else return (dayType == 1 || dayType == 2);
            }
            else
            {
                int dayOfWeek = now.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)now.DayOfWeek;
                return task.SelectedDays.Contains(dayOfWeek);
            }
        }

        private async Task<int> GetChineseDayType(DateTime date)
        {
            try
            {
                string url = $"https://timor.tech/api/holiday/info/{date:yyyy-MM-dd}";
                var resp = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(resp);
                return doc.RootElement.GetProperty("type").GetProperty("type").GetInt32();
            }
            catch { return 0; }
        }

        private async Task SendBark(string msg)
        {
            try
            {
                string url = $"{TxtBarkUrl.Text.TrimEnd('/')}/{Uri.EscapeDataString(msg)}?group=Work&sound=calypso";
                await _httpClient.GetAsync(url);
                UpdateStatus($"成功发送: {msg}");
            }
            catch (Exception ex) { UpdateStatus($"发送失败: {ex.Message}"); }
        }

        // --- 事件处理 ---

        private void AddBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(NewTime.Text)) return;
            var task = new ReminderTask { Time = NewTime.Text, Message = NewMsg.Text };

            if (RbWorkday.IsChecked == true) task.ScheduleMode = 0;
            else if (RbHoliday.IsChecked == true) task.ScheduleMode = 1;
            else
            {
                task.ScheduleMode = 2;
                if (Cb1.IsChecked == true) task.SelectedDays.Add(1);
                if (Cb2.IsChecked == true) task.SelectedDays.Add(2);
                if (Cb3.IsChecked == true) task.SelectedDays.Add(3);
                if (Cb4.IsChecked == true) task.SelectedDays.Add(4);
                if (Cb5.IsChecked == true) task.SelectedDays.Add(5);
                if (Cb6.IsChecked == true) task.SelectedDays.Add(6);
                if (Cb7.IsChecked == true) task.SelectedDays.Add(7);
            }
            _tasks.Add(task);
            SaveData();
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DgReminders.SelectedItem is ReminderTask task)
            {
                _tasks.Remove(task);
                SaveData();
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (WeekPanel != null)
                WeekPanel.Visibility = RbCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // 当单元格编辑结束时自动保存
        private void DgReminders_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // 稍等片刻确保数据已写入对象
            Task.Delay(100).ContinueWith(_ => Dispatcher.Invoke(SaveData));
        }

        private void BarkUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoaded) SaveData();
        }

        private void UpdateStatus(string txt) => Dispatcher.Invoke(() => StatusTxt.Text = $"状态: {txt} ({DateTime.Now:HH:mm:ss})");

        private void SaveData()
        {
            try
            {
                var config = new AppConfig { BarkUrl = TxtBarkUrl.Text, Tasks = _tasks.ToList() };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config));
            }
            catch { }
        }

        private void LoadData()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath));
                    if (config != null)
                    {
                        TxtBarkUrl.Text = config.BarkUrl;
                        _tasks = new ObservableCollection<ReminderTask>(config.Tasks);
                        return;
                    }
                }
                catch { }
            }
            _tasks.Add(new ReminderTask { Time = "07:55", Message = "上班打卡", ScheduleMode = 0 });
            _tasks.Add(new ReminderTask { Time = "17:00", Message = "下班打卡", ScheduleMode = 0 });
        }
    }

    // 实现 INotifyPropertyChanged 确保 UI 和数据双向绑定正常
    public class ReminderTask : INotifyPropertyChanged
    {
        private string _time = "";
        public string Time
        {
            get => _time;
            set { _time = value; OnPropertyChanged(nameof(Time)); }
        }

        private string _message = "";
        public string Message
        {
            get => _message;
            set { _message = value; OnPropertyChanged(nameof(Message)); }
        }

        public int ScheduleMode { get; set; }
        public List<int> SelectedDays { get; set; } = new();

        public string ModeDisplay
        {
            get
            {
                if (ScheduleMode == 0) return "🇨🇳 法定工作日";
                if (ScheduleMode == 1) return "🎉 法定节假日";
                return "📅 周:" + string.Join(",", SelectedDays);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppConfig
    {
        public string BarkUrl { get; set; } = "";
        public List<ReminderTask> Tasks { get; set; } = new();
    }
}