using System.Runtime.Versioning;
using CS2Craft.Bridge;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using CEntityKeyValues = CounterStrikeSharp.API.Modules.Utils.CEntityKeyValues;
using KeyValuesType = CounterStrikeSharp.API.Modules.Utils.KeyValuesType;
using GameServer = CounterStrikeSharp.API.Server;

namespace CS2Craft.Server;

public sealed class CraftConfig : BasePluginConfig
{
    public bool Enabled { get; set; } = true;
    public bool ExperimentalMovement { get; set; } = false;
    public bool ExperimentalWorldRendering { get; set; } = true;
    public bool CoarseWorldOcclusion { get; set; } = true;
    public bool HideCS2Viewmodel { get; set; } = true;
    public bool HideCS2Hud { get; set; } = true;
    // Minecraft reports hits on bots in CS2 health points (per-weapon table in Cs2Damage.java); this scales them.
    public float DamageMultiplier { get; set; } = 1;
    // Seconds after a linked death before the local player is respawned; negative waits for CS2's own respawn.
    public float RespawnDelaySeconds { get; set; } = 3;
    // Remove every block placed in the Minecraft world when a CS2 round starts.
    public bool ClearBlocksEachRound { get; set; } = true;
    // Moving sprints by default, like running in CS2; Shift still sneaks.
    public bool AutoSprint { get; set; } = true;
    // Invisible solid CS2 colliders in placed Minecraft blocks, so bots and bullets are stopped by them.
    public bool BotBlockCollision { get; set; } = true;
    public int MaxBlockColliders { get; set; } = 2048;
    // Minecraft HUD size; 0 follows CS2's window so the HUD is drawn 1:1 instead of stretched.
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;
}

