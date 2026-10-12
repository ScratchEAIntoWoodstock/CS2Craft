using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using System.Numerics;
using CS2Craft.Bridge;
using var pak = new Package();
pak.Read(args[0]);
var entry = pak.Entries!.SelectMany(p => p.Value).First(e => e.GetFullPath().Contains("world_physics"));
pak.ReadEntry(entry, out var bytes);
using var resource = new Resource { FileName = entry.GetFullPath() };
resource.Read(new MemoryStream(bytes));
var phys = (PhysAggregateData)(resource.DataBlock is PhysAggregateData d ? d : resource.GetBlockByType(BlockType.PHYS)!);

var triangles=new List<CS2Craft.Bridge.Triangle>();
uint? Layer(int index)
{
    var data=phys.CollisionAttributes[index];
    var tags=data.GetArray<string>("m_InteractAsStrings") ?? data.GetArray<string>("m_PhysicsTagStrings") ?? [];
    if(tags.Contains("sky")||tags.Contains("csgo_grenadeclip"))return null;
    if(tags.Length==0||tags.Contains("solid")||tags.Contains("passbullets"))return 0;
    if(tags.Contains("playerclip"))return 2; // Collides, but is not visible or a placement surface.
    return null;
}
Point Convert(Vector3 v)=>Coordinates.ToMinecraft(new(v.X,v.Y,v.Z));
void Add(Vector3 a,Vector3 b,Vector3 c,uint flags)
{
    if(Vector3.Cross(b-a,c-a).LengthSquared()>1e-8)
        triangles.Add(new(Convert(a),Convert(b),Convert(c),flags));
}
foreach(var part in phys.Parts)
{
    foreach(var desc in part.Shape.Meshes)
    {
        if(Layer(desc.CollisionAttributeIndex) is not { } flags)continue;
        var vertices=desc.Shape.GetVertices();
        foreach(var t in desc.Shape.GetTriangles())Add(vertices[t.X],vertices[t.Y],vertices[t.Z],flags);
    }
    foreach(var desc in part.Shape.Hulls)
    {
        if(Layer(desc.CollisionAttributeIndex) is not { } flags)continue;
        var vertices=desc.Shape.GetVertexPositions();var edges=desc.Shape.GetEdges();
        foreach(var face in desc.Shape.GetFaces())
        {
            int first=face.Edge,e=edges[first].Next;
            for(int guard=0;guard<edges.Length;guard++)
            {
                int next=edges[e].Next;if(next==first)break;
                Add(vertices[edges[first].Origin],vertices[edges[e].Origin],vertices[edges[next].Origin],flags);
                e=next;
            }
        }
    }
}
if(triangles.Count==0)throw new InvalidDataException("Map has no supported collision geometry.");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
string temporary=args[1]+".tmp";
using(var w=new BinaryWriter(File.Create(temporary)))
{
    w.Write(0x3243324du);w.Write(1);w.Write(triangles.Count);
    foreach(var t in triangles)
    {
        foreach(var p in new[]{t.A,t.B,t.C}){w.Write((float)p.X);w.Write((float)p.Y);w.Write((float)p.Z);}
        w.Write(t.Flags);
    }
}
File.Move(temporary,args[1],true);
Console.WriteLine($"Exported {triangles.Count:N0} map triangles ({triangles.Count(t=>t.Flags==2):N0} player-clip triangles): {args[1]}");
