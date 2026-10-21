// Value definitions reconstructed from assembly metadata; no COM or original implementation.
namespace Aperture.Models;

[Flags]
public enum AccessFlags : int
{
    Configure = 8,
    Control = 4,
    Manage = 16,
    Read = 1,
    Write = 2,
}

[Flags]
public enum AutomationButtonFlags : int
{
    A_Button = 4096,
    B_Button = 8192,
    BackButton = 32,
    Bind_Button = 2048,
    DPadDown = 2,
    DPadLeft = 4,
    DPadRight = 8,
    DPadUp = 1,
    LeftShoulderButton = 256,
    LeftThumbButton = 64,
    RightShoulderButton = 512,
    RightThumbButton = 128,
    StartButton = 16,
    X_Button = 16384,
    Xbox360_Button = 1024,
    Y_Button = 32768,
}

public enum BreakpointType : int
{
    NoBreakpoint = 0,
    OnExecute = 4,
    OnExecuteHW = 3,
    OnRead = 2,
    OnWrite = 1,
}

public enum ConsoleType : int
{
    DevelopmentKit = 0,
    ReviewerKit = 2,
    TestKit = 1,
}

public enum CreateDisposition : int
{
    CreateAlways = 2,
    CreateNew = 1,
    OpenAlways = 4,
    OpenExisting = 3,
}

[Flags]
public enum DebugConnectFlags : int
{
    Force = 1,
    MonitorOnly = 2,
}

public enum DebugEventType : int
{
    AssertionFailed = 10,
    AssertionFailedEx = 11,
    BugCheck = 19,
    DataBreak = 12,
    DebugString = 2,
    Exception = 9,
    ExecStateChange = 3,
    ExecutionBreak = 1,
    FiberCreate = 17,
    FiberDestroy = 18,
    ModuleLoad = 5,
    ModuleUnload = 6,
    NoEvent = 0,
    PgoModuleStartup = 20,
    RIP = 13,
    SectionLoad = 14,
    SectionUnload = 15,
    SingleStep = 4,
    StackTrace = 16,
    ThreadCreate = 7,
    ThreadDestroy = 8,
}

[Flags]
public enum DumpFlags : int
{
    FilterMemory = 8,
    FilterModulePaths = 128,
    Normal = 0,
    ScanMemory = 16,
    WithDataSegs = 1,
    WithFullMemory = 2,
    WithHandleData = 4,
    WithIndirectlyReferencedMemory = 64,
    WithPrivateReadWriteMemory = 512,
    WithProcessThreadData = 256,
    WithUnloadedModules = 32,
}

public enum DumpMode : int
{
    Disabled = 2,
    Enabled = 1,
    Smart = 0,
}

[Flags]
public enum DumpReportFlags : int
{
    AlwaysReport = 1,
    DestinationGroup = 15,
    FormatFullHeap = 0,
    FormatGroup = 3840,
    FormatNoHeap = 512,
    FormatPartialHeap = 256,
    FormatRetail = 1024,
    LocalDestination = 0,
    NeverReport = 2,
    PromptToReport = 0,
    RemoteDestination = 16,
    ReportGroup = 15,
}

[Flags]
public enum EventDeferFlags : int
{
    CanDeferAssertionFailed = 8,
    CanDeferAssertionFailedEx = 16,
    CanDeferDataBreak = 32,
    CanDeferDebugString = 2,
    CanDeferExecutionBreak = 1,
    CanDeferRIP = 64,
    CanDeferSingleStep = 4,
}

[Flags]
public enum ExceptionFlags : int
{
    FirstChance = 2,
    Noncontinuable = 1,
}

public enum ExecutionState : int
{
    Pending = 3,
    PendingTitle = 5,
    Rebooting = 2,
    RebootingTitle = 4,
    Running = 1,
    Stopped = 0,
}

public enum FunctionType : int
{
    Handler = 3,
    NoHandler = 1,
    NoPData = -1,
    RestoreMillicode = 2,
    SaveMillicode = 0,
}

[Flags]
public enum MemoryRegionFlags : int
{
    Execute = 16,
    ExecuteRead = 32,
    ExecuteReadWrite = 64,
    ExecuteWriteCopy = 128,
    Guard = 256,
    NoAccess = 1,
    NoCache = 512,
    ReadOnly = 2,
    ReadWrite = 4,
    UserReadOnly = 4096,
    UserReadWrite = 8192,
    WriteCombine = 1024,
    WriteCopy = 8,
}

[Flags]
public enum ModuleInfoFlags : int
{
    Dll = 4,
    Main = 1,
    Tls = 2,
}

[Flags]
public enum RebootFlags : int
{
    Cold = 2,
    Stop = 8,
    Title = 0,
    Wait = 1,
    Warm = 4,
}

public enum Register32 : int
{
    cr = 3,
    iar = 1,
    lr = 2,
    msr = 0,
    xer = 4,
}

