using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SpaceCraft;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ContainerFinder
{
    [BepInPlugin("xkursat.ContainerFinder", "Container Finder", "1.0.0")]
    public class BepInExPlugin : BaseUnityPlugin
    {
        public static BepInExPlugin context;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<string> scanKey;
        public static ConfigEntry<float> maxDistance;
        public static ConfigEntry<bool> showGoldenOnly;

        private List<ContainerInfo> foundContainers = new List<ContainerInfo>();
        private float displayTimer = 0f;
        private const float DISPLAY_DURATION = 30f;
        private bool isScanning = false;
        private bool show3DMarkers = true; // NEW: Toggle for 3D markers

        private static InputAction scanAction;

        public static void Dbgl(string str = "", LogLevel logLevel = LogLevel.Debug)
        {
            if (isDebug.Value)
                context.Logger.Log(logLevel, str);
        }

        private void Awake()
        {
            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", true, "Enable debug logs");
            scanKey = Config.Bind<string>("Hotkeys", "ScanKey", "<Keyboard>/g", "Key to scan for containers");
            maxDistance = Config.Bind<float>("Options", "MaxDistance", 5000f, "Maximum scan distance (meters)");
            showGoldenOnly = Config.Bind<bool>("Options", "ShowGoldenOnly", false, "Show only Golden Containers");

            Logger.LogInfo("=".PadRight(80, '='));
            Logger.LogInfo("ContainerFinder v1.0.0 Loaded!");
            Logger.LogInfo($"Press G key to scan for containers.");
            Logger.LogInfo($"MaxDistance: {maxDistance.Value}m");
            Logger.LogInfo($"ShowGoldenOnly: {showGoldenOnly.Value}");
            Logger.LogInfo($"ModEnabled: {modEnabled.Value}");
            Logger.LogInfo("=".PadRight(80, '='));
            
            // Apply Harmony patches - use Assembly not typeof!
            try
            {
                var harmony = new Harmony("xkursat.ContainerFinder");
                harmony.PatchAll();
                Logger.LogInfo($"Harmony patches applied! Total patches: {harmony.GetPatchedMethods().Count()}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to apply Harmony patches: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
            }

            // Initialize InputAction AFTER Harmony patches
            try
            {
                scanAction = new InputAction(binding: scanKey.Value);
                scanAction.Enable();
                Logger.LogInfo($"InputAction created with binding: {scanKey.Value}");
                Logger.LogInfo($"InputAction enabled: {scanAction.enabled}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to create InputAction: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        [HarmonyPatch(typeof(PlayerInputDispatcher), "Update")]
        public static class PlayerInputDispatcher_Update_Patch
        {
            private static int frameCounter = 0;

            public static void Postfix()
            {
                if (!modEnabled.Value || context == null)
                    return;

                frameCounter++;

                // Always update timer
                if (context.displayTimer > 0)
                {
                    context.displayTimer -= Time.deltaTime;

                    if (context.displayTimer <= 0)
                    {
                        context.isScanning = false;
                    }
                }

                // Check for key press
                if (scanAction != null && scanAction.WasPressedThisFrame())
                {
                    context.Logger.LogInfo("=== G KEY PRESSED! Starting container scan... ===");
                    context.ScanForContainers();
                }
            }
        }

        private void ScanForContainers()
        {
            isScanning = true;
            foundContainers.Clear();
            Logger.LogInfo("=== STARTING CONTAINER SCAN ===");

            var player = GetPlayerPosition();
            if (player == Vector3.zero)
            {
                Logger.LogError("Player not found!");
                displayTimer = 5f;
                isScanning = false;
                return;
            }

            Logger.LogInfo($"Player position: {player}");

            var allWorldObjects = FindObjectsOfType<WorldObjectAssociated>();
            Logger.LogInfo($"Scanning {allWorldObjects.Length} objects...");
            
            int containerCount = 0;
            
            foreach (var woa in allWorldObjects)
            {
                if (woa == null)
                    continue;
                    
                var worldObject = woa.GetWorldObject();
                if (worldObject == null || worldObject.GetGroup() == null)
                    continue;

                string groupId = worldObject.GetGroup().GetId();

                if (IsContainer(groupId))
                {
                    containerCount++;
                    
                    bool isGolden = groupId.Contains("Golden");
                    if (showGoldenOnly.Value && !isGolden)
                        continue;

                    Vector3 position = woa.transform.position;
                    float distance = Vector3.Distance(player, position);

                    if (distance <= maxDistance.Value)
                    {
                        foundContainers.Add(new ContainerInfo
                        {
                            groupId = groupId,
                            position = position,
                            distance = distance,
                            isGolden = isGolden,
                            direction = GetDirectionArrow(player, position)
                        });
                    }
                }
            }

            foundContainers = foundContainers.OrderBy(c => c.distance).ToList();

            Logger.LogInfo($"=== SCAN COMPLETE ===");
            Logger.LogInfo($"Found {foundContainers.Count} containers within {maxDistance.Value}m (Total containers: {containerCount})");
            
            if (foundContainers.Count > 0)
            {
                Logger.LogInfo($"Closest 5:");
                foreach (var container in foundContainers.Take(5))
                {
                    string prefix = container.isGolden ? "[GOLDEN]" : "";
                    Logger.LogInfo($"  {prefix} {container.groupId} - {container.distance:F1}m");
                }
            }

            displayTimer = DISPLAY_DURATION;
            isScanning = false;
        }

        private bool IsContainer(string groupId)
        {
            return groupId.StartsWith("Container") ||
                   groupId.Contains("GoldenContainer") ||
                   groupId == "canister" ||
                   groupId.Contains("ContainerAqualis") ||
                   groupId.Contains("ContainerToxic") ||
                   groupId.Contains("StarformContainer");
        }

        private Vector3 GetPlayerPosition()
        {
            try
            {
                var playersManager = Managers.GetManager<PlayersManager>();
                if (playersManager == null)
                {
                    Logger.LogWarning("PlayersManager is null!");
                    return Vector3.zero;
                }

                var player = playersManager.GetActivePlayerController();
                if (player == null)
                {
                    Logger.LogWarning("ActivePlayerController is null!");
                    return Vector3.zero;
                }

                Logger.LogInfo($"Player found: {player.name}");
                return player.transform.position;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error getting player position: {ex.Message}");
                return Vector3.zero;
            }
        }

        private string GetDirectionArrow(Vector3 playerPos, Vector3 targetPos)
        {
            var playersManager = Managers.GetManager<PlayersManager>();
            var player = playersManager?.GetActivePlayerController();
            
            if (player == null)
                return "?";
            
            Vector3 directionToTarget = (targetPos - playerPos).normalized;
            Vector3 playerForward = player.transform.forward;
            float angle = Vector3.SignedAngle(playerForward, directionToTarget, Vector3.up);
            float verticalDiff = targetPos.y - playerPos.y;
            
            string arrow;
            if (angle >= -22.5f && angle < 22.5f)
                arrow = "⬆️";
            else if (angle >= 22.5f && angle < 67.5f)
                arrow = "↗️";
            else if (angle >= 67.5f && angle < 112.5f)
                arrow = "➡️";
            else if (angle >= 112.5f && angle < 157.5f)
                arrow = "↘️";
            else if (angle >= 157.5f || angle < -157.5f)
                arrow = "⬇️";
            else if (angle >= -157.5f && angle < -112.5f)
                arrow = "↙️";
            else if (angle >= -112.5f && angle < -67.5f)
                arrow = "⬅️";
            else
                arrow = "↖️";
            
            if (verticalDiff > 5f)
                arrow += "⬆";
            else if (verticalDiff < -5f)
                arrow += "⬇";
            
            return arrow;
        }

        private void OnGUI()
        {
            // Status indicator at bottom left - ALWAYS VISIBLE
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label);
            statusStyle.fontSize = 18;
            statusStyle.fontStyle = FontStyle.Bold;

            string statusText;
            if (isScanning)
            {
                statusStyle.normal.textColor = Color.yellow;
                statusText = "🔍 SCANNING FOR CONTAINERS... Please wait!";
            }
            else
            {
                statusStyle.normal.textColor = Color.green;
                statusText = "ContainerFinder Active - Press G to scan";
            }

            // Draw with background box
            GUI.backgroundColor = new Color(0, 0, 0, 0.7f);
            GUI.Box(new Rect(5, Screen.height - 40, 600, 35), "");
            GUI.backgroundColor = Color.white;
            GUI.Label(new Rect(10, Screen.height - 35, 590, 30), statusText, statusStyle);

            // Draw 3D world markers
            if (show3DMarkers && displayTimer > 0 && foundContainers.Count > 0)
            {
                DrawWorldMarkers();
            }

            // Show results list
            if (!modEnabled.Value || displayTimer <= 0 || foundContainers.Count == 0)
                return;

            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 14;
            boxStyle.normal.textColor = Color.white;
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.wordWrap = false;
            boxStyle.richText = true;

            string text = $"<b>=== CONTAINERS ({foundContainers.Count}) ===</b>\n";
            text += $"<color=yellow>{displayTimer:F0}s</color> | <color=cyan>G=Refresh</color>\n\n";
            
            foreach (var container in foundContainers.Take(10))
            {
                if (container.isGolden)
                {
                    text += $"<color=yellow>⭐ {container.groupId}</color>\n";
                    text += $"<color=yellow>{container.distance:F0}m</color>\n";
                }
                else
                {
                    text += $"<color=lime>• {container.groupId}</color>\n";
                    text += $"<color=white>{container.distance:F0}m</color>\n";
                }
            }
            
            if (foundContainers.Count > 10)
            {
                text += $"\n<color=gray>+{foundContainers.Count - 10} more</color>";
            }

            GUI.backgroundColor = new Color(0, 0, 0, 0.85f);
            GUI.Box(new Rect(10, 50, 280, 500), text, boxStyle);
            GUI.backgroundColor = Color.white;
        }

        private void DrawWorldMarkers()
        {
            var playersManager = Managers.GetManager<PlayersManager>();
            var player = playersManager?.GetActivePlayerController();
            
            if (player == null)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            foreach (var container in foundContainers)
            {
                // Convert 3D world position to 2D screen position
                Vector3 screenPos = camera.WorldToScreenPoint(container.position);

                // Check if behind camera
                if (screenPos.z < 0)
                    continue;

                // Convert Unity screen coords (bottom-left origin) to GUI coords (top-left origin)
                screenPos.y = Screen.height - screenPos.y;

                // Check if on screen
                if (screenPos.x < 0 || screenPos.x > Screen.width || screenPos.y < 0 || screenPos.y > Screen.height)
                    continue;

                // Calculate scale based on distance (closer = bigger)
                float scale = Mathf.Clamp(50f / container.distance, 0.5f, 2f);
                int fontSize = Mathf.RoundToInt(16 * scale);

                // Create label style
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.fontSize = fontSize;
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.alignment = TextAnchor.MiddleCenter;
                labelStyle.normal.textColor = container.isGolden ? Color.yellow : Color.cyan;

                // Add shadow/outline for visibility
                GUIStyle shadowStyle = new GUIStyle(labelStyle);
                shadowStyle.normal.textColor = Color.black;

                // Build label text
                string label = container.isGolden ? "⭐ GOLD" : "📦";
                label += $"\n{container.distance:F0}m";

                // Calculate label size
                Vector2 labelSize = labelStyle.CalcSize(new GUIContent(label));
                Rect labelRect = new Rect(
                    screenPos.x - labelSize.x / 2,
                    screenPos.y - labelSize.y / 2,
                    labelSize.x,
                    labelSize.y
                );

                // Draw shadow (4 directions)
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        Rect shadowRect = new Rect(labelRect.x + x, labelRect.y + y, labelRect.width, labelRect.height);
                        GUI.Label(shadowRect, label, shadowStyle);
                    }
                }

                // Draw main label
                GUI.Label(labelRect, label, labelStyle);

                // Optional: Draw line from label to container
                if (container.distance < 100f)
                {
                    DrawLine(
                        new Vector2(screenPos.x, screenPos.y),
                        new Vector2(screenPos.x, screenPos.y + 30),
                        container.isGolden ? Color.yellow : Color.cyan,
                        2f
                    );
                }
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 direction = (end - start).normalized;
            float distance = Vector2.Distance(start, end);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            GUIStyle lineStyle = new GUIStyle();
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            lineStyle.normal.background = texture;

            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.Box(new Rect(start.x, start.y, distance, width), GUIContent.none, lineStyle);
            GUI.matrix = matrix;
        }

        private class ContainerInfo
        {
            public string groupId;
            public Vector3 position;
            public float distance;
            public bool isGolden;
            public string direction; // NEW: Direction indicator
        }
    }
}
