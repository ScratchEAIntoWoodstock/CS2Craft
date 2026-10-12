using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CS2Craft.Bridge;

namespace CS2Craft.Server;

/// <summary>Local desktop only. One producer (the server thread), including hurt events.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DesktopInput
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    readonly Dictionary<int, bool> held = new();
    static readonly (int Vk,ushort Scan)[] Keys = Enumerable.Range(0,26)
        .Select(i=>(0x41+i,(ushort)(4+i)))
        .Concat(new (int,ushort)[]{
            (0x74,62),(0x20,44),(0x10,225),(0x11,224),(0x12,226),
            (0x31,30),(0x32,31),(0x33,32),(0x34,33),(0x35,34),(0x36,35),(0x37,36),(0x38,37),(0x39,38),(0x30,39),
            (0x1B,41),(0x0D,40),(0x08,42),(0x09,43),(0x2E,76),(0x24,74),(0x23,77),
            (0x25,80),(0x26,82),(0x27,79),(0x28,81),(0xBF,56),(0xBD,45),(0xBB,46)
        }).ToArray();
    public void Poll(SharedBridge bridge,NativeHud native,bool enabled,bool gui,int width,int height)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        bool active=enabled&&pid==Environment.ProcessId;
        native.InputMode(active,gui,width,height);
        // Only this tick thread produces shared input. Native WndProc queues cursor/text/wheel separately.
        if(!active){Release(bridge);return;}
        if(gui&&native.Cursor(out int x,out int y))bridge.PushInput(new(4,0,x,y));
        foreach(var (vk,scan) in Keys)Send(bridge,vk,1,scan);
        Send(bridge,1,2,1);Send(bridge,2,2,3);Send(bridge,4,2,2);
        for(int i=0;i<128&&native.Poll(out var input);i++)if(!bridge.PushInput(input))break;
    }
    void Send(SharedBridge b, int vk, ushort type, ushort code)
    {
        bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (held.GetValueOrDefault(vk) == down) return;
        if (b.PushInput(new(type, code, down ? 1 : 0))) held[vk] = down;
    }
    public void Release(SharedBridge bridge)
    {
        if (held.Values.Any(x => x) && !bridge.PushInput(new(6,0))) return;
        held.Clear();
    }
}
