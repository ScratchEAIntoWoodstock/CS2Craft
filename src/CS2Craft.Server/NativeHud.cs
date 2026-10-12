using System.Runtime.InteropServices;
using CS2Craft.Bridge;

namespace CS2Craft.Server;
internal sealed class NativeHud
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StartDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StopDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong CountDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CameraDelegate(double x,double y,double z,float yaw,float pitch,float fov,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void OccluderDelegate(int x,int y,int z,[In] float[] vertices,uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)] delegate void CaptureDelegate([MarshalAs(UnmanagedType.LPWStr)]string path);
    CaptureDelegate? capture;
    StartDelegate? captureStatus;
    public int CaptureStatus => captureStatus?.Invoke() ?? -4;
    public void Capture(string path) => capture?.Invoke(path);
    [StructLayout(LayoutKind.Sequential)] struct NativeInput {public ushort Type,Code;public int A,B,C;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InputModeDelegate(int active,int gui,int width,int height);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CursorDelegate(out int x,out int y);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PollDelegate(out NativeInput input);
    InputModeDelegate? inputMode;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void TestMouseDelegate(int x,int y);
    TestMouseDelegate? testMouse;
    public void TestMouse(int x,int y)=>testMouse?.Invoke(x,y);
    CursorDelegate? cursor,look;
    public bool Look(out int x,out int y){x=y=0;return look?.Invoke(out x,out y)==1;}
    PollDelegate? poll;
    public void InputMode(bool active,bool gui,int width,int height)=>inputMode?.Invoke(active?1:0,gui?1:0,width,height);
    public bool Cursor(out int x,out int y){x=y=0;return cursor?.Invoke(out x,out y)==1;}
    public bool Poll(out InputEvent input)
    {
        input=default;
        if(poll is null || poll(out var e)!=1)return false;
        input=new(e.Type,e.Code,e.A,e.B,e.C);return true;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void AvatarDelegate(double x,double y,double z,int show);
    AvatarDelegate? avatar;
    public void Avatar(Point p,bool show)=>avatar?.Invoke(p.X,p.Y,p.Z,show?1:0);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InputStatsDelegate(out ulong window,out ulong hooked,out int status);
    InputStatsDelegate? inputStats;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PollSolidsDelegate(out int x,out int y,out int z,[Out] ulong[] bits);
    PollSolidsDelegate? pollSolids;
    StopDelegate? resendSolids;
    /// <summary>One changed 16^3 section of Minecraft solid blocks (bit x+16z+256y); false when none are pending.</summary>
    public bool PollSolids(out int x,out int y,out int z,ulong[] bits)
    {
        x=y=z=0;
        return pollSolids is not null && bits.Length==64 && pollSolids(out x,out y,out z,bits)==1;
    }
    public void ResendSolids() => resendSolids?.Invoke();
    CursorDelegate? backbufferSize;
    /// <summary>CS2's swap-chain size once the renderer has presented a linked frame.</summary>
    public bool BackbufferSize(out int width,out int height){width=height=0;return backbufferSize?.Invoke(out width,out height)==1;}
    /// <summary>Raw mouse packets seen through the window subclass and through the GetRawInput* hooks.</summary>
    public void InputStats(out ulong window,out ulong hooked,out int status)
    {
        window=hooked=0;status=-1;
        inputStats?.Invoke(out window,out hooked,out status);
    }
    CameraDelegate? camera;
    OccluderDelegate? occluder;
    CountDelegate? worldFrames,sections,rejected,occluderRegions;
    StopDelegate? clearOccluders;
    public ulong OccluderRegions => occluderRegions?.Invoke() ?? 0;
    public void ClearOccluders() => clearOccluders?.Invoke();
    nint module;
    StopDelegate? stop;
    CountDelegate? frames;
    public string Status { get; private set; } = "not loaded";
    public ulong Frames => frames?.Invoke() ?? 0;
    public ulong WorldFrames => worldFrames?.Invoke() ?? 0;
    public ulong Sections => sections?.Invoke() ?? 0;
    public ulong Rejected => rejected?.Invoke() ?? 0;
    public void Camera(Point eye,float yaw,float pitch,float verticalFov,bool active,bool coarseOcclusion)
        => camera?.Invoke(eye.X,eye.Y,eye.Z,yaw,pitch,verticalFov,(active?1u:0u)|(coarseOcclusion?2u:0u));
    public void Occluder(int x,int y,int z,IReadOnlyList<Triangle> triangles)
    {
        if(occluder is null)return;
        var vertices=new float[triangles.Count*9];int i=0;
        foreach(var t in triangles)foreach(var p in new[]{t.A,t.B,t.C}){vertices[i++]=(float)p.X;vertices[i++]=(float)p.Y;vertices[i++]=(float)p.Z;}
        occluder(x,y,z,vertices,(uint)(triangles.Count*3));
    }
    public void Start(string directory)
    {
        string path = Path.Combine(directory,"CS2Craft.Renderer.dll");
        if (!File.Exists(path)) { Status = "DLL absent"; return; }
        module = NativeLibrary.Load(path);
        var start = Marshal.GetDelegateForFunctionPointer<StartDelegate>(NativeLibrary.GetExport(module,"CS2Craft_Start"));
        stop = Marshal.GetDelegateForFunctionPointer<StopDelegate>(NativeLibrary.GetExport(module,"CS2Craft_Stop"));
        frames = Marshal.GetDelegateForFunctionPointer<CountDelegate>(NativeLibrary.GetExport(module,"CS2Craft_OverlayFrames"));
        camera = Marshal.GetDelegateForFunctionPointer<CameraDelegate>(NativeLibrary.GetExport(module,"CS2Craft_SetCamera"));
        occluder = Marshal.GetDelegateForFunctionPointer<OccluderDelegate>(NativeLibrary.GetExport(module,"CS2Craft_SetOccluder"));
        clearOccluders = Marshal.GetDelegateForFunctionPointer<StopDelegate>(NativeLibrary.GetExport(module,"CS2Craft_ClearOccluders"));
        occluderRegions = Marshal.GetDelegateForFunctionPointer<CountDelegate>(NativeLibrary.GetExport(module,"CS2Craft_OccluderRegions"));
        worldFrames = Marshal.GetDelegateForFunctionPointer<CountDelegate>(NativeLibrary.GetExport(module,"CS2Craft_WorldFrames"));
        sections = Marshal.GetDelegateForFunctionPointer<CountDelegate>(NativeLibrary.GetExport(module,"CS2Craft_WorldSections"));
        rejected = Marshal.GetDelegateForFunctionPointer<CountDelegate>(NativeLibrary.GetExport(module,"CS2Craft_RejectedPackets"));
        capture = Marshal.GetDelegateForFunctionPointer<CaptureDelegate>(NativeLibrary.GetExport(module,"CS2Craft_RequestCapture"));
        captureStatus = Marshal.GetDelegateForFunctionPointer<StartDelegate>(NativeLibrary.GetExport(module,"CS2Craft_CaptureStatus"));
        inputMode = Marshal.GetDelegateForFunctionPointer<InputModeDelegate>(NativeLibrary.GetExport(module,"CS2Craft_InputMode"));
        cursor = Marshal.GetDelegateForFunctionPointer<CursorDelegate>(NativeLibrary.GetExport(module,"CS2Craft_Cursor"));
        testMouse=Marshal.GetDelegateForFunctionPointer<TestMouseDelegate>(NativeLibrary.GetExport(module,"CS2Craft_TestMouse"));
        look = Marshal.GetDelegateForFunctionPointer<CursorDelegate>(NativeLibrary.GetExport(module,"CS2Craft_Look"));
        poll = Marshal.GetDelegateForFunctionPointer<PollDelegate>(NativeLibrary.GetExport(module,"CS2Craft_PollInput"));
        avatar=Marshal.GetDelegateForFunctionPointer<AvatarDelegate>(NativeLibrary.GetExport(module,"CS2Craft_SetAvatar"));
        inputStats=Marshal.GetDelegateForFunctionPointer<InputStatsDelegate>(NativeLibrary.GetExport(module,"CS2Craft_InputStats"));
        pollSolids=Marshal.GetDelegateForFunctionPointer<PollSolidsDelegate>(NativeLibrary.GetExport(module,"CS2Craft_PollSolids"));
        resendSolids=Marshal.GetDelegateForFunctionPointer<StopDelegate>(NativeLibrary.GetExport(module,"CS2Craft_ResendSolids"));
        backbufferSize=Marshal.GetDelegateForFunctionPointer<CursorDelegate>(NativeLibrary.GetExport(module,"CS2Craft_BackbufferSize"));
        int result = start();
        Status = result == 0 ? "hook installed (DX11)" : result == 1 ? "skipped (dedicated or secure process)" : $"start failed ({result})";
    }
    public void Stop()
    {
        stop?.Invoke(); stop=null; avatar=null; inputStats=null; pollSolids=null; resendSolids=null; backbufferSize=null; frames=null; camera=null; occluder=null; clearOccluders=null; occluderRegions=null; worldFrames=null; sections=null; rejected=null;capture=null;captureStatus=null;inputMode=null;cursor=null;look=null;testMouse=null;poll=null;
        // Keep the native module resident until process exit: an in-flight Present may still be returning.
        // CS2Craft_Stop unhooks and releases all resources. A hot reload can start it again.
        Status="stopped";
    }
}
