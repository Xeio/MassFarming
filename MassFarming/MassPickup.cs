﻿using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MassFarming
{
    [HarmonyPatch]
    public static class MassPickup
    {
        static FieldInfo m_interactMaskField = AccessTools.Field(typeof(Player), "m_interactMask");

        [HarmonyPatch(typeof(Player), "Interact")]
        public static void Prefix(Player __instance, GameObject go, bool hold)
        {
            if (__instance.InAttack() || __instance.InDodge())
            {
                return;
            }

            if (hold)
            {
                //Ignore button holds
                return;
            }

            if (!Input.GetKey(MassFarming.ControllerPickupHotkey.Value.MainKey) && !Input.GetKey(MassFarming.MassActionHotkey.Value.MainKey))
            {
                //Hotkey required
                return;
            }

            var interactible = go.GetComponentInParent<Interactable>();
            if (interactible is Pickable targetedPickable)
            {
                var interactMask = (int)m_interactMaskField.GetValue(__instance);
                var colliders = Physics.OverlapSphere(go.transform.position, MassFarming.MassInteractRange.Value, interactMask);
                
                foreach (var collider in colliders)
                {
                    if (collider?.gameObject?.GetComponentInParent<Pickable>() is Pickable nearbyPickable &&
                        nearbyPickable != targetedPickable)
                    {
                        if (nearbyPickable.m_itemPrefab.name == targetedPickable.m_itemPrefab.name)
                        {
                            //Pick up all prefabs with the same name
                            nearbyPickable.Interact(__instance, false);
                        }
                    }
                }
            }
        }
    }
}