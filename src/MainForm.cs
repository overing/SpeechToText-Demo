using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SpeechToText
{
    sealed partial class MainForm : Form
    {
        ILogger Logger;

        static readonly string DefaultFileButtonText = "(選擇檔案或拖曳到此)";
        static readonly string AnalyzeButtonText_Start = "開始分析";
        static readonly string AnalyzeButtonText_Cancel = "取消分析";
        static readonly string SaveButtonText_Cancel = "儲存字幕檔";

        static readonly string IconPngBase64Raw = @"
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";

        ToolTip ToolTip;
        Button OpenButton;
        ComboBox ApiComboBox;
        Panel ApiOptionsPanel;
        Panel[] ApiOptionsPanels;
        Button AnalyzeButton;
        TextBox ResultTextBox;
        Button SaveButton;

        [STAThread]
        static void Main(string[] args)
        {
            new HostBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .UseWindowsFormsLifetime<MainForm>(options =>
                {
                    options.EnableVisualStyles = true;
                    options.CompatibleTextRenderingDefault = false;
                    options.HighDpiMode = HighDpiMode.PerMonitorV2;
                })
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                })
                .ConfigureLogging((context, builder) =>
                {
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        builder.SetMinimumLevel(LogLevel.Trace);
                        builder.AddDebug();
                        return;
                    }
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddEventLog(settings =>
                    {
                        settings.SourceName = typeof(MainForm).Assembly.GetName().Name;
                    });
                })

                .AddMicrosoftSpeechToText()
                .AddGoogleSpeechToText()
                .AddAmazonSpeechToText()

                .Build()
                .Run();
        }

        public MainForm(ILogger<MainForm> logger, IEnumerable<ISpeechToTextApi> apis)
        {
            Logger = logger;

            SuspendLayout();

            var image = Image.FromStream(new MemoryStream(Convert.FromBase64String(IconPngBase64Raw)));

            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(800, 480);
            Text = "MP4 語音解析工具 v" + GetType().Assembly.GetName().Version;
            Icon = Icon.FromHandle(((Bitmap)image).GetHicon());

            ToolTip = new ToolTip();

            Controls.Add(LayoutRootPanel(
                out OpenButton,
                out ApiComboBox,
                out ApiOptionsPanel,
                out AnalyzeButton,
                out ResultTextBox,
                out SaveButton
            ));

            ApiOptionsPanels = apis.Select(a => a.OptionsPanel).ToArray();
            foreach (var panel in ApiOptionsPanels.Where(p => p != null))
                ApiOptionsPanel.Controls.Add(panel);

            ApiComboBox.Items.AddRange(apis.ToArray());
            ApiComboBox.SelectionChangeCommitted += OnApiSelectionChangeCommitted;
            ApiComboBox.SelectedIndex = 0;

            OpenButton.AllowDrop = true;
            OpenButton.Click += OnClickFileButton;
            OpenButton.DragEnter += OnDragEnterFileButton;
            OpenButton.DragDrop += OnDragDropFileButton;
            if (Environment.GetCommandLineArgs().FirstOrDefault(e => File.Exists(e) && e.EndsWith(".mp4")) is string f)
            {
                OpenButton.Text = f;
                ToolTip.SetToolTip(OpenButton, f);
            }

            AnalyzeButton.Click += OnClickAnalyzeButton;

            SaveButton.Click += OnClickSaveButton;

            ResumeLayout(performLayout: true);
        }

        void OnApiSelectionChangeCommitted(object? sender, EventArgs e)
        {
            for (var i = 0; i < ApiOptionsPanels.Length; i++)
            {
                var panel = ApiOptionsPanels[i];
                if (ApiComboBox.SelectedIndex == i)
                    panel.Show();
                else
                    panel.Hide();
            }
        }

        CancellationTokenSource AnalyzeCancellationTokenSource = new CancellationTokenSource(TimeSpan.Zero);

        async void OnClickAnalyzeButton(object? sender, EventArgs e)
        {
            if (StringComparer.Ordinal.Equals(AnalyzeButton.Text, AnalyzeButtonText_Cancel))
            {
                if (AnalyzeCancellationTokenSource.IsCancellationRequested == false)
                    AnalyzeCancellationTokenSource.Cancel(throwOnFirstException: true);
                return;
            }

            try
            {
                OpenButton.Enabled = false;
                ApiComboBox.Enabled = false;
                ApiOptionsPanel.Enabled = false;
                SaveButton.Enabled = false;
                ResultTextBox.Text = string.Empty;
                AnalyzeButton.Text = AnalyzeButtonText_Cancel;

                if (ApiComboBox.SelectedItem is not ISpeechToTextApi api)
                    throw new InvalidProgramException("未知的 API");

                var file = OpenButton.Text;
                if (file == DefaultFileButtonText)
                    throw new InvalidOperationException("未選擇 MP4 檔案");

                if (!File.Exists(file))
                    throw new FileNotFoundException("MP4 檔案不存在", file);

                var token = (AnalyzeCancellationTokenSource = new CancellationTokenSource()).Token;

                await Task.Yield();

                Logger.LogInformation("Analyze start");
                var analyze = api.AnalyzeAsync(file, token);
                await foreach (var result in analyze.WithEnforcedCancellation(token))
                {
                    ResultTextBox.AppendText(result);
                    ResultTextBox.AppendText(Environment.NewLine);
                }

                Logger.LogInformation("Analyze done");
                MessageBox.Show(this, "分析已完成", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (TaskCanceledException)
            {
                Logger.LogInformation("Analyze canceled");
                MessageBox.Show(this, "分析已取消", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Analyze error");
                MessageBox.Show(this, $"分析發生錯誤: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex.StackTrace}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                OpenButton.Enabled = true;
                ApiComboBox.Enabled = true;
                ApiOptionsPanel.Enabled = true;
                SaveButton.Enabled = true;
                AnalyzeButton.Text = AnalyzeButtonText_Start;
            }
        }

        void OnClickFileButton(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "開啟要解析語音的 MP4 影片檔",
                Filter = "MP4 檔案 (*.mp4)|*.mp4|所有檔案 (*.*)|*.*",
                FilterIndex = 1,
                RestoreDirectory = true,
            };
            var current = OpenButton.Text;
            if (File.Exists(current))
            {
                dialog.FileName = current;
                dialog.InitialDirectory = Path.GetDirectoryName(current);
            }
            if (dialog.ShowDialog() == DialogResult.Cancel) return;
            current = dialog.FileName;
            OpenButton.Text = File.Exists(current) ? current : DefaultFileButtonText;
            ToolTip.SetToolTip(OpenButton, OpenButton.Text);
        }

        void OnDragEnterFileButton(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
        }

        void OnDragDropFileButton(object? sender, DragEventArgs e)
        {
            var data = e.Data;
            if (data == null) return;
            string[] files = (string[])data.GetData(DataFormats.FileDrop);
            var file = files.Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)).FirstOrDefault(File.Exists);
            if (file == null) return;
            OpenButton.Text = file;
            ToolTip.SetToolTip(OpenButton, OpenButton.Text);
        }

        void OnClickSaveButton(object? sender, EventArgs e)
        {
            var content = ResultTextBox.Text;
            if (string.IsNullOrWhiteSpace(content))
                return;

            var filePath = OpenButton.Text;
            if (!File.Exists(filePath))
                return;

            var srt = filePath + ".語音解析.srt";
            try
            {
                using var dialog = new SaveFileDialog
                {
                    Title = "儲存解析結果字幕檔",
                    Filter = "SRT 檔案 (*.srt)|*.srt|所有檔案 (*.*)|*.*",
                    FilterIndex = 1,
                    InitialDirectory = Path.GetDirectoryName(filePath),
                    RestoreDirectory = true,
                    FileName = srt,
                };
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                srt = dialog.FileName;
                Console.WriteLine($"結果: {srt}");
                if (File.Exists(srt)) File.Delete(srt);
                File.WriteAllText(srt, content);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Save error");
                MessageBox.Show(this, $"儲存發生錯誤: {ex.Message}{Environment.NewLine}{Environment.NewLine}{ex.StackTrace}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static Control LayoutRootPanel(
            out Button fileButton,
            out ComboBox apiComboBox,
            out Panel apiOptionsPanel,
            out Button analyzeButton,
            out TextBox resultTextBox,
            out Button saveButton
        )
        {
            var root = new SplitContainer
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ClientSize = new Size(640, 480),
                Panel1MinSize = 320,
                Panel2MinSize = 320,
            };
            root.SuspendLayout();

            var leftTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                RowCount = 3,
                ColumnCount = 2,
            };
            leftTable.Controls.Add(new Label
            {
                Text = "MP4 檔:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(left: 0, top: 8, right: 0, bottom: 4),
            });
            leftTable.Controls.Add(fileButton = new Button
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = DefaultFileButtonText,
                TextAlign = ContentAlignment.MiddleLeft,
            });
            leftTable.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "解析法:",
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(left: 0, top: 8, right: 0, bottom: 4),
            });
            leftTable.Controls.Add(apiComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
            });
            leftTable.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "選項:",
                TextAlign = ContentAlignment.TopRight,
                Padding = new Padding(left: 0, top: 8, right: 0, bottom: 4),
            });
            leftTable.Controls.Add(apiOptionsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
            });
            root.Panel1.Controls.Add(leftTable);

            root.Panel2.Controls.Add(resultTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
            });
            root.Panel2.Controls.Add(analyzeButton = new Button
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = AnalyzeButtonText_Start,
            });
            root.Panel2.Controls.Add(saveButton = new Button
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Text = SaveButtonText_Cancel,
            });

            root.AutoScaleMode = AutoScaleMode.Inherit;
            root.ResumeLayout(performLayout: true);

            return root;
        }
    }

    public interface ISpeechToTextApi
    {
        Panel OptionsPanel { get; }

        IAsyncEnumerable<string> AnalyzeAsync(string file, CancellationToken cancellationToken = default);
    }
}
