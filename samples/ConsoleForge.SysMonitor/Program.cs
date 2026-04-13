using ConsoleForge.Core;
using ConsoleForge.Layout;
using ConsoleForge.Styling;
using ConsoleForge.Widgets;

namespace ConsoleForge.SysMonitor;

// ── Domain types ─────────────────────────────────────────────────────────────

record CpuSample(double Percent);
record ProcessInfo(int Pid, string Name, double Cpu, long MemKb);
record NetStats(long BytesIn, long BytesOut);
record LogEntry(DateTimeOffset Time, string Level, string Message);

enum Section { Cpu, Memory, Processes, Network, Logs }

// ── Messages ─────────────────────────────────────────────────────────────────

record TickMsg(DateTimeOffset At) : IMsg;
record SelectSectionMsg(Section S) : IMsg;
record ScrollMsg(int Delta) : IMsg;

// ── Model ────────────────────────────────────────────────────────────────────

record SysMonitorModel(
    Section ActiveSection,
    IReadOnlyList<CpuSample> CpuHistory,
    double MemPercent,
    IReadOnlyList<ProcessInfo> Processes,
    NetStats Net,
    IReadOnlyList<LogEntry> Logs,
    int ScrollOffset,
    DateTimeOffset LastTick) : IModel
{
    private static readonly Random _rng = new();

    public static SysMonitorModel Initial() => new(
        ActiveSection: Section.Cpu,
        CpuHistory: GenerateCpuHistory(60),
        MemPercent: 42.0,
        Processes: GenerateProcesses(),
        Net: new NetStats(1_048_576, 262_144),
        Logs: GenerateSeedLogs(),
        ScrollOffset: 0,
        LastTick: DateTimeOffset.UtcNow
    );

    public ICmd? Init() => Cmd.Tick(TimeSpan.FromSeconds(1), at => new TickMsg(at));

    public (IModel Model, ICmd? Cmd) Update(IMsg msg)
    {
        switch (msg)
        {
            case KeyMsg { Key: ConsoleKey.Q }:
                return (this, Cmd.Quit());

            case KeyMsg { Key: ConsoleKey.UpArrow } or KeyMsg { Key: ConsoleKey.K, Character: 'k' }:
            {
                var s = (Section)Math.Max(0, (int)ActiveSection - 1);
                return (this with { ActiveSection = s, ScrollOffset = 0 }, null);
            }

            case KeyMsg { Key: ConsoleKey.DownArrow } or KeyMsg { Key: ConsoleKey.J, Character: 'j' }:
            {
                var s = (Section)Math.Min((int)Section.Logs, (int)ActiveSection + 1);
                return (this with { ActiveSection = s, ScrollOffset = 0 }, null);
            }

            case KeyMsg { Key: ConsoleKey.Tab }:
            {
                var s = (Section)(((int)ActiveSection + 1) % 5);
                return (this with { ActiveSection = s, ScrollOffset = 0 }, null);
            }

            // Scroll content in active pane
            case KeyMsg { Key: ConsoleKey.PageDown }:
                return (this with { ScrollOffset = ScrollOffset + 5 }, null);

            case KeyMsg { Key: ConsoleKey.PageUp }:
                return (this with { ScrollOffset = Math.Max(0, ScrollOffset - 5) }, null);

            case TickMsg tick:
            {
                var newCpu = CpuHistory.Skip(1)
                    .Append(new CpuSample(NextCpu(CpuHistory[^1].Percent)))
                    .ToArray();

                var newMem = Math.Clamp(MemPercent + (_rng.NextDouble() * 4 - 2), 10, 95);

                var newNet = new NetStats(
                    Net.BytesIn  + (long)(_rng.NextDouble() * 200_000),
                    Net.BytesOut + (long)(_rng.NextDouble() * 50_000)
                );

                var newLog = Logs
                    .Append(RandomLog(tick.At))
                    .TakeLast(200)
                    .ToArray();

                var newProcs = ActiveSection == Section.Processes
                    ? GenerateProcesses()
                    : Processes;

                var next = this with
                {
                    CpuHistory = newCpu,
                    MemPercent = newMem,
                    Net = newNet,
                    Logs = newLog,
                    Processes = newProcs,
                    LastTick = tick.At
                };

                // Re-arm tick
                return (next, Cmd.Tick(TimeSpan.FromSeconds(1), at => new TickMsg(at)));
            }

            default:
                return (this, null);
        }
    }

    public IWidget View()
    {
        var accentColor  = Color.FromHex("#00D7FF"); // bright cyan
        var dimColor     = Color.FromHex("#626262");
        var greenColor   = Color.FromHex("#00FF87");
        var orangeColor  = Color.FromHex("#FFB347");
        var redColor     = Color.FromHex("#FF5F5F");

        var headerStyle  = Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(Color.BrightWhite);
        var statusStyle  = Style.Default.Background(Color.FromHex("#1C1C1C")).Foreground(dimColor);
        var sidebarStyle = Style.Default.BorderForeground(accentColor).Border(Borders.Thick);
        var mainStyle    = Style.Default.BorderForeground(accentColor).Border(Borders.Rounded);

        // ── Header bar ────────────────────────────────────────────────────────
        var headerLeft  = new TextBlock(" ConsoleForge SysMonitor",
            style: Style.Default.Foreground(accentColor).Bold());
        var headerRight = new TextBlock(LastTick.LocalDateTime.ToString("HH:mm:ss") + " ",
            style: Style.Default.Foreground(dimColor));

        var header = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: headerStyle,
            children: [
                new Container(Axis.Vertical,
                    width: SizeConstraint.Flex(1),
                    children: [headerLeft]),
                new Container(Axis.Vertical,
                    width: SizeConstraint.Fixed(10),
                    children: [headerRight])
            ]);

        // ── Status bar ────────────────────────────────────────────────────────
        var statusBar = new Container(Axis.Horizontal,
            height: SizeConstraint.Fixed(1),
            style: statusStyle,
            children: [
                new TextBlock(" ↑↓/jk Sections   Tab Cycle   PgUp/PgDn Scroll   Q Quit",
                    style: statusStyle)
            ]);

        // ── Sidebar ───────────────────────────────────────────────────────────
        var sectionNames = new[] { "CPU", "Memory", "Processes", "Network", "Logs" };
        var sidebarItems = sectionNames.Select((name, i) =>
        {
            var prefix = (Section)i == ActiveSection ? "▶ " : "  ";
            return prefix + name;
        }).ToArray();

        var sidebarList = new List(
            items: sidebarItems,
            selectedIndex: (int)ActiveSection,
            selectedItemStyle: Style.Default.Foreground(accentColor).Bold()
        );

        var sidebar = new BorderBox(
            title: " Nav ",
            body: sidebarList,
            style: sidebarStyle
        ) { Width = SizeConstraint.Fixed(20) };

        // ── Main pane content ─────────────────────────────────────────────────
        IWidget mainContent = ActiveSection switch
        {
            Section.Cpu        => BuildCpuView(accentColor, greenColor, orangeColor, redColor, dimColor),
            Section.Memory     => BuildMemoryView(greenColor, orangeColor, redColor, dimColor),
            Section.Processes  => BuildProcessesView(accentColor, dimColor),
            Section.Network    => BuildNetworkView(accentColor, dimColor),
            Section.Logs       => BuildLogsView(greenColor, orangeColor, redColor, dimColor),
            _                  => new TextBlock("Unknown section")
        };

        var mainPane = new BorderBox(
            title: $" {sectionNames[(int)ActiveSection]} ",
            body: mainContent,
            style: mainStyle
        );

        // ── Body row (sidebar + main) ─────────────────────────────────────────
        var body = new Container(Axis.Horizontal,
            height: SizeConstraint.Flex(1),
            children: [sidebar, mainPane]);

        // ── Root: header + body + statusbar ──────────────────────────────────
        var root = new Container(Axis.Vertical,
            children: [header, body, statusBar]);

        return root;
    }

    // ── Section views ─────────────────────────────────────────────────────────

    private IWidget BuildCpuView(IColor accent, IColor green, IColor orange, IColor red, IColor dim)
    {
        var latest = CpuHistory[^1].Percent;
        var cpuColor = latest > 80 ? red : latest > 50 ? orange : green;

        var summaryLine = $"  Current: {latest:F1}%   Avg (60s): {CpuHistory.Average(s => s.Percent):F1}%   Peak: {CpuHistory.Max(s => s.Percent):F1}%";
        var summaryWidget = new TextBlock(summaryLine,
            style: Style.Default.Foreground(cpuColor).Bold());

        // Sparkline: show last 30 samples as block chars
        var samples = CpuHistory.TakeLast(30).ToArray();
        var sparkLine = BuildSparkline(samples.Select(s => s.Percent / 100.0).ToArray());
        var sparkWidget = new TextBlock("\n  " + sparkLine,
            style: Style.Default.Foreground(accent));

        // Bar chart: last 30 samples, one bar per sample
        var chartLines = BuildBarChart(samples.Select(s => s.Percent).ToArray(), red, orange, green, dim);
        var chartWidget = new TextBlock("\n" + string.Join("\n", chartLines),
            style: Style.Default.Foreground(dim));

        return new Container(Axis.Vertical, [summaryWidget, sparkWidget, chartWidget]);
    }

    private IWidget BuildMemoryView(IColor green, IColor orange, IColor red, IColor dim)
    {
        var color = MemPercent > 80 ? red : MemPercent > 50 ? orange : green;
        var totalGb = 16.0;
        var usedGb = totalGb * MemPercent / 100.0;

        var header = new TextBlock(
            $"  Used: {usedGb:F2} GB / {totalGb:F1} GB   ({MemPercent:F1}%)",
            style: Style.Default.Foreground(color).Bold());

        // Wide progress bar
        var barWidth = 50;
        var filled = (int)(barWidth * MemPercent / 100.0);
        var bar = "[" + new string('█', filled) + new string('░', barWidth - filled) + "]";
        var barWidget = new TextBlock("\n  " + bar,
            style: Style.Default.Foreground(color));

        var details = new TextBlock(
            $"\n  Cached:    {4.1:F2} GB\n  Buffers:   {0.8:F2} GB\n  Available: {totalGb - usedGb:F2} GB",
            style: Style.Default.Foreground(dim));

        return new Container(Axis.Vertical, [header, barWidget, details]);
    }

    private IWidget BuildProcessesView(IColor accent, IColor dim)
    {
        var header = new TextBlock(
                        $"  {"PID",-6} {"NAME",-20} {"CPU%",6} {"MEM(MB)",9}",
                        style: Style.Default.Foreground(accent).Bold()) 
        {
            Height = SizeConstraint.Fixed(1)
        };

        var visibleProcs = Processes
            .Skip(ScrollOffset)
            .Take(30)
            .Select(p =>
                $"  {p.Pid,-6} {p.Name,-20} {p.Cpu,5:F1}% {p.MemKb / 1024.0,8:F1}M"
            ).ToArray();

        var rows = new TextBlock(string.Join("\n", visibleProcs),
            style: Style.Default.Foreground(dim))
        {
            Height = SizeConstraint.Flex()
        };

        return new Container(Axis.Vertical, [header, rows]);
    }

    private IWidget BuildNetworkView(IColor accent, IColor dim)
    {
        static string FormatBytes(long b) => b switch
        {
            >= 1_073_741_824 => $"{b / 1_073_741_824.0:F2} GB",
            >= 1_048_576     => $"{b / 1_048_576.0:F2} MB",
            >= 1_024         => $"{b / 1024.0:F2} KB",
            _                => $"{b} B"
        };

        var stats = new TextBlock(
            $"  Total RX: {FormatBytes(Net.BytesIn)}\n  Total TX: {FormatBytes(Net.BytesOut)}",
            style: Style.Default.Foreground(accent).Bold());

        var note = new TextBlock(
            "\n  (simulated counters — updated each tick)",
            style: Style.Default.Foreground(dim));

        return new Container(Axis.Vertical, [stats, note]);
    }

    private IWidget BuildLogsView(IColor green, IColor orange, IColor red, IColor dim)
    {
        var visible = Logs
            .Reverse()
            .Skip(ScrollOffset)
            .Take(40)
            .Reverse()
            .ToArray();

        var lines = visible.Select(e =>
        {
            var levelColor = e.Level switch
            {
                "ERROR" => red,
                "WARN"  => orange,
                "INFO"  => green,
                _       => dim
            };
            var ts = e.Time.LocalDateTime.ToString("HH:mm:ss");
            return $"  {ts} [{e.Level,-5}] {e.Message}";
        });

        var text = string.Join("\n", lines);
        return new TextBlock(text, style: Style.Default.Foreground(dim));
    }

    // ── Render helpers ────────────────────────────────────────────────────────

    private static string BuildSparkline(double[] values)
    {
        var chars = "▁▂▃▄▅▆▇█";
        return string.Concat(values.Select(v =>
        {
            var idx = (int)Math.Clamp(v * chars.Length, 0, chars.Length - 1);
            return chars[idx];
        }));
    }

    private static string[] BuildBarChart(double[] values, IColor red, IColor orange, IColor green, IColor dim)
    {
        var lines = new List<string>();
        var maxVal = 100.0;
        var barHeight = 8;

        for (var row = barHeight; row >= 1; row--)
        {
            var threshold = row / (double)barHeight * maxVal;
            var sb = new System.Text.StringBuilder("  ");
            foreach (var v in values)
            {
                sb.Append(v >= threshold ? "█" : " ");
                sb.Append(' ');
            }
            lines.Add(sb.ToString());
        }
        lines.Add("  " + string.Join(" ", values.Select(_ => "─")));
        return lines.ToArray();
    }

    // ── Data generators ───────────────────────────────────────────────────────

    private static IReadOnlyList<CpuSample> GenerateCpuHistory(int count)
    {
        var rng = new Random();
        var list = new List<CpuSample>(count);
        var v = 30.0;
        for (var i = 0; i < count; i++)
        {
            v = Math.Clamp(v + rng.NextDouble() * 10 - 5, 5, 95);
            list.Add(new CpuSample(v));
        }
        return list;
    }

    private static double NextCpu(double last)
    {
        return Math.Clamp(last + _rng.NextDouble() * 10 - 5, 5, 95);
    }

    private static IReadOnlyList<ProcessInfo> GenerateProcesses()
    {
        var rng = new Random();
        var names = new[] {
            "systemd", "kernel", "sshd", "nginx", "postgres", "redis",
            "node", "dotnet", "python3", "bash", "vim", "htop",
            "cron", "rsyslogd", "NetworkManager", "dbus-daemon", "Xorg",
            "pulseaudio", "gnome-shell", "chrome", "firefox", "vscode"
        };
        return names.Select((n, i) => new ProcessInfo(
            Pid: 1000 + i * 7,
            Name: n,
            Cpu: Math.Round(rng.NextDouble() * 15, 1),
            MemKb: (long)(rng.NextDouble() * 512_000 + 4_096)
        )).OrderByDescending(p => p.Cpu).ToArray();
    }

    private static IReadOnlyList<LogEntry> GenerateSeedLogs()
    {
        var rng = new Random();
        var levels = new[] { "INFO", "INFO", "INFO", "WARN", "ERROR" };
        var messages = new[] {
            "Service started", "Health check OK", "Connection established",
            "Disk usage at 72%", "Retry attempt 1/3", "Cache miss",
            "Slow query detected (412ms)", "Memory pressure warning",
            "Config reloaded", "New connection from 192.168.1.42"
        };
        var now = DateTimeOffset.UtcNow;
        return Enumerable.Range(0, 20).Select(i => new LogEntry(
            Time: now.AddSeconds(-20 + i),
            Level: levels[rng.Next(levels.Length)],
            Message: messages[rng.Next(messages.Length)]
        )).ToArray();
    }

    private static LogEntry RandomLog(DateTimeOffset at)
    {
        var rng = new Random();
        var levels = new[] { "INFO", "INFO", "INFO", "WARN", "ERROR" };
        var messages = new[] {
            "Heartbeat OK", "Request processed in 12ms", "Cache hit",
            "Disk I/O spike", "Connection pool at 80%", "GC pause 8ms",
            "Worker thread idle", "Batch job completed", "Config watched",
            "Auth token refreshed", "Rate limit approached", "Low memory"
        };
        return new LogEntry(
            Time: at,
            Level: levels[rng.Next(levels.Length)],
            Message: messages[rng.Next(messages.Length)]
        );
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────

static class EntryPoint
{
    static void Main()
    {
        var theme = new Theme(
            name: "SysMonitor",
            baseStyle: Style.Default.Foreground(Color.BrightWhite),
            borderStyle: Style.Default.BorderForeground(Color.FromHex("#00D7FF")).Border(Borders.Rounded),
            focusedStyle: Style.Default.BorderForeground(Color.FromHex("#FFB347"))
        );

        Program.Run(SysMonitorModel.Initial(), theme: theme);
    }
}
