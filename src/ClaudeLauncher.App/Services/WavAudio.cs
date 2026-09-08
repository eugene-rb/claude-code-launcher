using System.Buffers.Binary;
using System.Text;

namespace ClaudeLauncher.App.Services;

/// <summary>WAV manipulation for the notification cues, kept pure (bytes in, bytes out - no file or
/// device I/O) so it can be unit tested directly, like <see cref="ProcessLauncherService"/>'s script
/// builders.
///
/// <para><see cref="Scale"/> exists because playback goes through <c>System.Media.SoundPlayer</c>,
/// which has no volume control of its own. Scaling the sample values before handing them over is what
/// makes the settings tab's volume slider mean anything. This is the app's own level, independent of
/// the Windows mixer.</para></summary>
public static class WavAudio
{
    private const int DefaultSampleRate = 44100;
    private const ushort PcmFormatTag = 1;
    private const ushort Bits16 = 16;

    /// <summary>Returns <paramref name="wav"/> with every 16-bit PCM sample multiplied by
    /// <paramref name="volume"/>, clipped to the 16-bit range. <paramref name="volume"/> is a linear
    /// 0.0-1.0 amplitude factor; callers wanting a perceptual scale should square the slider value
    /// (see <see cref="PerceptualAmplitude"/>) before calling.
    ///
    /// <para>Returns the input unchanged - never throws - when it is not 16-bit PCM, or when the
    /// header can't be read. A cue that plays at the wrong volume is a far better failure than one
    /// that takes an unattended failover down with an exception.</para></summary>
    public static byte[] Scale(byte[] wav, double volume)
    {
        if (volume >= 1.0)
        {
            return wav;
        }

        if (!TryLocateData(wav, out var dataOffset, out var dataLength))
        {
            return wav;
        }

        var factor = Math.Clamp(volume, 0.0, 1.0);
        var scaled = (byte[])wav.Clone();

        // Trailing odd byte (if any) isn't half a sample worth reading; leave it as-is.
        for (var i = dataOffset; i + 1 < dataOffset + dataLength; i += 2)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(scaled.AsSpan(i, 2));
            var value = (int)Math.Round(sample * factor);
            BinaryPrimitives.WriteInt16LittleEndian(
                scaled.AsSpan(i, 2),
                (short)Math.Clamp(value, short.MinValue, short.MaxValue));
        }

        return scaled;
    }

    /// <summary>Maps a 0.0-1.0 slider position to an amplitude factor. Loudness is perceived roughly
    /// logarithmically, so the low end needs a boost rather than another reduction: squaring the
    /// slider made a 25% setting play at only 6.25% amplitude, which rendered spoken cues effectively
    /// inaudible. A square-root curve keeps fine control near full volume while making ordinary low
    /// settings useful.</summary>
    public static double PerceptualAmplitude(double sliderValue)
    {
        var clamped = Math.Clamp(sliderValue, 0.0, 1.0);
        return Math.Sqrt(clamped);
    }

    /// <summary>Builds a mono 16-bit PCM sine tone. Used only as the fallback cue when a voice asset
    /// is missing from the build, so an incomplete install still signals rather than going silent.
    /// Both ends are faded to avoid the click a hard start/stop on a non-zero sample produces.</summary>
    public static byte[] BuildTone(double frequencyHz, TimeSpan duration, double volume)
    {
        var sampleCount = Math.Max(1, (int)(DefaultSampleRate * duration.TotalSeconds));
        var amplitude = Math.Clamp(volume, 0.0, 1.0) * short.MaxValue * 0.6;
        var fadeSamples = Math.Min(sampleCount / 2, DefaultSampleRate / 200);

        var samples = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var envelope = fadeSamples == 0
                ? 1.0
                : Math.Min(1.0, Math.Min(i, sampleCount - 1 - i) / (double)fadeSamples);
            var value = (short)Math.Clamp(
                amplitude * envelope * Math.Sin(2 * Math.PI * frequencyHz * i / DefaultSampleRate),
                short.MinValue,
                short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i * 2, 2), value);
        }

        return WrapPcm(samples, DefaultSampleRate, channels: 1);
    }

    /// <summary>Wraps raw 16-bit PCM samples in a canonical 44-byte RIFF/WAVE header.</summary>
    public static byte[] WrapPcm(byte[] samples, int sampleRate, int channels)
    {
        const int HeaderSize = 44;
        var blockAlign = (ushort)(channels * (Bits16 / 8));
        var wav = new byte[HeaderSize + samples.Length];
        var span = wav.AsSpan();

        Encoding.ASCII.GetBytes("RIFF").CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), (uint)(36 + samples.Length));
        Encoding.ASCII.GetBytes("WAVE").CopyTo(span.Slice(8, 4));
        Encoding.ASCII.GetBytes("fmt ").CopyTo(span.Slice(12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(20, 2), PcmFormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(22, 2), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(28, 4), (uint)(sampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(32, 2), blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(34, 2), Bits16);
        Encoding.ASCII.GetBytes("data").CopyTo(span.Slice(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(40, 4), (uint)samples.Length);
        samples.CopyTo(span[HeaderSize..]);

        return wav;
    }

    /// <summary>Walks the RIFF chunk list for the `data` chunk, checking on the way that `fmt ` says
    /// 16-bit PCM. Chunk-walking rather than assuming a 44-byte header: encoders (the voice assets
    /// among them) routinely insert `LIST`/`fact` chunks before the samples, and scaling from a fixed
    /// offset would corrupt those bytes instead of the audio.</summary>
    private static bool TryLocateData(byte[] wav, out int dataOffset, out int dataLength)
    {
        dataOffset = 0;
        dataLength = 0;

        if (wav.Length < 12
            || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF"
            || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
        {
            return false;
        }

        var formatVerified = false;
        var position = 12;

        while (position + 8 <= wav.Length)
        {
            var chunkId = Encoding.ASCII.GetString(wav, position, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(position + 4, 4));
            var body = position + 8;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16 || body + 16 > wav.Length)
                {
                    return false;
                }

                var format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body, 2));
                var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14, 2));
                if (format != PcmFormatTag || bitsPerSample != Bits16)
                {
                    return false;
                }

                formatVerified = true;
            }
            else if (chunkId == "data")
            {
                if (!formatVerified)
                {
                    return false;
                }

                // A truncated file (or one whose declared size overruns the buffer) is scaled only as
                // far as the bytes actually present.
                dataOffset = body;
                dataLength = (int)Math.Min(chunkSize, (uint)(wav.Length - body));
                return dataLength > 0;
            }

            // Chunks are word-aligned: an odd size is followed by a pad byte.
            var advance = 8 + (long)chunkSize + (chunkSize % 2);
            if (advance <= 0 || position + advance > wav.Length)
            {
                return false;
            }

            position += (int)advance;
        }

        return false;
    }
}
