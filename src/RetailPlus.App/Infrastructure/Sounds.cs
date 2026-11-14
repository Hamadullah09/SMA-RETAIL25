using System.IO;
using System.Media;
using System.Runtime.InteropServices;

namespace RetailPlus.App.Infrastructure;

/// <summary>
/// The counter's sounds: one short scanner-style beep for each RFID tag rung up. The readers' own buzzers stay quiet
/// (they would beep at every tag lying on the antenna, every round), so a beep always means a new piece on the sale.
/// The exit-gate alarm plays a loud siren, over and over until someone silences it. Both are made here in code.
/// </summary>
public static class Sounds
{
    private static readonly Lazy<SoundPlayer?> SirenSound = new(() => Player(Siren()));

    /// <summary>True while the gate siren is sounding.</summary>
    public static bool SirenOn { get; private set; }

    /// <summary>Starts the gate siren, repeating until <see cref="StopSiren"/> (whatever the beep setting says).</summary>
    public static void StartSiren()
    {
        if (SirenOn) return;
        SirenOn = true;
        if (AppServices.Unattended)
        {
            Log.Write("Gate siren on");
            return;
        }
        try
        {
            SirenSound.Value?.PlayLooping();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static void StopSiren()
    {
        if (!SirenOn) return;
        SirenOn = false;
        if (AppServices.Unattended)
        {
            Log.Write("Gate siren silenced");
            return;
        }
        try
        {
            SirenSound.Value?.Stop();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static SoundPlayer? Player(byte[] wav)
    {
        try
        {
            var player = new SoundPlayer(new MemoryStream(wav));
            player.Load();
            return player;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static readonly Lazy<SoundPlayer?> TagBeep = new(() =>
    {
        try
        {
            var player = new SoundPlayer(new MemoryStream(Tone(2700, 0.11)));
            player.Load();
            return player;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return null;
        }
    });

    /// <summary>
    /// A tag was rung up (unless beeping is off in Setup › This station). Plays without waiting. Quiet while the gate
    /// siren sounds: Windows plays one sound at a time, and a beep would end the siren.
    /// </summary>
    public static void TagRead()
    {
        if (!AppServices.Config.RfidBeep || SirenOn) return;
        try
        {
            TagBeep.Value?.Play();
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Whether the computer's speakers are muted or turned right down (or missing), so a beep cannot be heard. False
    /// when that cannot be told.
    /// </summary>
    public static bool IsSilenced()
    {
        try
        {
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (devices.GetDefaultAudioEndpoint(0, 1, out var speakers) != 0 || speakers is null) return true;   // render, multimedia
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(speakers.Activate(ref iid, 23, IntPtr.Zero, out var activated));   // CLSCTX_ALL
            var volume = (IAudioEndpointVolume)activated;
            Marshal.ThrowExceptionForHR(volume.GetMute(out bool muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float level));
            return muted || level < 0.02f;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// A sine tone as a 16-bit mono WAV file, after a moment of silence (speakers that sleep wake up during it instead of
    /// swallowing the start of the beep) and faded in and out so it does not click.
    /// </summary>
    private static byte[] Tone(double hertz, double seconds, int sampleRate = 22050)
    {
        int lead = sampleRate / 25, tone = (int)(sampleRate * seconds), fade = sampleRate / 250;
        var samples = new short[lead + tone];
        for (int i = 0; i < tone; i++)
        {
            double envelope = Math.Min(1, Math.Min(i, tone - i) / (double)fade);
            samples[lead + i] = (short)(Math.Sin(2 * Math.PI * hertz * i / sampleRate) * envelope * 0.5 * short.MaxValue);
        }
        return Wav(samples, sampleRate);
    }

    /// <summary>
    /// The gate siren: one wail of 1.6 seconds, rising from 600 to 1 500 Hz and falling back, played over and over. The
    /// wave is softly clipped (close to a square wave), which sounds much louder than a sine at the same level, and it
    /// starts and ends at the same pitch and phase, so the repeats join without a click.
    /// </summary>
    internal static byte[] Siren(int sampleRate = 22050)
    {
        const double low = 600, high = 1500, seconds = 1.6;
        int count = (int)(sampleRate * seconds);
        var samples = new short[count];
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / count;                                  // 0 → 1 over the wail
            double sweep = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t);          // 0 → 1 → 0, smoothly
            phase += 2 * Math.PI * (low + (high - low) * sweep) / sampleRate;
            samples[i] = (short)(Math.Tanh(3 * Math.Sin(phase)) / Math.Tanh(3) * 0.9 * short.MaxValue);
        }
        return Wav(samples, sampleRate);
    }

    /// <summary>16-bit mono samples as a WAV file.</summary>
    private static byte[] Wav(short[] data, int sampleRate)
    {
        int samples = data.Length;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(sampleRate);
        w.Write(sampleRate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        foreach (var sample in data) w.Write(sample);
        w.Flush();
        return stream.ToArray();
    }

    // Windows Core Audio, only as much as reading the speakers' mute and volume needs (methods in vtable order).
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float levelDb, ref Guid context);
        int SetMasterVolumeLevelScalar(float level, ref Guid context);
        int GetMasterVolumeLevel(out float levelDb);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        int GetChannelVolumeLevel(uint channel, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channel, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
