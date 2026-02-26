using System.Runtime.InteropServices;

namespace OpenOrca.Cli.Repl;

/// <summary>
/// Helper to inject synthetic key events into the console input buffer.
/// Used to unblock a Console.ReadKey() call when cancelling an interactive prompt.
/// </summary>
internal static class ConsoleInputHelper
{
    /// <summary>
    /// Inject a synthetic Enter keypress into the console input buffer so that a
    /// blocking Console.ReadKey() call returns immediately. Windows only; no-ops elsewhere.
    /// </summary>
    public static void SendEnterKey()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var handle = GetStdHandle(STD_INPUT_HANDLE);
            var records = new INPUT_RECORD[]
            {
                new()
                {
                    EventType = KEY_EVENT,
                    KeyEvent = new KEY_EVENT_RECORD
                    {
                        bKeyDown = 1,
                        wRepeatCount = 1,
                        wVirtualKeyCode = VK_RETURN,
                        UnicodeChar = '\r'
                    }
                },
                new()
                {
                    EventType = KEY_EVENT,
                    KeyEvent = new KEY_EVENT_RECORD
                    {
                        bKeyDown = 0,
                        wRepeatCount = 1,
                        wVirtualKeyCode = VK_RETURN,
                        UnicodeChar = '\r'
                    }
                }
            };
            WriteConsoleInputW(handle, records, (uint)records.Length, out _);
        }
        catch
        {
            // Best effort — if this fails the orphan thread persists until next keypress.
        }
    }

    private const int STD_INPUT_HANDLE = -10;
    private const ushort KEY_EVENT = 1;
    private const ushort VK_RETURN = 0x0D;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteConsoleInputW(
        IntPtr hConsoleInput,
        INPUT_RECORD[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsWritten);

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct KEY_EVENT_RECORD
    {
        [FieldOffset(0)] public int bKeyDown;
        [FieldOffset(4)] public ushort wRepeatCount;
        [FieldOffset(6)] public ushort wVirtualKeyCode;
        [FieldOffset(8)] public ushort wVirtualScanCode;
        [FieldOffset(10)] public char UnicodeChar;
        [FieldOffset(12)] public uint dwControlKeyState;
    }
}
