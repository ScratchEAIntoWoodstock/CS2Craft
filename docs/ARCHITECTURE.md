# CS2Craft implementation notes

## Ownership

Minecraft owns inventory, crafting, items, damage calculation, block state, persistence and player physics. CS2 owns its map, bots and the pawn targeted by bots. The plugin mirrors bots into Minecraft and moves the CS2 pawn to Minecraft's position in drive mode.

Protocol is SkyCraft release **v0.1.0 / version 10**, not main-branch version 11. The mapping is `Local\CS2Craft_v1`, 200,327,168 bytes.

One block is 40 Source units:

```text
Minecraft (x,y,z) = (Source.x/40, Source.z/40, -Source.y/40)
Source (x,y,z) = (Minecraft.x*40, -Minecraft.z*40, Minecraft.y*40)
Minecraft yaw = wrap(-Source yaw - 90)
Minecraft pitch = Source pitch
```

## Thread / memory rules

Host/player/actor records use seqlocks. Input/combat use bounded fixed-record rings. Collision/rendering use aligned byte rings with explicit wrap padding. HUD exchange is the upstream triple buffer: initial renderer front=2, Java back=1, shared middle=0.

Only the C# tick thread produces shared input, including damage, key states and events drained from the native window queue. Minecraft produces combat and render data. The native compositor consumes render/HUD streams in CS2; the fixture consumes them during isolated testing. Never use both consumers together.

C# volatile publication, Windows interlocked operations and Java acquire/release FFM handles synchronize ownership. Invalid counters, dimensions and non-finite positions are rejected. A second host cannot reset a live mapping.

## Graphics

CounterStrikeSharp loads the DLL into the local CS2 process. It refuses secure/dedicated processes and hooks DXGI Present for DX11. Whole-context D3D11.1 state swaps preserve the game's pipeline.

The world backend retains texture atlases/patches, entity textures, sections, avatar batches and scene meshes. It validates packet sizes, indices and vertices, caps mesh memory at 128 MiB and textures at 256 MiB, and removes zero-vertex sections. Opaque/cutout geometry writes depth; translucent batches blend without writing depth. Sampling is point-filtered; lighting is approximate Minecraft sky/block light plus face shading.

A networked camera entity follows Minecraft's interpolated feet and eye height, including crouch, and its F5 mode/distance. Front view reverses yaw/pitch. A small CS2 hull trace shortens the camera against map walls. The native renderer receives the same camera position/orientation and a provisional Source-style 4:3 FOV conversion. Server/client interpolation still differs; this is not a hook of CS2's final view matrix.

The CS2 pawn and its weapon attachments are made transparent while driving and restored on detach. Minecraft supplies Steve's posed body, armor, held items and textures; avatar batches render at Minecraft's feet in third person. The pinned 26.3 default wide Steve skin is applied to the local linked player only.

Occlusion uses a separate depth buffer populated with actual map collision triangles. On attach, `OccluderFeed` streams the whole map's visible triangles once, nearest regions first, eight regions per tick. Each triangle is sent once, bucketed by centroid region. The renderer keeps them as position-only buffers (12 bytes per vertex, own 256 MiB budget, no region cap) and uploads at most 64 regions per frame. They survive scene pauses (death, round start) and are cleared only on detach or map change. Invisible player-clip surfaces participate in movement but not occlusion or block targeting.

Moving occluders cover what the static map cannot: every tick the plugin rebuilds depth-only triangles for each living player and bot within 96 blocks (`MovingShapes.Person`: legs, torso, head and arms/weapon boxes, scaled by the pawn's eye height for crouching, slightly slimmer than the model so blocks never vanish beside a person) and for doors (`prop_door_rotating`, `func_door*`, their collision box at the current angle). `CS2Craft_SetMovingOccluders` replaces the set (at most 65,536 vertices, all finite), and the renderer draws it into its depth buffer right after the map triangles, from one dynamic vertex buffer.

Minecraft's per-frame world-entity table (0x1C000, seqlock) is now drawn too; the original SkyCraft drew it inside Skyrim. It covers arrows and tridents (crossed planes like Minecraft's arrow model), dropped items (spinning sprites), dropped blocks (small cubes) and mining cracks, plus the targeted-block outline (40% black lines). These are rebuilt each frame into one dynamic vertex buffer: cutouts in the opaque pass, cracks and lines in the translucent pass. Vertex flag 256 marks untextured geometry. Contact shadows (kind 6) are skipped. Dynamic bots/doors/props, transparent surfaces and shadows still need separate rendering integration; CS2's own depth buffer is not used.

