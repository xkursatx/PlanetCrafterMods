# Auto Collector Container v3.0.0

A BepInEx mod for **The Planet Crafter** that adds intelligent automation to containers.

---

## 🎯 Features

### **AutoCollect**
Automatically collects items from the ground within a configurable radius.
- **Radius**: 20m (default, configurable)
- **MaxPerItem**: Limit how many of each item type to collect
- **Smart Filtering**: Set to 0 for unlimited, or specify exact limits per item

### **AutoForward**
Automatically transfers items to named target containers.
- **Radius**: 50m (default, configurable)
- **MaxPerItem**: Target container stops receiving when it reaches the limit per item
- **Named Targets**: Only containers with custom names can be selected as targets

---

## 🎮 How to Use

### **Step 1: Install the Mod**
1. Install [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)
2. Copy `AutoCollector.dll` to `BepInEx/plugins/`
3. Launch the game

### **Step 2: Set Up Containers**
1. Place containers in your base
2. **Important**: Give containers custom names using the game's built-in naming system
   - Open container inventory
   - Look for the text input field at the top
   - Enter a name (e.g., "Iron Storage", "Main Container")

### **Step 3: Configure AutoCollect**
1. Open any container inventory
2. Click **AutoCollect** toggle (turns green when ON)
3. Adjust **MaxPerItem** using +/- buttons
   - `0` = Unlimited (collects everything within radius)
   - `10` = Collects max 10 of each item type
4. Items on ground within 20m will be automatically collected every 5 seconds

### **Step 4: Configure AutoForward**
1. Ensure target container has a **custom name**
2. Open source container inventory
3. Click **AutoForward** toggle (turns green when ON)
4. Click **Select Target** button
5. Choose target container from the list
6. Adjust **MaxPerItem** for target container
   - `10` = Target stops receiving when it has 10 of each item

---

## ⚙️ Configuration

Edit `BepInEx/config/xkursat.AutoCollector.cfg`:

```ini
[General]
Enabled = true
IsDebug = false          # Enable for detailed logs
IncludeMinables = true   # Collect minable resources (ores, rocks)
UpdateInterval = 5       # Seconds between collection/forward cycles

[AutoCollect]
Radius = 20              # Ground collection radius (meters)
MaxPerItem = 0           # Max per item type (0 = unlimited)

[AutoForward]
Radius = 50              # Target container search radius (meters)
MaxPerItem = 10          # Max per item in target container
```

---

## 🧪 Design Evolution: Why Container-Only?

### **What We Tried**

#### **Attempt 1: Producer Device Integration**
**Goal**: Auto-forward from production machines (VegetableGrower, Beehive, etc.)

**Problem**: 
- `VegetableGrower` has **2 inventories**: Input (seeds) + Output (vegetables)
- When accessing the machine, game only opens the **input inventory**
- Our mod would auto-forward **seeds** instead of **vegetables** ❌
- Production stopped because seeds were removed!

**Lesson**: Multi-inventory devices are too complex to handle reliably.

#### **Attempt 2: Output Inventory Detection**
**Goal**: Detect and target only the output inventory

**Problem**:
```json
// Game save data structure
{"id":207895838,"gId":"VegetableGrower2","liId":24,"siIds":"25"}
  ↓
{"id":24,"woIds":"203807224","size":1}  ← INPUT (shown to player)
{"id":25,"woIds":"205757019","size":1}  ← OUTPUT (hidden)
```
- `liId` = Linked inventory (what opens when player clicks)
- `siIds` = Secondary inventory (hidden, output)
- No reliable API to access secondary inventory from our mod context

**Lesson**: Game's inventory system is designed for player interaction, not programmatic access.

#### **Attempt 3: Device Blacklist**
**Goal**: Skip problematic devices, only automate "safe" ones

**Problem**:
- Required maintaining a hardcoded list of every device
- New game updates would break the mod
- Still had edge cases (Incubator, custom modded machines)

