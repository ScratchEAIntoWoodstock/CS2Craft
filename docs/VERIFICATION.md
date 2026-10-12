# Live probe: buy menu, CS2 economy, person occlusion, default kit — 2026-10-12

The client probe now runs to 112 s. The default kit changed the hotbar (1 stone sword, 2 bow, 3 arrows, 4 planks, 5 beef), so the probe places planks from slot 4 and draws the bow from slot 2. New steps:
- at 70 s, finish the bow target through the real hit handler (kill reward and scoreboard);
- at 95 s, set money to the maximum, with `mp_buy_anywhere 1` and a long `mp_buytime`;
- press B (open), capture, press B (close);
- type `/cs2buy Iron Armor` in Minecraft chat.

Final run (07:02, `CS2Craft-LiveTest-0712`):

| Check | Result |
|---|---|
| Stone sword (default kit) | 20 (100 → 80) |
| Bow | 48 by projectile (80 → 32) |
| Kill from Minecraft | Money 1000 → 1750: CS2Craft's $600 melee reward plus $150 that CS2 itself pays in this game mode when the bot dies. Score 0 → 2 |
| Buy menu | B opened it (`McScreen` set) and B closed it; `client-buymenu.tga` shows the panel over CS2 with money, the warmup notice, icons and greyed offers with reasons |
| Purchase | `/cs2buy Iron Armor`: plugin charged $1000 (10000 → 9000, `mp_maxmoney` 10000 here); the Minecraft log shows "bought Iron Armor for $1000" |
| Round restart | Minecraft log: "round start ... (default kit)"; 0 colliders afterwards, `drivingAfterRound` true |
| Person occluders | 1296 vertices = 9 people × 144. In `client-bow.tga` the bot stands in front of the placed planks and his body and hand cover them |
| Colliders | `colliderTraceHit` true (the check now accepts any block collider); the launched bot stopped short of the block (`botPushBlocked`, x 280 < 320) |
| Other | `maxFloorErrorUnits` 7.4, `sprintForward` true, `explosionSpawned` true |

The first run (06:58) gave the same buy, menu and occluder results. Its trace check failed only because of the old same-entity rule.

**Offline.** Native WARP tests 26–28 cover moving occluders: hiding, clearing and NaN rejection. The bridge has 45 checks, including the money/buy-time/round-time layout, non-finite buy time and the person shape bounds. The 29 Minecraft tests and the C# build pass.

**Not verified live.** The probe sends B through the bridge, so a physical B press reaching CS2's own buy menu was not tested; the window-procedure block is in place. Also not exercised: throwing an ender pearl, potions, TNT ignition, the shield against bot fire, kept gear across a normal round end, the CS2-death default-kit path (input 67) and doors as occluders (no doors near the probe position).

# Live probe: TNT effects, durability, HUD feedback, auto HUD size — 2026-10-11

The client probe (`Start-CS2.ps1 -ClientProbe` with a fresh Minecraft save) now runs to 100 s. After the original checks it adds:
- a full-draw bow shot at a frozen bot;
- CS2's explosion handler, with a capture;
- a bullet-mask trace through a placed block's collider, and a bot launched at the block;
- `mp_restartgame`;
- holding W for auto-sprint.

Final run (21:12, `CS2Craft-LiveTest-2112`):

| Check | Result |
|---|---|
| Sword | 30 (100 → 70) |
| Bow | 54 by projectile (70 → 16), shown as "Dashen 16 HP (-54)" in the action bar of the captured CS2 backbuffer |
| `explosionSpawned` | true; `client-explosion.tga` shows CS2's HE-grenade particle in Dust II |
| `sprintForward` | true |
| Colliders | 2 built. The trace stopped at −60 units, the face of the neighbouring block's crate (the same-entity check is too strict, so `colliderTraceHit` reads false). After the round restart: 0 colliders, `drivingAfterRound` true |

The earlier run's Minecraft log showed the round reset (2 blocks removed, loadout restored).