Minecraft's premultiplied HUD is composited after world drawing. A virtual cursor is drawn into that layer when native inventory routing is active. The compositor can capture the actual CS2 backbuffer on explicit diagnostic requests.

On stop, hooks are disabled and resources released. Disabled Present trampolines and the DLL remain resident so an in-flight callback can still return safely. A window subclass is restored if it remains the current procedure; if another overlay chains it, it remains dormant rather than breaking that chain.

## Input and presentation

Desktop polling is restricted to foreground CS2 in the same process. Native window routing queues wheel/text and tracks raw mouse movement for a virtual inventory cursor. The tick thread publishes the cursor before clicks. Raw mouse input and mouse button messages are consumed while driving. Relative gameplay deltas feed an independent camera orientation; GUI deltas move the inventory cursor. Focus loss and GUI transitions discard pending look deltas. Sensitivity uses available CS2 sensitivity/m_yaw/m_pitch values, with defaults when unavailable.

CS2's SDL3 reads relative mouse input on its own thread through a hidden raw-input window (`GetRawInputBuffer`), so `WM_INPUT` may never reach the subclassed game window. The renderer therefore also hooks `GetRawInputBuffer`/`GetRawInputData` with MinHook and feeds mouse packets read there into the same handler while routing is active and CS2 is foreground. The window procedure reads through the trampoline, so its packets are not counted twice. `css_cs2craft_status` reports window and hooked raw mouse packet counts and the hook status (0 = both hooks active).

Physical mouse/text/wheel routing has not yet passed manual acceptance. The automated inventory test directly drives the shared input protocol. Some host bindings/menu transitions can still conflict with Minecraft. F5 is forwarded; CS2 duck prediction no longer controls the Minecraft camera.

Local `r_drawviewmodel` and `cl_drawhud` values are saved, disabled while Minecraft is active, and restored on detach/loss/unload. The host must release native input on stale/dead links. The camera is recreated if round cleanup removes its view entity. The Minecraft dev client starts hidden and saves/quits when its host exits; the launcher supplies a host PID for shutdown even if attachment never completes.

## Collision

The launch script uses tools/CS2Craft.MapExport (pinned ValveResourceFormat 20.0.6980) to read world_physics.vphys_c or the PHYS block of world_physics.vmdl_c from the local map VPK. Triangular meshes and convex hull faces become exact triangles at 40 Source units per block. Sky/grenade-only shapes are excluded. Player-clip triangles carry flag bit 1 (value 2), a CS2Craft extension of the pinned protocol. Unknown upstream peers should not consume this stream.

The cache is a local .c2map file: magic 0x3243324D, format 1, triangle count, then 40 bytes per triangle (nine floats and flags). The launcher invalidates the cache when the source VPK size or modification timestamp changes. No extracted Valve geometry is packaged with source.

Loading/indexing and surface voxelization run on worker tasks. Nearby eight-block regions are reprioritized by distance after every completed region, across a five-region cube centered on the player. Triangles publish before their 8x8x8 support masks; only then is a region known. Every movement sweep checks coverage, so unloaded space cannot act as air. Renderer occlusion is fed separately from the whole map (see Graphics).

