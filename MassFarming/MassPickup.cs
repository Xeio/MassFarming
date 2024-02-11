using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace MassFarming
{
    [HarmonyPatch]
    public static class MassPickup
    {
        static FieldInfo m_interactMaskField = AccessTools.Field(typeof(Player), "m_interactMask");
        static MethodInfo _ExtractMethod = AccessTools.Method(typeof(Beehive), "Extract");

        [HarmonyPatch(typeof(Player), "Interact")]
        public static void Prefix(Player __instance, GameObject go, bool hold, bool alt)
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

            var controllerKey = MassFarming.ControllerPickupHotkey.Value.MainKey;
			var controllerKeyEmpty = controllerKey == KeyCode.None;
            var controllerPressed = Input.GetKey(controllerKey);
            var controllerOk = controllerKeyEmpty || controllerPressed;

            var keyboardKey = MassFarming.MassActionHotkey.Value.MainKey;
            var keyboardKeyEmpty = keyboardKey == KeyCode.None;
            var keyboardPressed = Input.GetKey(keyboardKey);
            var keyboardOk = keyboardKeyEmpty || keyboardPressed;

			if (!controllerOk && !keyboardOk)
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
                            nearbyPickable.Interact(__instance, false, alt);
                        }
                    }
                }
            }
            else if(interactible is Beehive beehive)
            {
                var interactMask = (int)m_interactMaskField.GetValue(__instance);
                var colliders = Physics.OverlapSphere(go.transform.position, MassFarming.MassInteractRange.Value, interactMask);

                foreach (var collider in colliders)
                {
                    if (collider?.gameObject?.GetComponentInParent<Beehive>() is Beehive nearbyBeehive &&
                        nearbyBeehive != beehive)
                    {
                        if (PrivateArea.CheckAccess(nearbyBeehive.transform.position))
                        {
                            _ExtractMethod.Invoke(nearbyBeehive, null);
                        }
                    }
                }
            }
        }
    }
}