**Open.**
- `maxFloorErrorUnits` was 46 at the new end position after the W walk; earlier positions measured 0.03. Probably a dynamic prop the map export cannot see; to investigate.
- The bot-push check is weak: the bot was placed against a neighbouring crate and never moved.
- TNT ignition by flint and steel and the blast exposure through walls are exercised only by the build.
- Minecraft advancement toasts still appear in the HUD.

The first probe attempt exposed a plugin bug: any Tick exception disposed the bridge, and re-attach was then impossible while Minecraft held the mapping. It is now limited to corrupt mappings.

# Bow, damage, round loadout, bot block collision, sprint — 2026-10-11

**Evidence from the 19:10 live session (previous update).** The round reset removed 60 blocks and re-linked the player. Diamond-sword hits on bots were 7.0 Minecraft (35 CS2 HP) at full charge, with lower values for spam clicks. The save showed the bow had been fired (bow wear, 16 arrows spent), yet no arrow ever hit a bot. Cause: CS2Craft never drew Minecraft's world-entity table, so arrows were invisible.

**Verified offline.** The native WARP world test now also checks that dropped-item sprites and arrows render, that a NaN entity is skipped, that the outline darkens the box edge and that occluders hide entities: all pass. 40/40 bridge checks pass, including solid-bitset decoding with negative sections. 29/29 Minecraft tests pass, and the C# plugin builds with 0 warnings.

The collider model was chosen by scanning all 13,105 models in pak01_dir.vpk for a single 8-vertex cube hull: `italy_wood_crate_1` is 40.0 × 39.7 × 39.6 units centred on its origin.

**Not yet live-verified.** Collider solidity and invisibility in game, bot behaviour against builds, arrow hits on bots, auto-sprint, the loadout restore and `/cs2craft loadout`.

# Ramps, wall occlusion, round reset — 2026-10-11

**Ramp sticking (offline reproduction).** A scratch simulator compiled the unmodified `TriCollider`/`SkyTri` against the real `de_dust2.c2map` (433,050 triangles). It walked, sprinted and sprint-jumped up and down every walkable slope triangle of at least 0.5 block², 19,158 runs in all. The collider itself rarely stalls: plain walks and sprints stall about 0.1% of the time, and sprint-jump stalls were real clip walls.

The real problem was the body-fit filter in `BlockCollisionsMixin`. It used a box test with 0.05 blocks of slope tolerance and rejected standing on 31.4% of grounded ramp ticks, and crouching on 30.7%. Minecraft's pose fallback therefore chose SWIMMING (crawl): a 0.6-block box, 0.4 eye height and 0.3 speed. The same filter feeds the server's silent "colliding with anything new" teleport-back.

After the fix (`TriCollider.blocksBody`, same cylinder/step rules as movement), standing is rejected on 0.45% of ticks. The remainder is real contact: steps exactly 0.6 tall, wall corners and overhangs. All 27 Dust II ceilings 1.55–1.75 above the floor still reject standing. Three new `TriColliderTest` cases cover the ramp, ceiling and wall contact; 29/29 Minecraft tests pass.

**Occlusion.** Builds pass, and the native WARP occluder hide/clear test passes on the new position-only depth path. Not yet verified in a live CS2 session: whole-map streaming, retention across death and round start, and the round block reset. `css_cs2craft_status` reports occluder regions sent and on the GPU.

**Deployment.** Server, bridge and renderer were staged in runtime/pending-update and installed with Install-PendingUpdate.ps1 while CS2 was closed. Minecraft changes apply on the next `runClient` launch.

# Local death/respawn and raw-input patch integration

Applied patches/death-respawn-mouselook.patch to the actual source, with a previous-life event barrier when entering respawn and before drive mode resumes. Builds pass; 37 bridge checks pass, including an unreadable respawn snapshot followed by discarding the old death event and preserving later events. Native HUD, world and input self-tests pass. Real SDL input and repeated live respawns still require acceptance testing.

