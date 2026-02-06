# Auto Collector Container

[![Version](https://img.shields.io/badge/version-2.1.0-blue.svg)](https://github.com/xkursatx/PlanetCrafterMods)
[![Game](https://img.shields.io/badge/game-The%20Planet%20Crafter-green.svg)](https://store.steampowered.com/app/1284190/The_Planet_Crafter/)

Turn any container into an automatic item collector. Place, enable, and forget!

## Overview

Auto Collector adds automatic item collection to containers. Simply toggle the feature and the container will collect all nearby items within your configured radius.

**Perfect for:**
- Automated mining setups
- Resource gathering points
- Keeping your base organized
- AFK farming operations

## Features

- **One-Click Toggle** - Enable/disable via in-game button
- **Visual Feedback** - Clear ON/OFF indicators
- **Persistent Settings** - Each container remembers its state
- **Smart Collection** - Stops when full, works with all container types
- **Configurable** - Adjust radius, interval, and item types

## Installation

1. Install [BepInEx 5.4.23+](https://github.com/BepInEx/BepInEx)
2. Extract mod to `BepInEx/plugins/`
3. Launch game

## Usage

1. Open any container inventory
2. Click the **"AutoCollect: OFF"** button at bottom of screen
3. Button turns green when enabled
4. Items within radius are automatically collected

## Configuration

Edit `BepInEx/config/xkursat.AutoCollector.cfg`:

```ini
[Options]
CollectionRadius = 50.0      # Collection range in meters
CollectionInterval = 5.0     # Seconds between scans
IncludeMinables = true       # Collect ores and rocks
```

## Compatibility

- **Game Version:** The Planet Crafter v1.618+
- **BepInEx:** 5.4.23+
- **Conflicts:** None known

## Credits

Created by [xkursat](https://github.com/xkursatx)  
Based on [aedenthorn's template](https://github.com/aedenthorn/PlanetCrafterMods)

## License

MIT License - Free for personal use

---

[Report Issues](https://github.com/xkursatx/PlanetCrafterMods/issues) • [View Source](https://github.com/xkursatx/PlanetCrafterMods)
