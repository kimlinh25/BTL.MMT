using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ET4230.Core.Discovery;

/// <summary>
/// Tracker TCP server tối giản theo Giao-thuc-v1.md: lắng nghe trên BindHost/BindPort,
/// điều phối frame tới <see cref="TrackerRequestDispatcher"/> và dọn sạch socket/task khi dừng.
/// </summary>
public sealed class TrackerServer : IAsyncDisposable
{
    private readonly TrackerOptions _options;
    private readonly TrackerRequestDispatcher _dispatcher;
    private readonly ConcurrentDictionary<int, TcpClient> _activeClients = new();
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private int _nextClientId;
    private int _started;
    private int _stopped;

    public TrackerServer(TrackerOptions options, IElapsedClock? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();

        var registry = new PeerRegistry(
            clock ?? new StopwatchElapsedClock(),
            _options.Ttl,
            _options.MaxEntries);

        _dispatcher = new TrackerRequestDispatcher(
            registry,
            _options.MaxPeersPerResponse,
            _options.MaxControlPayloadBytes);
    }

    public int BoundPort { get; private set; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("TrackerServer đã được khởi động trước đó.");

        var bindIp = IPAddress.Parse(_options.BindHost.Trim());
        _listener = new TcpListener(bindIp, _options.BindPort);
        _listener.Start();

        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _cts?.Cancel();
        _listener?.Stop();

        foreach (var kvp in _activeClients)
        {
            kvp.Value.Dispose();
        }
        _activeClients.Clear();

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        var pendingTasks = _clientTasks.Values;
        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        _cts?.Dispose();
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            int clientId = Interlocked.Increment(ref _nextClientId);
            _activeClients[clientId] = client;

            var task = HandleClientAsync(clientId, client, cancellationToken);
            _clientTasks[clientId] = task;
        }
    }

    private async Task HandleClientAsync(int clientId, TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            await using (var stream = client.GetStream())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readCts.CancelAfter(_options.ReadTimeout);

                    FrameMessage? frame;
                    try
                    {
                        frame = await FrameStreamHelper.ReadFrameAsync(
                            stream,
                            _options.MaxControlPayloadBytes,
                            readCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break; // Read timeout cho kết nối rảnh
                    }
                    catch (IOException)
                    {
                        break;
                    }
                    catch (InvalidDataException)
                    {
                        break;
                    }

                    if (frame is null)
                        break; // Client đóng kết nối sạch

                    var result = _dispatcher.Dispatch(frame.Header, frame.Payload);

                    await FrameStreamHelper.WriteFrameAsync(
                        stream,
                        result.ResponseHeader,
                        result.ResponsePayload,
                        cancellationToken).ConfigureAwait(false);

                    if (result.CloseConnection)
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            _activeClients.TryRemove(clientId, out _);
            _clientTasks.TryRemove(clientId, out _);
        }
    }
}
