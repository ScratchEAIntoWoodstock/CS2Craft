namespace CS2Craft.Bridge;

public readonly record struct Triangle(Point A, Point B, Point C, uint Flags = 0);
public readonly record struct CollisionBlock(int X, int Y, int Z, ulong[] Layers);

public static class CollisionRegion
{
    public static byte[] Triangles(int rx, int ry, int rz, uint epoch, IReadOnlyList<Triangle> triangles)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        Header(w, rx, ry, rz, epoch, triangles.Count);
        foreach (var t in triangles)
        {
            foreach (var p in new[] { t.A, t.B, t.C }) { w.Write((float)p.X); w.Write((float)p.Y); w.Write((float)p.Z); }
            w.Write(t.Flags);
        }
        return stream.ToArray();
    }
    public static byte[] Blocks(int rx, int ry, int rz, uint epoch, IReadOnlyList<CollisionBlock> blocks)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        Header(w, rx, ry, rz, epoch, blocks.Count);
        foreach (var b in blocks)
        {
            if (b.Layers.Length != 8) throw new ArgumentException("Collision blocks need eight layers.");
            w.Write(b.X); w.Write(b.Y); w.Write(b.Z); w.Write(0u);
            foreach (ulong layer in b.Layers) w.Write(layer);
        }
        return stream.ToArray();
    }
    static void Header(BinaryWriter w, int rx, int ry, int rz, uint epoch, int count)
    {
        w.Write(rx * 8); w.Write(ry * 8); w.Write(rz * 8);
        w.Write(rx * 8 + 7); w.Write(ry * 8 + 7); w.Write(rz * 8 + 7);
        w.Write(epoch); w.Write(count);
    }
}
