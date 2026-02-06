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
    [BepInPlugin("xkursat.AutoCollector", "Auto Collector Container", "2.1.0")]
    public class BepInExPlugin : BaseUnityPlugin
    {
        public static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> includeMinables;
        public static ConfigEntry<float> collectionRadius;
        public static ConfigEntry<float> collectionInterval;

        public static void Dbgl(string str = "", LogLevel logLevel = LogLevel.Debug)
        {
            if (isDebug.Value)
                context.Logger.Log(logLevel, str);
        }

        private void Awake()
        {
            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", false, "Enable debug logs");
            includeMinables = Config.Bind<bool>("Options", "IncludeMinables", true, "Include minable resources (ores, rocks, etc)");
            collectionRadius = Config.Bind<float>("Options", "CollectionRadius", 50f, "Radius to collect items (meters)");
            collectionInterval = Config.Bind<float>("Options", "CollectionInterval", 5f, "How often to collect items (seconds)");

            Harmony.CreateAndPatchAll(typeof(ContainerPatches));
            Dbgl("AutoCollector Container Plugin loaded");
        }

        public void LogAllGameItems()
        {
            try
            {
                Dbgl("=".PadRight(80, '='));
                Dbgl("LOGGING ALL GAME ITEMS");
                Dbgl("=".PadRight(80, '='));

                var allGroupsField = typeof(GroupsHandler).GetField("_allGroups", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (allGroupsField == null)
                {
                    Dbgl("_allGroups field not found!", LogLevel.Error);
                    return;
                }

                var groupsList = allGroupsField.GetValue(null) as System.Collections.IList;
                if (groupsList == null || groupsList.Count == 0)
                {
                    Dbgl("groupsList is null or empty!", LogLevel.Error);
                    return;
                }

                Dbgl($"Total groups found: {groupsList.Count}");
                Dbgl("");

                Dictionary<string, List<string>> categorizedItems = new Dictionary<string, List<string>>();

                foreach (var group in groupsList)
                {
                    if (group == null)
                        continue;

                    var groupType = group.GetType();
                    
                    var idProperty = groupType.GetProperty("id", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                    var categoryField = groupType.GetField("groupCategory", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                    
                    // Also check for itemCategory if groupCategory is null
                    var itemCategoryField = groupType.GetField("itemCategory", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
                    
                    if (idProperty != null)
                    {
                        string itemId = idProperty.GetValue(group, null) as string;
                        string category = "Unknown";
                        
                        // Try groupCategory first
                        if (categoryField != null)
                        {
                            var catValue = categoryField.GetValue(group);
                            if (catValue != null)
                                category = "Building_" + catValue.ToString();
                        }
                        
                        // If not found, try itemCategory
                        if (category == "Unknown" && itemCategoryField != null)
                        {
                            var itemCatValue = itemCategoryField.GetValue(group);
                            if (itemCatValue != null)
                                category = "Item_" + itemCatValue.ToString();
                        }

                        if (!string.IsNullOrEmpty(itemId))
                        {
                            if (!categorizedItems.ContainsKey(category))
                            {
                                categorizedItems[category] = new List<string>();
                            }
                            categorizedItems[category].Add(itemId);
                        }
                    }
                }

                foreach (var kvp in categorizedItems.OrderBy(x => x.Key))
                {
                    Dbgl("");
                    Dbgl($"### CATEGORY: {kvp.Key} ({kvp.Value.Count} items) ###");
                    Dbgl("-".PadRight(80, '-'));
                    
                    foreach (var itemId in kvp.Value.OrderBy(x => x))
                    {
                        Dbgl($"  - {itemId}");
                    }
                }

                Dbgl("");
                Dbgl("=".PadRight(80, '='));
                Dbgl($"TOTAL: {categorizedItems.Values.Sum(list => list.Count)} items in {categorizedItems.Count} categories");
                Dbgl("=".PadRight(80, '='));
            }
            catch (Exception ex)
            {
                Dbgl($"Error logging items: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public class AutoCollectorComponent : MonoBehaviour
    {
        private float timer = 0f;
        private InventoryAssociated inventoryAssociated;
        private Inventory containerInventory;
        public bool isEnabled = false;
        private int containerWorldObjectId = -1;
        public bool isReady = false;

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
                    BepInExPlugin.Dbgl($"AutoCollector pre-initialized for container {containerWorldObjectId}, state: {isEnabled}");
                }
                
                inventoryAssociated.GetInventory((inventory) => {
                    containerInventory = inventory;
                    isReady = true;
                    BepInExPlugin.Dbgl($"AutoCollector ready for container {containerWorldObjectId}");
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
            if (!BepInExPlugin.modEnabled.Value || !isEnabled || !isReady || containerInventory == null)
                return;

            timer += Time.deltaTime;
            if (timer >= BepInExPlugin.collectionInterval.Value)
            {
                timer = 0f;
                CollectNearbyItems();
            }
        }

        public void ToggleAutoCollect()
        {
            isEnabled = !isEnabled;
            BepInExPlugin.Dbgl($"AutoCollect toggled: {(isEnabled ? "ENABLED" : "DISABLED")}");
            
            if (isEnabled)
            {
                timer = 0f;
            }
            
            SaveState();
        }

        private void SaveState()
        {
            if (containerWorldObjectId >= 0)
            {
                PlayerPrefs.SetInt($"AutoCollector_{containerWorldObjectId}", isEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        private void LoadState()
        {
            if (containerWorldObjectId >= 0)
            {
                isEnabled = PlayerPrefs.GetInt($"AutoCollector_{containerWorldObjectId}", 0) == 1;
            }
        }

        private void CollectNearbyItems()
        {
            if (containerInventory.IsFull())
                return;

            Vector3 containerPos = transform.position;
            int collectedCount = 0;

            ActionGrabable[] allGrabbables = FindObjectsByType<ActionGrabable>(FindObjectsSortMode.None);
            foreach (ActionGrabable grabable in allGrabbables)
            {
                if (containerInventory.IsFull())
                    break;

                if (grabable == null || !grabable.GetCanGrab())
                    continue;

                float distance = Vector3.Distance(containerPos, grabable.transform.position);
                if (distance > BepInExPlugin.collectionRadius.Value)
                    continue;

                WorldObjectAssociated woa = grabable.GetComponent<WorldObjectAssociated>();
                if (woa == null)
                    continue;

                WorldObject worldObject = woa.GetWorldObject();
                if (worldObject == null || worldObject.GetGroup() == null)
                    continue;

                if (containerInventory.AddItem(worldObject))
                {
                    Destroy(grabable.gameObject);
                    worldObject.SetDontSaveMe(false);
                    collectedCount++;
                }
            }

            if (BepInExPlugin.includeMinables.Value)
            {
                ActionMinable[] allMinables = FindObjectsByType<ActionMinable>(FindObjectsSortMode.None);
                foreach (ActionMinable minable in allMinables)
                {
                    if (containerInventory.IsFull())
                        break;

                    if (minable == null)
                        continue;

                    if (minable.GetComponentInParent<MachineAutoCrafter>() != null)
                        continue;

                    float distance = Vector3.Distance(containerPos, minable.transform.position);
                    if (distance > BepInExPlugin.collectionRadius.Value)
                        continue;

                    WorldObjectAssociated woa = minable.GetComponent<WorldObjectAssociated>();
                    if (woa == null)
                        continue;

                    WorldObject worldObject = woa.GetWorldObject();
                    if (worldObject == null || worldObject.GetGroup() == null)
                        continue;

                    if (containerInventory.AddItem(worldObject))
                    {
                        Destroy(minable.gameObject);
                        worldObject.SetDontSaveMe(false);
                        Managers.GetManager<DisplayersHandler>().GetItemWorldDisplayer()?.Hide();
                        collectedCount++;
                    }
                }
            }

            if (collectedCount > 0)
            {
                BepInExPlugin.Dbgl($"Collection cycle complete: {collectedCount} items collected");
            }
        }
    }

    [HarmonyPatch]
    public class ContainerPatches
    {
        private static Dictionary<UiWindowContainer, GameObject> toggleButtons = new Dictionary<UiWindowContainer, GameObject>();
        private static bool hasLoggedItems = false;

        [HarmonyPatch(typeof(InventoryAssociated), "Start")]
        [HarmonyPostfix]
        static void AddAutoCollectorComponent(InventoryAssociated __instance)
        {
            if (!BepInExPlugin.modEnabled.Value)
                return;

            if (!hasLoggedItems && BepInExPlugin.isDebug.Value)
            {
                hasLoggedItems = true;
                //BepInExPlugin.context.LogAllGameItems();
            }

            WorldObjectAssociated woa = __instance.GetComponent<WorldObjectAssociated>();
            if (woa == null)
                return;

            WorldObject worldObject = woa.GetWorldObject();
            if (worldObject == null || worldObject.GetGroup() == null)
                return;

            string containerId = worldObject.GetGroup().GetId();
            
            if (containerId.StartsWith("Container"))
            {
                if (__instance.GetComponent<AutoCollectorComponent>() == null)
                {
                    __instance.gameObject.AddComponent<AutoCollectorComponent>();
                    BepInExPlugin.Dbgl($"Added AutoCollector component to {containerId}");
                }
            }
        }

        [HarmonyPatch(typeof(UiWindowContainer), "OnOpen")]
        [HarmonyPostfix]
        static void AddToggleButton(UiWindowContainer __instance)
        {
            if (!BepInExPlugin.modEnabled.Value)
                return;

            __instance.StartCoroutine(CreateButtonWhenReady(__instance));
        }

        private static System.Collections.IEnumerator CreateButtonWhenReady(UiWindowContainer window)
        {
            yield return null;

            var inventoryRightField = AccessTools.Field(typeof(UiWindowContainer), "_inventoryRight");
            if (inventoryRightField == null)
                yield break;

            Inventory containerInventory = (Inventory)inventoryRightField.GetValue(window);
            if (containerInventory == null)
                yield break;

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
                
                if (found)
                    break;
            }

            if (targetInventoryAssociated == null)
                yield break;

            AutoCollectorComponent autoCollector = targetInventoryAssociated.GetComponent<AutoCollectorComponent>();
            if (autoCollector == null)
                yield break;

            float timeout = 2f;
            float elapsed = 0f;
            while (!autoCollector.isReady && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (toggleButtons.ContainsKey(window))
            {
                if (toggleButtons[window] != null)
                {
                    UnityEngine.Object.Destroy(toggleButtons[window]);
                }
                toggleButtons.Remove(window);
            }

            GameObject buttonObj = new GameObject("AutoCollectToggle");
            buttonObj.transform.SetParent(window.transform, false);

            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.anchoredPosition = new Vector2(0f, 10f);
            rectTransform.sizeDelta = new Vector2(200f, 40f);

            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = autoCollector.isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);

            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = autoCollector.isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
            colors.highlightedColor = autoCollector.isEnabled ? new Color(0.3f, 1f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
            colors.pressedColor = autoCollector.isEnabled ? new Color(0.1f, 0.6f, 0.1f) : new Color(0.2f, 0.2f, 0.2f);
            button.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            Text buttonText = textObj.AddComponent<Text>();
            buttonText.text = autoCollector.isEnabled ? "AutoCollect: ON" : "AutoCollect: OFF";
            buttonText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            buttonText.fontSize = 16;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.fontStyle = FontStyle.Bold;

            button.onClick.AddListener(() => {
                autoCollector.ToggleAutoCollect();
                UpdateButtonAppearance(buttonObj, autoCollector.isEnabled);
            });

            toggleButtons[window] = buttonObj;
        }

        [HarmonyPatch(typeof(UiWindowContainer), "OnClose")]
        [HarmonyPostfix]
        static void CleanupButton(UiWindowContainer __instance)
        {
            if (toggleButtons.ContainsKey(__instance))
            {
                if (toggleButtons[__instance] != null)
                {
                    UnityEngine.Object.Destroy(toggleButtons[__instance]);
                }
                toggleButtons.Remove(__instance);
            }
        }

        private static void UpdateButtonAppearance(GameObject buttonObj, bool isEnabled)
        {
            if (buttonObj == null)
                return;

            Image buttonImage = buttonObj.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
            }

            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                ColorBlock colors = button.colors;
                colors.normalColor = isEnabled ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.3f, 0.3f, 0.3f);
                colors.highlightedColor = isEnabled ? new Color(0.3f, 1f, 0.3f) : new Color(0.4f, 0.4f, 0.4f);
                colors.pressedColor = isEnabled ? new Color(0.1f, 0.6f, 0.1f) : new Color(0.2f, 0.2f, 0.2f);
                button.colors = colors;
            }

            Text buttonText = buttonObj.GetComponentInChildren<Text>();
            if (buttonText != null)
            {
                buttonText.text = isEnabled ? "AutoCollect: ON" : "AutoCollect: OFF";
            }
        }
    }
}
