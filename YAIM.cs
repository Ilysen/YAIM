using HutongGames.PlayMaker;
using MSCLoader;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Ceres.YAIM.UI;
using HutongGames.PlayMaker.Actions;

namespace Ceres.YAIM
{
	/// <summary>
	/// The main mod script. Core logic is split between here, <see cref="InventoryHandler"/>, and <see cref="UIHandler"/>.
	/// </summary>
	public class YAIM : Mod
	{
		#region Mod setup and settings
		public override string ID => "YAIM";
		public override string Name => "Yet Another Inventory Mod";
		public override string Author => "Ceres et al.";
		public override string Version => "0.1";
		public override string Description => "Carry stuff around! A spiritual successor to many other backpack mods.";

		public override void ModSetup()
		{
			SetupFunction(Setup.PreLoad, Mod_PreLoad);
			SetupFunction(Setup.OnLoad, Mod_Load);
			SetupFunction(Setup.PostLoad, Mod_PostLoad);
			SetupFunction(Setup.Update, Mod_Update);
			SetupFunction(Setup.ModSettings, Mod_Settings);
		}

		private readonly Keybind KeybindToggleGUI = new Keybind("ToggleGUI", "Open or close inventory list", KeyCode.X);
		private readonly Keybind KeybindPickUp = new Keybind("pickUp", "Pick up an item", KeyCode.E);
		private readonly Keybind KeybindDropSelected = new Keybind("dropSelected", "Drop selected item", KeyCode.Y);
		private readonly Keybind KeybindDropAll = new Keybind("dropAll", "Drop all items", KeyCode.Y, KeyCode.LeftControl);

		internal static SettingsCheckBox SettingShowMessages;
		internal static SettingsCheckBox SettingPlaySounds;

		internal static SettingsCheckBox SettingLegacyMode;

		internal static SettingsTextBox SettingMaxSlots;
		internal static SettingsTextBox SettingWeightLimit;
		internal static SettingsTextBox SettingLengthLimit;

		internal static SettingsCheckBox SettingLogSystem;
		internal static SettingsCheckBox SettingLogSaveLoad;
		internal static SettingsCheckBox SettingLogPickupAndDrop;
		internal static SettingsCheckBox SettingLogPickupLogic;

		internal static SettingsCheckBox SettingDisableBlacklist;

		/// <summary>
		/// When this value reaches 0, the interface will automatically be refreshed.
		/// We do this to make sure that players who keep the interface open will see it update with food spoilage, etc.
		/// </summary>
		private float RefreshTimer = 30f;

		/// <summary>
		/// This string is updated with a readable error message when a pickup attempt fails.
		/// </summary>
		private static string FailMessage = string.Empty;

		/// <summary>
		/// The amount of time remaining for <see cref="FailMessage"/> to be displayed.
		/// </summary>
		private static float FailMessageTimer = 0f;

		/// <summary>
		/// The <see cref="FsmString"/> whose value we're overriding with <see cref="FailMessage"/>.
		/// </summary>
		private FsmString FailMessageText;

		/// <summary>
		/// A list of <see cref="GameObject"/>s near the temp point when the scene preloads.
		/// Used to detect objects that should be picked back up after a save/load.
		/// </summary>
		internal static HashSet<GameObject> LoadedColliders;

		/// <summary>
		/// The <see cref="GameObject"/> with an active <see cref="LoadCatcher"/>, used to "catch" saved objects during preload.
		/// </summary>
		private GameObject LoadCatcher;

