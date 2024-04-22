using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
#if GOOGLE_STT
    using Google.Api.Gax.Grpc;
    using Google.Cloud.Speech.V1;
#endif

    public static class GoogleSpeechToTextExtensions
    {
        public static IHostBuilder AddGoogleSpeechToText(this IHostBuilder builder)
        {
#if GOOGLE_STT
            builder.ConfigureServices((context, collection)
                => collection
                    .Configure<GoogleSpeechToTextOptions>(context.Configuration.GetSection(nameof(GoogleSpeechToTextOptions)))
                    .AddSingleton<ISpeechToTextApi, GoogleSpeechToText>()

            );
#endif
            return builder;
        }
    }

#if GOOGLE_STT
    public sealed class GoogleSpeechToTextOptions
    {
        /// <summary>
        /// JSON API 證書格式
        /// </summary>
        public string JsonCredentials { get; set; } = string.Empty;
    }

    public sealed class GoogleSpeechToText : ISpeechToTextApi
    {
        public Panel OptionsPanel { get; }
        GoogleSpeechToTextOptions Options;
        ILogger Logger;

        public GoogleSpeechToText(
            IOptions<GoogleSpeechToTextOptions> options,
            ILogger<GoogleSpeechToText> logger)
        {
            Options = options.Value;
            Logger = logger;
            OptionsPanel = LayoutOptionsPanel();
        }

        Panel LayoutOptionsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true };
            panel.Controls.Add(new Label { Text = string.Empty });
            return panel;
        }

        public async IAsyncEnumerable<string> AnalyzeAsync(
            string file,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Logger.LogTrace(nameof(AnalyzeAsync));

            using var reader = new MediaFoundationReader(file);
            var f = reader.WaveFormat;

            var buffer = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(buffer, reader);
            buffer.Position = 0;

            var config = new RecognitionConfig
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = f.SampleRate,
                AudioChannelCount = f.Channels,
                LanguageCode = "zh-TW",
                EnableAutomaticPunctuation = true,
                EnableWordTimeOffsets = true,
                DiarizationConfig = new SpeakerDiarizationConfig { },
            };
            var speechClient = await new SpeechClientBuilder
            {
                JsonCredentials = Options.JsonCredentials,
            }
            .BuildAsync(cancellationToken);
            var batch = new byte[Math.Clamp((int)reader.Length / 50, 4096, 32767)];
            var callSettings = CallSettings.FromCancellationToken(cancellationToken);
            var streamingSettings = new BidirectionalStreamingSettings(batch.Length);
            var streamingCall = speechClient.StreamingRecognize(callSettings, streamingSettings);

            await streamingCall.WriteAsync(new StreamingRecognizeRequest
            {
                StreamingConfig = new StreamingRecognitionConfig
                {
                    Config = config,
                    // SingleUtterance = true,
                    // InterimResults = true,
                }
            });

            _ = Task.Run(async () =>
            {
                while (await buffer.ReadAsync(batch, cancellationToken) is int read && read > 0)
                {
                    // Logger.LogDebug("載入檔案: {0:P}", (float)buffer.Position / buffer.Length);
                    await streamingCall.WriteAsync(new StreamingRecognizeRequest
                    {
                        AudioContent = Google.Protobuf.ByteString.CopyFrom(batch, 0, read)
                    });
                }
                await streamingCall.WriteCompleteAsync();
                Logger.LogDebug("載入檔案: {0:P}", (float)buffer.Position / buffer.Length);
            }, cancellationToken);

            var index = 0u;
            var streaming = streamingCall.GetResponseStream();
            while (await streaming.MoveNextAsync())
            {
                foreach (var result in streaming.Current.Results)
                {
                    var alt = result.Alternatives.First();
                    if (alt.Words.Count == 0) continue;
                    int offset = 0;
                    foreach (var content in alt.Transcript.Split(new char[] { '，', '。' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var start = alt.Words[offset].StartTime.ToTimeSpan();
                        var last = Math.Min(offset + content.Length - 1, alt.Words.Count - 1);
                        var end = alt.Words[last].EndTime.ToTimeSpan();
                        var line = $"{++index}{Environment.NewLine}{start:hh\\:mm\\:ss\\,fff} --> {end:hh\\:mm\\:ss\\,fff}{Environment.NewLine}{content}";
                        Logger.LogDebug(line);
                        yield return line + Environment.NewLine;

                        offset += content.Length;
                    }
                }
            }
        }

        public override string ToString() => "Google Cloud Speech API";
    }
#endif
}