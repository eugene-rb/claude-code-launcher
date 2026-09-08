using System.Buffers.Binary;
using System.Text;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class WavAudioTests
{
    private static byte[] PcmWav(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), samples[i]);
        }

        return WavAudio.WrapPcm(bytes, sampleRate: 44100, channels: 1);
    }

    private static short[] ReadSamples(byte[] wav, int count)
    {
        // WrapPcm emits the canonical 44-byte header, so the samples start there.
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2, 2));
        }

        return samples;
    }

    [Fact]
    public void Scale_FullVolume_ReturnsInputUnchanged()
    {
        var wav = PcmWav(1000, -1000, 32767);

        Assert.Same(wav, WavAudio.Scale(wav, 1.0));
    }

    [Fact]
    public void Scale_Zero_SilencesEverySample()
    {
        var scaled = WavAudio.Scale(PcmWav(1000, -1000, 32767, short.MinValue), 0.0);

        Assert.All(ReadSamples(scaled, 4), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void Scale_Half_HalvesEverySample()
    {
        var scaled = WavAudio.Scale(PcmWav(1000, -1000, 200), 0.5);

        Assert.Equal([500, -500, 100], ReadSamples(scaled, 3));
    }

    [Fact]
    public void Scale_LeavesTheHeaderIntact()
    {
        var original = PcmWav(1000, 2000);

        var scaled = WavAudio.Scale(original, 0.25);

        Assert.Equal(original[..44], scaled[..44]);
        Assert.Equal(original.Length, scaled.Length);
    }

    [Fact]
    public void Scale_DoesNotMutateTheInput()
    {
        var original = PcmWav(1000, 2000);
        var before = (byte[])original.Clone();

        WavAudio.Scale(original, 0.1);

        Assert.Equal(before, original);
    }

    [Fact]
    public void Scale_SkipsChunksBetweenFmtAndData()
    {
        // Encoders routinely insert LIST/fact chunks before the samples. Scaling from a fixed 44-byte
        // offset would corrupt those bytes instead of the audio.
        var samples = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(0, 2), 1000);
        BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(2, 2), -400);

        var wav = new List<byte>();
        wav.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        wav.AddRange(BitConverter.GetBytes(0));
        wav.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        wav.AddRange(Encoding.ASCII.GetBytes("fmt "));
        wav.AddRange(BitConverter.GetBytes(16));
        wav.AddRange(BitConverter.GetBytes((ushort)1));
        wav.AddRange(BitConverter.GetBytes((ushort)1));
        wav.AddRange(BitConverter.GetBytes(44100));
        wav.AddRange(BitConverter.GetBytes(88200));
        wav.AddRange(BitConverter.GetBytes((ushort)2));
        wav.AddRange(BitConverter.GetBytes((ushort)16));
        wav.AddRange(Encoding.ASCII.GetBytes("LIST"));
        wav.AddRange(BitConverter.GetBytes(4));
        wav.AddRange(Encoding.ASCII.GetBytes("INFO"));
        var dataOffset = wav.Count + 8;
        wav.AddRange(Encoding.ASCII.GetBytes("data"));
        wav.AddRange(BitConverter.GetBytes(samples.Length));
        wav.AddRange(samples);

        var scaled = WavAudio.Scale([.. wav], 0.5);

        Assert.Equal("INFO", Encoding.ASCII.GetString(scaled, dataOffset - 12, 4));
        Assert.Equal(500, BinaryPrimitives.ReadInt16LittleEndian(scaled.AsSpan(dataOffset, 2)));
        Assert.Equal(-200, BinaryPrimitives.ReadInt16LittleEndian(scaled.AsSpan(dataOffset + 2, 2)));
    }

    [Fact]
    public void Scale_NonPcmInput_ReturnsInputUnchangedInsteadOfThrowing()
    {
        Assert.Same(Array.Empty<byte>(), WavAudio.Scale([], 0.5));

        var notAWav = Encoding.ASCII.GetBytes("this is not a wav file at all");
        Assert.Same(notAWav, WavAudio.Scale(notAWav, 0.5));
    }

    [Fact]
    public void Scale_EightBitPcm_IsLeftAloneRatherThanCorrupted()
    {
        var wav = new List<byte>();
        wav.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        wav.AddRange(BitConverter.GetBytes(0));
        wav.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        wav.AddRange(Encoding.ASCII.GetBytes("fmt "));
        wav.AddRange(BitConverter.GetBytes(16));
        wav.AddRange(BitConverter.GetBytes((ushort)1));
        wav.AddRange(BitConverter.GetBytes((ushort)1));
        wav.AddRange(BitConverter.GetBytes(8000));
        wav.AddRange(BitConverter.GetBytes(8000));
        wav.AddRange(BitConverter.GetBytes((ushort)1));
        wav.AddRange(BitConverter.GetBytes((ushort)8));
        wav.AddRange(Encoding.ASCII.GetBytes("data"));
        wav.AddRange(BitConverter.GetBytes(2));
        wav.AddRange(new byte[] { 200, 100 });

        var input = wav.ToArray();
        Assert.Same(input, WavAudio.Scale(input, 0.5));
    }

    [Fact]
    public void PerceptualAmplitude_IsMonotonicAndClampedToTheUnitRange()
    {
        Assert.Equal(0.0, WavAudio.PerceptualAmplitude(0));
        Assert.Equal(1.0, WavAudio.PerceptualAmplitude(1));
        Assert.Equal(0.0, WavAudio.PerceptualAmplitude(-5));
        Assert.Equal(1.0, WavAudio.PerceptualAmplitude(5));
        // The low end is boosted so useful speech remains audible at modest settings.
        Assert.True(WavAudio.PerceptualAmplitude(0.5) > 0.5);
        Assert.True(WavAudio.PerceptualAmplitude(0.5) > WavAudio.PerceptualAmplitude(0.25));
    }

    [Fact]
    public void BuildTone_ProducesAPlayable16BitPcmWavThatScaleUnderstands()
    {
        var tone = WavAudio.BuildTone(880, TimeSpan.FromMilliseconds(50), 1.0);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(tone, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(tone, 8, 4));
        Assert.Equal(44 + 44100 * 50 / 1000 * 2, tone.Length);

        var quiet = WavAudio.Scale(tone, 0.0);
        Assert.NotSame(tone, quiet);
        Assert.All(ReadSamples(quiet, 100), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void BuildTone_ZeroVolume_IsSilent()
    {
        var tone = WavAudio.BuildTone(880, TimeSpan.FromMilliseconds(20), 0.0);

        Assert.All(ReadSamples(tone, 100), sample => Assert.Equal(0, sample));
    }
}
