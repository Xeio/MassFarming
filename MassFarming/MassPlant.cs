using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace MassFarming
{
    [HarmonyPatch]
    public static class MassPlant
    {
        private static readonly FieldInfo m_noPlacementCostField = AccessTools.Field(typeof(Player), "m_noPlacementCost");
        private static readonly FieldInfo m_placementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo m_buildPiecesField = AccessTools.Field(typeof(Player), "m_buildPieces");
        private static readonly MethodInfo _GetRightItemMethod = AccessTools.Method(typeof(Humanoid), "GetRightItem");

        private static Vector3 placedPosition;
        private static Quaternion placedRotation;
        private static Piece placedPiece;
        private static bool placeSuccessful = false;
        private static int? massFarmingRotation = null;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        public static void TryPlacePiecePrefix(int ___m_placeRotation)
        {
            // When MassFarming is used, save rotation before placing.
            if (IsHotKeyPressed && massFarmingRotation is null)
            {
                massFarmingRotation = ___m_placeRotation;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        public static void TryPlacePiecePostfix(Player __instance, ref bool __result, Piece piece, ref int ___m_placeRotation)
        {
            placeSuccessful = __result;
            if (__result)
            {
                var placeGhost = (GameObject)m_placementGhostField.GetValue(__instance);
                placedPosition = placeGhost.transform.position;
                placedRotation = placeGhost.transform.rotation;
                placedPiece = piece;
            }
            // When MassFarming is used, revert any change to rotation during placing to last state saved before placing.
            if (IsHotKeyPressed)
            {
                ___m_placeRotation = massFarmingRotation.Value;
            }            
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPrefix(bool takeInput, float dt, ref int ___m_placeRotation)
        {
            //Clear any previous place result
            placeSuccessful = false;
            // When MassFarming is used, reset rotation to mod's last used rotation
            if (IsHotKeyPressed && massFarmingRotation.HasValue)
            {
                ___m_placeRotation = massFarmingRotation.Value;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacement")]
        public static void UpdatePlacementPostfix(Player __instance, bool takeInput, float dt, int ___m_placeRotation)
        {
            // When MassFarming is used, save user changes of rotation
            if (IsHotKeyPressed) 
            { 
                massFarmingRotation = ___m_placeRotation;
            }

            if (!placeSuccessful)
            {
                //Ignore when the place didn't happen
                return;
            }

            var plant = placedPiece.gameObject.GetComponent<Plant>();
            if (!plant)
            {
                return;
            }

            if (!IsHotKeyPressed)
            {
                //Hotkey required
                return;
            }

            var heightmap = Heightmap.FindHeightmap(placedPosition);
            if (!heightmap)
            {
                return;
            }

            foreach (var newPos in BuildPlantingGridPositions(placedPosition, plant, placedRotation))
            {
                if (placedPiece.m_cultivatedGroundOnly && !heightmap.IsCultivated(newPos))
                {
                    continue;
                }

                if (placedPosition == newPos)
                {
                    //Trying to place around the origin point, so avoid placing a duplicate at the same location
                    continue;
                }

                var tool = _GetRightItemMethod.Invoke(__instance, Array.Empty<object>()) as ItemDrop.ItemData;
                if(tool is null)
                {
                    //This shouldn't really ever happen...
                    continue;
                }

                var hasStamina = MassFarming.IgnoreStamina.Value || __instance.HaveStamina(tool.m_shared.m_attack.m_attackStamina);

                if (!hasStamina)
                {
                    Hud.instance.StaminaBarUppgradeFlash();
                    return;
                }

                var hasMats = (bool)m_noPlacementCostField.GetValue(__instance) || __instance.HaveRequirements(placedPiece, Player.RequirementMode.CanBuild);
                if (!hasMats)
                {
                    return;
                }

                if (!HasGrowSpace(newPos, placedPiece.gameObject))
                {
                    continue;
                }

                GameObject newPlaceObj = UnityEngine.Object.Instantiate(placedPiece.gameObject, newPos, placedRotation );
                Piece component = newPlaceObj.GetComponent<Piece>();
                if (component)
                {
                    component.SetCreator(__instance.GetPlayerID());
                }
                placedPiece.m_placeEffect.Create(newPos, placedRotation, newPlaceObj.transform);
                Game.instance.IncrementPlayerStat(PlayerStatType.Builds);

                __instance.ConsumeResources(placedPiece.m_resources, 0, -1);
                if (!MassFarming.IgnoreStamina.Value)
                {
                    __instance.UseStamina(tool.m_shared.m_attack.m_attackStamina);
                }
                if (!MassFarming.IgnoreDurability.Value && tool.m_shared.m_useDurability)
                {
                    tool.m_durability -= tool.m_shared.m_useDurabilityDrain;
                    if (tool.m_durability <= 0f)
                    {
                        return;
                    }
                }
            }
        }

        private static bool IsHotKeyPressed => Input.GetKey(MassFarming.ControllerPickupHotkey.Value.MainKey) || Input.GetKey(MassFarming.MassActionHotkey.Value.MainKey);

        private static List<Vector3> BuildPlantingGridPositions(Vector3 originPos, Plant placedPlant, Quaternion rotation)
        {
            var plantRadius = placedPlant.m_growRadius * 2;

            List<Vector3> gridPositions = new List<Vector3>(MassFarming.PlantGridWidth.Value * MassFarming.PlantGridLength.Value);
            Vector3 left = rotation * Vector3.left * plantRadius;
            Vector3 forward = rotation * Vector3.forward * plantRadius;
            Vector3 gridOrigin = originPos;
            if (MassFarming.GridAnchorLength.Value)
            {
                gridOrigin -= forward * (MassFarming.PlantGridLength.Value / 2);
            }
            if (MassFarming.GridAnchorWidth.Value)
            {
                gridOrigin -= left * (MassFarming.PlantGridWidth.Value / 2);
            }

            Vector3 newPos;
            for (var x = 0; x < MassFarming.PlantGridLength.Value; x++)
            {
                newPos = gridOrigin;
                for (var z = 0; z < MassFarming.PlantGridWidth.Value; z++)
                {
                    newPos.y = ZoneSystem.instance.GetGroundHeight(newPos);
                    gridPositions.Add(newPos);
                    newPos += left;
                }
                gridOrigin += forward;
            }
            return gridPositions;
        }

        static readonly int _plantSpaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static bool HasGrowSpace(Vector3 newPos, GameObject go)
        {
            if (go.GetComponent<Plant>() is Plant placingPlant)
            {
                Collider[] nearbyObjects = Physics.OverlapSphere(newPos, placingPlant.m_growRadius, _plantSpaceMask);
                return nearbyObjects.Length == 0;
            }
            return true;
        }

        private static GameObject[] _placementGhosts = new GameObject[1];
        private static Piece _fakeResourcePiece;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        public static void SetupPlacementGhostPrefix(Player __instance, int ___m_placeRotation)
        {
            if (IsHotKeyPressed && massFarmingRotation is null)
            {
                massFarmingRotation = ___m_placeRotation;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
        public static void SetupPlacementGhostPostfix(Player __instance, ref int ___m_placeRotation)
        {
            if (IsHotKeyPressed && massFarmingRotation.HasValue)
            {
                ___m_placeRotation = massFarmingRotation.Value;
            }
            DestroyGhosts();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
        public static void UpdatePlacementGhostPostfix(Player __instance, bool flashGuardStone)
        {
            var ghost = (GameObject)m_placementGhostField.GetValue(__instance);
            if(!ghost || !ghost.activeSelf)
            {
                SetGhostsActive(false);
                return;
            }

            if (!Input.GetKey(MassFarming.ControllerPickupHotkey.Value.MainKey) && !Input.GetKey(MassFarming.MassActionHotkey.Value.MainKey))
            {
                //Hotkey required
                SetGhostsActive(false);
                return;
            }

            var plant = ghost.GetComponent<Plant>();
            if (!plant)
            {
                SetGhostsActive(false);
                return;
            }

            if (!EnsureGhostsBuilt(__instance))
            {
                SetGhostsActive(false);
                return;
            }

            //Find the required resource to plant the item
            //Assuming that for plants there is only a single resource requirement...
            var requirement = ghost.GetComponent<Piece>().m_resources.FirstOrDefault(r => r.m_resItem && r.m_amount > 0);
            _fakeResourcePiece.m_resources[0].m_resItem = requirement.m_resItem;
            _fakeResourcePiece.m_resources[0].m_amount = requirement.m_amount;

            var currentStamina = __instance.GetStamina();
            var tool = _GetRightItemMethod.Invoke(__instance, Array.Empty<object>()) as ItemDrop.ItemData;
            if (tool is null)
            {
                //This shouldn't really ever happen...
                return;
            }
            var heightmap = Heightmap.FindHeightmap(ghost.transform.position);
            var positions = BuildPlantingGridPositions(ghost.transform.position, plant, ghost.transform.rotation);
            for (int i = 0; i < _placementGhosts.Length; i++)
            {
                var newPos = positions[i];
                if (ghost.transform.position == newPos)
                {
                    //Trying to place around the origin point, so avoid placing a duplicate at the same location
                    _placementGhosts[i].SetActive(false);
                    continue;
                }

                //Track total cost of each placement
                _fakeResourcePiece.m_resources[0].m_amount += requirement.m_amount;

                _placementGhosts[i].transform.position = newPos;
                _placementGhosts[i].transform.rotation = ghost.transform.rotation;
                _placementGhosts[i].SetActive(true);

                bool invalid = false;
                if (ghost.GetComponent<Piece>().m_cultivatedGroundOnly && !heightmap.IsCultivated(newPos))
                {
                    invalid = true;
                }
                else if (!HasGrowSpace(newPos, ghost.gameObject))
                {
                    invalid = true;
                }
                else if (!MassFarming.IgnoreStamina.Value && currentStamina < tool.m_shared.m_attack.m_attackStamina)
                {
                    Hud.instance.StaminaBarUppgradeFlash();
                    invalid = true;
                }
                else if (!(bool)m_noPlacementCostField.GetValue(__instance) && !__instance.HaveRequirements(_fakeResourcePiece, Player.RequirementMode.CanBuild))
                {
                    invalid = true;
                }
                currentStamina -= tool.m_shared.m_attack.m_attackStamina;

                _placementGhosts[i].GetComponent<Piece>().SetInvalidPlacementHeightlight(invalid);                
            }
        }

        private static bool EnsureGhostsBuilt(Player player)
        {
            var requiredSize = MassFarming.PlantGridWidth.Value * MassFarming.PlantGridLength.Value;
            bool needsRebuild = !_placementGhosts[0] || _placementGhosts.Length != requiredSize;
            if (needsRebuild) 
            {
                DestroyGhosts();

                if(_placementGhosts.Length != requiredSize)
                {
                    _placementGhosts = new GameObject[requiredSize];
                }

                if (m_buildPiecesField.GetValue(player) is PieceTable pieceTable && pieceTable.GetSelectedPrefab() is GameObject prefab)
                {
                    if (prefab.GetComponent<Piece>().m_repairPiece)
                    {
                        //Repair piece doesn't have ghost
                        return false;
                    }

                    for (int i = 0; i < _placementGhosts.Length; i++)
                    {
                        _placementGhosts[i] = SetupMyGhost(player, prefab);
                    }
                }
                else
                {
                    //No prefab, so don't need ghost (this probably shouldn't ever happen)
                    return false;
                }
            }

            if (!_fakeResourcePiece)
            {
                _fakeResourcePiece = _placementGhosts[0].GetComponent<Piece>();
                _fakeResourcePiece.m_dlc = string.Empty;
                _fakeResourcePiece.m_resources = new Piece.Requirement[]
                {
                    new Piece.Requirement()
                };
            }

            return true;
        }

        private static void DestroyGhosts()
        {
            for (int i = 0; i < _placementGhosts.Length; i++)
            {
                if (_placementGhosts[i])
                {
                    UnityEngine.Object.Destroy(_placementGhosts[i]);
                    _placementGhosts[i] = null;
                }
            }
            _fakeResourcePiece = null;
        }

        private static void SetGhostsActive(bool active)
        {
            foreach(var ghost in _placementGhosts) 
            {
                ghost?.SetActive(active);
            }
        }

        private static GameObject SetupMyGhost(Player player, GameObject prefab)
        {
            //This takes some shortcuts because it's only ever used for Plant pieces

            ZNetView.m_forceDisableInit = true;
            var newGhost = UnityEngine.Object.Instantiate(prefab);
            ZNetView.m_forceDisableInit = false;
            newGhost.name = prefab.name;

            foreach (Joint joint in newGhost.GetComponentsInChildren<Joint>())
            {
                UnityEngine.Object.Destroy(joint);
            }

            foreach (Rigidbody rigidBody in newGhost.GetComponentsInChildren<Rigidbody>())
            {
                UnityEngine.Object.Destroy(rigidBody);
            }

            int layer = LayerMask.NameToLayer("ghost");
            foreach(var childTransform in newGhost.GetComponentsInChildren<Transform>())
            {
                childTransform.gameObject.layer = layer;
            }

            foreach(var terrainModifier in newGhost.GetComponentsInChildren<TerrainModifier>())
            { 
                UnityEngine.Object.Destroy(terrainModifier);
            }

            foreach (GuidePoint guidepoint in newGhost.GetComponentsInChildren<GuidePoint>())
            {
                UnityEngine.Object.Destroy(guidepoint);
            }

            foreach (Light light in newGhost.GetComponentsInChildren<Light>())
            {
                UnityEngine.Object.Destroy(light);
            }

            Transform ghostOnlyTransform = newGhost.transform.Find("_GhostOnly");
            if ((bool)ghostOnlyTransform)
            {
                ghostOnlyTransform.gameObject.SetActive(value: true);
            }

            foreach (MeshRenderer meshRenderer in newGhost.GetComponentsInChildren<MeshRenderer>())
            {
                if (!(meshRenderer.sharedMaterial == null))
                {
                    Material[] sharedMaterials = meshRenderer.sharedMaterials;
                    for (int j = 0; j < sharedMaterials.Length; j++)
                    {
                        Material material = new Material(sharedMaterials[j]);
                        material.SetFloat("_RippleDistance", 0f);
                        material.SetFloat("_ValueNoise", 0f);
                        sharedMaterials[j] = material;
                    }
                    meshRenderer.sharedMaterials = sharedMaterials;
                    meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            return newGhost;
        }
    }
}
