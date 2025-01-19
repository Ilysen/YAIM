using HutongGames.PlayMaker;
using MSCLoader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Mail;
using UnityEngine;
using YAIM.UI;

namespace YAIM
{
	public class YAIM : Mod
	{
		#region Mod setup and settings
		public override string ID => "YAIM"; // Your (unique) mod ID 
		public override string Name => "Yet Another Inventory Mod"; // Your mod name
		public override string Author => "Ceres et al."; // Name of the Author (your name)
		public override string Version => "0.1"; // Version
		public override string Description => ""; // Short description of your mod

		public override void ModSetup()
		{
			SetupFunction(Setup.OnLoad, Mod_Load);
			SetupFunction(Setup.OnSave, Mod_OnSave);
			SetupFunction(Setup.Update, Mod_Update);
			SetupFunction(Setup.ModSettings, Mod_Settings);
		}

		private readonly Keybind _pickUpKey = new Keybind("pickUp", "Pick up an item", KeyCode.E);
		private readonly Keybind _dropSelectedKey = new Keybind("dropSelected", "Drop selected item", KeyCode.Y);
		private readonly Keybind _dropAllKey = new Keybind("dropAll", "Drop all items", KeyCode.Y, KeyCode.LeftControl);
		private readonly Keybind _toggleGuiKey = new Keybind("ToggleGUI", "Open or close inventory list", KeyCode.X);

		internal static SettingsCheckBox ShowMessages;
		internal static SettingsCheckBox PickupOnlyWhenOpen;
		internal static SettingsCheckBox DropOnlyWhenOpen;

		internal static SettingsTextBox ItemLimit;

		internal static SettingsCheckBox EnableSufferingMode;
		internal static SettingsTextBox WeightLimit;
		internal static SettingsTextBox LengthLimit;

		internal static SettingsCheckBox LogSystem;
		internal static SettingsCheckBox LogSaveLoad;
		internal static SettingsCheckBox LogPickupAndDrop;
		internal static SettingsCheckBox LogPickupLogic;
		internal static SettingsCheckBox LogInvalidPickups;
		internal static SettingsCheckBox LogRejectedPickups;

		internal static SettingsCheckBox DisableBlacklist;

		private float RefreshTimer = 30f;
		internal static float FailMessageTimer = 0f;
		internal static string FailMessage = string.Empty;
		private FsmString FailMessageText;

		private void Mod_Settings()
		{
			Color headingColor = new Color(0.1f, 0.1f, 0.1f);

			Settings.AddHeader(this, "System", headingColor, Color.white);
			PickupOnlyWhenOpen = Settings.AddCheckBox(this, "pickupOnlyWhenOpen", "Pick up only if the list is visible", true);
			DropOnlyWhenOpen = Settings.AddCheckBox(this, "dropOnlyWhenOpen", "Drop items only if the list is visible", true);
			ShowMessages = Settings.AddCheckBox(this, "showMessages", "Show messages when failing to pick something up", true);

			Settings.AddHeader(this, "Balance", headingColor, Color.white);
			ItemLimit = Settings.AddTextBox(this, "itemCapacityString", "Item limit", "10", "Enter a valid number. Values will be clamped between 1 and 50.", UnityEngine.UI.InputField.ContentType.IntegerNumber);
			Settings.AddText(this, "Determines how many items you can hold at a time. Suffering mode uses its own system and ignores this.");

			Settings.AddHeader(this, "Suffering mode", headingColor, Color.white);
			EnableSufferingMode = Settings.AddCheckBox(this, "simulateContainer", "Enable suffering mode", true);
			Settings.AddText(this, "Roughly simulates an actual container using weight and length limits. No volume, though! For true misery, cut the default values by three quarters to simulate jeans pockets.");
			WeightLimit = Settings.AddTextBox(this, "weightLimitString", "Weight capacity (kg)", "16", "Enter a value.", UnityEngine.UI.InputField.ContentType.DecimalNumber);
			LengthLimit = Settings.AddTextBox(this, "lengthLimitString", "Max item length (cm)", "40", "Enter a value.", UnityEngine.UI.InputField.ContentType.DecimalNumber);

			Settings.AddHeader(this, "Debug", headingColor, Color.white);
			LogSystem = Settings.AddCheckBox(this, "logSystem", "Log system messages", true);
			LogSaveLoad = Settings.AddCheckBox(this, "logSaveLoad", "Log save/load logic", true);
			LogPickupAndDrop = Settings.AddCheckBox(this, "logPickups", "Log pickup and drop events", false);
			LogPickupLogic = Settings.AddCheckBox(this, "logPickupLogic", "Log pickup logic", false);
			LogInvalidPickups = Settings.AddCheckBox(this, "logInvalids", "Log invalid pickup attempts", false);
			LogRejectedPickups = Settings.AddCheckBox(this, "logRejected", "Log rejected pickup attempts", false);

			Settings.AddHeader(this, "Danger zone", Color.red, Color.white);
			DisableBlacklist = Settings.AddCheckBox(this, "disableBlacklist", "Disable blacklist", false);
			Settings.AddText(this, "Certain objects are blacklisted for stability purposes to ensure things don't break, like the Jonnez. If this option is enabled, that blacklist will be ignored. Don't use this unless you know what you're doing or you're comfortable risking a broken save.");
			/*Settings.AddButton(this, "Refresh in-game values", RefreshValues);
			Settings.AddText(this, "Inventory details like item max, suffering mode, etc. are cached during game load and won't change mid-game on their own. This button forces the backpack to re-initialize, which will update any values that have been changed in the settings since the save was loaded.");*/

			Keybind.Add(this, _pickUpKey);
			Keybind.Add(this, _dropAllKey);
			Keybind.Add(this, _dropSelectedKey);
			Keybind.Add(this, _toggleGuiKey);
		}
		#endregion