[SupportedOSPlatform("windows")]
[MinimumApiVersion(376)]
public sealed partial class CS2CraftPlugin : BasePlugin, IPluginConfig<CraftConfig>
{
    public override string ModuleName => "CS2Craft";
    public override string ModuleVersion => "0.1.0-dev";
    public override string ModuleAuthor => "CS2Craft contributors; Minecraft bridge by chasmlol";
    public override string ModuleDescription => "Experimental local Minecraft/CS2 engine bridge";
    public CraftConfig Config { get; set; } = new();
    SharedBridge? bridge;
    CCSPlayerController? owner;
    CCSPlayerPawn? drivenPawn;
    MoveType_t originalMove, originalActualMove;
    float? originalNextAttack;
    readonly Dictionary<uint, CCSPlayerPawn> actors = new();
    readonly DesktopInput input = new();
    readonly MouseLook look = new();
    readonly WeaponGate weaponGate = new();
    readonly NativeHud hud = new();
    readonly LocalPresentation presentation = new();
    readonly CraftCamera craftCamera = new();
    CollisionSampler? collision;
    OccluderFeed? occluders;
    BlockColliders? colliders;
    uint epoch = 1, teleport = 1;
    uint attachedPawn;
    HostState host;
    bool driving,pendingPlay;
    // Death/respawn keeps the owner linked: Minecraft is parked until the pawn is alive again, then re-linked.
    bool awaitingRespawn,resumePlay;
    float respawnAt=-1,relinkAt=-1;
    int ticks;
    /// <summary>Minecraft HUD size: the configured one, else CS2's swap chain (1280x720 until it is known).</summary>
    (int W,int H) Viewport()
    {
        if (Config.Width > 0 && Config.Height > 0) return (Config.Width, Config.Height);
        return hud.BackbufferSize(out int w, out int h)
            ? (Math.Clamp(w, 320, Protocol.MaxWidth), Math.Clamp(h, 200, Protocol.MaxHeight)) : (1280, 720);
    }
    int ViewWidth => Viewport().W;
    int ViewHeight => Viewport().H;
    public void OnConfigParsed(CraftConfig config)
    {
        if ((config.Width != 0 || config.Height != 0)
            && (config.Width < 320 || config.Width > Protocol.MaxWidth || config.Height < 200 || config.Height > Protocol.MaxHeight)
            || !float.IsFinite(config.DamageMultiplier) || config.DamageMultiplier <= 0 || config.DamageMultiplier > 10
            || !float.IsFinite(config.RespawnDelaySeconds) || config.RespawnDelaySeconds > 60
            || config.MaxBlockColliders < 0 || config.MaxBlockColliders > 8192)
            throw new ArgumentException("Invalid CS2Craft viewport, damage scale or respawn delay.");
        Config = config;
    }
    public override void Load(bool hotReload)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The shared-memory adapter currently requires Windows.");
        if (!Environment.GetCommandLineArgs().Any(x => x.Equals("-insecure", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("CS2Craft requires a local -insecure mod session.");
        AddCommand("css_cs2craft_play", "Attach and start experimental local Minecraft gameplay when ready", Play);
        AddCommand("css_cs2craft_status", "Show bridge status", Status);
        AddCommand("css_cs2craft_attach", "Attach your local pawn to the Minecraft bridge", Attach);
        AddCommand("css_cs2craft_detach", "Restore ordinary CS2 movement", (_, _) => Detach());
        AddCommand("css_cs2craft_drive", "Toggle experimental Minecraft movement (config opt-in required)", Drive);
        RegisterListener<Listeners.OnTick>(Tick);
        RegisterEventHandler<EventWeaponFire>((e,_)=>{
            if(driving && e.Userid?.EntityHandle.Raw==owner?.EntityHandle.Raw)sourceShots++;
            return HookResult.Continue;
        });
        // A respawn (after death or at round start) moves the pawn; re-link so Minecraft follows it.
        RegisterEventHandler<EventPlayerSpawn>((e,_)=>{
            if(owner?.IsValid==true && e.Userid?.EntityHandle.Raw==owner.EntityHandle.Raw)WaitForRespawn(false);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventRoundStart>((_,_)=>{
            if(Config.ClearBlocksEachRound && owner is not null && bridge is not null && !bridge.PushInput(new(Protocol.InputRoundReset,0)))
                Logger.LogWarning("CS2Craft: input ring full; this round's Minecraft block reset was skipped.");
            return HookResult.Continue;
        });
        // The block collider model belongs to another map; precache it for whichever map loads.
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource(BlockColliders.Model);
            manifest.AddResource(ExplosionEffect);
        });
        RegisterListener<Listeners.OnMapStart>(_ => { Detach(); epoch++; });
        RegisterListener<Listeners.OnMapEnd>(Detach);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(DamageToPlayer);
        hud.Start(ModuleDirectory);
        Logger.LogInformation("CS2Craft renderer: {Status}",hud.Status);
        Logger.LogInformation("CS2Craft loaded. Use css_cs2craft_attach in a local bot map. Renderer integration is experimental.");
    }
    void Status(CCSPlayerController? player, CommandInfo command)
    {
        hud.InputStats(out ulong windowMouse,out ulong hookedMouse,out int hookStatus);
        command.ReplyToCommand($"CS2Craft {ModuleVersion}: host={bridge is not null}, minecraft={bridge?.MinecraftAlive == true}, attached={owner?.PlayerName ?? "none"}, driving={driving}, awaitingRespawn={awaitingRespawn}, regions={collision?.Completed ?? 0}, occluders={occluders?.Sent ?? 0}/{occluders?.Total ?? 0} sent/{hud.OccluderRegions} on GPU, block colliders={colliders?.Count ?? 0} (+{colliders?.Pending ?? 0} pending), HUD={hud.Status}/{hud.Frames} frames, scene={hud.WorldFrames} frames/{hud.Sections} sections/{hud.Rejected} rejected packets, raw mouse={windowMouse} window/{hookedMouse} hooked (hook {hookStatus}). Camera and occlusion remain experimental.");
    }
    void Play(CCSPlayerController? player, CommandInfo command)
    {
        if(owner is null)AttachCore(player,command.ReplyToCommand);
        if(player?.IsValid!=true || owner?.IsValid!=true || player.EntityHandle.Raw!=owner.EntityHandle.Raw)return;
        if(driving){command.ReplyToCommand("Minecraft gameplay is already active.");return;}
        pendingPlay=true;
        command.ReplyToCommand("Waiting for Minecraft and map collision. Gameplay will start automatically; css_cs2craft_detach restores CS2.");
    }
    void Attach(CCSPlayerController? player, CommandInfo command)
        => AttachCore(player,command.ReplyToCommand);
    void AttachCore(CCSPlayerController? player, Action<string> reply)
    {
        if (!Config.Enabled) { reply("CS2Craft is disabled in config."); return; }
        if (player is not { IsValid: true, IsBot: false, PawnIsAlive: true } || player.PlayerPawn.Value is not { IsValid: true } pawn)
        { reply("Run this from a living local player."); return; }
        if (ConVar.Find("sv_lan")?.GetPrimitiveValue<bool>() != true)
        { reply("Start the isolated local session with sv_lan 1 first."); return; }
        if (Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot) != 1)
        { reply("This prototype supports one local player plus bots."); return; }
        if (owner is not null) { reply("A pawn is already attached. Detach first."); return; }
        try { bridge ??= new SharedBridge(); }
        catch (IOException)
        { reply("An existing Minecraft/CS2Craft process holds the bridge. Close that instance before attaching."); return; }
        owner = player;
        attachedPawn = pawn.EntityHandle.Raw;
        epoch++; teleport++;
        collision = new CollisionSampler(bridge, epoch, Path.Combine(ModuleDirectory,"collision",GameServer.MapName+".c2map"));
        var p = pawn.AbsOrigin!;
        hud.ClearOccluders();
        occluders = new OccluderFeed(collision.Map, Coordinates.ToMinecraft(new(p.X, p.Y, p.Z)));
        colliders = Config.BotBlockCollision ? new BlockColliders(Config.MaxBlockColliders) : null;
        hud.ResendSolids();
        host = new(Protocol.HostInGame, MapId(GameServer.MapName), epoch,
            Coordinates.ToMinecraft(new(p.X, p.Y, p.Z)), Coordinates.Yaw(pawn.EyeAngles.Y), pawn.EyeAngles.X,
            teleport, (uint)ViewWidth, (uint)ViewHeight);
        bridge.Publish(host);
        bridge.PushCollision(1, BitConverter.GetBytes(epoch));
        reply("CS2Craft attached. Start the CS2Craft Minecraft profile. State/actor mirroring is active; movement stays in CS2 until css_cs2craft_drive.");
    }
    void Drive(CCSPlayerController? player, CommandInfo command)
        => DriveCore(player,command.ReplyToCommand);
    void DriveCore(CCSPlayerController? player, Action<string> reply)
    {
        if (player?.IsValid != true || owner?.IsValid != true || player.EntityHandle.Raw != owner.EntityHandle.Raw)
        { reply("Attach your pawn first."); return; }
        if (driving) { RestoreMovement(); reply("CS2 movement restored."); return; }
        if (!Config.ExperimentalMovement && !pendingPlay)
        { reply("Movement is a developer experiment. Set ExperimentalMovement=true in the CS2Craft config before testing it."); return; }
        if (bridge is null || !bridge.MinecraftAlive || !bridge.TryReadPlayer(out var mc)
            || (mc.Flags & Protocol.McInWorld) == 0 || (mc.Flags & Protocol.McDead)!=0 || mc.TeleportAck != teleport || collision?.Ready(mc.Position) != true)
        { reply("Minecraft and sampled collision must be ready first."); return; }
        var pawn = owner.PlayerPawn.Value!;
        originalMove = pawn.MoveType; originalActualMove = pawn.ActualMoveType;
        drivenPawn = pawn;
        look.Reset(Coordinates.Yaw(pawn.EyeAngles.Y),pawn.EyeAngles.X);
        originalNextAttack = pawn.WeaponServices is { } weapons ? new CCSPlayer_WeaponServices(weapons.Handle).NextAttack : null;
        pawn.MoveType = MoveType_t.MOVETYPE_NONE;
        pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        // Observation/previous-life events cannot act on this newly driven pawn.
        // This barrier runs after the teleport acknowledgement, even if an earlier state read failed.
        bridge.DiscardEvents();
        driving = true;pendingPlay=false;
        reply("Experimental Minecraft movement active. css_cs2craft_detach restores CS2.");
    }
    void Tick()
    {
        if (clientProbe) ProbeBeforeAttach();
        if (bridge is null) return;
        try
        {
            bridge.Heartbeat();
            if (owner is not { IsValid: true }) { if (owner is not null) Detach(); return; }
            if (!owner.PawnIsAlive || owner.PlayerPawn.Value is not { IsValid: true } pawn)
            { WaitForRespawn(false); RespawnWhenDue(); return; }
            if (!awaitingRespawn && pawn.EntityHandle.Raw != attachedPawn) WaitForRespawn(false);
            if (awaitingRespawn && !Relink(pawn)) return;
            if (!bridge.MinecraftAlive && driving) RestoreMovement();
            PlayerState playerState=default;
            bool ready = bridge.MinecraftAlive && bridge.TryReadPlayer(out playerState) && (playerState.Flags&Protocol.McInWorld)!=0;
            if(!ready){hud.InputMode(false,false,ViewWidth,ViewHeight);input.Release(bridge);}
            presentation.Update(ready && hud.Frames>0,Config.HideCS2Viewmodel,Config.HideCS2Hud);
            if(pendingPlay&&ready&&playerState.TeleportAck==teleport&&(playerState.Flags&Protocol.McDead)==0&&collision?.Ready(playerState.Position)==true)
                DriveCore(owner,message=>owner?.PrintToChat(message));
            if(ready&&(!clientProbe||(inputProbe&&probeGameplayFinished)))input.Poll(bridge,hud,driving,(playerState.Flags&Protocol.McScreen)!=0,ViewWidth,ViewHeight);
            if(driving&&hud.Look(out int dx,out int dy))
                look.Move(dx,dy,ConVar.Find("sensitivity")?.GetPrimitiveValue<float>()??2.5f,
                    ConVar.Find("m_yaw")?.GetPrimitiveValue<float>()??.022f,ConVar.Find("m_pitch")?.GetPrimitiveValue<float>()??.022f);
            if (driving && ready && bridge.TryReadPlayer(out var mc)
                && mc.TeleportAck == teleport && (mc.Flags & Protocol.McInWorld) != 0)
            {
                weaponGate.Update(pawn);
                if(pawn.WeaponServices is { } weapons)
                    new CCSPlayer_WeaponServices(weapons.Handle).NextAttack=GameServer.CurrentTime+0.5f;
                if ((mc.Flags & Protocol.McDead) != 0) { MinecraftDied(pawn); return; }
                if ((mc.Flags & Protocol.McScreen) == 0)
                {
                    var p = Coordinates.ToSource(mc.Position);
                    // Refuse non-finite or out-of-world coordinates from a broken peer.
                    if (Math.Abs(p.X) <= 32768 && Math.Abs(p.Y) <= 32768 && Math.Abs(p.Z) <= 32768)
                        pawn.Teleport(new Vector((float)p.X, (float)p.Y, (float)p.Z), null, new Vector(0, 0, 0));
                }
            }
            var origin = pawn.AbsOrigin!;
            // Observe mode sends a teleport when the host moves; drive mode lets Minecraft own its position.
            var hostPosition = Coordinates.ToMinecraft(new(origin.X, origin.Y, origin.Z));
            if (!driving && DistanceSquared(hostPosition, host.Position) > 0.01) teleport++;
            host = host with { Position = hostPosition, Yaw = driving?look.Yaw:Coordinates.Yaw(pawn.EyeAngles.Y),
                Pitch = driving?look.Pitch:pawn.EyeAngles.X, Teleport = teleport,
                Width = (uint)ViewWidth, Height = (uint)ViewHeight,
                Flags = Protocol.HostInGame | (driving && Config.AutoSprint ? Protocol.HostAutoSprint : 0) };

            bridge.Publish(host);
            var viewOffset = pawn.ViewOffset;
            Point eye = Coordinates.ToMinecraft(new(origin.X+viewOffset.X,origin.Y+viewOffset.Y,origin.Z+viewOffset.Z));
            uint fov = pawn.CameraServices is { } cameraServices ? new CCSPlayerBase_CameraServices(cameraServices.Handle).FOV : 90;
            // Provisional Source-style 4:3 horizontal FOV conversion; scoped transitions/client prediction need validation.
            float horizontalFov = fov is >=10 and <=150 ? fov : 90;
            float verticalFov = (float)(2*Math.Atan(Math.Tan(horizontalFov*Math.PI/360)*0.75)*180/Math.PI);
            float cameraYaw=host.Yaw,cameraPitch=host.Pitch;
            if(driving&&ready)
            {
                craftCamera.Update(pawn,playerState,host.Yaw,host.Pitch);
                eye=craftCamera.Eye;cameraYaw=craftCamera.Yaw;cameraPitch=craftCamera.Pitch;
            }
            hud.Avatar(playerState.Position,driving&&ready&&playerState.CameraMode!=0);
            hud.Camera(eye,cameraYaw,cameraPitch,verticalFov,Config.ExperimentalWorldRendering,Config.CoarseWorldOcclusion);
            collision?.Tick(hostPosition, pawn, 48);
            occluders?.Tick(hud, 8);
            colliders?.Tick(hud);
            if (ready)
            {
                if (clientProbe) ProbeConnected(pawn);
                if(owner is null)return;
                MirrorActors(pawn);
                for (int i = 0; i < 128 && bridge.TryReadEvent(out var ev); i++) HandleEvent(ev, pawn);
            }
            if(inputProbe&&probeGameplayFinished&&ticks%32==0)
                File.WriteAllText(Path.Combine(ModuleDirectory,"input-probe.json"),
                    System.Text.Json.JsonSerializer.Serialize(new{look.Yaw,look.Pitch,mcYaw=playerState.Yaw,mcPitch=playerState.Pitch,
                        position=playerState.Position,sourceShots,clip=pawn.WeaponServices?.ActiveWeapon.Value?.Clip1,driving}));
            if (++ticks % 640 == 0) Logger.LogInformation("CS2Craft: minecraft={Ready}, driving={Drive}, sampledRegions={Regions}",
                ready, driving, collision?.Completed ?? 0);
        }
        catch (Exception e)
        {
            // Only a corrupt mapping justifies dropping the bridge: Minecraft keeps the mapping open, so
            // a new bridge cannot be created until Minecraft restarts. Other faults just restore CS2.
            bool corrupt = e is InvalidDataException;
            Logger.LogError(e, corrupt ? "CS2Craft bridge corrupt; restoring CS2 controls"
                : "CS2Craft fault; restoring CS2 controls (css_cs2craft_play re-attaches)");
            try { Detach(); }
            catch (Exception detach) { Logger.LogError(detach, "CS2Craft: restoring CS2 controls failed"); }
            if (corrupt) { bridge?.Dispose(); bridge = null; }
        }
    }
    void MirrorActors(CCSPlayerPawn playerPawn)
    {
        if (bridge is null) return;
        actors.Clear();
        var records = new List<Actor>();
        foreach (var p in Utilities.GetPlayers())
        {
            if (!p.IsValid || p.EntityHandle.Raw == owner!.EntityHandle.Raw || !p.IsBot || !p.PawnIsAlive
                || p.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } pos) continue;
            Point mc = Coordinates.ToMinecraft(new(pos.X, pos.Y, pos.Z));
            if (DistanceSquared(mc, host.Position) > 64 * 64) continue;
            uint id = pawn.EntityHandle.Raw;
            actors[id] = pawn;
            records.Add(new(id, pawn.TeamNum != playerPawn.TeamNum ? 1u : 0u, mc,
                Coordinates.Yaw(pawn.EyeAngles.Y), 0.8f, 1.8f, pawn.Health / (float)Math.Max(1, pawn.MaxHealth), p.PlayerName));
        }
        bridge.PublishActors(records);
    }
    void HandleEvent(GameEvent e, CCSPlayerPawn pawn)
    {
        if (!driving) return; // Never apply gameplay effects during read-only observation.
        if (e.Type == 2) { MinecraftDied(pawn); return; }
        if (e.Type == 3) { Explosion(e); return; }
        if (e.Type != 1 || !float.IsFinite(e.A) || e.A <= 0 || e.A > 1000
            || !actors.TryGetValue(e.ActorId, out var target) || !target.IsValid || target.Health <= 0) return;
        // e.A is already in CS2 health points (Minecraft's per-weapon table, Cs2Damage.java).
        int damage = Math.Clamp((int)Math.Round(e.A * Config.DamageMultiplier), 1, 10000), before = target.Health;
        if ((e.Flags & 2) != 0) probeProjectileHits++;
        // Prototype health bridge. Full Source 2 damage attribution/impulses remain a separate integration.
        bool killed = damage >= target.Health;
        if (killed) target.CommitSuicide(false, true);
        else { target.Health -= damage; Utilities.SetStateChanged(target, "CBaseEntity", "m_iHealth"); }
        bridge?.PushInput(new(Protocol.InputHitFeedback, (ushort)(killed ? 1 : 0), unchecked((int)e.ActorId), Math.Max(0, before - damage), damage));
        Logger.LogInformation("CS2Craft: Minecraft hit bot {Bot} for {Damage} ({Before} -> {After} HP){Projectile}",
            target.Controller.Value?.PlayerName ?? "?", damage, before, Math.Max(0, before - damage),
            ((e.Flags & 2) != 0 ? " by projectile" : "") + ((e.Flags & 1) != 0 ? " (critical)" : ""));
    }
    // CS2's own HE-grenade blast: lit, fogged and post-processed by CS2 like any other effect.
    const string ExplosionEffect = "particles/explosions_fx/explosion_hegrenade.vpcf";
    float explosionWindowStart;
    int explosionsInWindow;
    /// <summary>A Minecraft explosion (TNT, creeper, bed...) at e.A/B/C with radius e.D: show and play it in CS2.</summary>
    bool Explosion(GameEvent e)
    {
        if (!float.IsFinite(e.A) || !float.IsFinite(e.B) || !float.IsFinite(e.C) || !float.IsFinite(e.D)) return false;
        var p = Coordinates.ToSource(new(e.A, e.B, e.C));
        if (Math.Abs(p.X) > 32768 || Math.Abs(p.Y) > 32768 || Math.Abs(p.Z) > 32768) return false;
        // TNT chains: at most a few effects per second; Minecraft still applies every blast's damage.
        if (GameServer.CurrentTime - explosionWindowStart > 1) { explosionWindowStart = GameServer.CurrentTime; explosionsInWindow = 0; }
        if (++explosionsInWindow > 6) return false;
        var effect = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (effect is null) return false;
        var origin = new Vector((float)p.X, (float)p.Y, (float)p.Z);
        using (var values = new CEntityKeyValues())
        {
            values["effect_name", KeyValuesType.TYPE_STRING] = ExplosionEffect;
            values["start_active", KeyValuesType.TYPE_BOOL] = true;
            values["origin", KeyValuesType.TYPE_VECTOR] = origin;
            effect.DispatchSpawn(values);
        }
        effect.Teleport(origin, null, null);
        effect.AcceptInput("Start");
        effect.EmitSound("BaseGrenade.Explode");
        AddTimer(5, () => { if (effect.IsValid) effect.Remove(); });
        return true;
    }
    HookResult DamageToPlayer(CCSPlayerPawn pawn, CTakeDamageInfo info)
    {
        if (!driving || bridge?.MinecraftAlive != true || drivenPawn?.IsValid != true
            || pawn.EntityHandle.Raw != drivenPawn.EntityHandle.Raw || !float.IsFinite(info.Damage) || info.Damage <= 0)
            return HookResult.Continue;
        // Upstream divides host damage by 5. CS2's 100 health therefore maps to 20 MC health.
        bool sent = bridge.PushInput(new(7, 1, (int)(Math.Clamp(info.Damage, 0, 10000) * 100),
            unchecked((int)(info.Attacker.Value?.EntityHandle.Raw ?? 0))));
        if (!sent) { RestoreMovement(); return HookResult.Continue; }
        info.Damage = 0;
        return HookResult.Handled;
    }
    void MinecraftDied(CCSPlayerPawn pawn)
    {
        // Leave drive mode before the suicide: DamageToPlayer would otherwise forward it to Minecraft and cancel it.
        WaitForRespawn(true);
        if (pawn.IsValid && owner?.PawnIsAlive == true) pawn.CommitSuicide(false, true);
    }
    void WaitForRespawn(bool autoRespawn)
    {
        if (!awaitingRespawn)
        {
            resumePlay = driving || pendingPlay;
            RestoreMovement(); pendingPlay = false;
            presentation.Restore();
            hud.Camera(default,0,0,73.7398f,false,false);
            awaitingRespawn = true; respawnAt = -1;
            // Park Minecraft as on a loading screen and hide the overlay until the pawn is alive again.
            if (bridge is not null)
            {
                bridge.Publish(host with { Flags = Protocol.HostLoading, Position = host.Position.IsFinite ? host.Position : default });
                bridge.PublishActors([]);
                bridge.DiscardEvents();
            }
            actors.Clear();
        }
        relinkAt = -1;
        if (autoRespawn && Config.RespawnDelaySeconds >= 0) respawnAt = GameServer.CurrentTime + Config.RespawnDelaySeconds;
    }
    void RespawnWhenDue()
    {
        if (respawnAt < 0 || GameServer.CurrentTime < respawnAt || owner is not { IsValid: true } player) return;
        respawnAt = -1;
        if (player.TeamNum is 2 or 3) player.Respawn();
    }
    bool Relink(CCSPlayerPawn pawn)
    {
        // Give the spawn a moment to place the pawn before Minecraft is teleported to it.
        if (relinkAt < 0) { relinkAt = GameServer.CurrentTime + 0.1f; return false; }
        if (GameServer.CurrentTime < relinkAt || bridge is null || pawn.AbsOrigin is not { } origin) return false;
        awaitingRespawn = false; relinkAt = respawnAt = -1;
        attachedPawn = pawn.EntityHandle.Raw;
        teleport++;
        host = host with { Flags = Protocol.HostInGame, Position = Coordinates.ToMinecraft(new(origin.X, origin.Y, origin.Z)),
            Yaw = Coordinates.Yaw(pawn.EyeAngles.Y), Pitch = pawn.EyeAngles.X, Teleport = teleport };
        bridge.Publish(host);
        pendingPlay |= resumePlay; resumePlay = false;
        if (pendingPlay) owner?.PrintToChat("Respawned. Minecraft gameplay resumes once it reaches your new position.");
        return true;
    }
    void RestoreMovement()
    {
        craftCamera.Restore();
        weaponGate.Restore();
        hud.Avatar(default,false);
        hud.InputMode(false,false,ViewWidth,ViewHeight);
        if (drivenPawn is { IsValid: true })
        {
            if(originalNextAttack is { } attack && drivenPawn.WeaponServices is { } weapons)
                new CCSPlayer_WeaponServices(weapons.Handle).NextAttack=attack;
            drivenPawn.Teleport(null,new CounterStrikeSharp.API.Modules.Utils.QAngle(look.Pitch,Coordinates.Yaw(look.Yaw),0),null);
            drivenPawn.MoveType = originalMove; drivenPawn.ActualMoveType = originalActualMove;
            Utilities.SetStateChanged(drivenPawn, "CBaseEntity", "m_MoveType");
        }
        drivenPawn = null; driving = false; originalNextAttack=null;
        if (bridge is not null) input.Release(bridge);
    }
    void Detach()
    {
        hud.InputMode(false,false,ViewWidth,ViewHeight);
        presentation.Restore();
        hud.Camera(default,0,0,73.7398f,false,false);
        RestoreMovement(); pendingPlay=false;owner = null; actors.Clear(); collision = null;
        occluders = null; hud.ClearOccluders();
        colliders?.Clear(); colliders = null;
        awaitingRespawn=resumePlay=false; respawnAt=relinkAt=-1;
        if (bridge is not null)
        {
            bridge.Publish(host with { Flags = Protocol.HostMenu, Position = host.Position.IsFinite ? host.Position : default });
            bridge.PublishActors([]);
        }
    }
    public override void Unload(bool hotReload) { hud.Stop(); Detach(); bridge?.Dispose(); bridge = null; }
    static double DistanceSquared(Point a, Point b) => Math.Pow(a.X-b.X,2) + Math.Pow(a.Y-b.Y,2) + Math.Pow(a.Z-b.Z,2);
    static uint MapId(string name) { uint h = 2166136261; foreach (char c in name) h = unchecked((h ^ c) * 16777619); return h; }
}
