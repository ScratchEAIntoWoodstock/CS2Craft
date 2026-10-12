using System.Numerics;
namespace CS2Craft.Bridge;

// Cache contains only geometry derived locally from the user's installed map.
public sealed class MapGeometry
{
    public readonly Dictionary<(int X,int Y,int Z),List<Triangle>> Regions = new();
    public int TriangleCount { get; private set; }
    public void Add(Triangle triangle)
    {
        var a=V(triangle.A);var b=V(triangle.B);var c=V(triangle.C);
        if(!triangle.A.IsFinite||!triangle.B.IsFinite||!triangle.C.IsFinite
            ||Vector3.Cross(b-a,c-a).LengthSquared()<1e-12f)return;
        var lo=Vector3.Min(a,Vector3.Min(b,c));var hi=Vector3.Max(a,Vector3.Max(b,c));
        if(Vector3.Max(Vector3.Abs(lo),Vector3.Abs(hi)).Length()>20000)throw new InvalidDataException("Map bounds exceed supported range.");
        for(int y=Floor((lo.Y-.001)/8);y<=Floor((hi.Y+.001)/8);y++)
        for(int z=Floor((lo.Z-.001)/8);z<=Floor((hi.Z+.001)/8);z++)
        for(int x=Floor((lo.X-.001)/8);x<=Floor((hi.X+.001)/8);x++)
        {
            if(!Overlaps(a,b,c,new(x*8+4,y*8+4,z*8+4),new(4.001f)))continue;
            if(!Regions.TryGetValue((x,y,z),out var list))Regions[(x,y,z)]=list=new();
            list.Add(triangle);
        }
        TriangleCount++;
    }
    public static MapGeometry Load(string path)
    {
        using var r=new BinaryReader(File.OpenRead(path));
        if(r.ReadUInt32()!=0x3243324d || r.ReadInt32()!=1)throw new InvalidDataException("Unsupported map collision cache.");
        int n=r.ReadInt32();if(n<0||n>5_000_000||r.BaseStream.Length!=12L+n*40L)throw new InvalidDataException("Invalid map collision cache size.");
        var map=new MapGeometry();
        Point Read()=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
        for(int i=0;i<n;i++)map.Add(new(Read(),Read(),Read(),r.ReadUInt32()));
        return map;
    }
    public double? Raycast(Point from,Point to)
    {
        var o=V(from);var d=V(to)-o;var end=V(to);
        var lo=Vector3.Min(o,end);var hi=Vector3.Max(o,end);
        float best=float.PositiveInfinity;
        for(int y=Floor(lo.Y/8);y<=Floor(hi.Y/8);y++)
        for(int z=Floor(lo.Z/8);z<=Floor(hi.Z/8);z++)
        for(int x=Floor(lo.X/8);x<=Floor(hi.X/8);x++)
        {
            if(!Regions.TryGetValue((x,y,z),out var triangles))continue;
            foreach(var triangle in triangles)
            {
                var a=V(triangle.A);var u=V(triangle.B)-a;var v=V(triangle.C)-a;
                var p=Vector3.Cross(d,v);float det=Vector3.Dot(u,p);
                if(Math.Abs(det)<1e-8)continue;
                var offset=o-a;float s=Vector3.Dot(offset,p)/det;
                if(s< -1e-5||s>1.00001)continue;
                var q=Vector3.Cross(offset,u);float t=Vector3.Dot(d,q)/det;
                if(t< -1e-5||s+t>1.00001)continue;
                float distance=Vector3.Dot(v,q)/det;
                if(distance>=0&&distance<=1&&distance<best)best=distance;
            }
        }
        return float.IsFinite(best)?best:null;
    }
    public static List<CollisionBlock> Voxelize(int rx,int ry,int rz,IReadOnlyList<Triangle> triangles)
    {
        var cells=new Dictionary<(int,int,int),ulong[]>();
        Vector3 regionMin=new(rx*64,ry*64,rz*64),regionMax=regionMin+new Vector3(63);
        foreach(var t in triangles)
        {
            var a=V(t.A)*8;var b=V(t.B)*8;var c=V(t.C)*8;
            var lo=Vector3.Max(regionMin,FloorV(Vector3.Min(a,Vector3.Min(b,c))-new Vector3(.001f)));
            var hi=Vector3.Min(regionMax,FloorV(Vector3.Max(a,Vector3.Max(b,c))+new Vector3(.001f)));
            var n=Vector3.Cross(b-a,c-a);
            int axis=Math.Abs(n.X)>Math.Abs(n.Y)?0:1;if(Math.Abs(n.Z)>Math.Abs(n[axis]))axis=2;
            int u=(axis+1)%3,v=(axis+2)%3;
            // Project onto the dominant plane, then examine only voxels touched by the surface.
            for(int i=(int)lo[u];i<=hi[u];i++)for(int j=(int)lo[v];j<=hi[v];j++)
            {
                float h=a[axis]-(n[u]*(i+.5f-a[u])+n[v]*(j+.5f-a[v]))/n[axis];
                float margin=.501f*(1+(Math.Abs(n[u])+Math.Abs(n[v]))/Math.Abs(n[axis]));
                int lower=Math.Max((int)lo[axis],Floor(h-margin)),upper=Math.Min((int)hi[axis],Floor(h+margin));
                for(int k=lower;k<=upper;k++)
                {
                    Vector3 center=default;center[u]=i+.5f;center[v]=j+.5f;center[axis]=k+.5f;
                    if(!Overlaps(a,b,c,center,new(.501f)))continue;
                    int sx=Floor(center.X),sy=Floor(center.Y),sz=Floor(center.Z);
                    var key=(Floor(sx/8.0),Floor(sy/8.0),Floor(sz/8.0));
                    if(!cells.TryGetValue(key,out var layers))cells[key]=layers=new ulong[8];
                    layers[sy&7]|=1UL<<((sx&7)+8*(sz&7));
                }
            }
        }
        return cells.Select(c=>new CollisionBlock(c.Key.Item1,c.Key.Item2,c.Key.Item3,c.Value)).ToList();
    }
    public static bool Overlaps(Vector3 a,Vector3 b,Vector3 c,Vector3 center,Vector3 half)
    {
        a-=center;b-=center;c-=center;
        var min=Vector3.Min(a,Vector3.Min(b,c));var max=Vector3.Max(a,Vector3.Max(b,c));
        if(min.X>half.X||min.Y>half.Y||min.Z>half.Z||max.X< -half.X||max.Y< -half.Y||max.Z< -half.Z)return false;
        var ab=b-a;var bc=c-b;var ca=a-c;
        if(Separated(Vector3.Cross(ab,bc),a,b,c,half))return false;
        return !Edge(ab,a,b,c,half)&&!Edge(bc,a,b,c,half)&&!Edge(ca,a,b,c,half);
    }
    static bool Edge(Vector3 e,Vector3 a,Vector3 b,Vector3 c,Vector3 h)
        =>Separated(new(0,e.Z,-e.Y),a,b,c,h)||Separated(new(-e.Z,0,e.X),a,b,c,h)||Separated(new(e.Y,-e.X,0),a,b,c,h);
    static bool Separated(Vector3 axis,Vector3 a,Vector3 b,Vector3 c,Vector3 half)
    {
        float x=Vector3.Dot(a,axis),y=Vector3.Dot(b,axis),z=Vector3.Dot(c,axis),r=Vector3.Dot(Vector3.Abs(axis),half);
        return Math.Min(x,Math.Min(y,z))>r||Math.Max(x,Math.Max(y,z))< -r;
    }
    static Vector3 FloorV(Vector3 v)=>new(MathF.Floor(v.X),MathF.Floor(v.Y),MathF.Floor(v.Z));
    static Vector3 V(Point p)=>new((float)p.X,(float)p.Y,(float)p.Z);
    static int Floor(double value)=>(int)Math.Floor(value);
}

