using CS2Craft.Bridge;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
namespace CS2Craft.Server;
/// <summary>
/// Gives every solid Minecraft block an invisible CS2 collider so bots, other players and bullets
/// collide with what the player builds. The collider is a stock crate whose physics hull is a
/// 40-unit cube centred on its origin: exactly one block. Bots do not re-path around it (the nav
/// mesh is baked); they walk into it and use their usual stuck/jump handling.
/// </summary>
internal sealed class BlockColliders
{
    public const string Model = "models/cs_italy/crate/italy_wood_crate_1.vmdl";
    readonly Dictionary<(int X,int Y,int Z),HashSet<(int X,int Y,int Z)>> sections = new();
    readonly Dictionary<(int X,int Y,int Z),CDynamicProp> props = new();
    readonly HashSet<(int X,int Y,int Z)> dirty = new();
    readonly ulong[] bits = new ulong[64];
    readonly int limit;
    int ticks;
    public int Count => props.Count;
    public int Pending => dirty.Count;
    public BlockColliders(int limit) { this.limit = limit; }
    /// <summary>Any live collider (the client probe checks it in game).</summary>
    public bool TryFirst(out (int X,int Y,int Z) block, out CDynamicProp prop)
    {
        foreach (var (key, value) in props) if (value.IsValid) { block = key; prop = value; return true; }
        block = default; prop = null!; return false;
    }
    public void Tick(NativeHud hud)
    {
        for (int i = 0; i < 64 && hud.PollSolids(out int sx, out int sy, out int sz, bits); i++)
        {
            var key = (sx, sy, sz);
            var now = SolidBlocks.Decode(sx, sy, sz, bits);
            if (sections.Remove(key, out var before)) dirty.UnionWith(before);
            if (now.Count > 0) sections[key] = now;
            dirty.UnionWith(now);
        }
        // Round cleanup deletes plugin-made entities; put back colliders for blocks that still exist.
        if (++ticks % 64 == 0)
            foreach (var (block, prop) in props.Where(p => !p.Value.IsValid).ToList()) { props.Remove(block); dirty.Add(block); }
        int spawned = 0;
        foreach (var block in dirty.ToList())
        {
            bool wanted = sections.TryGetValue(SolidBlocks.Section(block), out var set) && set.Contains(block);
            if (wanted == props.ContainsKey(block)) { dirty.Remove(block); continue; }
            if (!wanted) { Remove(block); dirty.Remove(block); continue; }
            // Spread entity creation over ticks; a large build appears over a few frames.
            if (spawned >= 32 || props.Count >= limit) continue;
            if (Spawn(block) is { } prop) props[block] = prop;
            dirty.Remove(block); spawned++;
        }
    }
    static CDynamicProp? Spawn((int X,int Y,int Z) block)
    {
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic_override");
        if (prop is null) return null;
        var center = Coordinates.ToSource(new(block.X + 0.5, block.Y + 0.5, block.Z + 0.5));
        var origin = new Vector((float)center.X, (float)center.Y, (float)center.Z);
        prop.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
        prop.RenderMode = RenderMode_t.kRenderNone;
        prop.Render = System.Drawing.Color.FromArgb(0, 255, 255, 255);
        using var values = new CEntityKeyValues();
        values["model", KeyValuesType.TYPE_STRING] = Model;
        values["solid", KeyValuesType.TYPE_INT] = (int)SolidType_t.SOLID_VPHYSICS;
        values["disableshadows", KeyValuesType.TYPE_BOOL] = true;
        values["origin", KeyValuesType.TYPE_VECTOR] = origin;
        prop.DispatchSpawn(values);
        prop.Teleport(origin, new QAngle(0, 0, 0), new Vector(0, 0, 0));
        Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_clrRender");
        return prop;
    }
    void Remove((int X,int Y,int Z) block)
    {
        if (props.Remove(block, out var prop) && prop.IsValid) prop.Remove();
    }
    public void Clear()
    {
        foreach (var prop in props.Values) if (prop.IsValid) prop.Remove();
        props.Clear(); sections.Clear(); dirty.Clear();
    }
}
