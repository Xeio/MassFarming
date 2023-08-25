using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MassFarming
{
    [BepInPlugin("xeio.MassFarming", "MassFarming", "1.8")]
    public class MassFarming : BaseUnityPlugin
    {
        public static ConfigEntry<KeyboardShortcut> MassActionHotkey { get; private set; }
        public static ConfigEntry<KeyboardShortcut> ControllerPickupHotkey { get; private set; }
        public static ConfigEntry<float> MassInteractRange { get; private set; }
        public static ConfigEntry<int> PlantGridWidth { get; private set; }
        public static ConfigEntry<int> PlantGridLength { get; private set; }
        public static ConfigEntry<bool> IgnoreStamina { get; private set; }
        public static ConfigEntry<bool> IgnoreDurability { get; private set; }
        public static ConfigEntry<bool> GridAnchorWidth { get; private set; }
        public static ConfigEntry<bool> GridAnchorLength { get; private set; }

        public static ManualLogSource logger;

        public void Awake()
        {
            logger = Logger;
            MassActionHotkey = Config.Bind("Hotkeys", nameof(MassActionHotkey), new KeyboardShortcut(KeyCode.LeftShift), "Mass activation hotkey for multi-pickup/multi-plant.");
            ControllerPickupHotkey = Config.Bind("Hotkeys", nameof(ControllerPickupHotkey), new KeyboardShortcut(KeyCode.JoystickButton4), "Mass activation hotkey for multi-pickup/multi-plant for controller.");

            MassInteractRange = Config.Bind("Pickup", nameof(MassInteractRange), 5f, "Range of auto-pickup.");

            PlantGridWidth = Config.Bind("PlantGrid", nameof(PlantGridWidth), 5, "Grid width of auto-plant. Recommend odd-number, default is '5' so (5x5).");
            PlantGridLength = Config.Bind("PlantGrid", nameof(PlantGridLength), 5, "Grid length of auto-plant. Recommend odd-number, default is '5' so (5x5).");
            IgnoreStamina = Config.Bind("Plant", nameof(IgnoreStamina), false, "Ignore stamina requirements when planting extra rows.");
            IgnoreDurability = Config.Bind("Plant", nameof(IgnoreDurability), false, "Ignore durability when planting extra rows.");
            GridAnchorWidth = Config.Bind("PlantGrid", nameof(GridAnchorWidth), true, "Planting grid anchor point (width). Default is 'enabled' so the grid will extend left and right from the crosshairs. Disable to set the anchor to the side of the grid. Disable both anchors to set to corner.");
            GridAnchorLength = Config.Bind("PlantGrid", nameof(GridAnchorLength), true, "Planting grid anchor point (length). Default is 'enabled' so the grid will extend forward and backward from the crosshairs. Disable to set the anchor to the side of the grid. Disable both anchors to set to corner.");

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }
    }
}
