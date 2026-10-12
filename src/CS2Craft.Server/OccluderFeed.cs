using CS2Craft.Bridge;
namespace CS2Craft.Server;
/// <summary>
/// Streams the whole map's visible collision triangles to the renderer's depth pre-pass, so
/// Minecraft geometry is hidden behind walls at any distance, not only near sampled collision.
/// Each triangle is sent once (owned by the region of its centroid); nearest regions go first.
/// </summary>
internal sealed class OccluderFeed
{
    readonly Task<List<((int X,int Y,int Z) Region,Triangle[] Triangles)>> building;
    int next;
    public int Sent=>next;
    public int Total=>building.IsCompletedSuccessfully?building.Result.Count:0;
    public OccluderFeed(Task<MapGeometry> map,Point start)
    {
        building=map.ContinueWith(t=>Build(t.Result,start),TaskContinuationOptions.OnlyOnRanToCompletion);
    }
    static List<((int X,int Y,int Z) Region,Triangle[] Triangles)> Build(MapGeometry map,Point start)
    {
        var owned=new Dictionary<(int X,int Y,int Z),List<Triangle>>();
        foreach(var list in map.Regions.Values)
            foreach(var t in list)
            {
                // Player-clip surfaces are invisible in CS2 and must not hide anything.
                if((t.Flags&2)!=0)continue;
                var key=((int)Math.Floor((t.A.X+t.B.X+t.C.X)/24),(int)Math.Floor((t.A.Y+t.B.Y+t.C.Y)/24),(int)Math.Floor((t.A.Z+t.B.Z+t.C.Z)/24));
                if(!owned.TryGetValue(key,out var target))owned[key]=target=new();
                target.Add(t);
            }
        // Regions list a triangle once per region it overlaps; keep one copy per owner region.
        return owned.Select(o=>(o.Key,o.Value.Distinct().ToArray()))
            .OrderBy(o=>Math.Pow(o.Key.X*8+4-start.X,2)+Math.Pow(o.Key.Y*8+4-start.Y,2)+Math.Pow(o.Key.Z*8+4-start.Z,2))
            .ToList();
    }
    /// <summary>Sends up to <paramref name="budget"/> regions; returns false once everything is sent.</summary>
    public bool Tick(NativeHud hud,int budget)
    {
        if(!building.IsCompletedSuccessfully)return !building.IsCompleted;
        var regions=building.Result;
        for(int i=0;i<budget&&next<regions.Count;i++,next++)
        {
            var (region,triangles)=regions[next];
            hud.Occluder(region.X,region.Y,region.Z,triangles);
        }
        return next<regions.Count;
    }
}
