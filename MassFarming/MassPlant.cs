using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MassFarming
{
    [HarmonyPatch]
    public class MassPlant
    {
        private static FieldInfo m_noPlacementCostField = AccessTools.Field(typeof(Player), "m_noPlacementCost");
        private static FieldInfo m_placementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");

        private static Vector3 placedPosition;
        private static Quaternion placedRotation;
        private static Piece placedPiece;
        private static bool placeSuccessful = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "PlacePiece")]
        public static void PlacePiecePostfix(Player __instance, ref bool __result, Piece piece)
        {
            placeSuccessful = __result;
            if (__result)
            {
                var placeGhost = (GameObject)m_placementGhostField.GetValue(__instance);
                placedPosition = placeGhost.transform.position;
                placedRotation = placeGhost.transform.rotation;
                placedPiece = piece;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPrefix(bool takeInput, float dt)
        {
            //Clear any previous place result
            placeSuccessful = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPostfix(Player __instance, bool takeInput, float dt)
        {
            if (!placeSuccessful)
            {
                //Ignore when the place didn't happen
                return;
            }

            if (!placedPiece.m_cultivatedGroundOnly)
            {
                //Only plants? Is this a reasonable check?
                return;
            }

            if (!Input.GetKey(MassFarming.ControllerPickupHotkey.Value.MainKey) && !Input.GetKey(MassFarming.MassActionHotkey.Value.MainKey))
            {
                //Hotkey required
                return;
            }

            int halfGrid = MassFarming.PlantGridSize.Value / 2;
            Vector3 newPos = placedPosition;
            ZoneSystem.instance.GetGroundData(ref placedPosition, out _, out _, out _, out var heightmap);

            for (var x = 0; x < MassFarming.PlantGridSize.Value; x++)
            {
                for (var z = 0; z < MassFarming.PlantGridSize.Value; z++)
                {
                    newPos.x = placedPosition.x - halfGrid + x;
                    newPos.z = placedPosition.z - halfGrid + z;
                    newPos.y = ZoneSystem.instance.GetGroundHeight(newPos);

                    if (placedPiece.m_cultivatedGroundOnly && !heightmap.IsCultivated(newPos))
                    {
                        continue;
                    }

                    if (placedPosition == newPos)
                    {
                        //Trying to place around the origin point, so avoid placing a duplicate at the same location
                        continue;
                    }

                    var tool = __instance.GetRightItem();
                    var hasStamina = MassFarming.IgnoreStamina.Value || __instance.HaveStamina(tool.m_shared.m_attack.m_attackStamina);

                    if (!hasStamina)
                    {
                        Hud.instance.StaminaBarNoStaminaFlash();
                        return;
                    }

                    var hasMats = (bool)m_noPlacementCostField.GetValue(__instance) || __instance.HaveRequirements(placedPiece, Player.RequirementMode.CanBuild);
                    if (!hasMats)
                    {
                        return;
                    }


                    GameObject newPlaceObj = UnityEngine.Object.Instantiate(placedPiece.gameObject, newPos, placedRotation);
                    Piece component = newPlaceObj.GetComponent<Piece>();
                    if (component)
                    {
                        component.SetCreator(__instance.GetPlayerID());
                    }
                    placedPiece.m_placeEffect.Create(newPos, placedRotation, newPlaceObj.transform);
                    Game.instance.GetPlayerProfile().m_playerStats.m_builds++;

                    __instance.ConsumeResources(placedPiece.m_resources, 0);
                    if (!MassFarming.IgnoreStamina.Value)
                    {
                        __instance.UseStamina(tool.m_shared.m_attack.m_attackStamina);
                    }
                    if (tool.m_shared.m_useDurability)
                    {
                        tool.m_durability -= tool.m_shared.m_useDurabilityDrain;
                        if(tool.m_durability <= 0f)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }
}