Both LocalPlayer and ServerPlayer movement use the same common Entity collision mixin, resolving exact triangles after vanilla Minecraft blocks. Keeping the server on voxel collision causes position corrections against otherwise walkable terrain. Triangle-only queries are scoped to movement; other queries see geometry again. Body-sized queries apply the movement collider's own rules (`TriCollider.blocksBody`): pose fit (stand, crouch, crawl) and the integrated server's "collides with anything new" movement validation. Those rules are a vertical cylinder, walkable surfaces only above step height, and walls from the step height while grounded. A plain box test counted the slope under the feet: on Dust II ramps it rejected standing on about 31% of ground ticks. That forced a crouch or crawl pose and gave the server grounds for silent teleport-backs. With the shared rules it is about 0.5%, and real low ceilings still block standing. Probes at or below the feet (support, edge, fall checks) keep exact triangle/AABB overlap. Initial spawn correction is limited to 0.15 blocks instead of lifting onto surfaces up to 2.5 blocks overhead.

The extraction describes baked map physics, not runtime changes to doors, breakables or moving props. Minecraft's collision cache still grows as the player explores a map. Placed Minecraft blocks do not create CS2 physics or bot navigation obstacles.

## Gameplay and lifecycle

Attach requires one living local human, `sv_lan 1`, and `-insecure`. Actors use serial-bearing raw handles, avoiding stale entity index reuse. Observation is the default; `css_cs2craft_play` explicitly requests gameplay once Minecraft acknowledges the teleport and collision is ready.

Drive mode stores both movement-type fields, sets them to NONE, and teleports the CS2 pawn to valid Minecraft positions. It holds the CS2 weapon-service next-attack time and replicates blocked primary/secondary attack ticks for every held weapon while Minecraft owns combat. Original attack ticks and movement are restored together. Heartbeat loss, invalid pawn, unload and detach restore controls.

Minecraft hits on a bot's stand-in are converted to CS2 health points on the Minecraft side (`Cs2Damage`). Melee weapons map a full-strength, non-critical hit to a per-item value: diamond sword 30, netherite 35, axes 25–45. Arrows multiply Minecraft damage by 7. Attack charge, crits and enchantments keep their Minecraft ratios. The host applies `DamageMultiplier` (default 1) and logs each hit with health before and after. Bot health is changed directly, with lethal hits using suicide.

Placed blocks get CS2 colliders. Minecraft already streams a per-section solid-block bitset (kRenSolids, originally for Skyrim NPC collision). The renderer DLL stores those bitsets outside the D3D device lifecycle and turns kRenClearAll into removals. `BlockColliders` polls the changes and keeps one `prop_dynamic_override` per solid block: `models/cs_italy/crate/italy_wood_crate_1`, whose single physics hull is a 40-unit cube centred on its origin. The props have SOLID_VPHYSICS, render mode none and no shadows. The model is precached on every map. Spawns are limited to 32 per tick and `MaxBlockColliders` in total. Colliders removed by round cleanup are re-created while their block exists. Bots' nav mesh is not updated.

Each hit is echoed to Minecraft as input event 65 (killed flag, actor id, health left, damage), shown in the action bar with the arrow-hit sound. Minecraft explosion events (type 3: centre and radius) spawn CS2's `explosion_hegrenade` particle system and `BaseGrenade.Explode` sound, at most six per second; the particle is precached on every map. Explosion exposure rays (`ServerExplosion.getSeenPercent`) stop at map geometry, so walls shield from blasts. Blast damage to bots uses ×2.

The Minecraft HUD size follows CS2's swap chain (`CS2Craft_BackbufferSize`) when Width/Height are 0, so it is drawn 1:1. Minecraft resizes its hidden window when the published viewport changes.

A Tick exception now drops the shared-memory bridge only when it is `InvalidDataException` (corrupt mapping). Other faults restore CS2 controls and keep the bridge, so `css_cs2craft_play` can re-attach: Minecraft holds the mapping open, so a disposed bridge could not be recreated.

`HostAutoSprint` (host flag bit 16, a CS2Craft extension) makes Minecraft hold the sprint key while linked. Minecraft's own sprint conditions still apply.

