using System.Text.Json;

return Run(args);

static int Run(string[] arguments)
{
    if (arguments.Length == 0 || arguments is ["--help"] or ["-h"])
    {
        PrintGeneralHelp();
        return 0;
    }

    if (arguments is ["--version"])
    {
        Console.WriteLine("ET4230.P2P 0.1.0-bootstrap");
        return 0;
    }

    string mode = arguments[0];
    if (mode is not ("tracker" or "seed" or "download"))
    {
        Console.Error.WriteLine($"Mode không được hỗ trợ: '{mode}'.");
        Console.Error.WriteLine("Chạy 'dotnet run --project src/ET4230.Cli -- --help' để xem các mode hiện có.");
        return 2;
    }

    if (arguments.Length == 2 && arguments[1] is "--help" or "-h")
    {
        PrintModeHelp(mode);
        return 0;
    }

    if (arguments.Length != 3 || arguments[1] != "--config")
    {
        Console.Error.WriteLine($"Cú pháp: dotnet run --project src/ET4230.Cli -- {mode} --config <đường-dẫn-json>");
        Console.Error.WriteLine("Các mode này hiện mới có khung CLI; xem 'dotnet run --project src/ET4230.Cli -- --help'.");
        return 2;
    }

    string configPath = Path.GetFullPath(arguments[2]);
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"Không tìm thấy file config: {configPath}");
        return 2;
    }

    try
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllBytes(configPath));
        if (config.RootElement.ValueKind != JsonValueKind.Object)
        {
            Console.Error.WriteLine("Config phải là một JSON object ở cấp cao nhất.");
            return 2;
        }
    }
    catch (JsonException error)
    {
        Console.Error.WriteLine($"Config không phải JSON hợp lệ: {error.Message}");
        return 2;
    }
    catch (IOException error)
    {
        Console.Error.WriteLine($"Không đọc được config: {error.Message}");
        return 2;
    }

    Console.Error.WriteLine(
        $"Mode '{mode}' và config JSON hợp lệ, nhưng chưa chạy: CLI chưa được nối với API Core của module này.");
    Console.Error.WriteLine("Exit code 3 nghĩa là mode đã nhận diện nhưng implementation còn thiếu.");
    return 3;
}

static void PrintGeneralHelp()
{
    Console.WriteLine("ET4230 P2P — CLI draft W03");
    Console.WriteLine();
    Console.WriteLine("Cách dùng:");
    Console.WriteLine("  dotnet run --project src/ET4230.Cli -- tracker --config <đường-dẫn-json>");
    Console.WriteLine("  dotnet run --project src/ET4230.Cli -- seed --config <đường-dẫn-json>");
    Console.WriteLine("  dotnet run --project src/ET4230.Cli -- download --config <đường-dẫn-json>");
    Console.WriteLine("  dotnet run --project src/ET4230.Cli -- <mode> --help");
    Console.WriteLine("  dotnet run --project src/ET4230.Cli -- --version");
    Console.WriteLine();
    Console.WriteLine("Ba mode trên mới là khung lệnh, chưa thực hiện tracker/seed/download.");
    Console.WriteLine("Tên mode và cấu hình cần được nhóm thống nhất trong Issue #23 trước khi nối API.");
    Console.WriteLine("Exit codes: 0 thành công/help; 2 sai cú pháp hoặc config; 3 module chưa được nối.");
}

static void PrintModeHelp(string mode)
{
    Console.WriteLine($"ET4230 {mode} — CLI draft W03");
    Console.WriteLine();
    Console.WriteLine($"Cách dùng: dotnet run --project src/ET4230.Cli -- {mode} --config <đường-dẫn-json>");
    Console.WriteLine("Config hiện chỉ được kiểm tra tồn tại và cú pháp JSON.");
    Console.WriteLine("Schema config và API thực thi mode này chưa được nhóm chốt/nối.");
}