**Lesson**: Fighting against the game's design is unsustainable.

---

### **Final Solution: Container-Only System** ✅

**Why This Works:**
1. **Single Inventory**: Containers always have exactly one inventory
2. **Player Control**: Users decide the automation flow
3. **Game Integration**: Uses built-in container naming system
4. **Flexible**: Chain containers however you want
5. **Future-Proof**: Works with any game version

**Example Setup:**
```
[Ground Items] 
    ↓ (AutoCollect)
[Collecting Container] 
    ↓ (AutoForward, MaxPerItem=20)
[Iron Storage]
    ↓ (AutoForward, MaxPerItem=10)
[Processing Container]
```

**Result**: 
- Collecting container picks up everything from ground
- Distributes 20 Iron to dedicated storage
- Sends 10 Iron to processing
- User has full control over flow logic

---

## 🐛 Debugging

Enable debug logs in config:
```ini
[General]
IsDebug = true
```

**Log Location**: `BepInEx/LogOutput.log`

**What You'll See**:
```
=== AutoCollect Cycle Start ===
MaxPerItem setting: 10 (0 = unlimited)
Current inventory items:
  - Iron: 5 items
  - Cobalt: 12 items
Scanning 23 grabbable items within 20m radius
  SKIPPED: Cobalt (already have 12/10)
  COLLECTED: Iron (now have 6/10)
  COLLECTED: Iron (now have 7/10)
=== AutoCollect Summary ===
Total collected: 2
Total skipped by limit: 5
```

---

## 🔧 Troubleshooting

### **AutoCollect not working**
- Check `IsDebug = true` and read logs
- Verify items are within 20m radius
- Check if container is full
- Try setting `MaxPerItem = 0` for testing

### **AutoForward not working**
- **Target container MUST have a custom name**
- Verify target is within 50m
- Check logs for "Target container not found" errors
- Try increasing `Radius` in config

### **"No Named Containers" message**
- You forgot to name your containers!
- Open container → Enter name in text field at top
- Only named containers appear in selection list

---

## 📝 Technical Notes

### **Why Named Containers Only?**
- Prevents accidental transfers to wrong containers
- Makes automation intentional and visible
- Uses game's existing naming system (no custom save data)

### **MaxPerItem Logic**

**AutoCollect**: 
```
If MaxPerItem = 10:
  - Container has 7 Iron → Collects 3 more (stops at 10)
  - Container has 10 Iron → Skips Iron on ground
  - Container has 15 Iron → Skips Iron (already over limit)
```

**AutoForward**:
```
If Target MaxPerItem = 10:
  - Target has 7 Iron → Transfers 3 more (stops at 10)
  - Target has 10 Iron → Stops transferring Iron
  - Source keeps remaining Iron for other targets
```

### **Performance**
- Update interval: 5 seconds (configurable)
- Only scans items within radius (spatial optimization)
- Dictionary-based counting (O(n) per cycle)
- No continuous polling, event-based updates

---

## 🤝 Contributing

Found a bug? Have an idea?
- Open an issue on GitHub
- Include log file (`BepInEx/LogOutput.log`)
- Describe your container setup

---

## 📜 License

MIT License - Feel free to modify and redistribute

---

## 🙏 Credits

- **Game**: The Planet Crafter by Miju Games
- **Framework**: BepInEx
- **Developer**: xkursat

---

## 📋 Changelog

### v3.0.0
- **BREAKING CHANGE**: Container-only system
- Removed producer device support (VegetableGrower, Beehive, etc.)
- Added MaxPerItem controls (UI + config)
- Added named container requirement for AutoForward
- Comprehensive debug logging
- Improved UI with +/- buttons for MaxPerItem

### v2.x
- Attempted multi-inventory device support
- Various producer device integrations (deprecated)

### v1.0
- Initial release
- Basic AutoCollect functionality

---

**Happy Automating! 🚀**