Economy (CS2Craft extensions). The host block also carries CS2 money (offset 0x40, int), buy time left (0x44, float seconds; 999 in warmup) and round time left (0x48), plus host flag bit 17 for "in a buy zone" (`m_bInBuyZone`, or `mp_buy_anywhere`). Minecraft's buy menu (`BuyScreen`, catalog in `dev.skycraft.shop.Shop`) checks these, then sends game event 64 (weapon = offer index, A = price). The plugin (`Economy.TryBuy`) re-checks zone, buy time (`m_fRoundStartTime + mp_buytime`, team can't-buy flags) and money, charges `m_iAccount`, and answers with input event 66 (code 1 = bought, A = offer, B = money left, C = reason). Only a bought answer makes the integrated server hand over the items. Bot stand-ins are 0.5 blocks wide (the CS2 body, not the 32-unit hull) and as tall as the pawn's eye height plus 8 units, so they shrink when crouching; projectiles hit their exact box, without Minecraft's usual 0.3-block projectile margin (`ProjectileUtilMixin`). Kills from Minecraft pay `KillRewardMelee`/`KillRewardRanged`, capped at `mp_maxmoney`, and add a kill and 2 points on the scoreboard (the bot dies through `CommitSuicide`, which credits nobody). While driving, the native window procedure swallows `BuyMenuKey` messages so CS2's buy menu never opens; Minecraft reads keys with `GetAsyncKeyState`, so it still sees B.

Loadouts (`RoundLoadout`). Joining, and every death (respawn), sets the inventory to the default kit (`Shop.defaultKit`, or the session's `/cs2craft loadout`). Input event 64 (round reset) carries code bit 1 = clear placed blocks and bit 2 = fresh gear (always while `KeepGearBetweenRounds` is off, the default; otherwise a new match: `m_totalRoundsPlayed` 0 or warmup, or the half). Without bit 2, survivors keep what they carry, repaired. Every round start restores health, hunger, air and clears effects. Input event 67 resets to the default kit after a CS2-side death that Minecraft did not report. The natural-regeneration game rule is off. The default kit is also restored before the world saves on quit. Attribution, impulses and full damage credit are incomplete.

A death keeps the player linked. When Minecraft reports a death, the plugin leaves drive mode first, so the damage hook cannot cancel the suicide, then kills the pawn and parks Minecraft with the host loading flag. After `RespawnDelaySeconds` (default 3; negative waits for CS2's own respawn) the local player is respawned. On any spawn of the linked player (death or round start), Minecraft is teleported to the new pawn and gameplay resumes if it was active. A CS2-side death that Minecraft did not report does not kill the Minecraft player; it is moved to the respawn point. Incoming CS2 damage is forwarded at the upstream 5:1 health scale and suppressed only if successfully queued. Each forwarded hit clears Minecraft's 0.5 s damage immunity first, since the CS2 damage was already cancelled and rapid fire would otherwise be lost. Every CS2 round start sends input event 64 (a CS2Craft extension). With `ClearBlocksEachRound` (default on) Minecraft removes all blocks from loaded chunks within 16 chunks of a player, plus dropped items, arrows, XP orbs, falling blocks and lit TNT. It then resets each player (see Loadouts). The mirror world is otherwise empty void. Incoming bot-fire behavior still needs a live acceptance test.

`tools/Play.ps1` selects a save named for the launch map. Direct developer launches default to a separate configurable save name. Changing CS2 maps inside a running session does not yet automatically switch Minecraft saves.

## Remaining work

- Dynamic obstacles, bounded cache eviction/resampling, additional maps and long movement routes.
- Final CS2 render-view/depth integration, smoother motion, FOV transitions, lighting and shadows.
- Bot navigation around placed blocks (colliders exist; the nav mesh is not updated).
- Manual GUI/input tests, host binding conflicts, resolution changes and overlay coexistence.
- Full damage, projectiles, death/respawn and lifecycle recovery.
- Authenticated Minecraft installation/profile workflow.
- Multiplayer only after the local client is reliable.
