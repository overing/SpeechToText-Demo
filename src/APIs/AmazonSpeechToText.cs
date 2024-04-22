using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeechToText
{
    using System;
#if AMAZON_STT
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.TranscribeService;
    using Amazon.TranscribeService.Model;
    using NAudio.Wave;
#endif

    public static class AmazonSpeechToTextExtensions
    {
        public static IHostBuilder AddAmazonSpeechToText(this IHostBuilder builder)
        {
#if AMAZON_STT
            builder.ConfigureServices((context, collection)
                => collection
                    .Configure<AmazonSpeechToTextOptions>(context.Configuration.GetSection(nameof(AmazonSpeechToTextOptions)))
                    .AddSingleton<ISpeechToTextApi, AmazonSpeechToText>()

            );
#endif
            return builder;
        }
    }

#if AMAZON_STT
    public sealed class AmazonSpeechToTextOptions
    {
        /// <summary>
        /// 20 字元全大寫英數混和
        /// </summary>
        public string AwsAccessKeyId { get; set; } = string.Empty;
        /// <summary>
        /// 40 字元大小寫英數混和與符號 '/'
        /// </summary>
        public string AwsSecretAccessKey { get; set; } = string.Empty;
        /// <summary>
        /// 任意字元
        /// </summary>
        public string BucketName { get; set; } = string.Empty;
    }

    public sealed class AmazonSpeechToText : ISpeechToTextApi
    {
        public Panel OptionsPanel { get; }
        AmazonSpeechToTextOptions Options;
        ILogger Logger;

        public AmazonSpeechToText(
            IOptions<AmazonSpeechToTextOptions> options,
            ILogger<AmazonSpeechToText> logger)
        {
            Options = options.Value;
            Logger = logger;
            OptionsPanel = LayoutOptionsPanel();
        }

        Panel LayoutOptionsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, AutoSize = true, };
            panel.Controls.Add(new Label { Text = string.Empty });
            return panel;
        }

        public async IAsyncEnumerable<string> AnalyzeAsync(
            string file,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Logger.LogTrace(nameof(AnalyzeAsync));

            // ref: https://stackoverflow.com/questions/54303167/amazon-transcribe-streaming-service-speech-to-text-for-net-sdk

            using var reader = new MediaFoundationReader(file);
            var f = reader.WaveFormat;

            var buffer = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(buffer, reader);
            buffer.Position = 0;

            string md5code;
            using (var md5 = MD5.Create())
            {
                var md5hash = await md5.ComputeHashAsync(buffer, cancellationToken);
                md5code = md5hash.Aggregate(new StringBuilder(), (sb, b) => sb.Append(b.ToString("x2"))).ToString();
            }
            buffer.Position = 0;

            var accessKeyId = Options.AwsAccessKeyId;
            var secretAccessKey = Options.AwsSecretAccessKey;
            var bucketName = Options.BucketName;
            var sourceObjectName = md5code + "-" + Path.GetFileNameWithoutExtension(file) + ".wav";
            var region = Amazon.RegionEndpoint.APNortheast1;
            var transcriptionJobName = $"{sourceObjectName}-transcribe";
            var resultObjectName = $"{transcriptionJobName}.json";

            using var s3Client = new AmazonS3Client(accessKeyId, secretAccessKey, region);
            var listObjRequest = new ListObjectsRequest
            {
                BucketName = bucketName,
                Prefix = transcriptionJobName,
            };
            var listObjResponse = await s3Client.ListObjectsAsync(listObjRequest, cancellationToken);
            if (!listObjResponse.S3Objects.Any())
            {
                listObjRequest = new ListObjectsRequest
                {
                    BucketName = bucketName,
                    Prefix = md5code + "-",
                };
                listObjResponse = await s3Client.ListObjectsAsync(listObjRequest, cancellationToken);
                if (listObjResponse.S3Objects.Any())
                    Logger.LogInformation($"Skip put wav with found in cloud '{sourceObjectName}'.");
                else
                {
                    var putObjectRequest = new PutObjectRequest
                    {
                        BucketName = bucketName,
                        Key = sourceObjectName,
                        ContentType = "audio/wav",
                        CannedACL = S3CannedACL.PublicRead,
                        InputStream = buffer,
                    };
                    Logger.LogInformation("Put wav to cloud ...");
                    var putResponse = await s3Client.PutObjectAsync(putObjectRequest);

                    if (putResponse.HttpStatusCode != HttpStatusCode.OK)
                        throw new IOException($"Put object to cloud status fault: {putResponse.HttpStatusCode}");
                }

                var transRequest = new StartTranscriptionJobRequest
                {
                    LanguageCode = LanguageCode.ZhTW,
                    Media = new Media { MediaFileUri = $"s3://{bucketName}/{sourceObjectName}" },
                    MediaFormat = MediaFormat.Wav,
                    MediaSampleRateHertz = f.SampleRate,
                    TranscriptionJobName = transcriptionJobName,
                    OutputBucketName = bucketName,
                };
                using var tClient = new AmazonTranscribeServiceClient(Options.AwsAccessKeyId, Options.AwsSecretAccessKey, region);
                Logger.LogInformation("Start transcription job ...");
                var jobResponse = await tClient.StartTranscriptionJobAsync(transRequest, cancellationToken);

                if (jobResponse.HttpStatusCode != HttpStatusCode.OK)
                    throw new IOException($"Start transcription job status fault: {jobResponse.HttpStatusCode}");

                var job = jobResponse.TranscriptionJob;
                Logger.LogInformation("Wait transcription job file ...");
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (job.TranscriptionJobStatus == TranscriptionJobStatus.COMPLETED)
                        break;
                    if (job.TranscriptionJobStatus == TranscriptionJobStatus.FAILED)
                        break;

                    // 因為這個 https://github.com/aws/aws-sdk-cpp/issues/1465 bug 造成狀態永遠不會完成; 所以要直接去看結果文件
                    listObjRequest = new ListObjectsRequest
                    {
                        BucketName = bucketName,
                        Prefix = job.TranscriptionJobName,
                    };
                    listObjResponse = await s3Client.ListObjectsAsync(listObjRequest, cancellationToken);
                    if (listObjResponse.S3Objects.Any())
                        break;

                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                if (job.TranscriptionJobStatus == TranscriptionJobStatus.FAILED)
                    throw new IOException(job.FailureReason);
            }

            var getObjectRequest = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = resultObjectName,
            };
            Logger.LogInformation("Wait transcription result ...");
            TranscriptionJobResult? jobResult = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                using var download = await s3Client.GetObjectAsync(getObjectRequest);
                using var stream = download.ResponseStream;

                jobResult = await Utf8Json.JsonSerializer.DeserializeAsync<TranscriptionJobResult>(stream);

                if (jobResult.status == TranscriptionJobStatus.COMPLETED)
                    break;
                if (jobResult.status == TranscriptionJobStatus.FAILED)
                    break;

                await Task.Delay(1000, cancellationToken);
                continue;
            }
            if (jobResult == null)
                throw new IOException("Parse result json got null");
            if (jobResult.status == TranscriptionJobStatus.FAILED)
                throw new IOException("Result fault");

            var index = 0u;
            foreach (var result in jobResult.results.items)
            {
                var content = result.alternatives.First().content;
                content = content.Replace("，", Environment.NewLine);
                content = content.Replace("。", Environment.NewLine);
                content = content.Replace("？", Environment.NewLine);
                content = content.Replace("！", Environment.NewLine);
                if (string.IsNullOrWhiteSpace(content))
                {
                    Logger.LogDebug("{0} continue with result empty", nameof(AnalyzeAsync));
                    continue;
                }
                var start = TimeSpan.FromSeconds(double.Parse(result.start_time));
                var end = TimeSpan.FromSeconds(double.Parse(result.end_time));
                var line = $"{++index}{Environment.NewLine}{start:hh\\:mm\\:ss\\,fff} --> {end:hh\\:mm\\:ss\\,fff}{Environment.NewLine}{content}{Environment.NewLine}";
                Logger.LogDebug(line);
                yield return line;
            }
        }

        public override string ToString() => "Amazon Transcribe";

        public sealed class TranscriptionJobResult
        {
            public string jobName { get; set; } = string.Empty;
            public string accountId { get; set; } = string.Empty;
            public string status { get; set; } = TranscriptionJobStatus.IN_PROGRESS;
            public TranscriptionResult results { get; set; } = new();
        }

        public sealed class TranscriptionResult
        {
            public IList<Transcript> transcripts { get; set; } = Array.Empty<Transcript>();
            public IList<TranscriptItem> items { get; set; } = Array.Empty<TranscriptItem>();
        }

        public sealed class Transcript
        {
            public string transcript { get; set; } = string.Empty;
        }

        public sealed class TranscriptItem
        {
            public string start_time { get; set; } = string.Empty;
            public string end_time { get; set; } = string.Empty;
            public IList<AlternativeTranscription> alternatives { get; set; } = Array.Empty<AlternativeTranscription>();
            public string type { get; set; } = string.Empty;
        }

        public sealed class AlternativeTranscription
        {
            public string confidence { get; set; } = string.Empty;
            public string content { get; set; } = string.Empty;
        }
    }
#endif
}