		#region Save/load
		private void Mod_OnSave()
		{
			for (int i = 0; i < Inventory.Singleton.Items.Count; i++)
			{
				Inventory.Singleton.DropCurrent();
			}
		}

		private void Mod_Load()
		{
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			PrintToConsole("YAIM is attempting to initialize", YAIM.ConsoleMessageScope.System);

			PrintToConsole("   Loading assets...", YAIM.ConsoleMessageScope.System);
			AssetBundle ab = LoadAssets.LoadBundle("YAIM.Assets.yaim_hud.unity3d");
			GameObject go = ab.LoadAsset<GameObject>("YAIM HUD.prefab");
			List<AudioClip> closeSounds = new List<AudioClip>();
			List<AudioClip> openSounds = new List<AudioClip>();
			foreach (var asset in ab.LoadAllAssets<AudioClip>())
			{
				if (asset.name.Contains("close"))
					closeSounds.Add(asset);
				else
					openSounds.Add(asset);
			}
			ab.Unload(false);

			PrintToConsole("   Instantiating canvas object...", YAIM.ConsoleMessageScope.System);
			GameObject gameObject = UnityEngine.Object.Instantiate(go);

			PrintToConsole("   Initializing scripts...", YAIM.ConsoleMessageScope.System);
			GameObject.Find("PLAYER").AddComponent<Inventory>();
			var handler = gameObject.AddComponent<UIHandler>();
			handler.CloseSounds = closeSounds;
			handler.OpenSounds = openSounds;
			FailMessageText = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");

			stopwatch.Stop();
			PrintToConsole($"YAIM initialized after {Math.Round((float)stopwatch.Elapsed.Milliseconds, 2)} seconds!", YAIM.ConsoleMessageScope.System);
		}
		#endregion

		#region Core logic
		private void Mod_Update()
		{
			if (ModLoader.CurrentScene != CurrentScene.Game || !UIHandler.Singleton || !Inventory.Singleton)
				return;
			if (FailMessageTimer > 0)
			{
				FailMessageTimer -= Time.deltaTime;
				FailMessageText.Value = FailMessage;
			}
			else
			{
				if (FailMessage != string.Empty && FailMessageText.Value == FailMessage)
				{
					FailMessageText.Value = string.Empty;
					FailMessage = string.Empty;
				}
			}

			// We need to occasionally refresh the interface to account for things like food spoilage
			// Instead of doing it every frame, we only do it every 30 seconds to preserve performance
			RefreshTimer -= Time.deltaTime;
			if (RefreshTimer < 0)
			{
				RefreshTimer = 30f;
				UIHandler.Singleton.Refresh();
			}

			if (_pickUpKey.GetKeybindDown())
			{
				var hits = UnifiedRaycast.GetRaycastHits();
				foreach (var hit in hits)
				{
					if (hit.distance <= 1f && hit.collider?.gameObject != null)
					{
						GameObject go = hit.collider.gameObject;
						if (Inventory.Singleton.PickUp(go))
						{
							UIHandler.Singleton.Refresh();
							break;
						}
					}
				}
			}
			if (_dropSelectedKey.GetKeybindDown())
			{
				Inventory.Singleton.DropCurrent();
				UIHandler.Singleton.Refresh();
			}
			if (_toggleGuiKey.GetKeybindDown())
			{
				var handler = UIHandler.Singleton;
				handler.gameObject.SetActive(!handler.gameObject.activeSelf);
				var listToUse = handler.gameObject.activeSelf ? handler.OpenSounds : handler.CloseSounds;
				AudioClip toPlay = listToUse[UnityEngine.Random.Range(0, listToUse.Count)];
				handler.Audio.pitch = (float)(UnityEngine.Random.Range(90, 111)) / 100f;
				handler.Audio.PlayOneShot(toPlay);
				handler.Refresh();
			}
			if (UIHandler.Singleton.gameObject.activeSelf)
			{
				float scroll = Input.GetAxis("Mouse ScrollWheel");
				if (scroll != 0f)
				{
					UIHandler.Singleton.AdjustActiveIndex(scroll < 0);
				}
			}
		}

		internal static void ThrowMessage(string message)
		{
			if (!ShowMessages.GetValue())
				return;
			FailMessage = message;
			FailMessageTimer = 1f;
		}
		#endregion

		#region Debug
		internal enum ConsoleMessageScope
		{
			System,
			SaveLoad,
			PickupAndDrop,
			PickupLogic,
			InvalidPickups,
			RejectedPickups
		}

		internal static void PrintToConsole(string msg, ConsoleMessageScope scope)
		{
			if (scope == ConsoleMessageScope.System && !LogSystem.GetValue())
				return;
			else if (scope == ConsoleMessageScope.SaveLoad && !LogSaveLoad.GetValue())
				return;
			else if (scope == ConsoleMessageScope.PickupAndDrop && !LogPickupAndDrop.GetValue())
				return;
			else if (scope == ConsoleMessageScope.PickupLogic && !LogPickupLogic.GetValue())
				return;
			else if (scope == ConsoleMessageScope.InvalidPickups && !LogInvalidPickups.GetValue())
				return;
			else if (scope == ConsoleMessageScope.RejectedPickups && !LogRejectedPickups.GetValue())
				return;
			ModConsole.Print(msg);
		}
		#endregion
	}
}
