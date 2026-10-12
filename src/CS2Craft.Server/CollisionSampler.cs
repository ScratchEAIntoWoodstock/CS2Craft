using CS2Craft.Bridge;
using CounterStrikeSharp.API.Core;
using System.Runtime.Versioning;
namespace CS2Craft.Server;
/// <summary>Streams locally extracted map physics, without whole-block hull approximation.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CollisionSampler
{
    readonly SharedBridge bridge;
    readonly uint epoch;
    readonly Action<int,int,int,IReadOnlyList<Triangle>>? completed;
    readonly Task<MapGeometry> loading;
    readonly HashSet<(int X,int Y,int Z)> done=new();
    Task<(List<Triangle> Triangles,List<CollisionBlock> Blocks)>? preparing;
    (int X,int Y,int Z) region;
    byte[]? trisPacket,blocksPacket;
    bool trisSent;
    public int Completed=>done.Count;
    public CollisionSampler(SharedBridge bridge,uint epoch,string path,Action<int,int,int,IReadOnlyList<Triangle>>? completed=null)
    {
        this.bridge=bridge;this.epoch=epoch;this.completed=completed;
        loading=Task.Run(()=>MapGeometry.Load(path));
    }
    public (int Compared,int Missing,double MaxErrorUnits) VerifyFloors(Point center,CCSPlayerPawn pawn)
    {
        if(!loading.IsCompletedSuccessfully)return (0,81,0);
        var map=loading.Result;int count=0,missing=0;double error=0;
        for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)
        {
            Point a=new(center.X+x,center.Y+2,center.Z+z),b=new(a.X,a.Y-8,a.Z);
            var sa=Coordinates.ToSource(a);var sb=Coordinates.ToSource(b);
            var hit=CounterStrikeSharp.API.Modules.Utils.Trace.TraceEndShape(
                new((float)sa.X,(float)sa.Y,(float)sa.Z),new((float)sb.X,(float)sb.Y,(float)sb.Z),pawn,
                new(){InteractsWith=CounterStrikeSharp.API.Modules.Utils.Masks.PlayerSolidBrushOnly});
            if(hit.IsAllSolid)continue;
            var fraction=map.Raycast(a,b);
            if(hit.DidHit() && fraction is {} f){count++;error=Math.Max(error,Math.Abs(f-hit.Fraction)*320);}
            else if(hit.DidHit() != (fraction is not null))missing++;
        }
        return (count,missing,error);
    }
    public bool Ready(Point p)
    {
        for(int y=(int)Math.Floor((p.Y-1)/8);y<=(int)Math.Floor((p.Y+2)/8);y++)
        for(int z=(int)Math.Floor((p.Z-.4)/8);z<=(int)Math.Floor((p.Z+.4)/8);z++)
        for(int x=(int)Math.Floor((p.X-.4)/8);x<=(int)Math.Floor((p.X+.4)/8);x++)
            if(!done.Contains((x,y,z)))return false;
        return true;
    }
    public void Tick(Point center,CCSPlayerPawn ignore,int budget)
    {
        if(!loading.IsCompleted)return;
        var map=loading.GetAwaiter().GetResult();
        for(int sent=0;sent<4;sent++)
        {
            if(preparing is null)
            {
                int rx=(int)Math.Floor(center.X/8),ry=(int)Math.Floor(center.Y/8),rz=(int)Math.Floor(center.Z/8);
                var candidates=new List<((int X,int Y,int Z) Key,double Distance)>();
                for(int y=ry-2;y<=ry+2;y++)for(int z=rz-2;z<=rz+2;z++)for(int x=rx-2;x<=rx+2;x++)
                {
                    if(done.Contains((x,y,z)))continue;
                    double dx=Math.Max(0,Math.Abs(x*8+4-center.X)-4);
                    double dy=Math.Max(0,Math.Abs(y*8+4-center.Y)-4);
                    double dz=Math.Max(0,Math.Abs(z*8+4-center.Z)-4);
                    candidates.Add(((x,y,z),dx*dx+dy*dy+dz*dz));
                }
                if(candidates.Count==0)return;
                region=candidates.MinBy(c=>c.Distance).Key;
                var r=region;
                preparing=Task.Run(()=>
                {
                    var triangles=map.Regions.GetValueOrDefault(r)??new List<Triangle>();
                    return (triangles,MapGeometry.Voxelize(r.X,r.Y,r.Z,triangles));
                });
                trisPacket=blocksPacket=null;trisSent=false;
            }
            if(!preparing.IsCompleted)return;
            var data=preparing.GetAwaiter().GetResult();
            trisPacket??=CollisionRegion.Triangles(region.X,region.Y,region.Z,epoch,data.Triangles);
            blocksPacket??=CollisionRegion.Blocks(region.X,region.Y,region.Z,epoch,data.Blocks);
            if(!trisSent){if(!bridge.PushCollision(3,trisPacket))return;trisSent=true;}
            if(!bridge.PushCollision(2,blocksPacket))return;
            completed?.Invoke(region.X,region.Y,region.Z,data.Triangles.Where(t=>(t.Flags&2)==0).ToArray());
            done.Add(region);preparing=null;
        }
    }
}