public enum Register64 : int
{
    ctr = 0,
    r0 = 1,
    r1 = 2,
    r10 = 11,
    r11 = 12,
    r12 = 13,
    r13 = 14,
    r14 = 15,
    r15 = 16,
    r16 = 17,
    r17 = 18,
    r18 = 19,
    r19 = 20,
    r2 = 3,
    r20 = 21,
    r21 = 22,
    r22 = 23,
    r23 = 24,
    r24 = 25,
    r25 = 26,
    r26 = 27,
    r27 = 28,
    r28 = 29,
    r29 = 30,
    r3 = 4,
    r30 = 31,
    r31 = 32,
    r4 = 5,
    r5 = 6,
    r6 = 7,
    r7 = 8,
    r8 = 9,
    r9 = 10,
}

public enum RegisterDouble : int
{
    fp0 = 0,
    fp1 = 1,
    fp10 = 10,
    fp11 = 11,
    fp12 = 12,
    fp13 = 13,
    fp14 = 14,
    fp15 = 15,
    fp16 = 16,
    fp17 = 17,
    fp18 = 18,
    fp19 = 19,
    fp2 = 2,
    fp20 = 20,
    fp21 = 21,
    fp22 = 22,
    fp23 = 23,
    fp24 = 24,
    fp25 = 25,
    fp26 = 26,
    fp27 = 27,
    fp28 = 28,
    fp29 = 29,
    fp3 = 3,
    fp30 = 30,
    fp31 = 31,
    fp4 = 4,
    fp5 = 5,
    fp6 = 6,
    fp7 = 7,
    fp8 = 8,
    fp9 = 9,
    fpscr = 32,
}

public enum RegisterVector : int
{
    v0 = 0,
    v1 = 1,
    v10 = 10,
    v100 = 100,
    v101 = 101,
    v102 = 102,
    v103 = 103,
    v104 = 104,
    v105 = 105,
    v106 = 106,
    v107 = 107,
    v108 = 108,
    v109 = 109,
    v11 = 11,
    v110 = 110,
    v111 = 111,
    v112 = 112,
    v113 = 113,
    v114 = 114,
    v115 = 115,
    v116 = 116,
    v117 = 117,
    v118 = 118,
    v119 = 119,
    v12 = 12,
    v120 = 120,
    v121 = 121,
    v122 = 122,
    v123 = 123,
    v124 = 124,
    v125 = 125,
    v126 = 126,
    v127 = 127,
    v13 = 13,
    v14 = 14,
    v15 = 15,
    v16 = 16,
    v17 = 17,
    v18 = 18,
    v19 = 19,
    v2 = 2,
    v20 = 20,
    v21 = 21,
    v22 = 22,
    v23 = 23,
    v24 = 24,
    v25 = 25,
    v26 = 26,
    v27 = 27,
    v28 = 28,
    v29 = 29,
    v3 = 3,
    v30 = 30,
    v31 = 31,
    v32 = 32,
    v33 = 33,
    v34 = 34,
    v35 = 35,
    v36 = 36,
    v37 = 37,
    v38 = 38,
    v39 = 39,
    v4 = 4,
    v40 = 40,
    v41 = 41,
    v42 = 42,
    v43 = 43,
    v44 = 44,
    v45 = 45,
    v46 = 46,
    v47 = 47,
    v48 = 48,
    v49 = 49,
    v5 = 5,
    v50 = 50,
    v51 = 51,
    v52 = 52,
    v53 = 53,
    v54 = 54,
    v55 = 55,
    v56 = 56,
    v57 = 57,
    v58 = 58,
    v59 = 59,
    v6 = 6,
    v60 = 60,
    v61 = 61,
    v62 = 62,
    v63 = 63,
    v64 = 64,
    v65 = 65,
    v66 = 66,
    v67 = 67,
    v68 = 68,
    v69 = 69,
    v7 = 7,
    v70 = 70,
    v71 = 71,
    v72 = 72,
    v73 = 73,
    v74 = 74,
    v75 = 75,
    v76 = 76,
    v77 = 77,
    v78 = 78,
    v79 = 79,
    v8 = 8,
    v80 = 80,
    v81 = 81,
    v82 = 82,
    v83 = 83,
    v84 = 84,
    v85 = 85,
    v86 = 86,
    v87 = 87,
    v88 = 88,
    v89 = 89,
    v9 = 9,
    v90 = 90,
    v91 = 91,
    v92 = 92,
    v93 = 93,
    v94 = 94,
    v95 = 95,
    v96 = 96,
    v97 = 97,
    v98 = 98,
    v99 = 99,
    vscr = 128,
}

[Flags]
public enum SectionInfoFlags : int
{
    Executable = 8,
    Loaded = 1,
    Readable = 2,
    Uninitialized = 16,
    Writeable = 4,
}

[Flags]
public enum SelectConsoleFlags : int
{
    FilterByAccess = 2,
    NoPromptIfDefaultExists = 0,
    NoPromptIfOnlyOne = 1,
}

public enum ShareMode : int
{
    ShareDelete = 4,
    ShareNone = 0,
    ShareRead = 1,
    ShareWrite = 2,
}

[Flags]
public enum StopOnFlags : int
{
    OnDebugString = 4,
    OnFirstChanceException = 2,
    OnModuleLoad = 16,
    OnPgoModuleStartup = 64,
    OnStackTrace = 8,
    OnThreadCreate = 1,
    OnTitleLaunch = 32,
}
