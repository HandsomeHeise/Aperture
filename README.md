# Aperture

Aperture is a standalone C# library for Xbox 360 debugging, remote calls, file access, memory inspection and controller automation. It provides a modern asynchronous .NET 10 API for compatible Xbox 360 debug monitors and JRPC2-compatible console handlers.

No `JRPC.dll`, `XDevkit.dll`, COM registration or Xbox SDK installation is required on the PC. The console still needs a compatible debug monitor and, for RPC calls, a handler such as JRPC2.

## Features

- Independent console connections, discovery and connection options.
- Typed big-endian memory access with strings, arrays and optional range caching.
- RPC calls by address or module/ordinal with scalar, array, string and void results.
- Remote files, directories, attributes, uploads, downloads and offset-based transfers.
- Modules, sections, threads, registers, breakpoints and execution control.
- PowerPC stack unwinding, epilog handling and VMX128 vector registers.
- Notifications, controller state, input queues and user access management.
- Screenshot capture with tiled framebuffer decoding.
- Xenon minidump reading, writing and capture.
- Executable metadata, local symbol lookup and standard symbol-server resolution.

## Requirements

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Xbox 360 with a compatible debug monitor reachable over the network

## Build and test

```powershell
dotnet build Aperture.slnx -c Release
dotnet test Aperture.slnx -c Release --no-build
```

The library project is `src/Aperture/Aperture.csproj`. The test projects and small diagnostic tools are included as source code and are not required at runtime.

## Usage

Reference `src/Aperture/Aperture.csproj` from a .NET 10 application:

```csharp
using Aperture;
using Aperture.Models;

await using var xbox = await XboxConsole.ConnectAsync("192.0.2.10");

Console.WriteLine($"{xbox.Name}: kernel {await xbox.Rpc.GetKernelVersionAsync()}");
Console.WriteLine(await xbox.Rpc.GetTemperatureAsync(TemperatureSensor.Cpu));

foreach (var module in await xbox.DebugTarget.GetModulesAsync())
    Console.WriteLine($"{module.Name}: {module.BaseAddress:X8}, {module.Size:X}");

var screenshot = await xbox.CaptureScreenshotAsync();
await screenshot.SaveBmpAsync("screenshot.bmp");
```

Replace the example address with the address of your console. Commands and responses are serialized per connection, and network APIs accept cancellation tokens.

Examples of memory, RPC and file operations:

```csharp
ulong value = await xbox.Memory.ReadAsync<ulong>(address);
await xbox.Memory.WriteAsync(address, value);

uint result = await xbox.Rpc.CallAsync<uint>("xam.xex", ordinal, [argument]);
await xbox.Files.DownloadAsync(@"HDD:\example.bin", "example.bin");
```

Addresses and RPC arguments must match the running title. Memory caching is disabled by default. Be careful with write, reboot, upload and controller APIs when connected to a live console.

## Scope

This repository contains the Aperture source code, build metadata, tests, diagnostic tools and this README. Compiled binaries, SDK files, generated packages and private analysis data are intentionally excluded.