		private void Mod_Settings()
		{
			Color headingColor = new Color(0.1f, 0.1f, 0.1f);

			Settings.AddText(this, "Inventory details are cached during game load and won't change mid-game on their own. This button forces the inventory to re-initialize, which will update any values that have been changed in the settings since the save was loaded.");
			Settings.AddButton(this, "Re-initialize inventory", RefreshValues);

			Settings.AddHeader(this, "System", headingColor, Color.white);
			SettingShowMessages = Settings.AddCheckBox(this, "showMessages", "Show messages when failing to pick something up", true);
			SettingPlaySounds = Settings.AddCheckBox(this, "playSounds", "Play a sound when opening or closing the GUI", true);

			Settings.AddHeader(this, "Balance", headingColor, Color.white);
			Settings.AddText(this, "For true misery, cut the default values by three quarters to simulate jeans pockets.");
			SettingWeightLimit = Settings.AddTextBox(this, "weightLimitString", "Weight capacity (kg)", "16", "Enter a value.", UnityEngine.UI.InputField.ContentType.DecimalNumber);
			SettingLengthLimit = Settings.AddTextBox(this, "lengthLimitString", "Max item length (cm)", "40", "Enter a value.", UnityEngine.UI.InputField.ContentType.DecimalNumber);

			Settings.AddHeader(this, "Legacy Mode", headingColor, Color.white);
			SettingLegacyMode = Settings.AddCheckBox(this, "legacyMode", "Enable legacy mode", false);
			Settings.AddText(this, "Capacity is determined by a flat number of items, rather than weight limit.");
			SettingMaxSlots = Settings.AddTextBox(this, "maxSlots", "Max items", "10", "Enter a valid number. Values will be clamped between 1 and 15.", UnityEngine.UI.InputField.ContentType.IntegerNumber);
			Settings.AddText(this, "Determines how many items you can hold at a time. <b>Suffering mode uses its own system and ignores whatever's entered here.</b>");

			Settings.AddHeader(this, "Debug", headingColor, Color.white);
			Settings.AddText(this, "If you're running into bugs, these settings will put extra info into your log that'll help the author diagnose the issues. For regular play, you can and should keep them all off.");
			SettingLogSystem = Settings.AddCheckBox(this, "logSystem", "Log system messages", false);
			SettingLogSaveLoad = Settings.AddCheckBox(this, "logSaveLoad", "Log save/load logic", false);
			SettingLogPickupAndDrop = Settings.AddCheckBox(this, "logPickups", "Log pickup and drop events", false);
			SettingLogPickupLogic = Settings.AddCheckBox(this, "logPickupLogic", "Log pickup logic", false);

			Settings.AddHeader(this, "Danger zone", Color.red, Color.white);
			SettingDisableBlacklist = Settings.AddCheckBox(this, "disableBlacklist", "Disable blacklist", false);
			Settings.AddText(this, "Certain objects are blacklisted for stability purposes to ensure things don't break, like the Jonnez. If this option is enabled, that blacklist will be ignored. Don't use this unless you're comfortable risking a broken save.");

			Keybind.Add(this, KeybindPickUp);
			Keybind.Add(this, KeybindDropAll);
			Keybind.Add(this, KeybindDropSelected);
			Keybind.Add(this, KeybindToggleGUI);
		}
		#endregion

		#region Save/load
		private void Mod_PreLoad()
		{
			// Our save/load system is done in a bit of a roundabout way.
			// Due to MSCLoader running its save methods *after* vanilla MSC's, objects and items in storage
			// will be saved at the location of TempPosition.
			//
			// To load these objects, we create a GameObject here (before physics start) with a script that aggressively checks for objects near itself,
			// and then in the regular load function, we iterate through that cache and add any valid objects to the inventory.
			// This is rather messy, but it lets us reliably(?) "load" stored items without requiring any new save data.

			PrintToConsole("Initializing load catcher...", ConsoleMessageScope.System);
			LoadedColliders = new HashSet<GameObject>();
			LoadCatcher = Ceres.YAIM.LoadCatcher.Create(InventoryHandler.TempPosition);
			if (LoadCatcher != null)
				PrintToConsole("Load catcher has been initialized and will detect any saved items.", ConsoleMessageScope.System);
			else
				ModConsole.LogError("Load catcher failed to init! Saved objects will not be detected! Report this to the mod author!");
		}

		private void Mod_Load()
		{
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			PrintToConsole($"{ID} version {Version} is attempting to initialize", ConsoleMessageScope.Core);

			PrintToConsole("Loading assets...", ConsoleMessageScope.System);
			AssetBundle ab = LoadAssets.LoadBundle("YAIM.Assets.yaim_hud.unity3d");
			GameObject hudObject = ab.LoadAsset<GameObject>("YAIM HUD.prefab");
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

			PrintToConsole("Instantiating canvas object...", ConsoleMessageScope.System);
			GameObject gameObject = UnityEngine.Object.Instantiate(hudObject);

			PrintToConsole("Initializing scripts...", ConsoleMessageScope.System);
			GameObject.Find("PLAYER").AddComponent<InventoryHandler>();
			var handler = gameObject.AddComponent<UIHandler>();
			handler.CloseSounds = closeSounds;
			handler.OpenSounds = openSounds;
			FailMessageText = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");

			stopwatch.Stop();
			PrintToConsole($"{ID} initialized after {stopwatch.Elapsed.Milliseconds} ms!", ConsoleMessageScope.Core);
		}