The built server and renderer are staged in runtime/pending-update with SHA-256 checksums. Start-CS2.ps1 installs a pending update before launching, after verifying CS2 is closed. The current running game retains its old DLLs until restart.

# Control regression fixes � 2026-10-11

The second live Dust II run used a fresh test world and the rebuilt private runtime. Evidence: dist/control-fixes/client-gameplay.json; runtime/cs2-20261011-152503-346.stdout.log; .cache/minecraft-controls2.log.

- Walking without jumping: 3.724 blocks over a two-second strafe. No Minecraft "moved wrongly" corrections.
- Incoming bridge damage: the attacker was placed behind the player, with open space in the direction of recoil. Knockback moved 1.195 blocks away and stayed there. Server velocity was also directed away. The first test ended against a wall and correctly produced no displacement; it is not knockback acceptance evidence.
- Camera: 600 relative mouse counts passed through the native mouse handler and the normal server camera path. Host and Minecraft yaw both changed from -180 to -147 degrees. No forced host-angle override was used.
- Sword: bot health 100 -> 65; zero Source weapon-fire events and CS2 ammunition remained 13.
- Crouch: eye height 1.62 -> 1.27 -> 1.62. Steve visible in third person; body and movement restored on detach.
- Floor: 72 comparisons, zero mismatches, maximum error 0.000029 Source units; final feet-to-floor gap zero.
- Builds: 34 bridge checks, 26 Java geometry checks, native HUD/world GPU tests and relative-mouse routing/reset tests passed.

The physical desktop input check timed out waiting for Computer Use app access. The mouse regression injects decoded relative input into the same native handler, bypassing Windows device delivery. Sword input was injected into Minecraft's bridge, so ammunition stability alone does not prove physical clicks are suppressed. Suppression now consumes raw mouse/button messages and replicates primary/secondary firing locks to each CS2 weapon; physical acceptance remains to be checked. The incoming-damage test injects the same hurt event used by the CS2 damage hook rather than relying on a bot firing. These are focused local tests, not a complete traversal or multiplayer verification.

# Collision and Steve update � 2026-10-11

The deployed private runtime now uses extracted map physics and a dedicated Minecraft camera.

- Dust II export: 433,050 exact triangles, including 12,339 invisible player-clip triangles. Clip-only geometry affects movement but not block targeting or depth occlusion.
- Final build: 31 C# bridge/geometry checks, 26 Java collision/ray/coverage tests, and native HUD/world GPU tests passed.
- Final live floor comparison: 53 valid floor hits matched CS2's native traces, zero missing-surface mismatches, maximum height error 0.0000191 Source units. The grid contains 81 casts; starts inside geometry are skipped and matching misses are not counted as floor hits.
- Final pawn floor gap at rest: 0.03677 Source units.
- Crouch eye height: 1.62 -> 1.27 -> 1.62 Minecraft blocks.
- F5 behind/front views captured Steve with equipped armor. The local CS2 body and weapon attachments were hidden, then restored on detach.
- Movement, inventory open/transfer/close, and sword damage 100 -> 65 remained working.
- All 125 nearby collision regions were ready at the first render checkpoint; zero rejected render packets.
- Unknown-floor and horizontal region boundaries are covered by regression tests. Clearance tests distinguish floor contact, a low ceiling and crouched height.

Evidence is in dist/collision-update/final, including client-gameplay.json, client-crouch.png, client-steve-back.png and client-steve-front.png. The final CS2 log is runtime/cs2-20261011-095138-017.stdout.log; Minecraft log is .cache/minecraft-collision-final.log.

The final placement click hit a bot, so that particular run is not evidence of block placement. Earlier update runs placed blocks against the extracted map; the second run's floor/camera captures are in dist/collision-update. Placement grid selection has separate positive/negative/fractional coordinate tests. Blocks no longer deliberately embed in fractional floors, but grid alignment can leave a gap.

