using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DigitalMonitoringSystem.Agent;

internal sealed class AgentSettings
{
    public string ApiBaseUrl { get; set; } = "http://localhost:5103";
    public string DeviceId { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public int IntervalSeconds { get; set; } = 30;
}

internal sealed class HeartbeatRequest
{
    public int Cpu { get; set; }
    public int Memory { get; set; }
    public int Disk { get; set; }
    public string AgentVersion { get; set; } = "1.0.0";
}

internal sealed class HeartbeatResponse
{
    public string DeviceId { get; set; } = "";
    public string Status { get; set; } = "";
    public string LastSeen { get; set; } = "";
    public int SampleCount { get; set; }
    public List<string> TriggeredAlertIds { get; set; } = new();
}

internal static class Program
{
    private const string Version = "1.0.0";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static ulong _previousIdle;
    private static ulong _previousKernel;
    private static ulong _previousUser;

    public static async Task Main()
    {
        Console.Title = "A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT - Monitoring Agent";
        var settings = await LoadSettingsAsync();

        Console.WriteLine("A DIGITAL MONITORING SYSTEM FOR COMPUTERS AND OTHER INFORMATION TECHNOLOGY DEVICES AT MUKONO DISTRICT LOCAL GOVERNMENT Monitoring Agent");
        Console.WriteLine($"Version: {Version}");
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(settings.DeviceId) || string.IsNullOrWhiteSpace(settings.DeviceToken))
        {
            Console.WriteLine("The agent is not configured yet.");
            Console.WriteLine("1. Enroll this computer from the administrator dashboard.");
            Console.WriteLine("2. Copy the returned Device ID and Enrollment Token into appsettings.json.");
            Console.WriteLine("3. Start the agent again.");
            return;
        }

        var interval = Math.Clamp(settings.IntervalSeconds, 10, 3600);
        Console.WriteLine($"Device ID: {settings.DeviceId}");
        Console.WriteLine($"API: {settings.ApiBaseUrl}");
        Console.WriteLine($"Heartbeat interval: {interval}s");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        while (true)
        {
            try
            {
                await SendHeartbeatAsync(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:T}] ERROR: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(interval));
        }
    }

    private static async Task SendHeartbeatAsync(AgentSettings settings)
    {
        var metrics = ReadMetrics();
        var payload = new HeartbeatRequest
        {
            Cpu = metrics.Cpu,
            Memory = metrics.Memory,
            Disk = metrics.Disk,
            AgentVersion = Version
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{settings.ApiBaseUrl.TrimEnd('/')}/api/devices/{Uri.EscapeDataString(settings.DeviceId)}/heartbeat")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Device-Token", settings.DeviceToken.Trim());

        using var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Heartbeat failed ({(int)response.StatusCode}): {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<HeartbeatResponse>();
        Console.WriteLine($"[{DateTime.Now:T}] Online | CPU {payload.Cpu}% | Memory {payload.Memory}% | Disk {payload.Disk}% | Samples {result?.SampleCount ?? 0}");

        if (result?.TriggeredAlertIds.Count > 0)
        {
            Console.WriteLine($"  Alerts raised: {string.Join(", ", result.TriggeredAlertIds)}");
        }
    }

    private static (int Cpu, int Memory, int Disk) ReadMetrics()
    {
        var cpu = ReadCpuPercentage();
        var memory = ReadMemoryPercentage();
        var disk = ReadDiskPercentage();
        return (cpu, memory, disk);
    }

    private static int ReadMemoryPercentage()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return 0;
        }

        return (int)Math.Clamp(status.MemoryLoad, 0, 100);
    }

    private static int ReadDiskPercentage()
    {
        try
        {
            var drive = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .OrderByDescending(d => d.TotalSize)
                .FirstOrDefault();
            if (drive is null || drive.TotalSize <= 0)
            {
                return 0;
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return (int)Math.Clamp(Math.Round(used * 100d / drive.TotalSize), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static int ReadCpuPercentage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var idleValue = ToUInt64(idle);
        var kernelValue = ToUInt64(kernel);
        var userValue = ToUInt64(user);

        if (_previousKernel == 0)
        {
            _previousIdle = idleValue;
            _previousKernel = kernelValue;
            _previousUser = userValue;
            return 0;
        }

        var idleDelta = idleValue - _previousIdle;
        var totalDelta = (kernelValue - _previousKernel) + (userValue - _previousUser);
        _previousIdle = idleValue;
        _previousKernel = kernelValue;
        _previousUser = userValue;

        if (totalDelta == 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round((1d - idleDelta / (double)totalDelta) * 100d), 0, 100);
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    private static async Task<AgentSettings> LoadSettingsAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            var defaults = new AgentSettings();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AgentSettings>(stream) ?? new AgentSettings();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
