﻿// <copyright file="TranscriptionRealTimeProcessor.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
// </copyright>

namespace FetchTranscription
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Connector;
    using Microsoft.CognitiveServices.Speech;
    using Microsoft.CognitiveServices.Speech.Audio;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    public class TranscriptionRealTimeProcessor
    {
        /// <summary>
        /// Gets or sets a list that accumulates transcription final results.
        /// </summary>
        private List<RealTimeUtt> FinalResultsCumulative = new List<RealTimeUtt>();

        private byte[] wavFileName;

        private ILogger log;

        private byte[] channelContent;

        private int channel;

        // The TaskCompletionSource must be rooted.
        // See https://blogs.msdn.microsoft.com/pfxteam/2011/10/02/keeping-async-methods-alive/ for details.
        private TaskCompletionSource<int> stopBaseRecognitionTaskCompletionSource;

        public TranscriptionRealTimeProcessor(string key, string region, byte[] channelContent, int channel, string language, ILogger log)
        {
            this.log = log;
            this.UseBaseModel = true;
            this.SubscriptionKey = key;
            this.Region = region;
            this.channelContent = channelContent;
            this.channel = channel;

            StartTranscription(language, region, channelContent);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the baseline model used for recognition.
        /// </summary>
        public bool UseBaseModel { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether custom model used for recognition.
        /// </summary>
        public bool UseCustomModel { get; set; }

        /// <summary>
        /// Gets or sets Subscription Key..
        /// </summary>
        public string SubscriptionKey { get; set; }

        /// <summary>
        /// Gets or sets region name of the service.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Gets or sets recognition language.
        /// </summary>
        public string RecognitionLanguage { get; set; }

        /// <summary>
        /// Handles the Click event of the StartButton:
        /// Disables Settings Panel in UI to prevent Click Events during Recognition
        /// Checks if keys are valid
        /// Plays audio if input source is a valid audio file
        /// Triggers Creation of specified Recognizers.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="RoutedEventArgs"/> instance containing the event data.</param>
        private void StartTranscription(string language, string region, byte[] channel)
        {
            wavFileName = channel;
            this.Region = region;
            this.RecognitionLanguage = language;
            stopBaseRecognitionTaskCompletionSource = new TaskCompletionSource<int>();

            Task.Run(async () => { await CreateRecognizer().ConfigureAwait(false); });
        }

        /// <summary>
        /// Creates Recognizer with baseline model and selected language:
        /// Creates a config with subscription key and selected region
        /// If input source is audio file, creates recognizer with audio file otherwise with default mic
        /// Waits on RunRecognition.
        /// </summary>
        private async Task CreateRecognizer()
        {
            // Todo: suport users to specifiy a different region.
            var config = SpeechConfig.FromSubscription(this.SubscriptionKey, this.Region);
            config.SpeechRecognitionLanguage = this.RecognitionLanguage;
            config.OutputFormat = OutputFormat.Detailed;

            SpeechRecognizer basicRecognizer;

            PushAudioInputStream pushStream = AudioInputStream.CreatePushStream();
            pushStream.Write(channelContent);
            pushStream.Close();
            using (var audioInput = AudioConfig.FromStreamInput(pushStream))
            {
                using (basicRecognizer = new SpeechRecognizer(config, audioInput))
                {
                    await this.RunRecognizer(basicRecognizer, stopBaseRecognitionTaskCompletionSource).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Subscribes to Recognition Events
        /// Starts the Recognition and waits until final result is received, then Stops recognition.
        /// </summary>
        /// <param name="recognizer">Recognizer object.</param>
        /// <param name="recoType">Type of Recognizer.</param>
        /// <value>
        ///   <c>Base</c> if Baseline model; otherwise, <c>Custom</c>.
        /// </value>
        private async Task RunRecognizer(SpeechRecognizer recognizer, TaskCompletionSource<int> source)
        {
            // subscribe to events
            EventHandler<SpeechRecognitionEventArgs> recognizedHandler = (sender, e) => RecognizedEventHandler(e);
            EventHandler<SpeechRecognitionCanceledEventArgs> canceledHandler = (sender, e) => CanceledEventHandler(e, source);
            EventHandler<SessionEventArgs> sessionStartedHandler = (sender, e) => SessionStartedEventHandler(e);
            EventHandler<SessionEventArgs> sessionStoppedHandler = (sender, e) => SessionStoppedEventHandler(e);

            recognizer.Recognized += recognizedHandler;
            recognizer.Canceled += canceledHandler;
            recognizer.SessionStarted += sessionStartedHandler;
            recognizer.SessionStopped += sessionStoppedHandler;

            // start,wait,stop recognition
            await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
            await source.Task.ConfigureAwait(false);
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Logs the final recognition result.
        /// </summary>
        private void RecognizedEventHandler(SpeechRecognitionEventArgs e)
        {
            string json = e.Result.Properties.GetProperty(PropertyId.SpeechServiceResponse_JsonResult);
            var utt = JsonConvert.DeserializeObject<RealTimeUtt>(json);
            FinalResultsCumulative.Add(utt);
        }

        /// <summary>
        /// Logs Canceled events
        /// And sets the TaskCompletionSource to 0, in order to trigger Recognition Stop.
        /// </summary>
        private void CanceledEventHandler(SpeechRecognitionCanceledEventArgs e, TaskCompletionSource<int> source)
        {
            if (log != null)
            {
                log.LogInformation($"Speech recognition: Session Cancelled: {0}.", e.ToString());

                if (FinalResultsCumulative.Count > 0)
                {
                    CreateTranscriptJson();
                }

                source.TrySetResult(0);

                return;
            }
        }

        /// <summary>
        /// Session started event handler.
        /// </summary>
        private void SessionStartedEventHandler(SessionEventArgs e)
        {
            if (log != null)
            {
                log.LogInformation($"Speech recognition: Session started event: {0}.", e.ToString());
                return;
            }
        }

        /// <summary>
        /// Session stopped event handler. Set the TaskCompletionSource to 0, in order to trigger Recognition Stop.
        /// </summary>
        private void SessionStoppedEventHandler(SessionEventArgs e)
        {
            if (log != null)
            {
                log.LogInformation($"Speech recognition: Session Stopped: {0}.", e.ToString());

                if (FinalResultsCumulative.Count > 0)
                {
                    CreateTranscriptJson();
                }

                return;
            }
        }

        private void CreateTranscriptJson()
        {
            if (log != null)
            {
                log.LogInformation($"Found {0} final results. Creating JSON.", FinalResultsCumulative.Count);
            }

            List<RecognizedPhrase> recognizedPhrases = new List<RecognizedPhrase>();
            int totalduration = 0;
            string totaldisplay = string.Empty;
            string totallexical = string.Empty;
            string totalitn = string.Empty;
            string totalmasked = string.Empty;
            string space = " ";
            Console.WriteLine("final results collected:{0}", FinalResultsCumulative.Count);
            foreach (var utt in FinalResultsCumulative)
            {
                totaldisplay = totaldisplay + utt.DisplayText + space;
                totalduration = totalduration + utt.Duration;

                var durationTicks = new TimeSpan(0, 0, 0, 0, utt.Duration).Ticks;
                var offsetTicks = new TimeSpan(0, 0, 0, 0, utt.Offset).Ticks;
                RecognizedPhrase recognizedPhrase = new RecognizedPhrase(utt.RecognitionStatus, channel, 0, utt.Offset.ToString(CultureInfo.InvariantCulture), utt.Duration.ToString(CultureInfo.InvariantCulture), offsetTicks, durationTicks, utt.NBest);
                recognizedPhrases.Add(recognizedPhrase);
            }

            var totalDurationTicks = new TimeSpan(0, 0, 0, 0, totalduration).Ticks;
            CombinedRecognizedPhrase combined = new CombinedRecognizedPhrase(0, totallexical, totalitn, totalmasked, totaldisplay, null);
            SpeechTranscript transcript = new SpeechTranscript(string.Empty, string.Empty, totalDurationTicks, totalduration.ToString(CultureInfo.InvariantCulture), new List<CombinedRecognizedPhrase> { combined }, recognizedPhrases);
            var json = JsonConvert.SerializeObject(transcript);
        }
    }
}