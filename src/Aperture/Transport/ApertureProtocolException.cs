namespace Aperture.Transport;

public sealed class ApertureProtocolException(int statusCode, string message) : IOException($"Debug monitor {statusCode}: {(string.IsNullOrEmpty(message) ? DebugMonitorErrors.Describe(statusCode - 400) : message)}")
{
    public int StatusCode { get; } = statusCode;
    public string ResponseMessage { get; } = message;
}

public static class DebugMonitorErrors
{
    public static string Describe(int code) => code switch
    {
        0 => "Unspecified monitor failure", 1 => "Connection limit reached", 2 => "File not found", 3 => "Module not found",
        4 => "Memory address is unmapped", 5 => "Thread not found", 6 => "Console clock has not been initialized", 7 => "Monitor does not recognize this command",
        8 => "Thread must be stopped", 9 => "Move requires copying the file", 10 => "File already exists", 11 => "Directory contains entries",
        12 => "Invalid file name", 13 => "File creation failed", 14 => "File access failed", 15 => "Storage is full", 16 => "Title does not allow debugging",
        17 => "Invalid counter type", 18 => "Counter data unavailable", 20 => "Console is not locked", 21 => "Authentication key exchange required",
        22 => "Command needs a dedicated connection", 23 => "Invalid argument", 24 => "Profiling is not active", 25 => "Profiling is already active",
        26 => "Execution is already stopped", 27 => "FastCAP disabled", 28 => "Monitor allocation failed", 29 => "Operation timed out", 30 => "Path not found",
        31 => "Unsupported screenshot input format", 32 => "Unsupported screenshot output format", 33 => "CallCAP disabled", 34 => "Conflicting capture configuration",
        35 => "Capture disabled", 36 => "Instrumentation branch exceeds its range", 37 => "Executable header field is absent", 38 => "Profiling output buffer is too small",
        39 => "Console is rebooting", 41 => "Duration limit exceeded", 42 => "Controller automation state does not allow this operation",
        43 => "Extension limit reached", 44 => "PMC session is already active", 45 => "PMC session is not active", 46 => "Command exceeds monitor length limit",
        0x50 => "Incompatible D3D debug command", 0x51 => "D3D surface unavailable", 0x100 => "Connection could not be established",
        0x101 => "Connection was lost", 0x103 => "File operation failed", 0x104 => "Enumeration is complete", 0x105 => "Output buffer is too small",
        0x106 => "Invalid executable image", 0x107 => "Memory write was incomplete", 0x108 => "Console name is missing", 0x109 => "Error description unavailable",
        0x10a => "Invalid monitor response", 0x150 => "Previous command is pending", _ => "Debug monitor error " + code
    };
}
