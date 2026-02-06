using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AutoCollector
{
    [BepInPlugin("xkursat.AutoCollector", "Auto Collector Container", "3.0.0")]
    public class BepInExPlugin : BaseUnityPlugin
    {
        public static BepInExPlugin context;

        // General settings
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> includeMinables;
        
        // AutoCollect settings
        public static ConfigEntry<float> autoCollectRadius;
        public static ConfigEntry<int> autoCollectMaxPerItem;
        
        // AutoForward settings
        public static ConfigEntry<float> autoForwardRadius;
        public static ConfigEntry<int> autoForwardMaxPerItem;
        
        // General timing
        public static ConfigEntry<float> updateInterval;

        public static void Dbgl(string str = "", LogLevel logLevel = LogLevel.Debug)
        {
            if (isDebug.Value)
                context.Logger.Log(logLevel, str);
        }

        private void Awake()
        {
            context = this;
            
            // General
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", false, "Enable debug logs");
            includeMinables = Config.Bind<bool>("General", "IncludeMinables", true, "Include minable resources (ores, rocks, etc)");
            updateInterval = Config.Bind<float>("General", "UpdateInterval", 5f, "How often to collect/forward items (seconds)");
            
            // AutoCollect
            autoCollectRadius = Config.Bind<float>("AutoCollect", "Radius", 20f, "Radius to collect items from ground (meters)");
            autoCollectMaxPerItem = Config.Bind<int>("AutoCollect", "MaxPerItem", 0, "Max items per type to collect (0 = unlimited)");
            
            // AutoForward
            autoForwardRadius = Config.Bind<float>("AutoForward", "Radius", 50f, "Radius to search for target containers (meters)");
            autoForwardMaxPerItem = Config.Bind<int>("AutoForward", "MaxPerItem", 10, "Max items per type in target container (0 = unlimited)");

            Harmony.CreateAndPatchAll(typeof(ContainerPatches));
            Dbgl("AutoCollector v3.0.0 loaded - Container-Only System");
        }
    }

    /// <summary>
    /// Enhanced AutoCollector with MaxPerItem support
    /// </summary>
    public class AutoCollectorComponent : MonoBehaviour
    {
        private float timer = 0f;
        private InventoryAssociated inventoryAssociated;
        private Inventory containerInventory;
        public bool autoCollectEnabled = false;
        public bool autoForwardEnabled = false;
        private int containerWorldObjectId = -1;
        public bool isReady = false;
        public int targetContainerId = -1;

        void Awake()
        {
            inventoryAssociated = GetComponent<InventoryAssociated>();
            if (inventoryAssociated != null)
            {
                WorldObjectAssociated woa = GetComponent<WorldObjectAssociated>();
                if (woa != null && woa.GetWorldObject() != null)
                {
                    containerWorldObjectId = woa.GetWorldObject().GetId();
                    LoadState();
                    BepInExPlugin.Dbgl($"Container {containerWorldObjectId} initialized - AutoCollect: {autoCollectEnabled}, AutoForward: {autoForwardEnabled}");
                }
                
                inventoryAssociated.GetInventory((inventory) => {
                    containerInventory = inventory;
                    isReady = true;
                });
            }
            else
            {
                Destroy(this);
            }
        }

        void OnDestroy()
        {
            SaveState();
        }

        void Update()
        {
            if (!BepInExPlugin.modEnabled.Value || !isReady || containerInventory == null)
                return;

            timer += Time.deltaTime;
            if (timer >= BepInExPlugin.updateInterval.Value)
            {
                timer = 0f;
                
                if (autoCollectEnabled)
                {
                    CollectNearbyItems();
                }
                
                if (autoForwardEnabled && targetContainerId > 0)
                {
                    ForwardItemsToTarget();
                }
            }
        }

        public void ToggleAutoCollect()
        {
            autoCollectEnabled = !autoCollectEnabled;
            BepInExPlugin.Dbgl($"AutoCollect toggled: {(autoCollectEnabled ? "ON" : "OFF")}");
            if (autoCollectEnabled) timer = 0f;
            SaveState();
        }

        public void ToggleAutoForward()
        {
            autoForwardEnabled = !autoForwardEnabled;
            BepInExPlugin.Dbgl($"AutoForward toggled: {(autoForwardEnabled ? "ON" : "OFF")}");
            if (autoForwardEnabled) timer = 0f;
            SaveState();
        }

        public void SetTargetContainer(int targetId)
        {
            targetContainerId = targetId;
            SaveState();
            BepInExPlugin.Dbgl($"AutoForward target set to: {targetId}");
        }

        private void SaveState()
        {
            if (containerWorldObjectId >= 0)
            {
                PlayerPrefs.SetInt($"AC_{containerWorldObjectId}_Collect", autoCollectEnabled ? 1 : 0);
                PlayerPrefs.SetInt($"AC_{containerWorldObjectId}_Forward", autoForwardEnabled ? 1 : 0);
                PlayerPrefs.SetInt($"AC_{containerWorldObjectId}_Target", targetContainerId);
                PlayerPrefs.Save();
            }
        }

        private void LoadState()
        {
            if (containerWorldObjectId >= 0)
            {
                autoCollectEnabled = PlayerPrefs.GetInt($"AC_{containerWorldObjectId}_Collect", 0) == 1;
                autoForwardEnabled = PlayerPrefs.GetInt($"AC_{containerWorldObjectId}_Forward", 0) == 1;
                targetContainerId = PlayerPrefs.GetInt($"AC_{containerWorldObjectId}_Target", -1);
            }
        }

        /// <summary>
        /// Collect items from ground with MaxPerItem limit
        /// </summary>
        private void CollectNearbyItems()
        {
            if (containerInventory.IsFull())
            {
                BepInExPlugin.Dbgl("AutoCollect: Container is FULL, skipping collection");
                return;
            }

            int maxPerItem = BepInExPlugin.autoCollectMaxPerItem.Value;
            Dictionary<string, int> currentCounts = GetItemCounts(containerInventory);
            
            BepInExPlugin.Dbgl("=== AutoCollect Cycle Start ===");
            BepInExPlugin.Dbgl($"MaxPerItem setting: {maxPerItem} (0 = unlimited)");
            BepInExPlugin.Dbgl($"Current inventory items:");
            foreach (var kvp in currentCounts)
            {
                BepInExPlugin.Dbgl($"  - {kvp.Key}: {kvp.Value} items");
            }
            
            Vector3 containerPos = transform.position;
            int collectedCount = 0;
            int skippedByLimit = 0;
            Dictionary<string, int> foundItems = new Dictionary<string, int>();

            // Collect grabbable items
            ActionGrabable[] allGrabbables = FindObjectsByType<ActionGrabable>(FindObjectsSortMode.None);
            BepInExPlugin.Dbgl($"Scanning {allGrabbables.Length} grabbable items within {BepInExPlugin.autoCollectRadius.Value}m radius");
            
            foreach (ActionGrabable grabable in allGrabbables)
            {
                if (containerInventory.IsFull()) break;
                
                if (grabable == null || !grabable.GetCanGrab()) continue;
                
                float distance = Vector3.Distance(containerPos, grabable.transform.position);
                if (distance > BepInExPlugin.autoCollectRadius.Value) continue;

                WorldObjectAssociated woa = grabable.GetComponent<WorldObjectAssociated>();
                if (woa == null) continue;

                WorldObject worldObject = woa.GetWorldObject();
                if (worldObject == null || worldObject.GetGroup() == null) continue;

                string itemId = worldObject.GetGroup().GetId();
                
                // Track found items
                if (!foundItems.ContainsKey(itemId))
                    foundItems[itemId] = 0;
                foundItems[itemId]++;
                
                // Check MaxPerItem limit
                if (maxPerItem > 0)
                {
                    if (!currentCounts.ContainsKey(itemId))
                        currentCounts[itemId] = 0;
                    
                    if (currentCounts[itemId] >= maxPerItem)
                    {
                        skippedByLimit++;
                        BepInExPlugin.Dbgl($"  SKIPPED: {itemId} (already have {currentCounts[itemId]}/{maxPerItem})");
                        continue;
                    }
                }

                if (containerInventory.AddItem(worldObject))
                {
                    Destroy(grabable.gameObject);
                    worldObject.SetDontSaveMe(false);
                    collectedCount++;
                    
                    if (maxPerItem > 0)
                        currentCounts[itemId]++;
                    
                    BepInExPlugin.Dbgl($"  COLLECTED: {itemId} (now have {currentCounts[itemId]}{(maxPerItem > 0 ? "/" + maxPerItem : "")})");
                }
            }

            // Collect minables
            if (BepInExPlugin.includeMinables.Value)
            {
                ActionMinable[] allMinables = FindObjectsByType<ActionMinable>(FindObjectsSortMode.None);
                BepInExPlugin.Dbgl($"Scanning {allMinables.Length} minable items");
                
                foreach (ActionMinable minable in allMinables)
                {
                    if (containerInventory.IsFull()) break;
                    
                    if (minable == null) continue;
                    if (minable.GetComponentInParent<MachineAutoCrafter>() != null) continue;

                    float distance = Vector3.Distance(containerPos, minable.transform.position);
                    if (distance > BepInExPlugin.autoCollectRadius.Value) continue;

                    WorldObjectAssociated woa = minable.GetComponent<WorldObjectAssociated>();
                    if (woa == null) continue;

                    WorldObject worldObject = woa.GetWorldObject();
                    if (worldObject == null || worldObject.GetGroup() == null) continue;

                    string itemId = worldObject.GetGroup().GetId();
                    
                    // Track found items
                    if (!foundItems.ContainsKey(itemId))
                        foundItems[itemId] = 0;
                    foundItems[itemId]++;
                    
                    // Check MaxPerItem limit
                    if (maxPerItem > 0)
                    {
                        if (!currentCounts.ContainsKey(itemId))
                            currentCounts[itemId] = 0;
                        
                        if (currentCounts[itemId] >= maxPerItem)
                        {
                            skippedByLimit++;
                            BepInExPlugin.Dbgl($"  SKIPPED (minable): {itemId} (already have {currentCounts[itemId]}/{maxPerItem})");
                            continue;
                        }
                    }

                    if (containerInventory.AddItem(worldObject))
                    {
                        Destroy(minable.gameObject);
                        worldObject.SetDontSaveMe(false);
                        Managers.GetManager<DisplayersHandler>().GetItemWorldDisplayer()?.Hide();
                        collectedCount++;
                        
                        if (maxPerItem > 0)
                            currentCounts[itemId]++;
                        
                        BepInExPlugin.Dbgl($"  COLLECTED (minable): {itemId} (now have {currentCounts[itemId]}{(maxPerItem > 0 ? "/" + maxPerItem : "")})");
                    }
                }
            }

            BepInExPlugin.Dbgl($"=== AutoCollect Summary ===");
            BepInExPlugin.Dbgl($"Found items within radius:");
            foreach (var kvp in foundItems)
            {
                BepInExPlugin.Dbgl($"  - {kvp.Key}: {kvp.Value} found");
            }
            BepInExPlugin.Dbgl($"Total collected: {collectedCount}");
            BepInExPlugin.Dbgl($"Total skipped by limit: {skippedByLimit}");
            BepInExPlugin.Dbgl($"=== End Cycle ===");
        }

        /// <summary>
        /// Forward items to target container until it reaches MaxPerItem
        /// </summary>
        private void ForwardItemsToTarget()
        {
            if (containerInventory.GetInsideWorldObjects().Count == 0)
                return;

            // Find target container
            Inventory targetInventory = FindTargetContainer();
            if (targetInventory == null)
            {
                BepInExPlugin.Dbgl($"AutoForward: Target container {targetContainerId} not found!");
                return;
            }

            int maxPerItem = BepInExPlugin.autoForwardMaxPerItem.Value;
            Dictionary<string, int> targetCounts = GetItemCounts(targetInventory);
            
            int transferredCount = 0;
            var itemsToTransfer = new List<WorldObject>(containerInventory.GetInsideWorldObjects());

            foreach (var item in itemsToTransfer)
            {
                if (targetInventory.IsFull())
                    break;

                string itemId = item.GetGroup().GetId();
                
                // Check MaxPerItem limit in target
                if (maxPerItem > 0)
                {
                    if (!targetCounts.ContainsKey(itemId))
                        targetCounts[itemId] = 0;
                    
                    if (targetCounts[itemId] >= maxPerItem)
                        continue; // Target already has max of this item
                }

                if (targetInventory.AddItem(item))
                {
                    containerInventory.RemoveItem(item);
                    transferredCount++;
                    
                    if (maxPerItem > 0)
                        targetCounts[itemId]++;
                }
            }

            if (transferredCount > 0)
            {
                BepInExPlugin.Dbgl($"AutoForward: Transferred {transferredCount} items to container {targetContainerId}");
            }
        }

        private Inventory FindTargetContainer()
        {
            InventoryAssociated[] allInventories = FindObjectsByType<InventoryAssociated>(FindObjectsSortMode.None);

            foreach (var invAssoc in allInventories)
            {
                var woa = invAssoc.GetComponent<WorldObjectAssociated>();
                if (woa == null) continue;

                var worldObject = woa.GetWorldObject();
                if (worldObject == null) continue;

                if (worldObject.GetId() == targetContainerId)
                {
                    Inventory inv = null;
                    invAssoc.GetInventory((i) => { inv = i; });
                    return inv;
                }
            }

            return null;
        }

        private Dictionary<string, int> GetItemCounts(Inventory inventory)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            
            foreach (var item in inventory.GetInsideWorldObjects())
            {
                string itemId = item.GetGroup().GetId();
                if (!counts.ContainsKey(itemId))
                    counts[itemId] = 0;
                counts[itemId]++;
            }
            
            return counts;
        }

        public List<ContainerInfo> GetNearbyNamedContainers()
        {
            List<ContainerInfo> containers = new List<ContainerInfo>();
            Vector3 sourcePos = transform.position;

            InventoryAssociated[] allInventories = FindObjectsByType<InventoryAssociated>(FindObjectsSortMode.None);

            foreach (var invAssoc in allInventories)
            {
                if (invAssoc == inventoryAssociated) continue;

                var woa = invAssoc.GetComponent<WorldObjectAssociated>();
                if (woa == null) continue;

                var worldObject = woa.GetWorldObject();
                if (worldObject == null || worldObject.GetGroup() == null) continue;

                string groupId = worldObject.GetGroup().GetId();
                if (!groupId.StartsWith("Container")) continue;

                float distance = Vector3.Distance(sourcePos, invAssoc.transform.position);
                if (distance > BepInExPlugin.autoForwardRadius.Value) continue;

                // ONLY include containers that have text (name) set in game
                string containerText = worldObject.GetText();
                if (string.IsNullOrEmpty(containerText))
                    continue;

                containers.Add(new ContainerInfo
                {
                    id = worldObject.GetId(),
                    name = containerText,
                    distance = distance,
                    groupId = groupId
                });
            }

            return containers.OrderBy(c => c.distance).ToList();
        }
    }

    public class ContainerInfo
    {
        public int id;
        public string name;
        public float distance;
        public string groupId;
    }

    [HarmonyPatch]
    public class ContainerPatches
    {
        private static Dictionary<UiWindowContainer, GameObject> uiPanels = new Dictionary<UiWindowContainer, GameObject>();

        [HarmonyPatch(typeof(InventoryAssociated), "Start")]
        [HarmonyPostfix]
        static void AddAutoCollectorComponent(InventoryAssociated __instance)
        {
            if (!BepInExPlugin.modEnabled.Value)
                return;

            WorldObjectAssociated woa = __instance.GetComponent<WorldObjectAssociated>();
            if (woa == null) return;

            WorldObject worldObject = woa.GetWorldObject();
            if (worldObject == null || worldObject.GetGroup() == null) return;

            string deviceId = worldObject.GetGroup().GetId();

            // Only add to containers
            if (deviceId.StartsWith("Container"))
            {
                if (__instance.GetComponent<AutoCollectorComponent>() == null)
                {
                    __instance.gameObject.AddComponent<AutoCollectorComponent>();
                    BepInExPlugin.Dbgl($"Added AutoCollector component to {deviceId}");
                }
            }
        }

        [HarmonyPatch(typeof(UiWindowContainer), "OnOpen")]
        [HarmonyPostfix]
        static void AddUI(UiWindowContainer __instance)
        {
            if (!BepInExPlugin.modEnabled.Value)
                return;

            __instance.StartCoroutine(CreateUIWhenReady(__instance));
        }

        private static System.Collections.IEnumerator CreateUIWhenReady(UiWindowContainer window)
        {
            yield return null;

            var inventoryRightField = AccessTools.Field(typeof(UiWindowContainer), "_inventoryRight");
            if (inventoryRightField == null) yield break;

            Inventory containerInventory = (Inventory)inventoryRightField.GetValue(window);
            if (containerInventory == null) yield break;

            InventoryAssociated targetInventoryAssociated = null;
            InventoryAssociated[] allInventories = UnityEngine.Object.FindObjectsByType<InventoryAssociated>(FindObjectsSortMode.None);

            foreach (var invAssoc in allInventories)
            {
                bool found = false;
                invAssoc.GetInventory((inv) => {
                    if (inv == containerInventory)
                    {
                        targetInventoryAssociated = invAssoc;
                        found = true;
                    }
                });

                if (found) break;
            }

            if (targetInventoryAssociated == null) yield break;

            AutoCollectorComponent autoCollector = targetInventoryAssociated.GetComponent<AutoCollectorComponent>();
            if (autoCollector == null) yield break;

            float timeout = 2f;
            float elapsed = 0f;
            while (!autoCollector.isReady && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (uiPanels.ContainsKey(window))
            {
                if (uiPanels[window] != null)
                    UnityEngine.Object.Destroy(uiPanels[window]);
                uiPanels.Remove(window);
            }

            // Create full UI panel with both features
            GameObject panel = CreateControlPanel(window, autoCollector);
            uiPanels[window] = panel;
        }

        [HarmonyPatch(typeof(UiWindowContainer), "OnClose")]
        [HarmonyPostfix]
        static void CleanupUI(UiWindowContainer __instance)
        {
            if (uiPanels.ContainsKey(__instance))
            {
                if (uiPanels[__instance] != null)
                    UnityEngine.Object.Destroy(uiPanels[__instance]);
                uiPanels.Remove(__instance);
            }
        }

        private static GameObject CreateControlPanel(UiWindowContainer window, AutoCollectorComponent component)
        {
            GameObject panel = new GameObject("AutoCollectorPanel");
            panel.transform.SetParent(window.transform, false);

            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 10f);
            rect.sizeDelta = new Vector2(600f, 100f);

            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // LEFT SIDE: AutoCollect
            CreateFeatureGroup(panel, "AutoCollect", new Vector2(-200f, 0f), component.autoCollectEnabled,
                BepInExPlugin.autoCollectMaxPerItem.Value,
                () => component.ToggleAutoCollect(),
                (newValue) => {
                    BepInExPlugin.autoCollectMaxPerItem.Value = newValue;
                    UpdateControlPanel(panel, component);
                },
                () => UpdateControlPanel(panel, component));

            // RIGHT SIDE: AutoForward
            CreateFeatureGroup(panel, "AutoForward", new Vector2(150f, 0f), component.autoForwardEnabled,
                BepInExPlugin.autoForwardMaxPerItem.Value,
                () => component.ToggleAutoForward(),
                (newValue) => {
                    BepInExPlugin.autoForwardMaxPerItem.Value = newValue;
                    UpdateControlPanel(panel, component);
                },
                () => UpdateControlPanel(panel, component));

            // Target Selection Button (only if AutoForward is enabled)
            if (component.autoForwardEnabled)
            {
                CreateButton(
                    panel,
                    "Select Target",
                    new Vector2(0f, -30f),
                    new Vector2(150f, 30f),
                    () => {
                        ShowTargetSelectionDialog(window, component);
                    }
                );
            }

            return panel;
        }

        private static void UpdateControlPanel(GameObject panel, AutoCollectorComponent component)
        {
            var parent = panel.transform.parent;
            var window = parent.GetComponent<UiWindowContainer>();
            UnityEngine.Object.Destroy(panel);
            
            if (window != null && uiPanels.ContainsKey(window))
            {
                GameObject newPanel = CreateControlPanel(window, component);
                uiPanels[window] = newPanel;
            }
        }

        private static void CreateFeatureGroup(GameObject parent, string label, Vector2 basePos, bool isEnabled, int maxPerItem,
            UnityEngine.Events.UnityAction onToggle, Action<int> onMaxChange, UnityEngine.Events.UnityAction onUpdate)
        {
            // Toggle Button
            CreateToggleButton(
                parent,
                label,
                new Vector2(basePos.x, basePos.y + 30f),
                isEnabled,
                () => {
                    onToggle();
                    onUpdate();
                }
            );

            // MaxPerItem Label
            GameObject labelObj = new GameObject($"{label}MaxLabel");
            labelObj.transform.SetParent(parent.transform, false);

            RectTransform labelRect = labelObj.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(basePos.x - 80f, basePos.y - 15f);
            labelRect.sizeDelta = new Vector2(50f, 20f);

            Text labelText = labelObj.AddComponent<Text>();
            labelText.text = "Max:";
            labelText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 12;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleRight;
            labelText.raycastTarget = false;

            // Minus Button - FIXED: 30x30
            CreateButton(
                parent,
                "-",
                new Vector2(basePos.x - 30f, basePos.y - 15f),
                new Vector2(30f, 30f),
                () => {
                    int newValue = Mathf.Max(0, maxPerItem - 5);
                    onMaxChange(newValue);
                }
            );

            // Value Display - FIXED: 60px wide
            GameObject valueObj = new GameObject($"{label}Value");
            valueObj.transform.SetParent(parent.transform, false);

            RectTransform valueRect = valueObj.AddComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(0.5f, 0.5f);
            valueRect.anchorMax = new Vector2(0.5f, 0.5f);
            valueRect.pivot = new Vector2(0.5f, 0.5f);
            valueRect.anchoredPosition = new Vector2(basePos.x, basePos.y - 15f);
            valueRect.sizeDelta = new Vector2(60f, 30f);

            Image valueImg = valueObj.AddComponent<Image>();
            valueImg.color = new Color(0.2f, 0.2f, 0.2f);

            GameObject valueTextObj = new GameObject("Text");
            valueTextObj.transform.SetParent(valueObj.transform, false);

            RectTransform valueTextRect = valueTextObj.AddComponent<RectTransform>();
            valueTextRect.anchorMin = Vector2.zero;
            valueTextRect.anchorMax = Vector2.one;
            valueTextRect.sizeDelta = Vector2.zero;

            Text valueText = valueTextObj.AddComponent<Text>();
            valueText.text = maxPerItem == 0 ? "?" : maxPerItem.ToString();
            valueText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            valueText.fontSize = 16;
            valueText.color = Color.yellow;
            valueText.alignment = TextAnchor.MiddleCenter;
            valueText.fontStyle = FontStyle.Bold;
            valueText.raycastTarget = false;

            // Plus Button - FIXED: 30x30
            CreateButton(
                parent,
                "+",
                new Vector2(basePos.x + 30f, basePos.y - 15f),
                new Vector2(30f, 30f),
                () => {
                    int newValue = maxPerItem + 5;
                    onMaxChange(newValue);
                }
            );
        }

        private static GameObject CreateToggleButton(GameObject parent, string label, Vector2 position, bool isEnabled, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = new GameObject($"{label}Button");
            btnObj.transform.SetParent(parent.transform, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(150f, 35f);

            Image img = btnObj.AddComponent<Image>();
            img.color = isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
            colors.highlightedColor = isEnabled ? new Color(0.3f, 1f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
            colors.pressedColor = isEnabled ? new Color(0.1f, 0.6f, 0.1f) : new Color(0.2f, 0.2f, 0.2f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Text text = textObj.AddComponent<Text>();
            text.text = $"{label}\n{(isEnabled ? "ON" : "OFF")}";
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;

            return btnObj;
        }

        private static GameObject CreateButton(GameObject parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = new GameObject($"{label.Replace(" ", "")}Button");
            btnObj.transform.SetParent(parent.transform, false);

            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.6f, 0.9f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor = new Color(0.2f, 0.6f, 0.9f);
            colors.highlightedColor = new Color(0.3f, 0.8f, 1f);
            colors.pressedColor = new Color(0.1f, 0.4f, 0.7f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Text text = textObj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = label.Length <= 2 ? 16 : 11;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.raycastTarget = false;

            return btnObj;
        }

        private static void ShowTargetSelectionDialog(UiWindowContainer window, AutoCollectorComponent component)
        {
            var nearbyContainers = component.GetNearbyNamedContainers();
            
            if (nearbyContainers.Count == 0)
            {
                BepInExPlugin.Dbgl("No NAMED containers found within range!");
                ShowMessageDialog(window, "No Named Containers", "No containers with custom names found within 50m.\n\nName your containers using the game's text field first!");
                return;
            }

            // Create selection dialog
            GameObject dialog = new GameObject("TargetSelectionDialog");
            dialog.transform.SetParent(window.transform, false);

            RectTransform rect = dialog.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(400f, 300f);

            Image bg = dialog.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.98f);

            // Title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(dialog.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -10f);
            titleRect.sizeDelta = new Vector2(-20f, 30f);

            Text titleText = titleObj.AddComponent<Text>();
            titleText.text = "Select Target Container";
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 16;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.cyan;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;

            // Container list
            float yPos = -50f;
            foreach (var container in nearbyContainers.Take(8))
            {
                GameObject itemBtn = CreateButton(
                    dialog,
                    $"{container.name} ({container.distance:F1}m)",
                    new Vector2(0f, yPos),
                    new Vector2(360f, 30f),
                    () => {
                        component.SetTargetContainer(container.id);
                        UnityEngine.Object.Destroy(dialog);
                        UpdateControlPanel(uiPanels[window], component);
                        BepInExPlugin.Dbgl($"Target set to: {container.name}");
                    }
                );
                yPos -= 35f;
            }

            // Close button
            CreateButton(
                dialog,
                "Cancel",
                new Vector2(0f, yPos - 10f),
                new Vector2(100f, 30f),
                () => {
                    UnityEngine.Object.Destroy(dialog);
                }
            );
        }

        private static void ShowMessageDialog(UiWindowContainer window, string title, string message)
        {
            GameObject dialog = new GameObject("MessageDialog");
            dialog.transform.SetParent(window.transform, false);

            RectTransform rect = dialog.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(400f, 200f);

            Image bg = dialog.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.98f);

            // Title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(dialog.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -10f);
            titleRect.sizeDelta = new Vector2(-20f, 30f);

            Text titleText = titleObj.AddComponent<Text>();
            titleText.text = title;
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 16;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = Color.yellow;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.raycastTarget = false;

            // Message
            GameObject msgObj = new GameObject("Message");
            msgObj.transform.SetParent(dialog.transform, false);

            RectTransform msgRect = msgObj.AddComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.pivot = new Vector2(0.5f, 0.5f);
            msgRect.anchoredPosition = new Vector2(0f, -10f);
            msgRect.sizeDelta = new Vector2(-40f, -80f);

            Text msgText = msgObj.AddComponent<Text>();
            msgText.text = message;
            msgText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            msgText.fontSize = 14;
            msgText.color = Color.white;
            msgText.alignment = TextAnchor.MiddleCenter;
            msgText.raycastTarget = false;

            // OK button
            CreateButton(
                dialog,
                "OK",
                new Vector2(0f, -70f),
                new Vector2(100f, 30f),
                () => {
                    UnityEngine.Object.Destroy(dialog);
                }
            );
        }
    }
}