		private void Mod_PostLoad()
		{
			// Handle loading saved items in post-load instead of regular load, to allow for modded items to initialize beforehand and thus be picked up
			PrintToConsole("Detecting saved items...", ConsoleMessageScope.SaveLoad);
			if (LoadedColliders.Count == 0)
				PrintToConsole("Found no saved items to load.", ConsoleMessageScope.SaveLoad);
			else
			{
				PrintToConsole("Loading saved items...", ConsoleMessageScope.SaveLoad);
				int loadedItems = 0;
				foreach (GameObject go in LoadedColliders)
				{
					if (InventoryHandler.Singleton.AttemptPickUp(go, true))
						loadedItems++;
				}
				PrintToConsole($"Loaded {loadedItems} saved item(s) to the inventory; {LoadedColliders.Count - loadedItems} collider(s) filtered out.", ConsoleMessageScope.SaveLoad);
			}

			PrintToConsole("Destroying load catcher...", ConsoleMessageScope.System);
			GameObject.Destroy(LoadCatcher);
		}
		#endregion

		#region Core logic
		private void Mod_Update()
		{
			//ModConsole.Print($"Fuelpump transform: {(fuelPump != null ? fuelPump.transform.position.ToString() : "NULL")}");
			if (ModLoader.CurrentScene != CurrentScene.Game || !UIHandler.Singleton || !InventoryHandler.Singleton)
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
			if (RefreshTimer < 0 && UIHandler.Singleton.gameObject.activeSelf)
			{
				RefreshTimer = 30f;
				UIHandler.Singleton.Refresh();
			}

			if (KeybindToggleGUI.GetKeybindDown())
				UIHandler.Singleton.Toggle();
			if (!UIHandler.Singleton.gameObject.activeSelf)
				return;
			if (KeybindDropAll.GetKeybindDown())
				InventoryHandler.Singleton.DropAll();
			else if (KeybindPickUp.GetKeybindDown())
			{
				var hits = UnifiedRaycast.GetRaycastHits();
				foreach (var hit in hits)
				{
					if (hit.distance <= 1f && hit.collider?.gameObject != null)
					{
						GameObject go = hit.collider.gameObject;
						if (InventoryHandler.Singleton.AttemptPickUp(go))
						{
							UIHandler.Singleton.Refresh();
							break;
						}
					}
				}
			}
			else if (KeybindDropSelected.GetKeybindDown())
			{
				InventoryHandler.Singleton.DropCurrent();
				UIHandler.Singleton.Refresh();
			}
			float scroll = Input.GetAxis("Mouse ScrollWheel");
			if (scroll != 0f)
				UIHandler.Singleton.AdjustActiveIndex(scroll < 0);
		}

		/// <summary>
		/// Throws a readable failure message with the provided contents for 1 second.
		/// </summary>
		/// <param name="Message">The message to display.</param>
		internal static void ThrowMessage(string Message)
		{
			if (!SettingShowMessages.GetValue())
				return;
			FailMessage = Message;
			FailMessageTimer = 1f;
		}

		/// <summary>
		/// Wrapper for calls <see cref="InventoryHandler.SetupValues"/>. Wrapping it in a function lets us make it nullable to avoid runtimes.
		/// </summary>
		private void RefreshValues() => InventoryHandler.Singleton?.SetupValues();
		#endregion

		#region Debug
		/// <summary>
		/// Used to track the context of a given debug message.
		/// </summary>
		internal enum ConsoleMessageScope
		{
			// Important stuff!
			Core, // Core logic that we always log
			System, // Intermediary steps in initialization
			SaveLoad, // Saving and loading items

			// Not as important, but still good to know
			PickupAndDrop, // Picking up and dropping items

			// Very granular info -- should skip outside of thorough debugging
			PickupLogic, // Detailed steps for each attempted item pickup, including reasons for failed pickups
		}

		/// <summary>
		/// Creates a debug message with the provided contents and scope.
		/// Each scope has an associated setting variable; that way, we can
		/// use settings to curate which messages appear in the console and which ones we gloss over.
		/// </summary>
		/// <param name="Message">The contents of the debug message.</param>
		/// <param name="Context">The context of the message.</param>
		internal static void PrintToConsole(string Message, ConsoleMessageScope Context)
		{
			if (Context == ConsoleMessageScope.System && !SettingLogSystem.GetValue())
				return;
			else if (Context == ConsoleMessageScope.SaveLoad && !SettingLogSaveLoad.GetValue())
				return;
			else if (Context == ConsoleMessageScope.PickupAndDrop && !SettingLogPickupAndDrop.GetValue())
				return;
			else if (Context == ConsoleMessageScope.PickupLogic && !SettingLogPickupLogic.GetValue())
				return;
			ModConsole.Print($"[YAIM] {Message}");
		}
		#endregion
	}
}
