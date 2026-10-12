using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using CS2Craft.Bridge;

[assembly: SupportedOSPlatform("windows")]
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
int count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
void Throws<T>(Action action, string label) where T : Exception
{
    try { action(); } catch (T) { Check(true, label); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + label);
}
var look=new MouseLook();
look.Reset(0,0);look.Move(100,50,2);
Check(Math.Abs(look.Yaw-4.4f)<.001&&Math.Abs(look.Pitch-2.2f)<.001,"mouse right/down rotates Minecraft camera right/down");
look.Move(0,100000,2);Check(look.Pitch==89.9f,"mouse pitch clamps before camera flip");
look.Reset(179,0);look.Move(100,0,2);Check(look.Yaw<0,"mouse yaw wraps through a full turn");
var random = new Random(52);
for (int i=0; i<1000; i++)
{
    var p = new Point(random.NextDouble()*40000-20000,random.NextDouble()*40000-20000,random.NextDouble()*40000-20000);
    var q = Coordinates.ToSource(Coordinates.ToMinecraft(p));
    if (Math.Abs(p.X-q.X)>1e-8 || Math.Abs(p.Y-q.Y)>1e-8 || Math.Abs(p.Z-q.Z)>1e-8) throw new Exception("coordinate roundtrip");
}
Check(true, "1000 Source/Minecraft position roundtrips including negative coordinates");
Check(Coordinates.ToMinecraft(new(40,80,120)) == new Point(1,3,-2), "axis handedness and 40-unit scale");
Check(Coordinates.Yaw(0)==-90 && Coordinates.Yaw(90)==-180 && Coordinates.Yaw(180)==90 && Coordinates.Yaw(-90)==0, "four cardinal look directions");
for(int angle=-1080;angle<=1080;angle++) if (Math.Abs(Coordinates.Wrap(Coordinates.Yaw(Coordinates.Yaw(angle))-angle))>0.001) throw new Exception("yaw roundtrip");
Check(true,"yaw roundtrips beyond one revolution");
string name = @"Local\CS2Craft_test_" + Guid.NewGuid().ToString("N");
using var bridge = new SharedBridge(name);
Throws<IOException>(() => { using var other = new SharedBridge(name); }, "second host cannot overwrite live mapping");
Check(bridge.Read<uint>(0)==Protocol.Magic && bridge.Read<uint>(4)==10, "pinned protocol v10 header");
Check(Protocol.MappingBytes==200327168, "cross-language total mapped size");
Check(bridge.Read<float>(Protocol.Water+16)==-1e30f && bridge.Read<float>(Protocol.Water+16+255*4)==-1e30f,"unconfigured water is absent");
var host = new HostState(1, 77, 4, new(-12,7,3), -90, 20, 12);
bridge.Publish(host);
Check(bridge.Read<double>(Protocol.Host+16)==-12 && bridge.Read<uint>(Protocol.Host+48)==12 && (bridge.Atomic32(Protocol.Host)&1)==0,"host seqlock layout");
bridge.Write(Protocol.Player+4, 1u);
bridge.Write(Protocol.Player+8, 17d); bridge.Write(Protocol.Player+16, 23d); bridge.Write(Protocol.Player+24, -4d);
bridge.Write(Protocol.Player+48, 12u); bridge.Atomic32(Protocol.Player, 2);
Check(bridge.TryReadPlayer(out var player) && player.Position==new Point(17,23,-4) && player.TeleportAck==12,"Minecraft player layout");
bridge.Atomic32(Protocol.Player,3);
Check(!bridge.TryReadPlayer(out _),"torn player publication rejected");
bridge.Atomic32(Protocol.Player,4); bridge.Write(Protocol.Player+8, double.NaN);
Check(!bridge.TryReadPlayer(out _),"non-finite player position rejected");
bridge.Write(Protocol.Player+8,17d);
bridge.PublishActors([new(0x123456,1,new(1,2,3),-90,0.8f,1.8f,0.5f,"Bot 🙂🙂🙂🙂🙂🙂")]);
Check(bridge.Read<uint>(Protocol.Actors+4)==1 && bridge.Read<uint>(Protocol.Actors+64)==0x123456 && bridge.Read<byte>(Protocol.Actors+64+63)==0,"actor table and bounded UTF-8 name");
for(int i=0;i<Protocol.InputCapacity;i++) if(!bridge.PushInput(new(1,26,i))) throw new Exception("input filled early");
Check(!bridge.PushInput(new(1,4,1)),"full input ring applies backpressure");
bridge.Atomic64(Protocol.Input+64,1);
Check(bridge.PushInput(new(1,4,777)) && bridge.Read<int>(Protocol.Input+128+4)==777,"input ring wraps without overwriting unread events");
bridge.Write(Protocol.Events+128,1u); bridge.Write(Protocol.Events+132,0x123456u); bridge.Write(Protocol.Events+136,7f);
bridge.Atomic64(Protocol.Events,1);
Check(bridge.TryReadEvent(out var e) && e.ActorId==0x123456 && e.A==7 && !bridge.TryReadEvent(out _),"combat event drains exactly once");
bridge.Write(Protocol.Events+128+32,2u); // Death from the previous life.
bridge.Atomic64(Protocol.Events,2);
bridge.Atomic32(Protocol.Player,5); // Re-link frame cannot read a consistent player snapshot.
Check(!bridge.TryReadPlayer(out _),"respawn can encounter an unreadable player snapshot");
bridge.Atomic32(Protocol.Player,6);
bridge.DiscardEvents(); // Drive-mode barrier must run even after that missed frame.
Check(!bridge.TryReadEvent(out _),"previous-life death is discarded before driving resumes");
bridge.Write(Protocol.Events+128+64,1u);bridge.Atomic64(Protocol.Events,3);
Check(bridge.TryReadEvent(out var freshEvent)&&freshEvent.Type==1,"events published after respawn barrier remain readable");
const long ring=0x900; const int bytes=192; // 64 bytes of data, exercises padding cheaply.
Check(bridge.PushPacket(ring,bytes,2,new byte[32]),"byte-ring first packet");
Check(bridge.TryReadPacket(ring,bytes,out var pkt) && pkt.Payload.Length==32,"byte-ring first read");
Check(bridge.PushPacket(ring,bytes,3,new byte[24]),"byte-ring padding and wrap");
Check(bridge.TryReadPacket(ring,bytes,out pkt) && pkt.Type==3 && bridge.Atomic64(ring+64)==96,"reader skips padding");
bridge.Atomic64(ring+64,0);
Throws<InvalidDataException>(() => bridge.TryReadPacket(ring,bytes,out _),"corrupt counters rejected");
bridge.Atomic64(ring,0); bridge.Atomic64(ring+64,0); bridge.Write(ring+128,2u); bridge.Write(ring+132,uint.MaxValue); bridge.Atomic64(ring,8);
Throws<InvalidDataException>(() => bridge.TryReadPacket(ring,bytes,out _),"oversized packet rejected before allocation");
var block=CollisionRegion.Blocks(-1,2,3,7,[new(-8,16,24,Enumerable.Repeat(ulong.MaxValue,8).ToArray())]);
Check(block.Length==112 && BitConverter.ToInt32(block,0)==-8 && BitConverter.ToUInt32(block,24)==7 && BitConverter.ToInt32(block,28)==1,"collision region encoding including negative regions");
var tri=CollisionRegion.Triangles(0,0,0,7,[new(new(0,0,0),new(1,0,0),new(1,0,1))]);
Check(tri.Length==72 && BitConverter.ToSingle(tri,44)==1,"triangle wire layout");
bridge.Write(Protocol.OverlayHeaders+64,1); bridge.Write(Protocol.OverlayHeaders+68,1);
bridge.WriteBytes(Protocol.Pixels+Protocol.OverlaySlotBytes,new byte[]{1,2,3,255});
bridge.Atomic32(Protocol.Overlay,5);
int front=2;
var frame=bridge.ReadOverlay(ref front,out int w,out int h,out _);
Check(front==1 && w==1 && h==1 && frame!.SequenceEqual(new byte[]{1,2,3,255}) && bridge.Atomic32(Protocol.Overlay)==2,"overlay triple-buffer ownership transfer");
Throws<ArgumentOutOfRangeException>(()=>bridge.Read<long>(Protocol.MappingBytes-1),"out-of-bounds access rejected");
var map=new MapGeometry();
var floor=new Triangle(new(-1,0.03,-1),new(1,0.03,-1),new(1,0.03,1));
map.Add(floor);
Check(map.Regions.ContainsKey((-1,0,-1)) && map.Regions.ContainsKey((0,0,0)),"exact triangle indexed across negative region boundaries");
Check(MapGeometry.Overlaps(new(-1,.03f,-1),new(1,.03f,-1),new(1,.03f,1),new(0,.03f,0),new(.01f)),"triangle SAT intersects actual floor");
Check(!MapGeometry.Overlaps(new(-1,.03f,-1),new(1,.03f,-1),new(1,.03f,1),new(0,.3f,0),new(.1f)),"triangle SAT rejects empty air");
var vox=MapGeometry.Voxelize(0,0,0,[floor]);
Check(vox.Count>0&&vox.All(b=>b.Layers.Skip(1).All(n=>n==0)),"voxel support stays confined to the actual surface layer");
var clipPacket=CollisionRegion.Triangles(0,0,0,1,[floor with {Flags=2}]);
Check(BitConverter.ToUInt32(clipPacket,68)==2,"invisible player-clip flag survives the wire");
Console.WriteLine($"All {count} bridge checks passed.");
