using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;

namespace SpeechToText
{
#if MACROSOFT_STT
    using Microsoft.CognitiveServices.Speech;
    using Microsoft.CognitiveServices.Speech.Audio;
#endif

    public static class MicrosoftSpeechToTextExtensions
    {
        public static IHostBuilder AddMicrosoftSpeechToText(this IHostBuilder builder)
        {
#if MACROSOFT_STT
            builder.ConfigureServices((context, collection)
                => collection
                    .Configure<MicrosoftSpeechToTextOptions>(context.Configuration.GetSection(nameof(MicrosoftSpeechToTextOptions)))
                    .AddSingleton<ISpeechToTextApi, MicrosoftSpeechToText>()

            );
#endif
            return builder;
        }
    }

#if MACROSOFT_STT
    public sealed class MicrosoftSpeechToTextOptions
    {
        public string Language { get; set; } = "zh-TW";
        /// <summary>
        /// 32 字元全小寫英數混和
        /// </summary>
        public string SubscriptionKey { get; set; } = string.Empty;
        public string Region { get; set; } = "eastasia";
        public bool Split { get; set; } = true;
    }

    public sealed class MicrosoftSpeechToText : ISpeechToTextApi
    {
        public Panel OptionsPanel { get; }
        MicrosoftSpeechToTextOptions Options;
        ILogger Logger;

        public MicrosoftSpeechToText(
            IOptions<MicrosoftSpeechToTextOptions> options,
            ILogger<MicrosoftSpeechToText> logger)
        {
            Options = options.Value;
            Logger = logger;
            OptionsPanel = LayoutOptionsPanel();
        }

        Panel LayoutOptionsPanel()
        {
            var table = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                AutoSize = true,
            };
            table.SuspendLayout();

            CheckBox checkBox;
            table.Controls.Add(checkBox = new CheckBox
            {
                Text = "平均拆分段落時間分割語句",
                Checked = Options.Split,
                AutoSize = true,
            });
            checkBox.CheckedChanged += (_, _) => Options.Split = checkBox.Checked;

            new ToolTip().SetToolTip(checkBox, @"分割語句會用整個段落的平均時間拆開句子
這可以讓句子單元縮小
但是可能會導致時間產生些微偏差");

            table.ResumeLayout(performLayout: true);

            return table;
        }

        public async IAsyncEnumerable<string> AnalyzeAsync(
            string file,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Logger.LogTrace("{0} begin", nameof(AnalyzeAsync));

            using var reader = new MediaFoundationReader(file);
            var f = reader.WaveFormat;
            var wavFormat = AudioStreamFormat.GetWaveFormatPCM((uint)f.SampleRate, (byte)f.BitsPerSample, (byte)f.Channels);
            using var pushAudioStream = PushAudioInputStream.CreatePushStream(wavFormat);

            var analyze = AnalyzeAsync(pushAudioStream, cancellationToken);

            _ = Task.Run(async () =>
            {
                var batch = new byte[Math.Clamp((int)reader.Length / 50, 4096, 32767)];
                while (await reader.ReadAsync(batch, cancellationToken) is int read && read > 0)
                {
                    pushAudioStream.Write(batch, read);
                    // Logger.LogDebug("載入檔案: {0:P}", (float)reader.Position / reader.Length);
                }
                Logger.LogDebug("載入檔案完成, 解析 '{0}' 中...", file);
                pushAudioStream.Close();
            }, cancellationToken);

            await foreach (var result in analyze.WithCancellation(cancellationToken))
                yield return result;

            Logger.LogTrace("{0} end", nameof(AnalyzeAsync));
        }

        async IAsyncEnumerable<string> AnalyzeAsync(
            AudioInputStream audioStream,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Logger.LogTrace("{0} a begin", nameof(AnalyzeAsync));

            var speechConfig = SpeechConfig.FromSubscription(Options.SubscriptionKey, Options.Region);
            speechConfig.OutputFormat = OutputFormat.Detailed;
            using var audioConfig = AudioConfig.FromStreamInput(audioStream);
            using var recognizer = new SpeechRecognizer(speechConfig, Options.Language, audioConfig);

            var index = 0u;
            while (cancellationToken.IsCancellationRequested == false)
            {
                var result = await recognizer.RecognizeOnceAsync().ConfigureAwait(continueOnCapturedContext: false);
                if (result.Reason == ResultReason.Canceled)
                {
                    var details = CancellationDetails.FromResult(result);
                    if (details.Reason == CancellationReason.Error)
                        throw new Exception($"{details.ErrorCode}: {details.ErrorDetails}");
                    Logger.LogWarning(new { details.Reason, details.ErrorCode, details.ErrorDetails }.ToString());
                    break;
                }
                var content = result.Text;
                if (string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogDebug("{0} continue with result empty", nameof(AnalyzeAsync));
                    continue;
                }
                content = content.Replace("，", Environment.NewLine);
                content = content.Replace("。", Environment.NewLine);
                content = content.Replace("？", Environment.NewLine);
                content = content.Replace("！", Environment.NewLine);
                var start = TimeSpan.FromTicks(result.OffsetInTicks);
                if (!Options.Split)
                {
                    var end = start + result.Duration;
                    var line = $"{++index}{Environment.NewLine}{start:hh\\:mm\\:ss\\,fff} --> {end:hh\\:mm\\:ss\\,fff}{Environment.NewLine}{content}";
                    Logger.LogDebug(line);
                    yield return line;
                    continue;
                }

                var duration = result.Duration;
                var durationOfChars = duration / content.Length;
                foreach (var line in content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                {
                    var end = start + durationOfChars * line.Length;
                    Logger.LogDebug(line);
                    yield return $"{++index}{Environment.NewLine}{start:hh\\:mm\\:ss\\,fff} --> {end:hh\\:mm\\:ss\\,fff}{Environment.NewLine}{line}{Environment.NewLine}";
                    start = end;
                }
            }
            Logger.LogTrace("{0} a end", nameof(AnalyzeAsync));
        }

        public override string ToString() => "Microsoft Azure Speech API";
    }
#endif
}
