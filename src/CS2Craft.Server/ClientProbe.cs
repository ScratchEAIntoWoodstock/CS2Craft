using CS2Craft.Bridge;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CS2Craft.Server;

// Runs only with the explicit -cs2craft-probe launch option in the private -insecure session.
// No desktop automation: joins the sole local player through the supported server API.
public sealed partial class CS2CraftPlugin
{
    readonly bool clientProbe = Environment.GetCommandLineArgs().Contains("-cs2craft-probe");
    readonly bool inputProbe=Environment.GetCommandLineArgs().Contains("-cs2craft-input-probe");
    int sourceShots,probeClip=-1;
    bool probeHurt,probeHurtMeasured,probeReverseAttacker,probeLook;
    float probeLookStart,probeLookEnd;
    double probeAwayPeak;
    Point probeHurtStart,probeHurtEnd;
    bool probeInventory,probePickup,probePickupUp,probeDrop,probeDropUp,probeInventoryCapture,probeInventoryClose,probeInventoryObserved;
    int probeTicks;
    bool probeClearBots;
    bool probeCrouch,probeCrouchCapture,probeUncrouch,probeStand,probeThird,probeThirdCapture,probeFront,probeFrontCapture,probeFirst;
    float probeStandingEye,probeCrouchingEye,probeStandingAgain;
    double probeGroundGap;
    uint probeThirdMode;
    bool probeHiddenBody;
    long probeReady;
    bool probeDrive,probeMove,probeStop,probeTargetReady,probeSword,probeHit,probeHitRelease,probeCombatCapture,probeGameplayFinished;
    Point probeMoveStart,probeMoveEnd;
    CCSPlayerPawn? probeTarget;
    int probeTargetHealth;
    bool probeSelected,probePressed,probeReleased,probeCapture,probeFinished,probeTurned;
    void ProbeBeforeAttach()
    {
        if (probeGameplayFinished || bridge is not null || ++probeTicks%64!=0) return;
        var humans=Utilities.GetPlayers().Where(p=>p.IsValid&&!p.IsBot).ToArray();
        if(humans.Length!=1)
        {
            if(probeTicks%640==0)Logger.LogInformation("CS2Craft client probe waiting for one local human; found {Count}.",humans.Length);
            return;
        }
        var player=humans[0];
        if(player.TeamNum!=3){player.ChangeTeam(CsTeam.CounterTerrorist);return;}
        if(!player.PawnIsAlive){player.Respawn();return;}
        AttachCore(player,message=>Logger.LogInformation("CS2Craft client probe: {Message}",message));
    }
    void ProbeConnected(CCSPlayerPawn pawn)
    {
        if(probeGameplayFinished || bridge is null || !bridge.TryReadPlayer(out var mc) || (mc.Flags&Protocol.McInWorld)==0)return;
        if(probeReady==0)probeReady=Environment.TickCount64;
        double seconds=(Environment.TickCount64-probeReady)/1000.0;

        if(seconds>3&&!probeTurned)
        {
            pawn.Teleport(null,new QAngle(35,0,0),null);probeTurned=true;
            Logger.LogInformation("CS2Craft client probe camera: origin={Origin}, viewOffset={Eye}, renderer={Renderer}",host.Position,pawn.ViewOffset.Z,hud.Status);
        }
        if(seconds>5&&!probeSelected){bridge.PushInput(new(1,34,1));bridge.PushInput(new(1,34,0));probeSelected=true;}
        if(seconds>7&&!probePressed){bridge.PushInput(new(2,3,1));probePressed=true;}
        if(seconds>7.25&&!probeReleased){bridge.PushInput(new(2,3,0));probeReleased=true;}
        if(seconds>12&&!probeCapture&&hud.Frames>30)
        {hud.Capture(Path.Combine(ModuleDirectory,"client-probe.tga"));probeCapture=true;}
        if(seconds>18&&!probeDrive)
        {
            Config.ExperimentalMovement=true; // explicit test process only; never persisted to config.
            DriveCore(owner,message=>Logger.LogInformation("CS2Craft drive probe: {Message}",message));
            probeDrive=driving;
        }
        if(seconds>19&&probeDrive&&!probeMove)
        {probeMoveStart=host.Position;bridge.PushInput(new(1,7,1));probeMove=true;}
        if(seconds>21&&probeMove&&!probeStop){bridge.PushInput(new(1,7,0));probeStop=true;}
        if(seconds>22&&probeStop&&!probeTargetReady)
        {
            probeMoveEnd=host.Position;
            probeTarget=Utilities.GetPlayers().Where(p=>p.IsValid&&p.IsBot&&p.PawnIsAlive)
                .Select(p=>p.PlayerPawn.Value).FirstOrDefault(p=>p is {IsValid:true});
            if(probeTarget is not null && pawn.AbsOrigin is { } position)
            {
                probeTarget.Teleport(new Vector(position.X,position.Y+80,position.Z),null,new Vector(0,0,0));
                probeTargetHealth=probeTarget.Health;
                pawn.Teleport(null,new QAngle(0,90,0),null);
                look.Reset(-180,0);
                probeClip=pawn.WeaponServices?.ActiveWeapon.Value?.Clip1??-1;
                probeTargetReady=true;
            }
        }
        if(seconds>24&&probeTargetReady&&!probeSword){bridge.PushInput(new(1,30,1));bridge.PushInput(new(1,30,0));probeSword=true;}
        if(seconds>26&&probeSword&&!probeHit){bridge.PushInput(new(2,1,1));probeHit=true;}
        if(seconds>26.2&&probeHit&&!probeHitRelease){bridge.PushInput(new(2,1,0));probeHitRelease=true;}
        if(seconds>28&&probeHitRelease&&!probeCombatCapture)
        {hud.Capture(Path.Combine(ModuleDirectory,"client-combat.tga"));probeCombatCapture=true;}
        if(seconds>28.2&&probeTargetReady&&!probeReverseAttacker && pawn.AbsOrigin is {} hurtPos){
            // Push back into the corridor we just traversed, away from the wall at the walk endpoint.
            probeTarget!.MoveType=probeTarget.ActualMoveType=MoveType_t.MOVETYPE_NONE;
            probeTarget.Teleport(new Vector(hurtPos.X,hurtPos.Y-80,hurtPos.Z),null,new Vector(0,0,0));
            probeReverseAttacker=true;
        }
        if(seconds>29&&probeTargetReady&&!probeHurt){
            probeHurtStart=mc.Position;
            bridge.PushInput(new(7,1,500,unchecked((int)probeTarget!.EntityHandle.Raw)));
            probeHurt=true;
        }
        if(probeHurt&&!probeHurtMeasured)probeAwayPeak=Math.Max(probeAwayPeak,probeHurtStart.Z-mc.Position.Z);
        if(seconds>31&&probeHurt&&!probeHurtMeasured){probeHurtEnd=mc.Position;probeHurtMeasured=true;}
        if(seconds>31.5&&!probeClearBots)
        {
            // Keep other bodies out of the avatar screenshots and movement-clearance checks.
            foreach(var bot in Utilities.GetPlayers().Where(p=>p.IsValid&&p.IsBot))
                if(bot.PlayerPawn.Value is {IsValid:true} bp && pawn.AbsOrigin is {} p)
                {bp.MoveType=bp.ActualMoveType=MoveType_t.MOVETYPE_NONE;bp.Teleport(new Vector(p.X+1000,p.Y+1000,p.Z+500),null,new Vector(0,0,0));}
            probeClearBots=true;
        }
        if(seconds>32&&!probeInventory){bridge.PushInput(new(1,8,1));bridge.PushInput(new(1,8,0));probeInventory=true;}
        if(seconds>33&&(mc.Flags&Protocol.McScreen)!=0)probeInventoryObserved=true;
        if(seconds>34&&probeInventoryObserved&&!probePickup)
        {bridge.PushInput(new(4,0,422,560));bridge.PushInput(new(2,1,1));probePickup=true;}
        if(seconds>34.15&&probePickup&&!probePickupUp){bridge.PushInput(new(2,1,0));probePickupUp=true;}
        if(seconds>36&&probePickupUp&&!probeDrop)
        {bridge.PushInput(new(4,0,530,441));bridge.PushInput(new(2,1,1));probeDrop=true;}
        if(seconds>36.15&&probeDrop&&!probeDropUp){bridge.PushInput(new(2,1,0));probeDropUp=true;}
        if(seconds>38&&probeDropUp&&!probeInventoryCapture)
        {hud.Capture(Path.Combine(ModuleDirectory,"client-inventory.tga"));probeInventoryCapture=true;}
        if(seconds>41&&probeInventory&&!probeInventoryClose)
        {bridge.PushInput(new(1,8,1));bridge.PushInput(new(1,8,0));probeInventoryClose=true;}
        if(seconds>43&&!probeCrouch){probeStandingEye=mc.EyeHeight;bridge.PushInput(new(1,225,1));probeCrouch=true;}
        if(seconds>45&&!probeCrouchCapture){probeCrouchingEye=mc.EyeHeight;hud.Capture(Path.Combine(ModuleDirectory,"client-crouch.tga"));probeCrouchCapture=true;}
        if(seconds>46&&!probeUncrouch){bridge.PushInput(new(1,225,0));probeUncrouch=true;}
        if(seconds>48&&!probeStand){probeStandingAgain=mc.EyeHeight;probeStand=true;}
        if(seconds>50&&!probeThird){bridge.PushInput(new(1,62,1));bridge.PushInput(new(1,62,0));probeThird=true;}
        if(seconds>54&&!probeThirdCapture){probeThirdMode=mc.CameraMode;probeHiddenBody=pawn.Render.A==0;hud.Capture(Path.Combine(ModuleDirectory,"client-steve-back.tga"));probeThirdCapture=true;}
        if(seconds>56&&!probeFront){bridge.PushInput(new(1,62,1));bridge.PushInput(new(1,62,0));probeFront=true;}
        if(seconds>59&&!probeFrontCapture){hud.Capture(Path.Combine(ModuleDirectory,"client-steve-front.tga"));probeFrontCapture=true;}
        if(seconds>60&&!probeLook){
            probeLookStart=look.Yaw;hud.InputMode(true,false,Config.Width,Config.Height);hud.TestMouse(600,0);probeLook=true;
        }
        if(seconds>62)probeLookEnd=look.Yaw;
        if(seconds>61&&!probeFirst){bridge.PushInput(new(1,62,1));bridge.PushInput(new(1,62,0));probeFirst=true;}
        if(seconds>64&&!probeGameplayFinished)
        {
            int healthAfter=probeTarget?.IsValid==true?probeTarget.Health:-1;
            if(pawn.AbsOrigin is {} pos)
            {
                var floor=Trace.TraceEndShape(new(pos.X,pos.Y,pos.Z+2),new(pos.X,pos.Y,pos.Z-80),pawn,new(){InteractsWith=Masks.PlayerSolidBrushOnly});
                probeGroundGap=floor.DidHit()?pos.Z-floor.EndPos.Z:999;
            }
            if(!inputProbe)RestoreMovement();
            var terrain=collision?.VerifyFloors(host.Position,pawn)??default;
            var gameplay=new {exactFloorSamples=terrain.Compared,floorMismatches=terrain.Missing,maxFloorErrorUnits=terrain.MaxErrorUnits,liveCS2=true,movementActivated=probeDrive,
                movementBlocks=Math.Sqrt(DistanceSquared(probeMoveStart,probeMoveEnd)),
                sourceShots,clipBefore=probeClip,clipAfter=pawn.WeaponServices?.ActiveWeapon.Value?.Clip1,lookStart=probeLookStart,lookEnd=probeLookEnd,lookMinecraft=mc.Yaw,knockbackPeakAwayBlocks=probeAwayPeak,knockbackAwayBlocks=probeHurtStart.Z-probeHurtEnd.Z,knockbackStart=probeHurtStart,knockbackEnd=probeHurtEnd,
                hostStart=probeMoveStart,hostEnd=probeMoveEnd,botHealthBefore=probeTargetHealth,
                botHealthAfter=healthAfter,swordDamageToBot=probeTargetHealth-healthAfter,
                captureStatus=hud.CaptureStatus,inventoryOpened=probeInventoryObserved,inventoryTransferAttempted=probeDrop,inventoryClosed=(mc.Flags&Protocol.McScreen)==0,standingEye=probeStandingEye,crouchingEye=probeCrouchingEye,standingAgain=probeStandingAgain,
                thirdPersonMode=probeThirdMode,bodyHidden=probeHiddenBody,
                nativeFloorGapUnits=probeGroundGap,bodyRestored=pawn.Render.A!=0,
                movementRestored=pawn.MoveType==originalMove&&pawn.ActualMoveType==originalActualMove};
            File.WriteAllText(Path.Combine(ModuleDirectory,"client-gameplay.json"),JsonSerializer.Serialize(gameplay,new JsonSerializerOptions{WriteIndented=true}));
            Logger.LogInformation("CS2Craft gameplay probe complete: {Result}",JsonSerializer.Serialize(gameplay));
            probeGameplayFinished=true;
            if(!inputProbe)Detach();
        }
        if(seconds>15&&!probeFinished)
        {
            var result=new {liveCS2=true,renderer=hud.Status,overlayFrames=hud.Frames,worldFrames=hud.WorldFrames,
                sections=hud.Sections,rejectedPackets=hud.Rejected,captureStatus=hud.CaptureStatus,
                sampledRegions=collision?.Completed??0,hostPosition=host.Position,eyeOffset=pawn.ViewOffset.Z,
                minecraftPosition=mc.Position,minecraftFlags=mc.Flags,placementAttempted=probePressed,
                screenshot=Path.Combine(ModuleDirectory,"client-probe.tga")};
            File.WriteAllText(Path.Combine(ModuleDirectory,"client-probe.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
            Logger.LogInformation("CS2Craft client probe complete: {Result}",JsonSerializer.Serialize(result));
            probeFinished=true;
        }
    }
}
