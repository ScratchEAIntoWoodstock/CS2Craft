namespace CS2Craft.Bridge;

/// <summary>Minecraft's per-section solid-block bitset (kRenSolids): 4096 bits, bit x + 16z + 256y.</summary>
public static class SolidBlocks
{
    public static HashSet<(int X,int Y,int Z)> Decode(int sx, int sy, int sz, ReadOnlySpan<ulong> bits)
    {
        if (bits.Length != 64) throw new ArgumentException("A section bitset has 64 words.", nameof(bits));
        var blocks = new HashSet<(int X,int Y,int Z)>();
        for (int bit = 0; bit < 4096; bit++)
            if ((bits[bit >> 6] & (1UL << (bit & 63))) != 0)
                blocks.Add((sx * 16 + (bit & 15), sy * 16 + (bit >> 8), sz * 16 + ((bit >> 4) & 15)));
        return blocks;
    }
    /// <summary>The section containing a block (floor division, correct for negative coordinates).</summary>
    public static (int X,int Y,int Z) Section((int X,int Y,int Z) block) => (block.X >> 4, block.Y >> 4, block.Z >> 4);
}
