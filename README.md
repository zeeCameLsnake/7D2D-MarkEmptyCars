# Mark Empty Cars (for 7 Days to Die 3.0 Experimental)

**Author:** zeeCameLsnake  
**Version:** 1.1.1

## Description
A Quality of Life (QoL) mod for 7 Days to Die (3.0 Experimental) that puts an end to re-checking the same empty vehicles!
**Mark Empty Cars** automatically places an open chest symbol (NavObject) inside any vehicle as soon as you have fully looted it.

## Features
- **Broad Vehicle Support:** Detects sedans, trucks, SUVs, minivans, police cars, ambulances, fire trucks, delivery vans, tractors, and buses.
- **Cross-Session Persistence:** Your looted vehicles are saved! Markers will remain perfectly in place even after you restart the game or reload your save.
- **Auto-Cleanup (Anti-Ghosting):** If a vehicle is destroyed (e.g. wrenched down) or if loot respawns, the marker will automatically disappear to keep your UI clean.
- **Lightweight & Robust:** Highly optimized background C# Harmony scanner designed to have virtually zero impact on performance.

## Multiplayer Compatibility
Since v1.1.0 this mod provides two modes of operation for multiplayer:

1. **Full Mode (Recommended):** Installed on both Server and Client.
   - Provides the custom open chest marker (range 45m - configurable in nav_objects.xml).
   - **Shared Markers in Co-Op:** Because the mod reads the vanilla network data, markers are automatically shared! If your friend loots a car, you will also see a marker on that car.
   - **Pro Tip:** In Co-Op, let the player with the highest "Grease Monkey" skill loot the vehicles first to maximize vehicle crafting magazine drops!

2. **Client-Only Fallback Mode:** Installed only on your Client.
   - You can use the mod on **ANY** server (as long as EAC is off).
   - The mod automatically detects that the server lacks the custom marker and switches to a vanilla marker (default: vending machine symbol, 20m range).
   - You can easily change this fallback marker to another vanilla symbol by editing the `config.txt` file.

## Installation
*Requires EAC (Easy Anti-Cheat) to be turned OFF.*

1. Download the mod and extract the archive.
2. Place the `zCs_MarkEmptyCars` folder directly into your 7 Days to Die Mods directory.
   Example: `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\`
3. Launch the game!

## Customization (Change Marker Range)
By default, the marker is visible up to 45 meters away. If you want to increase or decrease this distance:
1. Open the `Config\nav_objects.xml` file inside the mod's folder.
2. Find the line: `<property name="max_distance" value="45"/>`
3. Change the value `45` to whatever distance you prefer.

## Uninstallation
Simply delete the `zCs_MarkEmptyCars` folder from your Mods directory. Any markers left in your savegame will safely be ignored by the vanilla game without causing errors.

## Troubleshooting & Debugging
If you encounter any issues (e.g. markers not appearing or ghosting), you can easily enable the mod's built-in Debug Mode:
1. Open the `config.txt` file located inside the mod's folder (`Mods/zCs_MarkEmptyCars/config.txt`).
2. Change the line `debug=false` to `debug=true` and save the file.
3. Start the game and play for a few minutes to let the issue occur.
4. The mod will now print diagnostic information to your game console (F1) and the output log file.
5. Provide the newest (most recent) output log file when reporting the problem. You can find your log files here:
   `%APPDATA%\7DaysToDie\logs` (`C:\Users\USERNAME\AppData\Roaming\7DaysToDie\logs`)
6. To disable the debug logs, simply change it back to `debug=false`.

## Technical Details
This is a C# Harmony mod. It uses deep reflection to dynamically hook into the loot window to instantly mark cars, and features a background scanner hooked into the game's main `UpdateTick` to verify chunk loaded states. This ensures markers are safely managed without ghosting in 3.0 Experimental.
