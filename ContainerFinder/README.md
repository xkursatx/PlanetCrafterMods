# Container Finder

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](https://github.com/xkursatx/PlanetCrafterMods)
[![Game](https://img.shields.io/badge/game-The%20Planet%20Crafter-green.svg)](https://store.steampowered.com/app/1284190/The_Planet_Crafter/)

Locate all containers on the planet with 3D markers and distance indicators.

## Overview

Press **G** to scan and see all containers marked with floating 3D labels in the game world. Never lose track of your storage or miss a golden container again.

**Perfect for:**
- Finding lost containers
- Treasure hunting
- Base organization
- Resource management

## Features

- **3D World Markers** - Floating labels appear on containers showing distance
- **Smart Scaling** - Markers resize based on distance
- **Color Coded** - Cyan for normal, yellow for golden containers
- **On-Screen List** - Compact distance-sorted list in corner
- **Configurable** - Adjust scan radius and hotkey

## Installation

1. Install [BepInEx 5.4.23+](https://github.com/BepInEx/BepInEx)
2. Extract mod to `BepInEx/plugins/`
3. Launch game

## Usage

1. Press **G** to scan for containers
2. See 3D markers on all containers within range
3. Markers show container type and distance
4. Display lasts 30 seconds, press G to refresh

**Status Indicators:**
- ?? Green: Ready to scan
- ?? Yellow: Scanning...

## Configuration

Edit `BepInEx/config/xkursat.ContainerFinder.cfg`:

```ini
[Hotkeys]
ScanKey = <Keyboard>/g       # Change scan key

[Options]
MaxDistance = 5000.0         # Scan radius (5000m = entire planet)
ShowGoldenOnly = false       # Filter to golden containers only
```

## Supported Containers

- Container1, Container2, Container3
- GoldenContainer
- ContainerAqualis, ContainerGoldenAqualis
- ContainerToxic1
- StarformContainer
- canister

## Compatibility

- **Game Version:** The Planet Crafter v1.618+
- **BepInEx:** 5.4.23+
- **Conflicts:** None known

## Tips

- **Golden hunting:** Set `ShowGoldenOnly = true` in config
- **Performance:** Reduce `MaxDistance` to 500m if needed
- **Combo:** Works great with Auto Collector mod!

## Credits

Created by [xkursat](https://github.com/xkursatx)  
Based on [aedenthorn's template](https://github.com/aedenthorn/PlanetCrafterMods)

## License

MIT License - Free for personal use

---

[Report Issues](https://github.com/xkursatx/PlanetCrafterMods/issues) • [View Source](https://github.com/xkursatx/PlanetCrafterMods)
