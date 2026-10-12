using System.Runtime.Versioning;
using CS2Craft.Bridge;
[assembly: SupportedOSPlatform("windows")]
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
int seconds = args.Length > 0 ? int.Parse(args[0]) : 90;
string output = Path.GetFullPath(args.Length > 1 ? args[1] : "dist/probe");
Directory.CreateDirectory(output);
using var link = new SharedBridge();
uint epoch=1;
var floor = new List<(uint Type,byte[] Payload)>();
for(int rz=-1;rz<=1;rz++) for(int rx=-1;rx<=1;rx++) for(int ry=-1;ry<=0;ry++)
{
    var blocks = new List<CollisionBlock>(); var triangles=new List<Triangle>();
    if(ry==-1)
    {
        for(int z=rz*8;z<rz*8+8;z++) for(int x=rx*8;x<rx*8+8;x++)
            blocks.Add(new(x,-1,z,Enumerable.Repeat(ulong.MaxValue,8).ToArray()));
        int x0=rx*8,z0=rz*8,x1=x0+8,z1=z0+8;
        triangles.Add(new(new(x0,0,z0),new(x1,0,z0),new(x1,0,z1)));
        triangles.Add(new(new(x0,0,z0),new(x1,0,z1),new(x0,0,z1)));
    }
    floor.Add((3,CollisionRegion.Triangles(rx,ry,rz,epoch,triangles)));
    floor.Add((2,CollisionRegion.Blocks(rx,ry,rz,epoch,blocks)));
}
link.PushCollision(1,BitConverter.GetBytes(epoch));
foreach(var (type,payload) in floor) if(!link.PushCollision(type,payload)) throw new Exception("Fixture collision ring full.");
link.PublishActors([new(0x1234,1,new(2,0,0),90,0.8f,1.8f,1,"Bridge test target")]);
var end = DateTime.UtcNow.AddSeconds(seconds);
long frameCount=0, renderCount=0, eventCount=0, lastFrame=0;
int front=2;
DateTime lastReport=DateTime.MinValue, readyAt=DateTime.MaxValue;
bool swordSelected=false,pressed=false,released=false,inventory=false,selected=false,placed=false,placeReleased=false,breaking=false,breakReleased=false;
var capture=new RenderCapture();bool captured=false;
long nonemptySections=0,emptySections=0; float yaw=-90,pitch=0;
Console.CancelKeyPress += (_, e)=>{e.Cancel=true; end=DateTime.UtcNow;};
Console.WriteLine("Fixture host ready. Start the CS2Craft Minecraft dev client; this is NOT a CS2 map.");
while(DateTime.UtcNow < end)
{
    link.Heartbeat();
    link.Publish(new(Protocol.HostInGame,0x435332,epoch,new(0,0,0),yaw,pitch,1,1280,720));
    if(link.MinecraftAlive && link.TryReadPlayer(out var state) && (state.Flags & Protocol.McInWorld)!=0)
    {
        if(readyAt==DateTime.MaxValue) readyAt=DateTime.UtcNow;
        double elapsed=(DateTime.UtcNow-readyAt).TotalSeconds;
        if(elapsed>2 && !swordSelected){link.PushInput(new(1,30,1));link.PushInput(new(1,30,0));swordSelected=true;}
        if(elapsed>5 && !pressed) { link.PushInput(new(2,1,1)); pressed=true; }
        if(elapsed>5.2 && !released) {link.PushInput(new(2,1,0));released=true;}
        if(elapsed>7 && !selected) {link.PushInput(new(1,34,1));link.PushInput(new(1,34,0));yaw=90;pitch=35;selected=true;}
        if(elapsed>8 && !placed) {link.PushInput(new(2,3,1));placed=true;}
        if(elapsed>8.15 && !placeReleased) {link.PushInput(new(2,3,0));placeReleased=true;}
        if(elapsed>10 && !breaking) {link.PushInput(new(2,1,1));breaking=true;}
        if(elapsed>16 && !breakReleased) {link.PushInput(new(2,1,0));breakReleased=true;}
        if(elapsed>18 && !inventory) {link.PushInput(new(1,8,1));link.PushInput(new(1,8,0));inventory=true;}
        lastFrame=(long)state.Frame;
        if(DateTime.UtcNow-lastReport>TimeSpan.FromSeconds(2))
        {
            Console.WriteLine($"MC frame={state.Frame}, pos={state.Position}, ack={state.TeleportAck}, renderPackets={renderCount}, events={eventCount}");
            lastReport=DateTime.UtcNow;
        }
    }
    for(int i=0;i<256 && link.TryReadRender(out var packet);i++)
    {
        renderCount++;capture.Accept(packet);
        if(packet.Type==2 && packet.Payload.Length>=16)
        {
            if(BitConverter.ToUInt32(packet.Payload,12)>0)
            {
                nonemptySections++;
                if(placed && !breaking && !captured && link.TryReadPlayer(out var player))
                {capture.Save(Path.Combine(output,"minecraft-world.c2rf"),player,yaw,pitch);captured=true;}
            }
            else emptySections++;
        }
        if(renderCount<=10) Console.WriteLine($"Render type={packet.Type}, bytes={packet.Payload.Length}");
    }
    while(link.TryReadEvent(out var ev)) {eventCount++;Console.WriteLine($"Event type={ev.Type} target={ev.ActorId:X} amount={ev.A}");}
    var rgba=link.ReadOverlay(ref front,out int width,out int height,out bool bottomUp);
    if(rgba is not null)
    {
        frameCount++;
        if(frameCount==1 || frameCount%120==0) SaveTga(Path.Combine(output,"minecraft-overlay.tga"),width,height,rgba,bottomUp);
    }
    Thread.Sleep(16);
}
File.WriteAllText(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{
    fixture=true, minecraftConnected=lastFrame>0, lastFrame,overlayFrames=frameCount,renderPackets=renderCount,events=eventCount,nonemptySections,emptySections,worldCapture=captured
},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Finished: {frameCount} overlay frames, {renderCount} render packets, {eventCount} events.");
return lastFrame>0 && frameCount>0 ? 0 : 1;

static void SaveTga(string path,int width,int height,byte[] rgba,bool bottomUp)
{
    using var w = new BinaryWriter(File.Create(path));
    byte[] header=new byte[18]; header[2]=2; header[12]=(byte)width; header[13]=(byte)(width>>8);
    header[14]=(byte)height; header[15]=(byte)(height>>8); header[16]=32; header[17]=(byte)(bottomUp?8:40);
    w.Write(header);
    for(int i=0;i<rgba.Length;i+=4) {w.Write(rgba[i+2]);w.Write(rgba[i+1]);w.Write(rgba[i]);w.Write(rgba[i+3]);}
}


// Retains only the latest world resources; captures no game binaries or account information.
sealed class RenderCapture
{
    readonly Dictionary<uint,Packet> textures=new();
    readonly Dictionary<(int,int,int),Packet> sections=new();
    Packet? atlas,scene;
    public void Accept(Packet p)
    {
        if(p.Type==3){atlas=null;scene=null;textures.Clear();sections.Clear();}
        else if(p.Type==1)atlas=p;
        else if(p.Type==4 && p.Payload.Length>=16)textures[BitConverter.ToUInt32(p.Payload,0)]=p;
        else if(p.Type==6)scene=p;
        else if(p.Type==2 && p.Payload.Length>=16)
        {
            var key=(BitConverter.ToInt32(p.Payload,0),BitConverter.ToInt32(p.Payload,4),BitConverter.ToInt32(p.Payload,8));
            if(BitConverter.ToUInt32(p.Payload,12)==0)sections.Remove(key);else sections[key]=p;
        }
        else if(p.Type==7 && p.Payload.Length>=16 && atlas is { } a)
        {
            uint x=BitConverter.ToUInt32(p.Payload,0),y=BitConverter.ToUInt32(p.Payload,4),
                w=BitConverter.ToUInt32(p.Payload,8),h=BitConverter.ToUInt32(p.Payload,12);
            uint aw=BitConverter.ToUInt32(a.Payload,0),ah=BitConverter.ToUInt32(a.Payload,4);
            if(x>aw||y>ah||w>aw-x||h>ah-y||(ulong)w*h*4!=(ulong)p.Payload.Length-16)return;
            for(uint row=0;row<h;row++)
                Buffer.BlockCopy(p.Payload,checked(16+(int)(row*w*4)),a.Payload,checked(8+(int)(((y+row)*aw+x)*4)),checked((int)(w*4)));
        }
    }
    public void Save(string path,PlayerState player,float yaw,float pitch)
    {
        using var writer=new BinaryWriter(File.Create(path));
        writer.Write(0x46523243u);
        writer.Write(player.Position.X);writer.Write(player.Position.Y+player.EyeHeight);writer.Write(player.Position.Z);
        writer.Write(yaw);writer.Write(pitch);writer.Write(player.VerticalFov);
        void Write(Packet p){writer.Write(p.Type);writer.Write(p.Payload.Length);writer.Write(p.Payload);}
        if(atlas is { } a)Write(a);
        foreach(var p in textures.Values)Write(p);
        foreach(var p in sections.Values)Write(p);
        if(scene is { } s)Write(s);
        Console.WriteLine($"Captured {sections.Count} placed-block sections for native GPU replay: {path}");
    }
}
