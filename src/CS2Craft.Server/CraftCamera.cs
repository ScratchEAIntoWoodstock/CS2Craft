using CS2Craft.Bridge;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
namespace CS2Craft.Server;
internal sealed class CraftCamera
{
    CDynamicProp? entity;
    CCSPlayerPawn? pawn;
    uint originalView;
    readonly Dictionary<uint,(CBasePlayerWeapon Weapon,RenderMode_t Mode,System.Drawing.Color Color)> weapons=new();
    RenderMode_t originalRender;
    System.Drawing.Color originalColor;
    public Point Eye {get;private set;}
    public float Yaw {get;private set;}
    public float Pitch {get;private set;}
    public void Update(CCSPlayerPawn target,PlayerState mc,float yaw,float pitch)
    {
        if(entity is {IsValid:false})Restore();
        if(entity is null)
        {
            var created=Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if(created is null)throw new InvalidOperationException("Could not create Minecraft camera.");
            created.Spawnflags=256;
            created.DispatchSpawn();
            if(created.CBodyComponent?.SceneNode?.GetSkeletonInstance() is {} skeleton) skeleton.Scale=0;
            created.RenderMode=RenderMode_t.kRenderTransAlpha;
            created.Render=System.Drawing.Color.FromArgb(0,255,255,255);
            Utilities.SetStateChanged(created,"CBaseEntity","m_CBodyComponent");
            Utilities.SetStateChanged(created,"CBaseModelEntity","m_clrRender");
            entity=created;pawn=target;
            originalView=target.CameraServices!.ViewEntity.Raw;
            originalRender=target.RenderMode;
            originalColor=target.Render;
            target.RenderMode=RenderMode_t.kRenderTransAlpha;
            target.Render=System.Drawing.Color.FromArgb(0,255,255,255);
            Utilities.SetStateChanged(target,"CBaseModelEntity","m_clrRender");
            Utilities.SetStateChanged(target,"CBaseModelEntity","m_nRenderMode");
            target.CameraServices.ViewEntity.Raw=entity.EntityHandle.Raw;
            Utilities.SetStateChanged(target,"CBasePlayerPawn","m_pCameraServices");
        }
        if(target.WeaponServices is {} service)
            foreach(var handle in service.MyWeapons)
                if(handle.Value is {IsValid:true} weapon && !weapons.ContainsKey(handle.Raw))
                {
                    weapons[handle.Raw]=(weapon,weapon.RenderMode,weapon.Render);
                    weapon.RenderMode=RenderMode_t.kRenderTransAlpha;
                    weapon.Render=System.Drawing.Color.FromArgb(0,255,255,255);
                    Utilities.SetStateChanged(weapon,"CBaseModelEntity","m_nRenderMode");
                    Utilities.SetStateChanged(weapon,"CBaseModelEntity","m_clrRender");
                }
        Eye=new(mc.Position.X,mc.Position.Y+Math.Clamp(mc.EyeHeight,0.2f,2f),mc.Position.Z);
        Yaw=yaw;Pitch=pitch;
        if(mc.CameraMode is 1 or 2)
        {
            double distance=Math.Clamp(mc.CameraDistance,0,4);
            double a=yaw*Math.PI/180,b=pitch*Math.PI/180,sign=mc.CameraMode==1?-1:1;
            var desired=new Point(Eye.X-sign*Math.Sin(a)*Math.Cos(b)*distance,
                Eye.Y-sign*Math.Sin(b)*distance,Eye.Z+sign*Math.Cos(a)*Math.Cos(b)*distance);
            var start=Coordinates.ToSource(Eye);var end=Coordinates.ToSource(desired);
            var trace=Trace.TraceHullShape(new((float)start.X,(float)start.Y,(float)start.Z),
                new((float)end.X,(float)end.Y,(float)end.Z),new(-4,-4,-4),new(4,4,4),target,
                new(){InteractsWith=Masks.PlayerSolidBrushOnly});
            double fraction=trace.IsAllSolid?0:Math.Max(0,trace.Fraction-.01);
            Eye=new(Eye.X+(desired.X-Eye.X)*fraction,Eye.Y+(desired.Y-Eye.Y)*fraction,Eye.Z+(desired.Z-Eye.Z)*fraction);
            if(mc.CameraMode==2){Yaw=Coordinates.Wrap(yaw+180);Pitch=-pitch;}
        }
        var p=Coordinates.ToSource(Eye);
        entity!.Teleport(new Vector((float)p.X,(float)p.Y,(float)p.Z),new QAngle(Pitch,Coordinates.Yaw(Yaw),0),new Vector(0,0,0));
    }
    public void Restore()
    {
        foreach(var saved in weapons.Values)if(saved.Weapon.IsValid)
        {
            saved.Weapon.RenderMode=saved.Mode;saved.Weapon.Render=saved.Color;
            Utilities.SetStateChanged(saved.Weapon,"CBaseModelEntity","m_nRenderMode");
            Utilities.SetStateChanged(saved.Weapon,"CBaseModelEntity","m_clrRender");
        }
        weapons.Clear();
        if(pawn?.IsValid==true)
        {
            if(pawn.CameraServices is {} services){services.ViewEntity.Raw=originalView;Utilities.SetStateChanged(pawn,"CBasePlayerPawn","m_pCameraServices");}
            pawn.RenderMode=originalRender;pawn.Render=originalColor;
            Utilities.SetStateChanged(pawn,"CBaseModelEntity","m_clrRender");
            Utilities.SetStateChanged(pawn,"CBaseModelEntity","m_nRenderMode");
        }
        if(entity?.IsValid==true)entity.Remove();
        pawn=null;entity=null;
    }
}
