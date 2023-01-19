using HarmonyLib;
using NeosModLoader;
using FrooxEngine;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Reflection;
using System;
using BaseX;

namespace ScreenDashFixer
{
    public class ScreenDashFixer : NeosMod
    {
        public override string Name => "ScreenDashFixer";
        public override string Author => "rampa3";
        public override string Version => "1.1.0";
        public override string Link => "https://github.com/rampa3/ScreenDashFixer/";
        private static ModConfiguration Config;
        private static bool desktopNotificationsPresent = false;
        public override void OnEngineInit()
        {
            checkForDesktopNotifications();  //detect if DesktopNotifications mod is present
            Config = GetConfiguration();
            Config.Save(true);
            Harmony harmony = new Harmony("net.rampa3.ScreenDashFixer");
            patchSlotPositioning(harmony);
            addDesktopControlPanelKeybind(harmony);
            patchCameraUI(harmony);
            if (!desktopNotificationsPresent)
            {
                patchNotifications(harmony);
            }
            Debug("All patches applied successfully!");
        }

		void checkForDesktopNotifications()
		{
			IEnumerable<NeosModBase> mods = ModLoader.Mods();
			foreach (NeosModBase mod in mods)
			{
				if (mod.Name == "DesktopNotifications")  //check for DesktopNotifications, and if present, set a boolean to tell the patch about its presence
				{
					desktopNotificationsPresent = true;
					break;
				}
			}
			Debug("DesktopNotificatons found: " + desktopNotificationsPresent);
		}

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<Key> DESKTOP_CONTROL_PANEL_KEY = new ModConfigurationKey<Key>("DesktopControlPanelKey", "Desktop tab control panel key", () => Key.N);

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<bool> RELEASE_CAM_UI = new ModConfigurationKey<bool>("ReleaseCamUI", "Release Camera Controls UI from its slider (requires restart on change)", () => false);

