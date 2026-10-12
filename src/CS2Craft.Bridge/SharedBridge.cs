using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;

namespace CS2Craft.Bridge;

/// <summary>One host thread owns this object. Native atomic loads/stores synchronize with Java FFM.</summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class SharedBridge : IDisposable
{
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    byte* ptr;
    bool disposed;
    public string Name { get; }
    public SharedBridge(string name = Protocol.MappingName)
    {
        Name = name;
        // Fail rather than reset a mapping still held by a running Minecraft.
        mapping = MemoryMappedFile.CreateNew(name, Protocol.MappingBytes, MemoryMappedFileAccess.ReadWrite);
        view = mapping.CreateViewAccessor(0, Protocol.MappingBytes, MemoryMappedFileAccess.ReadWrite);
        view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        Write(0, Protocol.Magic);
        Write(4, Protocol.Version);
        Write(8, (uint)Environment.ProcessId);
        // Triple buffer starts with middle=0, Java back=1, renderer front=2.
        for (int i = 0; i < 256; i++) Write(Protocol.Water + 16 + i * 4, -1e30f);
        Heartbeat();
    }
    public void Heartbeat() => Atomic64(0x10, Environment.TickCount64);
    public bool MinecraftAlive
    {
        get { long age = Environment.TickCount64 - Atomic64(0x18); return Read<uint>(12) != 0 && age >= 0 && age < 3000; }
    }
    public uint MinecraftPid => Read<uint>(12);
    public T Read<T>(long offset) where T : unmanaged { Check(offset, sizeof(T)); return *(T*)(ptr + offset); }
    public void Write<T>(long offset, T value) where T : unmanaged { Check(offset, sizeof(T)); *(T*)(ptr + offset) = value; }
    public byte[] ReadBytes(long offset, int count) { Check(offset, count); return new ReadOnlySpan<byte>(ptr + offset, count).ToArray(); }
    public void WriteBytes(long offset, ReadOnlySpan<byte> data) { Check(offset, data.Length); data.CopyTo(new Span<byte>(ptr + offset, data.Length)); }
    public long Atomic64(long o) { Check(o, 8); return Volatile.Read(ref *(long*)(ptr + o)); }
    public void Atomic64(long o, long v) { Check(o, 8); Volatile.Write(ref *(long*)(ptr + o), v); }
    public uint Atomic32(long o) { Check(o, 4); return Volatile.Read(ref *(uint*)(ptr + o)); }
    public void Atomic32(long o, uint v) { Check(o, 4); Volatile.Write(ref *(uint*)(ptr + o), v); }
    void Check(long o, int bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (o < 0 || bytes < 0 || o > Protocol.MappingBytes - bytes) throw new ArgumentOutOfRangeException(nameof(o));
    }
    uint Begin(long offset)
    {
        uint seq = Atomic32(offset);
        seq = (seq + 1) | 1;
        Atomic32(offset, seq);
        Thread.MemoryBarrier();
        return seq;
    }
    void End(long offset, uint seq) { Thread.MemoryBarrier(); Atomic32(offset, seq + 1); }
    public void Publish(HostState state)
    {
        if (!state.Position.IsFinite || !float.IsFinite(state.Yaw) || !float.IsFinite(state.Pitch)
            || state.Width is 0 or > Protocol.MaxWidth || state.Height is 0 or > Protocol.MaxHeight
            || !float.IsFinite(state.BuySeconds) || !float.IsFinite(state.RoundSeconds))
            throw new ArgumentException("Invalid host state.");
        long o = Protocol.Host;
        uint seq = Begin(o);
        Write(o + 4, state.Flags); Write(o + 8, state.WorldId); Write(o + 12, state.Epoch);
        Write(o + 16, state.Position.X); Write(o + 24, state.Position.Y); Write(o + 32, state.Position.Z);
        Write(o + 40, state.Yaw); Write(o + 44, state.Pitch); Write(o + 48, state.Teleport);
        Write(o + 52, state.Width); Write(o + 56, state.Height); Write(o + 60, 12f);
        Write(o + 64, state.Money); Write(o + 68, Math.Max(0, state.BuySeconds)); Write(o + 72, Math.Max(0, state.RoundSeconds));
        End(o, seq);
    }
    public bool TryReadPlayer(out PlayerState state)
    {
        long o = Protocol.Player;
        for (int tries = 0; tries < 4; tries++)
        {
            uint seq = Atomic32(o);
            if ((seq & 1) != 0) continue;
            state = new(Read<uint>(o + 4), new(Read<double>(o + 8), Read<double>(o + 16), Read<double>(o + 24)),
                Read<float>(o + 32), Read<float>(o + 36), Read<float>(o + 40), Read<uint>(o + 48),
                Read<ulong>(o + 56), Read<float>(o + 64), Read<uint>(o+0xC0), Read<float>(o+0xC4));
            Thread.MemoryBarrier();
            if (seq == Atomic32(o)) return state.Valid;
        }
        state = default;
        return false;
    }
    public void PublishActors(IReadOnlyList<Actor> actors)
    {
        if (actors.Count > Protocol.MaxActors) throw new ArgumentException("Too many actors.");
        uint seq = Begin(Protocol.Actors);
        Write(Protocol.Actors + 4, (uint)actors.Count);
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i]; long o = Protocol.Actors + 64 + i * 64;
            new Span<byte>(ptr + o, 64).Clear();
            Write(o, a.Id); Write(o + 4, a.Flags);
            Write(o + 8, (float)a.Position.X); Write(o + 12, (float)a.Position.Y); Write(o + 16, (float)a.Position.Z);
            Write(o + 20, a.Yaw); Write(o + 24, a.Width); Write(o + 28, a.Height);
            Write(o + 32, Math.Clamp(a.HealthFraction, 0, 1)); Write(o + 36, (ushort)1);
            Encoding.UTF8.GetEncoder().Convert(a.Name.AsSpan(), new Span<byte>(ptr + o + 40, 23), true,
                out _, out _, out _);
        }
        End(Protocol.Actors, seq);
    }
    public bool PushInput(InputEvent e)
    {
        long head = Atomic64(Protocol.Input), tail = Atomic64(Protocol.Input + 64);
        ValidateCounters(head, tail, Protocol.InputCapacity);
        if (head - tail == Protocol.InputCapacity) return false;
        long o = Protocol.Input + 128 + head % Protocol.InputCapacity * 16;
        Write(o, e.Type); Write(o + 2, e.Code); Write(o + 4, e.A); Write(o + 8, e.B); Write(o + 12, e.C);
        Atomic64(Protocol.Input, head + 1);
        return true;
    }
    /// <summary>Discard only events already published; later events remain readable.</summary>
    public void DiscardEvents()
    {
        long head = Atomic64(Protocol.Events), tail = Atomic64(Protocol.Events + 64);
        ValidateCounters(head, tail, Protocol.EventCapacity);
        Atomic64(Protocol.Events + 64, head);
    }
    public bool TryReadEvent(out GameEvent e)
    {
        long head = Atomic64(Protocol.Events), tail = Atomic64(Protocol.Events + 64);
        ValidateCounters(head, tail, Protocol.EventCapacity);
        if (head == tail) { e = default; return false; }
        long o = Protocol.Events + 128 + tail % Protocol.EventCapacity * 32;
        e = new(Read<uint>(o), Read<uint>(o + 4), Read<float>(o + 8), Read<float>(o + 12),
            Read<float>(o + 16), Read<float>(o + 20), Read<uint>(o + 24), Read<uint>(o + 28));
        Atomic64(Protocol.Events + 64, tail + 1);
        return true;
    }
    public bool PushCollision(uint type, ReadOnlySpan<byte> payload) => PushPacket(Protocol.Collision, Protocol.CollisionBytes, type, payload);
    public bool PushPacket(long ring, int bytes, uint type, ReadOnlySpan<byte> payload)
    {
        int capacity = bytes - 128, needed = Protocol.Align8(checked(8 + payload.Length));
        if (type == 0 || needed > capacity) throw new ArgumentException("Invalid ring packet.");
        long head = Atomic64(ring), tail = Atomic64(ring + 64);
        ValidateCounters(head, tail, capacity);
        int pos = (int)(head % capacity), pad = pos + needed > capacity ? capacity - pos : 0;
        if (capacity - (head - tail) < needed + pad) return false;
        if (pad > 0) { Write(ring + 128 + pos, 0u); Write(ring + 132 + pos, 0u); head += pad; pos = 0; }
        long o = ring + 128 + pos;
        Write(o, type); Write(o + 4, (uint)payload.Length); WriteBytes(o + 8, payload);
        Atomic64(ring, head + needed);
        return true;
    }
    public bool TryReadRender(out Packet packet) => TryReadPacket(Protocol.Render, Protocol.RenderBytes, out packet);
    public bool TryReadPacket(long ring, int bytes, out Packet packet)
    {
        int capacity = bytes - 128;
        long head = Atomic64(ring), tail = Atomic64(ring + 64);
        ValidateCounters(head, tail, capacity);
        while (tail < head)
        {
            int pos = (int)(tail % capacity);
            long o = ring + 128 + pos;
            uint type = Read<uint>(o), size = Read<uint>(o + 4);
            if (type == 0)
            {
                int pad = capacity - pos;
                if (pad > head - tail) throw new InvalidDataException("Incomplete ring padding.");
                tail += pad; Atomic64(ring + 64, tail); continue;
            }
            if (size > capacity - 8) throw new InvalidDataException("Oversized ring packet.");
            int total = Protocol.Align8(checked((int)size + 8));
            if (pos + total > capacity || total > head - tail) throw new InvalidDataException("Incomplete ring packet.");
            packet = new(type, ReadBytes(o + 8, (int)size));
            Atomic64(ring + 64, tail + total);
            return true;
        }
        packet = default;
        return false;
    }
    public byte[]? ReadOverlay(ref int front, out int width, out int height, out bool bottomUp)
    {
        width = height = 0; bottomUp = false;
        uint state = Atomic32(Protocol.Overlay);
        if ((state & 4) == 0) return null;
        state = (uint)Interlocked.Exchange(ref *(int*)(ptr + Protocol.Overlay), front);
        front = (int)(state & 3);
        if (front > 2) throw new InvalidDataException("Invalid overlay slot.");
        long h = Protocol.OverlayHeaders + front * 64;
        width = Read<int>(h); height = Read<int>(h + 4); bottomUp = (Read<uint>(h + 8) & 1) != 0;
        if (width <= 0 || height <= 0 || width > Protocol.MaxWidth || height > Protocol.MaxHeight)
            throw new InvalidDataException("Invalid overlay dimensions.");
        return ReadBytes(Protocol.Pixels + front * Protocol.OverlaySlotBytes, checked(width * height * 4));
    }
    static void ValidateCounters(long head, long tail, int capacity)
    {
        if (tail < 0 || head < tail || head - tail > capacity) throw new InvalidDataException("Corrupt ring counters.");
    }
    public void Dispose()
    {
        if (disposed) return;
        Atomic64(0x10, 0);
        view.SafeMemoryMappedViewHandle.ReleasePointer();
        ptr = null; view.Dispose(); mapping.Dispose(); disposed = true;
    }
}
