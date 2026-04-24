using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MenuBar.Services
{
    public sealed class EyeBreakIpcService : IDisposable
    {
        // Flip to false when the issue is diagnosed to stop log growth.
        private static bool DebugLogEnabled = true;
        private const long MaxLogBytes = 10 * 1024; // ~10KB

        public sealed class EyeBreakSnapshot
        {
            public int v { get; set; }
            public long ts { get; set; }
            public bool enabled { get; set; }
            public string mode { get; set; } = "";
            public Dot dot { get; set; } = new Dot();
            public Flash flash { get; set; } = new Flash();
            public string tooltip { get; set; } = "";

            public sealed class Dot
            {
                public int marginPx { get; set; } = 12;
                public int dotSizePx { get; set; } = 12;
                public int gapPx { get; set; } = 4;
                public int zonePx { get; set; } = 40;
                public int stackCount { get; set; } = 1;
                public string baseColor { get; set; } = "#6CCB5F";
                public bool visible { get; set; } = true;
            }

            public sealed class Flash
            {
                public string kind { get; set; } = "none"; // "none" | "toggle"
                public int intervalMs { get; set; } = 0;
            }
        }

        private readonly CancellationTokenSource _cts = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private Task _listenTask;
        private Task _cmdTask;
        private readonly ConcurrentQueue<string> _pendingCommands = new();
        private readonly SemaphoreSlim _cmdSignal = new(0);

        public string StatePipeName { get; }
        public string CommandPipeName { get; }

        public event Action<EyeBreakSnapshot> SnapshotReceived;

        private static readonly object _logLock = new();
        private static string LogPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MenuBar",
                "eye-break-ipc.log");

        public EyeBreakIpcService(
            string statePipeName = "MenuBar.202020.state",
            string commandPipeName = "MenuBar.202020.cmd")
        {
            StatePipeName = statePipeName;
            CommandPipeName = commandPipeName;
        }

        public void Start()
        {
            _listenTask ??= Task.Run(ListenLoopAsync);
            _cmdTask ??= Task.Run(CommandLoopAsync);
        }

        private async Task ListenLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Log($"State server waiting: {StatePipeName}");
                    using var pipe = new NamedPipeServerStream(
                        StatePipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_cts.Token);
                    Log("State client connected");

                    using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                    while (!_cts.IsCancellationRequested && pipe.IsConnected)
                    {
                        string line = await reader.ReadLineAsync();
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        Log($"State line: {line}");
                        EyeBreakSnapshot snap = null;
                        try
                        {
                            snap = JsonSerializer.Deserialize<EyeBreakSnapshot>(line, _jsonOptions);
                        }
                        catch
                        {
                            // ignore malformed lines
                        }

                        if (snap?.v == 1)
                        {
                            Log($"Parsed snapshot: enabled={snap.enabled} mode={snap.mode} stack={snap.dot?.stackCount} color={snap.dot?.baseColor} visible={snap.dot?.visible}");
                            try { SnapshotReceived?.Invoke(snap); } catch { }
                        }
                        else
                        {
                            Log("Snapshot ignored (parse failed or v!=1)");
                        }
                    }

                    Log("State client disconnected");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(500, _cts.Token); } catch { }
                }
            }
        }

        public void EnqueueCommand(string jsonCommand)
        {
            if (string.IsNullOrWhiteSpace(jsonCommand)) return;
            string entry = jsonCommand.TrimEnd() + "\n";
            _pendingCommands.Enqueue(entry);
            Log($"EnqueueCommand: {jsonCommand.TrimEnd()} (queue depth now ~{_pendingCommands.Count})");
            try { _cmdSignal.Release(); } catch { }
        }

        // Exposed so MainWindow can write to the same log without depending on internals.
        public static void LogExternal(string message) => LogImpl(message);

        private async Task CommandLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    Log($"Cmd server waiting: {CommandPipeName}");
                    using var pipe = new NamedPipeServerStream(
                        CommandPipeName,
                        PipeDirection.Out,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await pipe.WaitForConnectionAsync(_cts.Token);
                    Log("Cmd client connected");

                    while (!_cts.IsCancellationRequested && pipe.IsConnected)
                    {
                        // Peek before dequeuing: only remove after a successful send so
                        // commands aren't silently dropped if the pipe breaks mid-write.
                        while (pipe.IsConnected && _pendingCommands.TryPeek(out var cmd))
                        {
                            Log($"Cmd send: {cmd.TrimEnd()}");
                            byte[] bytes = Encoding.UTF8.GetBytes(cmd);
                            await pipe.WriteAsync(bytes, 0, bytes.Length, _cts.Token);
                            await pipe.FlushAsync(_cts.Token);
                            _pendingCommands.TryDequeue(out _); // confirmed sent
                        }

                        if (!pipe.IsConnected) break;

                        // Poll with a short timeout so we detect AHK disconnection quickly.
                        // WaitAsync returns false on timeout (not a cancellation).
                        bool signalled = await _cmdSignal.WaitAsync(
                            TimeSpan.FromMilliseconds(500), _cts.Token);

                        // If we woke on timeout, loop back and check pipe.IsConnected before
                        // trying to send; if signalled, drain any newly queued commands.
                        _ = signalled;
                    }

                    Log("Cmd client disconnected");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Cmd loop error: {ex.Message}");
                    try { await Task.Delay(300, _cts.Token); } catch { }
                }
            }
        }

        private static void Log(string message)
        {
            if (!DebugLogEnabled) return;
            LogImpl(message);
        }

        private static void LogImpl(string message)
        {
            try
            {
                lock (_logLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    PruneLogIfNeeded();
                    File.AppendAllText(
                        LogPath,
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
                    PruneLogIfNeeded();
                }
            }
            catch
            {
            }
        }

        private static void PruneLogIfNeeded()
        {
            try
            {
                var fi = new FileInfo(LogPath);
                if (!fi.Exists) return;
                if (fi.Length < MaxLogBytes) return;

                long keepBytes = MaxLogBytes;
                try
                {
                    using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length <= keepBytes) return;

                    fs.Seek(-keepBytes, SeekOrigin.End);
                    byte[] buffer = new byte[keepBytes];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = fs.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }

                    using var ws = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    if (read > 0)
                    {
                        ws.Write(buffer, 0, read);
                        ws.Flush(flushToDisk: false);
                    }
                }
                catch
                {
                    // best-effort; logging must never crash the app
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listenTask?.Wait(200); } catch { }
            try { _cmdTask?.Wait(200); } catch { }
            _cts.Dispose();
            try { _cmdSignal.Dispose(); } catch { }
            SnapshotReceived = null;
        }
    }
}