		private static void addDesktopControlPanelKeybind(Harmony harmony)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(DesktopController), "OnCommonUpdate", new Type[] { });
			MethodInfo postfix = AccessTools.DeclaredMethod(typeof(ScreenDashFixer), nameof(DesktopControlsKeybindPostfix));
			harmony.Patch(original, postfix: new HarmonyMethod(postfix));
			Debug("Desktop tab control panel key added!");
		}

		private static void DesktopControlsKeybindPostfix(DesktopController __instance)
		{
            MethodInfo toggleControls = __instance.GetType().GetMethod("ToggleControls", BindingFlags.NonPublic | BindingFlags.Instance);
            if (__instance.InputInterface.GetKeyDown(Config.GetValue(DESKTOP_CONTROL_PANEL_KEY)) && __instance.InputInterface.ScreenActive)
            {
                toggleControls.Invoke(__instance, new Object[] { });
            }
        }

		private static void patchNotifications(Harmony harmony)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(NotificationPanel), "AddNotification", new Type[] { typeof(string), typeof(string), typeof(Uri), typeof(color), typeof(string), typeof(Uri), typeof(IAssetProvider<AudioClip>) });
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ScreenDashFixer), nameof(NotificationsTranspiler));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			Debug("Notifications patched!");
		}

		private static IEnumerable<CodeInstruction> NotificationsTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count; i++)
			{
				if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Call && ((MethodInfo)codes[i + 1].operand == typeof(InputInterface).GetMethod("get_Slot")) && codes[i + 2].opcode == OpCodes.Ldarg_S && codes[i + 3].opcode == OpCodes.Ldc_R4) //find the right getter to spoof by looking at stuff above it
				{
					codes[i + 4].opcode = OpCodes.Nop;  //Nop loading base reference onto stack
					codes[i + 5].opcode = OpCodes.Nop;  //Nop call to get InputInterface instance, that would use the base refernce
					codes[i + 6].opcode = OpCodes.Ldc_I4_1;  //instead nof callvirt for method getting the state of VR_Active, load true on stack
				}

				if (codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Call && ((MethodInfo)codes[i + 1].operand == typeof(Worker).GetMethod("get_InputInterface")) && codes[i + 2].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i + 2].operand == typeof(InputInterface).GetMethod("get_VR_Active")) && codes[i + 3].opcode == OpCodes.Brtrue_S && codes[i + 4].opcode == OpCodes.Ret)
				{
					codes[i].opcode = OpCodes.Nop;
					codes[i + 1].opcode = OpCodes.Nop;
					codes[i + 2].opcode = OpCodes.Nop;  //change the whole if statement and it's contents to do nothing
					codes[i + 3].opcode = OpCodes.Nop;
					codes[i + 4].opcode = OpCodes.Nop;
				}
			}
			return codes.AsEnumerable();
		}

		private static void patchCameraUI(Harmony harmony)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(InteractiveCameraControl), "OnAttach", new Type[] { });
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ScreenDashFixer), nameof(CameraUITranspiler));
			MethodInfo postfix = AccessTools.DeclaredMethod(typeof(ScreenDashFixer), nameof(removeCamUISlider));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			harmony.Patch(original, postfix: new HarmonyMethod(postfix));
			Debug("Camera Controls patched!");
		}

		private static IEnumerable<CodeInstruction> CameraUITranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			for (var i = 0; i < codes.Count; i++)
			{
				if (!Config.GetValue(RELEASE_CAM_UI) && codes[i].opcode == OpCodes.Ldarg_0 && codes[i + 1].opcode == OpCodes.Call && codes[i + 2].opcode == OpCodes.Callvirt && ((MethodInfo)codes[i + 2].operand == typeof(InputInterface).GetMethod("get_VR_Active")) && codes[i + 3].opcode == OpCodes.Brfalse_S)
				{
					/*Debug(codes[i]);
					Debug(codes[i + 1]);
					Debug(codes[i + 2]);
					Debug(codes[i + 3]);*/
					codes[i].opcode = OpCodes.Nop;
					codes[i + 1].opcode = OpCodes.Nop;
					codes[i + 2].opcode = OpCodes.Nop;  //change the whole if statement and it's contents to do nothing
					codes[i + 3].opcode = OpCodes.Nop;
					/*Debug(codes[i]);
					Debug(codes[i + 1]);
					Debug(codes[i + 2]);
					Debug(codes[i + 3]);*/
				}

				if (Config.GetValue(RELEASE_CAM_UI) && codes[i].opcode == OpCodes.Dup && codes[i + 1].opcode == OpCodes.Brtrue_S && codes[i + 2].opcode == OpCodes.Pop && codes[i + 3].opcode == OpCodes.Br_S)  //find the grabbable destroy call
				{
					codes[i + 4].opcode = OpCodes.Pop;  //remove it and instead remove the surplus grabbacle reference
				}
			}
			return codes.AsEnumerable();
		}

		private static void removeCamUISlider(InteractiveCameraControl __instance)
		{
			if (Config.GetValue(RELEASE_CAM_UI))
			{
				Slider slider = __instance.Slot.GetComponent<Slider>(null, false);
				slider.Destroy();
			}
		}

		private static void patchSlotPositioning(Harmony harmony)
		{
			MethodInfo original = AccessTools.DeclaredMethod(typeof(SlotPositioning), "PositionInFrontOfUser", new Type[] { typeof(Slot), typeof(float3?), typeof(float3?), typeof(float), typeof(User), typeof(bool), typeof(bool), typeof(bool) });
			MethodInfo transpiler = AccessTools.DeclaredMethod(typeof(ScreenDashFixer), nameof(positioningTranspiler));
			harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));
			Debug("Slot positioning patched!");
		}

		private static IEnumerable<CodeInstruction> positioningTranspiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = new List<CodeInstruction>(instructions);
			if (codes[0].opcode == OpCodes.Ldarg_0 && codes[1].opcode == OpCodes.Callvirt && codes[2].opcode == OpCodes.Call && codes[3].opcode == OpCodes.Bne_Un_S)
			{
				codes[0].opcode = OpCodes.Nop;
				codes[1].opcode = OpCodes.Nop; //replace if with unconditional jump
				codes[2].opcode = OpCodes.Nop;
				codes[3].opcode = OpCodes.Br_S;
			}
			else
			{
				Error("SlotPositioning.PositionInFrontOfUser: Could not patch because of unexpected opcode");
				return instructions;
			}
			return codes;
		}
	}
}