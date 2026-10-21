using System.Text.Json;
using Aperture;
using Aperture.Consoles;
using Aperture.Rpc;

var host = args.Length > 0 ? args[0] : new ConsoleManager().DefaultConsole ?? throw new InvalidOperationException("Specify a console host.");
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
await using var console = await XboxConsole.ConnectAsync(host, cancellationToken: deadline.Token);
var results = new Dictionary<string, object?> { ["name"] = console.Name, ["host"] = host, ["timeUtc"] = DateTimeOffset.UtcNow };
results["kernel"] = await console.Rpc.GetKernelVersionAsync(deadline.Token);
results["titleId"] = (await console.Rpc.GetCurrentTitleIdAsync(deadline.Token)).ToString("X8");
results["consoleType"] = await console.Rpc.GetConsoleTypeAsync(deadline.Token);
results["cpuTemperature"] = await console.Rpc.GetTemperatureAsync(TemperatureSensor.Cpu, deadline.Token);
results["drives"] = await console.Files.GetDrivesAsync(deadline.Token);
results["rootFiles"] = await console.Files.ListAsync(@"HDD:\", deadline.Token);
try { results["xamTextBytes"] = Convert.ToHexString(await console.Memory.ReadAsync(0x81670000, 32, deadline.Token)); }
catch (IOException error) { results["memoryReadError"] = error.Message; }
results["systemInfo"] = await console.GetSystemInfoAsync(deadline.Token);
results["modules"] = await console.DebugTarget.GetModulesAsync(deadline.Token);
results["threadIds"] = await console.DebugTarget.GetThreadIdsAsync(deadline.Token);
results["sections"] = await console.DebugTarget.GetSectionsAsync("xam.xex", deadline.Token);
results["memoryRegions"] = await console.DebugTarget.GetMemoryRegionsAsync(deadline.Token);
results["runningProcess"] = await console.GetRunningProcessAsync(deadline.Token);
try { results["titleIpAddress"] = (await console.GetTitleIPAddressAsync(deadline.Token)).ToString(); }
catch (IOException error) { results["titleIpError"] = error.Message; }
try { results["systemTime"] = await console.GetSystemTimeAsync(deadline.Token); }
catch (IOException error) { results["systemTimeError"] = error.Message; }
results["discovery"] = await ConsoleDiscovery.DiscoverAsync(TimeSpan.FromSeconds(1), new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host), 730), deadline.Token);
try
{
    results["xamEntryPoint"] = (await console.DebugTarget.GetExecutable("xam.xex").GetEntryPointAddressAsync(deadline.Token)).ToString("X8");
    results["xamPeName"] = await console.DebugTarget.GetExecutable("xam.xex").GetPeModuleNameAsync(deadline.Token);
}
catch (IOException error) { results["xexFieldError"] = error.Message; }
try
{
    await using var notifications = await Aperture.Debugging.NotificationSession.SubscribeAsync(console.Connection, cancellationToken: deadline.Token);
    results["notificationSubscription"] = "registered; disposed and unregistered after read-only probe";
}
catch (IOException error) { results["notificationError"] = error.Message; }
if (args.Length > 1)
{
    var screenshot = await console.CaptureScreenshotAsync(deadline.Token);
    await screenshot.SaveBmpAsync(args[1], deadline.Token);
    results["screenshot"] = new { screenshot.Width, screenshot.Height, screenshot.Pitch, Format = screenshot.Format.ToString("X8"), SavedPath = args[1] };
}
Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
