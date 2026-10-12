namespace CS2Craft.Bridge;

// Wire-compatible with SkyCraft v0.1.0, commit adba1e3, protocol 10.
// Offsets retain upstream names on the Java side; the host is now CS2.
public static class Protocol
{
    public const string MappingName = @"Local\CS2Craft_v1";
    public const uint Magic = 0x43594B53, Version = 10;
    public const long Host = 0x100, Player = 0x200, Overlay = 0x300, OverlayHeaders = 0x340;
    public const long Water = 0x400, Input = 0x1000, Actors = 0x12000, Events = 0x17000;
    public const long WorldEntities = 0x1C000, Collision = 0x20000;
    public const int CollisionBytes = 32 << 20, RenderBytes = 64 << 20;
    public const int MaxWidth = 3840, MaxHeight = 2160;
    public const long OverlaySlotBytes = (long)MaxWidth * MaxHeight * 4;
    public const long Pixels = Collision + CollisionBytes;
    public const long Render = Pixels + OverlaySlotBytes * 3;
    public const long MappingBytes = Render + RenderBytes;
    public const int InputCapacity = 4096, EventCapacity = 512, MaxActors = 256;
    public const uint HostInGame = 1, HostMenu = 2, HostLoading = 4;
    // CS2Craft extension: Minecraft holds sprint while moving (CS2 runs faster than Minecraft walks).
    public const uint HostAutoSprint = 1u << 16;
    public const uint McInWorld = 1, McScreen = 2, McDead = 32;
    // CS2Craft extension input event: a CS2 round started; Minecraft removes all placed blocks.
    public const ushort InputRoundReset = 64;
    public static int Align8(int size) => checked((size + 7) & ~7);
}

public readonly record struct Point(double X, double Y, double Z)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
}
public static class Coordinates
{
    // 72-unit CS2 standing hull corresponds to Minecraft's 1.8-block player.
    public const double UnitsPerBlock = 40;
    public static Point ToMinecraft(Point p) => new(p.X / UnitsPerBlock, p.Z / UnitsPerBlock, -p.Y / UnitsPerBlock);
    public static Point ToSource(Point p) => new(p.X * UnitsPerBlock, -p.Z * UnitsPerBlock, p.Y * UnitsPerBlock);
    public static float Yaw(float yaw) => Wrap(-yaw - 90);
    public static float Wrap(float yaw) => (yaw % 360 + 540) % 360 - 180;
}

public readonly record struct HostState(uint Flags, uint WorldId, uint Epoch, Point Position,
    float Yaw, float Pitch, uint Teleport, uint Width = 1280, uint Height = 720);
public readonly record struct PlayerState(uint Flags, Point Position, float Yaw, float Pitch,
    float EyeHeight, uint TeleportAck, ulong Frame, float VerticalFov, uint CameraMode=0, float CameraDistance=0)
{
    public bool Valid => Position.IsFinite && Math.Abs(Position.X) < 1e6 && Math.Abs(Position.Y) < 1e6
        && Math.Abs(Position.Z) < 1e6 && float.IsFinite(Yaw) && float.IsFinite(Pitch);
}
public readonly record struct Actor(uint Id, uint Flags, Point Position, float Yaw,
    float Width, float Height, float HealthFraction, string Name);
public readonly record struct GameEvent(uint Type, uint ActorId, float A, float B, float C, float D, uint Flags, uint Weapon);
public readonly record struct InputEvent(ushort Type, ushort Code, int A = 0, int B = 0, int C = 0);
public readonly record struct Packet(uint Type, byte[] Payload);
