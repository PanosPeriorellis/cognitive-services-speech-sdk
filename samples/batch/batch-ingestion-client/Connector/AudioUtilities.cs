// <copyright file="AudioUtilities.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
// </copyright>
namespace Connector
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using NAudio.Wave;
    using NAudio.Wave.SampleProviders;

    internal class AudioUtilities
    {
        internal static int CountAudioChannels(byte[] audioBytes)
        {
            return default;
        }

        internal static byte[] GetAudioChannelByNumber(int channel)
        {
            return default;
        }

        internal static byte[] RedactAudio(string path, TI_Transcript ti_transcript)
        {
            var rawCuttings = ti_transcript.transcript.Where(utt => utt.RedactedAudioSpans.Count() > 0);
            var cuts = new Dictionary<int, int>(rawCuttings.ToDictionary(t => t.Offset, t => t.Duration));
            var channelCuts = ti_transcript.transcript
                .Where(utt => utt.RedactedLexicalSpans.Count() > 0)
                .GroupBy(p => p.Channel, p => p, (key, g) => new { Channel = key, Spans = g.OrderBy(t => t.Offset).Select(t => new RedactionStamp(t.Offset, t.Duration)) })
                .ToDictionary(t => t.Channel, t => t.Spans);

            return RedactAudio(path, channelCuts);
        }

        internal static byte[] RedactAudio(string path, Dictionary<int, IEnumerable<RedactionStamp>> redactionStamps)
        {
            var memoryStream = new MemoryStream(File.ReadAllBytes(path));
            var waveFileReader = new WaveFileReader(memoryStream);
            var sampleRate = waveFileReader.WaveFormat.SampleRate;
            var channels = waveFileReader.WaveFormat.Channels;
            var millisecondsPerFrame = 1000 / (float)sampleRate;

            var currentRedactionStamps = new Tuple<int, RedactionStamp>[channels];
            foreach (var channelId in redactionStamps.Keys)
            {
                currentRedactionStamps[channelId] = Tuple.Create(0, redactionStamps[channelId].First());
            }

            var outStream = new MemoryStream();
            var writer = new WaveFileWriter(outStream, new WaveFormat(sampleRate, channels));

            float[] buffer;
            var counter = 0;
            while ((buffer = waveFileReader.ReadNextSampleFrame())?.Length > 0)
            {
                var offsetInMilliseconds = (long)(counter * millisecondsPerFrame);

                // i is channel index
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (currentRedactionStamps[i] == null || currentRedactionStamps[i].Item2 == null)
                    {
                        writer.WriteSample(buffer[i]);
                        continue;
                    }

                    var currentRedactionStamp = currentRedactionStamps[i].Item2;
                    if (offsetInMilliseconds >= currentRedactionStamp.Offset && offsetInMilliseconds <= currentRedactionStamp.Offset + currentRedactionStamp.Duration)
                    {
                        writer.WriteSample(0f);
                    }
                    else
                    {
                        writer.WriteSample(buffer[i]);

                        if (offsetInMilliseconds > currentRedactionStamp.Offset + currentRedactionStamp.Duration)
                        {
                            var newIndex = currentRedactionStamps[i].Item1 + 1;
                            currentRedactionStamps[i] = Tuple.Create(newIndex, redactionStamps[i].ElementAtOrDefault(newIndex));
                        }
                    }
                }

                counter++;
            }

            waveFileReader.Dispose();
            writer.Flush();
            writer.Dispose();
            var outBytes = outStream.ToArray();

            return outBytes;
        }

        internal static IWaveProvider CutSegmentsInMonoAudio(WaveStream wave, Dictionary<int, int> cuts)
        {
            ISampleProvider sourceProvider = wave.ToSampleProvider();
            ISampleProvider returnedProvider = null;
            TimeSpan timestampAdjustmentBy = new TimeSpan(0);

            if (cuts == null)
            {
                throw new ArgumentNullException(nameof(cuts));
            }

            if (cuts.Count == 0)
            {
                wave.ToSampleProvider();
            }

            // check if timestamps are correct
            if (!CutsVerified(cuts))
            {
                wave.ToSampleProvider();
            }

            foreach (var keyValuePair in cuts)
            {
                TimeSpan startPosition = new TimeSpan(0, 0, 0, 0, keyValuePair.Key);
                TimeSpan duration = new TimeSpan(0, 0, 0, 0, keyValuePair.Value);

                long currentPosition = wave != null ? wave.Position : throw new ArgumentNullException(nameof(wave)); // Save stream position
                TimeSpan endPosition = startPosition + duration;

                // String s = String.Format("Cutting startPosition: {0}, duration: {1}, endPosition:{2}", startPosition, duration, endPosition);
                // Take audio from the beginning of file until {startPosition}
                OffsetSampleProvider offset1 = new OffsetSampleProvider(sourceProvider)
                {
                    Take = startPosition
                };

                // Take audio after {endPosition} until the end of file
                OffsetSampleProvider offset2 = new OffsetSampleProvider(sourceProvider)
                {
                    SkipOver = endPosition - startPosition,
                    Take = TimeSpan.Zero
                };

                var beep1 = new SignalGenerator(offset1.WaveFormat.SampleRate, offset1.WaveFormat.Channels) { Frequency = 1000, Gain = 0.2 }.Take(offset2.SkipOver).ToWaveProvider().ToSampleProvider();
                var silence = new SilenceProvider(beep1.WaveFormat).ToSampleProvider().Take(offset2.SkipOver);
                OffsetSampleProvider offset_silence = new OffsetSampleProvider(silence)
                {
                    Take = TimeSpan.Zero
                };

                wave.Position = currentPosition; // Restore stream position
                returnedProvider = offset1.FollowedBy(offset_silence).FollowedBy(offset2);
                sourceProvider = returnedProvider;
            }

            return returnedProvider.ToWaveProvider();
        }

        private static string GetFileName(string path)
        {
            var name = path.Split('/').LastOrDefault();
            if (name != null)
            {
                return name.Split('.').FirstOrDefault();
            }

            return string.Empty;
        }

        private static bool CutsVerified(Dictionary<int, int> cuts)
        {
            int startingPOsition = 0;
            foreach (var cut in cuts)
            {
                if (cut.Key + cut.Value < startingPOsition)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
