# Installing CS2Craft on another machine

CS2Craft runs on Windows only. The project folder holds source code; each machine builds it and prepares its own private copy of CS2. Nothing in the Steam installation is modified. Run the steps below in PowerShell from the project folder.

## 1. Requirements

| Install | Notes |
|---|---|
| Windows 10 or 11, 64-bit, with a DirectX 11 graphics card | The renderer hooks CS2's DirectX 11 output. |
| [Steam](https://store.steampowered.com/about/) with **Counter-Strike 2** | Let it finish downloading. Any Steam library folder works. |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | The SDK, not only the runtime. |
| [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) (Community is fine) | Workload **Desktop development with C++**. Keep its default **C++ CMake tools for Windows** and **Windows 11 SDK** (or 10 SDK) components ticked. Visual Studio 2022 specifically: the native build uses its CMake generator. |
| **Java 25** | Either install the [Minecraft Launcher](https://www.minecraft.net/download), which brings Java 25 to the path the scripts use by default, or install [Eclipse Temurin 25](https://adoptium.net/temurin/releases/?version=25) and pass `-JavaHome` to the scripts. |
| Internet on the first run | The scripts download Metamod, CounterStrikeSharp 1.0.376 (checksum-checked), NuGet packages, Gradle, Fabric and Minecraft 26.3. |
| About **60 GB free** on the project's drive | The private CS2 copy is about 53 GB; builds and caches add a few GB. |

Minecraft runs as a development client under an offline name (`CS2Crafter`), so no Minecraft login is asked for. Owning Minecraft Java Edition is still the proper way to use it.

## 2. Copy the project

Copy the project folder without `runtime/`, `.cache/`, `dist/` and any `bin/`, `obj/`, `build/` or `.gradle/` folders; the new machine recreates them. A zip of the folder or a private git repository both work.

Windows marks files from a downloaded zip as untrusted, which stops PowerShell scripts from running. Once, in the project folder:

```powershell
Get-ChildItem -Recurse | Unblock-File
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## 3. Check the machine

```powershell
.\tools\Check-Prerequisites.ps1
```

It only reads: Windows, Steam and CS2, .NET, Visual Studio C++, CMake, Windows SDK, Java 25, disk space and internet access. Each line is PASS, WARN or FAIL with what to do. It ends with the next command to run. If you installed Java somewhere else, add `-JavaHome "C:\path\to\jdk-25"`. If CS2 is not found, add `-CS2Root "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"`.

## 4. Build, prepare, play

```powershell
.\tools\Build.ps1
```

Builds the renderer, the CS2 plugin, the map exporter and the Minecraft mod, and runs every test (a few minutes; longer the first time while Gradle downloads Minecraft). Add `-JavaHome` if Java 25 is not the Minecraft Launcher's.

```powershell
.\tools\Prepare-CS2.ps1
```

Creates the private CS2 copy in `runtime/`, installs Metamod, CounterStrikeSharp, the plugin and the renderer. Add `-CS2Root` if CS2 was not found. Takes a while: it copies about 53 GB.

```powershell
.\tools\Play.ps1
```

Starts CS2 (Steam must be running) and the hidden Minecraft client. In CS2, join a team, open the console with **~** and enter `css_cs2craft_play`. The first launch also builds Dust II's collision file and downloads Minecraft's assets, so allow a few minutes. Add `-JavaHome` here too if needed.

CS2 starts with `-insecure`: VAC is off and only local games with bots work. Only Dust II is prepared.

## Settings

The plugin's settings file is created on first launch at `runtime\game\csgo\addons\counterstrikesharp\configs\plugins\CS2Craft.Server\CS2Craft.Server.json`. The options are listed in the [README](../README.md). It lives in `runtime/`, so copy it over from another machine if you want the same settings.

## Updating

- **After a CS2 update:** run `.\tools\Prepare-CS2.ps1` again to refresh the private copy.
- **After updating the project files:** run `.\tools\Build.ps1`, then `.\tools\Prepare-CS2.ps1` (it installs the new plugin and renderer). Close CS2 first.

## Troubleshooting

| Message | Fix |
|---|---|
| `...cannot be loaded because running scripts is disabled` or `is not digitally signed` | Step 2: `Unblock-File` and `Set-ExecutionPolicy`. |
| `CS2 not found. Pass -CS2Root...` | Pass the folder that contains `game\bin\win64\cs2.exe`. |
| `Pass -JavaHome pointing to a Java 25 installation.` | Install Java 25 (see Requirements) and pass its folder, the one that contains `bin\java.exe`. |
| `Install the Visual Studio C++ tools and CMake` / `Native configure failed` | Add the C++ workload and CMake in the Visual Studio Installer; the check script shows which is missing. |
| `Checksum mismatch` | A download was corrupted or changed: delete `.cache\downloads` and rerun `Prepare-CS2.ps1`. |
| `Run tools/Build.ps1 and tools/Prepare-CS2.ps1 first.` | Do steps 4 in order. |
| In CS2: `An existing Minecraft/CS2Craft process holds the bridge` | A previous Minecraft is still saving. Wait for it to exit (or end `java.exe` in Task Manager) and try again. |

## Removing

Delete the project folder; `runtime\` holds everything CS2Craft created. The Steam installation of CS2 was never changed.
