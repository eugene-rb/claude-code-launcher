using System.Runtime.InteropServices;

namespace ClaudeLauncher.App.Services;

/// <summary>Injects text as synthetic keyboard input into another process's console, via
/// AttachConsole + WriteConsoleInput. Used to unattended-nudge a freshly auto-resumed CLI session
/// (which reopens at an idle prompt after `-c`, waiting for input) back into action.
///
/// ASCII-only by design: confirmed via direct experimentation that this technique reliably delivers
/// plain ASCII characters (each maps to a real virtual-key code via <see cref="VkKeyScan"/>), but
/// Unicode characters with no mapping on the active keyboard layout (VkKeyScan returns -1, e.g. all of
/// Japanese) are corrupted or silently dropped depending on what virtual-key code is substituted -
/// PSReadLine's handling of a synthetic key event with no real key behind it is inconsistent. Do not
/// widen this to arbitrary user-typed text without solving that first.</summary>
public static class ConsoleInputInjector
{
    private const ushort KeyEventType = 0x0001;
    private const ushort VkReturn = 0x0D;
    private const int StdInputHandle = -10;

    /// <summary>Attaches to <paramref name="processId"/>'s console, types <paramref name="text"/> as
    /// if from the keyboard, and detaches. Returns false (without throwing) if the process has no
    /// console to attach to, e.g. it already exited. The caller's own process must not already have a
    /// console attached - <see cref="ClaudeLauncher.App"/> is a WPF (Windows subsystem) app, so this is
    /// always true here.</summary>
    public static bool TrySendText(int processId, string text)
    {
        if (!AttachConsole((uint)processId))
        {
            return false;
        }

        try
        {
            var stdIn = GetStdHandle(StdInputHandle);
            if (stdIn == IntPtr.Zero || stdIn == new IntPtr(-1))
            {
                return false;
            }

            var records = new List<InputRecord>(text.Length * 2);
            foreach (var c in text)
            {
                var vk = ResolveVirtualKeyCode(c);
                records.Add(new InputRecord { EventType = KeyEventType, KeyDown = 1, RepeatCount = 1, UnicodeChar = c, VirtualKeyCode = vk });
                records.Add(new InputRecord { EventType = KeyEventType, KeyDown = 0, RepeatCount = 1, UnicodeChar = c, VirtualKeyCode = vk });
            }

            var arr = records.ToArray();
            return WriteConsoleInput(stdIn, arr, (uint)arr.Length, out _);
        }
        finally
        {
            FreeConsole();
        }
    }

    private static ushort ResolveVirtualKeyCode(char c)
    {
        if (c is '\r' or '\n')
        {
            return VkReturn;
        }

        var scan = VkKeyScan(c);
        return scan == -1 ? (ushort)0 : (ushort)(scan & 0xFF);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(12)] public ushort VirtualScanCode;
        [FieldOffset(14)] public ushort UnicodeChar;
        [FieldOffset(16)] public uint ControlKeyState;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteConsoleInput(IntPtr hConsoleInput, [In] InputRecord[] lpBuffer, uint nLength, out uint lpNumberOfEventsWritten);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);
}
