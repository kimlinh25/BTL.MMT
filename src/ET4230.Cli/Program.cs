using System.Net.Sockets;
using System.Text.Json;
using ET4230.Cli;
using ET4230.Core.Discovery;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments is ["--help"] or ["-h"])
    {
        Console.WriteLine("ET4230 P2P — truyền tệp TCP");
        Console.WriteLine("Các mode: tracker, seed, download (một nguồn), peer (P2P).");
        Console.WriteLine("Cách dùng: dotnet run --project src/ET4230.Cli -- <mode> --config <tệp-json>");
        Console.WriteLine("Xem <mode> --help, --version và hướng dẫn trong README.md.");
        return 0;
    }

    if (arguments is ["--version"])
    {
        Console.WriteLine("ET4230.P2P 0.2.0");
        return 0;
    }

    string mode = arguments[0];
    if (mode is not ("tracker" or "seed" or "download" or "peer"))
    {
        Console.Error.WriteLine($"Mode không được hỗ trợ: '{mode}'. Dùng --help để xem các mode.");
        return 2;
    }

    string usage = $"Cách dùng: dotnet run --project src/ET4230.Cli -- {mode} --config <tệp-json>";
    if (arguments is [_, "--help" or "-h"])
    {
        Console.WriteLine(usage);
        Console.WriteLine(mode switch
        {
            "tracker" => "Lắng nghe tại tracker.bindHost:port.",
            "seed" => "Xác minh file.sourcePath, tạo hoặc đọc manifest và đăng ký seed với tracker.",
            "peer" => "Tải từ nhiều peer qua tracker và phục vụ piece đã xác minh trong khi tải.",
            _ => "Đọc manifest tin cậy, tìm một seed qua tracker và tải tới file.outputPath."
        });
        Console.WriteLine("Xem cấu hình mẫu trong README.md.");
        return 0;
    }

    if (arguments is not [_, "--config", var configPath])
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    using var shutdown = new CancellationTokenSource();
    ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };
    Console.CancelKeyPress += onCancel;
    try
    {
        W03Config config = await W03Config.LoadAsync(configPath, shutdown.Token);
        switch (mode)
        {
            case "tracker":
                await W03Transfer.RunTrackerAsync(config, shutdown.Token);
                break;
            case "seed":
                await W03Transfer.RunSeedAsync(config, shutdown.Token);
                break;
            case "download":
                await W03Transfer.RunDownloadAsync(config, shutdown.Token);
                break;
            case "peer":
                await W03Transfer.RunPeerAsync(config, shutdown.Token);
                break;
        }
        return 0;
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        Console.Error.WriteLine($"Đã dừng {mode}.");
        return 130;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine($"{mode}: hết thời gian chờ tracker.");
        return 1;
    }
    catch (Exception error) when (error is ArgumentException or JsonException)
    {
        Console.Error.WriteLine($"{mode}: cấu hình không hợp lệ. {error.Message}");
        return 2;
    }
    catch (Exception error) when (error is IOException or InvalidDataException or
        UnauthorizedAccessException or SocketException or TrackerRequestException)
    {
        Console.Error.WriteLine($"{mode}: {error.Message}");
        return 1;
    }
    finally
    {
        Console.CancelKeyPress -= onCancel;
    }
}
