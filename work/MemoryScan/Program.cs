using System.Diagnostics;
using System.Runtime.InteropServices;

if (args.Length != 4 || !new[] { "scan", "filter", "inspect", "map", "scanfloat", "filterfloat", "probe", "pulse", "hold", "freeze" }.Contains(args[0]))
{
    Console.Error.WriteLine("Usage: MemoryScan scan|filter|inspect|map|scanfloat|filterfloat|probe|pulse|hold|freeze PID VALUE SNAPSHOT_OR_ADDRESS");
    return 2;
}
if (!int.TryParse(args[1], out int pid) || !int.TryParse(args[2], out int value)) return 2;
int matchBits = args[0].EndsWith("float", StringComparison.Ordinal) ? BitConverter.SingleToInt32Bits(value) : value;
using var process = Process.GetProcessById(pid);
if (!string.Equals(process.ProcessName, "hl", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Expected hl.exe; refusing to scan a different process.");
    return 2;
}
IntPtr handle = Native.OpenProcess(args[0] is "probe" or "pulse" or "hold" or "freeze" ? 0x0438u : 0x0410u, false, pid); // VM_READ | QUERY_INFORMATION; probes also VM_WRITE | VM_OPERATION
if (handle == IntPtr.Zero) { Console.Error.WriteLine($"OpenProcess failed: {Marshal.GetLastWin32Error()}"); return 1; }
try
{
    var addresses = new List<uint>();
    if (args[0] is "hold" or "freeze")
    {
        uint address = Convert.ToUInt32(args[3].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
        byte[] before = new byte[4];
        if (!Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, before, 4, out nuint n) || n != 4) throw new IOException("Read failed.");
        int original = BitConverter.ToInt32(before);
        if (original != value) throw new InvalidOperationException($"Expected {value}, found {original}; no write performed.");
        const int testValue = 80;
        if (!Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, BitConverter.GetBytes(testValue), 4, out n) || n != 4) throw new IOException("Write failed.");
        try
        {
            if (args[0] == "freeze")
            {
                Console.WriteLine($"FROZEN 0x{address:X8} from {original} to {testValue} for 4 seconds");
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 4000)
                {
                    if (!Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, BitConverter.GetBytes(testValue), 4, out n) || n != 4) throw new IOException("Freeze write failed.");
                    Thread.Sleep(1);
                }
            }
            else
            {
                Console.WriteLine($"HELD 0x{address:X8} from {original} to {testValue} for 3 seconds");
                Thread.Sleep(3000);
            }
        }
        finally
        {
            byte[] current = new byte[4];
            if (Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, current, 4, out n) && n == 4 && BitConverter.ToInt32(current) == testValue)
            {
                if (!Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, before, 4, out n) || n != 4)
                    Console.Error.WriteLine("WARNING: original value could not be restored.");
                else Console.WriteLine("restoredOriginal=true");
            }
            else Console.WriteLine("value changed by game; did not overwrite it");
        }
        return 0;
    }
    if (args[0] == "pulse")
    {
        using (var snapshot = new BinaryReader(File.OpenRead(args[3])))
        {
            if (snapshot.ReadUInt32() != 0x4E435343 || snapshot.ReadInt32() != pid)
                throw new InvalidDataException("Snapshot does not match this process.");
            _ = snapshot.ReadInt32();
            int count = snapshot.ReadInt32();
            for (int i = 0; i < count; i++) addresses.Add(snapshot.ReadUInt32());
        }
        if (addresses.Count > 20) throw new InvalidOperationException("Pulse limited to 20 candidates.");
        int Read(uint address)
        {
            byte[] bytes = new byte[4];
            if (!Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, bytes, 4, out nuint got) || got != 4)
                throw new IOException($"Read failed at 0x{address:X8}.");
            return BitConverter.ToInt32(bytes);
        }
        bool Write(uint address, int newValue) => Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, BitConverter.GetBytes(newValue), 4, out nuint got) && got == 4;
        foreach (uint candidate in addresses)
        {
            if (Read(candidate) != value) { Console.WriteLine($"SKIP 0x{candidate:X8}: no longer {value}"); continue; }
            int trial = value - 1;
            if (!Write(candidate, trial)) { Console.WriteLine($"WRITE FAILED 0x{candidate:X8}"); continue; }
            try
            {
                foreach (int delay in new[] { 10, 100, 250 })
                {
                    Thread.Sleep(delay);
                    string states = string.Join(" ", addresses.Select(a => $"{a:X8}={Read(a)}"));
                    Console.WriteLine($"target=0x{candidate:X8} elapsed+={delay}ms {states}");
                }
            }
            finally
            {
                if (Read(candidate) == trial) Write(candidate, value);
            }
        }
        return 0;
    }
    if (args[0] == "probe")
    {
        uint address = Convert.ToUInt32(args[3].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
        var before = new byte[4];
        if (!Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, before, 4, out nuint beforeRead) || beforeRead != 4)
            throw new IOException("Could not read target address.");
        int oldValue = BitConverter.ToInt32(before);
        if (oldValue != value) throw new InvalidOperationException($"Expected {value}, found {oldValue}; no write performed.");
        int testValue = checked(value + 1);
        if (!Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, BitConverter.GetBytes(testValue), 4, out nuint written) || written != 4)
            throw new IOException($"WriteProcessMemory failed: {Marshal.GetLastWin32Error()}");
        try
        {
            Thread.Sleep(250);
            var now = new byte[4];
            if (!Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, now, 4, out nuint got) || got != 4) throw new IOException("Probe reread failed.");
            Console.WriteLine($"PROBE address=0x{address:X8} before={oldValue} written={testValue} after250ms={BitConverter.ToInt32(now)}");
            if (BitConverter.ToInt32(now) == testValue)
            {
                Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, before, 4, out _);
                Console.WriteLine("restoredOriginal=true");
            }
        }
        catch
        {
            var current = new byte[4];
            if (Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, current, 4, out nuint got) && got == 4 && BitConverter.ToInt32(current) == testValue)
                Native.WriteProcessMemory(handle, (IntPtr)(ulong)address, before, 4, out _);
            throw;
        }
        return 0;
    }
    if (args[0] == "inspect" || args[0] == "map")
    {
        using var input = new BinaryReader(File.OpenRead(args[3]));
        if (input.ReadUInt32() != 0x4E435343 || input.ReadInt32() != pid)
            throw new InvalidDataException("Snapshot does not match this process.");
        _ = input.ReadInt32();
        int count = input.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            uint address = input.ReadUInt32();
            if (args[0] == "map")
            {
                nuint result = Native.VirtualQueryEx(handle, (IntPtr)(ulong)address, out var regionInfo, (nuint)Marshal.SizeOf<Native.MemoryBasicInformation>());
                if (result != 0) Console.WriteLine($"0x{address:X8} base=0x{regionInfo.BaseAddress.ToInt64():X8} allocation=0x{regionInfo.AllocationBase.ToInt64():X8} size=0x{regionInfo.RegionSize:X} type=0x{regionInfo.Type:X} protect=0x{regionInfo.Protect:X}");
                continue;
            }
            var bytes = new byte[68];
            if (!Native.ReadProcessMemory(handle, (IntPtr)(ulong)(address - 32), bytes, (nuint)bytes.Length, out nuint read) || read != (nuint)bytes.Length)
                continue;
            var fields = Enumerable.Range(0, 17).Select(j => $"{j * 4 - 32:+#;-#;0}:{BitConverter.ToInt32(bytes, j * 4)}");
            Console.WriteLine($"0x{address:X8} {string.Join(" ", fields)}");
        }
        return 0;
    }
    if (args[0] == "scan" || args[0] == "scanfloat")
    {
        ulong cursor = 0x10000;
        var buffer = new byte[1024 * 1024];
        while (cursor < 0x80000000)
        {
            nuint got = Native.VirtualQueryEx(handle, (IntPtr)cursor, out var region, (nuint)Marshal.SizeOf<Native.MemoryBasicInformation>());
            if (got == 0) { cursor += 0x1000; continue; }
            ulong start = (ulong)region.BaseAddress.ToInt64();
            ulong end = start + region.RegionSize;
            if (end <= cursor) break;
            if (region.State == 0x1000 && IsReadableWritable(region.Protect) && start < 0x80000000)
            {
                for (ulong pos = start; pos < Math.Min(end, 0x80000000); pos += (ulong)buffer.Length)
                {
                    int size = (int)Math.Min((ulong)buffer.Length, end - pos);
                    if (!Native.ReadProcessMemory(handle, (IntPtr)pos, buffer, (nuint)size, out nuint count) || count < 4) continue;
                    int limit = (int)count - 3;
                    for (int i = 0; i < limit; i += 4)
                    {
                        if (BitConverter.ToInt32(buffer, i) == matchBits) addresses.Add((uint)(pos + (uint)i));
                    }
                }
            }
            cursor = end;
        }
    }
    else
    {
        using var reader = new BinaryReader(File.OpenRead(args[3]));
        if (reader.ReadUInt32() != 0x4E435343 || reader.ReadInt32() != pid)
            throw new InvalidDataException("Snapshot does not match this process.");
        _ = reader.ReadInt32();
        int count = reader.ReadInt32();
        var raw = new byte[4];
        for (int i = 0; i < count; i++)
        {
            uint address = reader.ReadUInt32();
            if (Native.ReadProcessMemory(handle, (IntPtr)(ulong)address, raw, 4, out nuint read) && read == 4 && BitConverter.ToInt32(raw) == matchBits)
                addresses.Add(address);
        }
    }
    string output = args[0].StartsWith("scan", StringComparison.Ordinal) ? args[3] : Path.ChangeExtension(args[3], $"{value}.bin");
    using var writer = new BinaryWriter(File.Create(output));
    writer.Write(0x4E435343u); writer.Write(pid); writer.Write(value); writer.Write(addresses.Count);
    foreach (uint address in addresses) writer.Write(address);
    Console.WriteLine($"PID={pid} value={value} candidates={addresses.Count} snapshot={output}");
    foreach (uint address in addresses.Take(20)) Console.WriteLine($"0x{address:X8}");
    return 0;
}
finally { Native.CloseHandle(handle); }

static bool IsReadableWritable(uint protect)
{
    uint p = protect & 0xff;
    return (protect & 0x100) == 0 && (p == 0x04 || p == 0x08 || p == 0x40 || p == 0x80);
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct MemoryBasicInformation
    {
        public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect;
        public ushort PartitionId; public nuint RegionSize; public uint State; public uint Protect; public uint Type;
    }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] public static extern nuint VirtualQueryEx(IntPtr handle, IntPtr address, out MemoryBasicInformation info, nuint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, [Out] byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, nuint size, out nuint written);
}