These checks cover several CT spawn locations, not a complete manual traversal of Dust II. Dynamic map geometry, moving-body occlusion, camera interpolation and physical input acceptance remain open. Existing Minecraft builds and gameplay saves were not modified by the probes; separate test worlds were used.

---

# Verification record — 2026-10-10

## Build and automated checks

- .NET 10 bridge/server/probe builds: zero warnings/errors.
- 26 bridge checks: coordinate/yaw roundtrips, layout, exclusive ownership, seqlocks, invalid floats, UTF-8 bounds, backpressure/wrap, events, byte-ring corruption, collision records, triple-buffer ownership and memory bounds.
- Minecraft 26.3 Fabric build: 21 inherited SkyRay/TriCollider checks.
- Native Visual Studio x64 build and D3D11 WARP HUD readback pass.
- New world renderer WARP tests cover textured projection, turning away, malformed/NaN packet rejection without losing a valid mesh, atlas patching, depth-only occlusion, section removal, cutout/alpha blending, avatar texture batches and clearing GPU resources.

## Actual Minecraft, synthetic host

Earlier fixtures captured real HUD/inventory frames and sword events. A new mesh-capture run recorded 3,340 HUD frames, 49,575 render packets, two nonempty and two empty section updates. It wrote `dist/world-probe/minecraft-world.c2rf`.

The native renderer replayed that capture: 5 resource/mesh packets, one section, 36 vertices / 1,152 mesh bytes. Its rendered plank texture/orientation was visually inspected in `dist/world-probe/minecraft-world.png`. This image is an offscreen fixture, not CS2.

The final probe source explicitly selects hotbar slot 1 before its sword test; earlier repeated-save fixtures could start with a different held item.

## Actual CS2 client on Dust II

The renderer hook loaded into the private CS2 client. Captures are from the game's actual DX11 backbuffer, after both Minecraft world geometry and HUD composition.

Final successful live test:
- 126 captured Minecraft HUD frames and 190 world-compositor frames at the first checkpoint.
- One populated block section, zero rejected render packets, successful backbuffer capture.
- 27 sampled collision regions.
- Minecraft movement moved the CS2 pawn **2.05449 blocks** during the scripted strafe.
- A Minecraft diamond sword hit a real CS2 bot for **7 Minecraft damage / 35 CS2 damage**: health **100 → 65**.
- Inventory opened; scripted cursor/click events moved the sword from hotbar slot 1 into the main inventory. The screenshot confirms the new slot and empty original slot.
- Inventory closed; both original CS2 movement-type fields restored.
- Detach completed without the earlier null-owner cleanup error.
- After stopping the test CS2 process, Minecraft saved and exited normally.

Evidence:
- `dist/verified-client/render.json`
- `dist/verified-client/gameplay.json`
- `dist/verified-client/client-probe.png`
- `dist/verified-client/client-combat.png`
- `dist/verified-client/client-inventory.png`
- `runtime/cs2-20261010-231231-449.stdout.log`
- `.cache/minecraft-final-probe.log`

The earlier renderer test exposed a 1.027-block vertical mismatch. Restricting the collision mask and refining floor surfaces reduced the mismatch to under one Source unit at tested spawns. This does not establish accuracy across the map; some cells still use coarse fallback cubes.

## Limits of the evidence

The scripted inventory test injects bridge input. It does not validate physical raw mouse/text/wheel routing, unusual keyboard layouts or desktop focus transitions. Only the first-person camera path was exercised.

No claim is made for exact world-depth matching, dynamic occlusion, all-map movement, bot collision/pathfinding around builds, incoming bot fire, full death/respawn handling, multiplayer or a polished launcher profile.

The dedicated-server fresh-log smoke test also passed previously; `runtime/smoke-result.json` records it. Source/runtime setup uses private copies, not links to Steam. Runtime, saves, game downloads and captured textures are ignored local artifacts and excluded from the source package.
