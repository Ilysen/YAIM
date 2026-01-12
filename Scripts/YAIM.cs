using Ceres.YAIM.UI;
using HutongGames.PlayMaker;
using MSCLoader;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

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
		public override string Version => "2.2";
		public override string Description => "Carry stuff around! A spiritual successor to many other backpack mods.";
		public override Game SupportedGames => Game.MySummerCar_And_MyWinterCar;

		internal static YAIM Singleton;

		public override void ModSetup()
		{
			SetupFunction(Setup.PreLoad, Mod_PreLoad);
			SetupFunction(Setup.OnLoad, Mod_Load);
			SetupFunction(Setup.Update, Mod_Update);
			SetupFunction(Setup.ModSettings, Mod_Settings);
		}

		public static SettingsKeybind KeybindToggleGUI;
		public static SettingsKeybind KeybindPickUp;
		public static SettingsKeybind KeybindDropSelected;
		public static SettingsKeybind KeybindDropAll;
		public static SettingsKeybind KeybindScrollUp;
		public static SettingsKeybind KeybindScrollDown;

		internal static SettingsCheckBox SettingShowMessages;
		internal static SettingsCheckBox SettingPlaySounds;
		internal static SettingsTextBox SettingsPageLimit;

		internal static SettingsCheckBox SettingLegacyMode;

		internal static SettingsTextBox SettingMaxSlots;
		internal static SettingsTextBox SettingWeightLimit;
		internal static SettingsTextBox SettingLengthLimit;

		internal static SettingsCheckBox SettingLogSystem;
		internal static SettingsCheckBox SettingLogSaveLoad;
		internal static SettingsCheckBox SettingLogPickupAndDrop;
		internal static SettingsCheckBox SettingLogPickupLogic;

		internal static SettingsCheckBox SettingVisibleLoadCatcher;
		internal static SettingsCheckBox SettingPersistLoadCatcher;

		internal static SettingsCheckBox SettingDisableBlacklist;

		/// <summary>
		/// When this value reaches 0, the interface will automatically be refreshed.
		/// We do this to make sure that players who keep the interface open will see it update with food spoilage, etc.
		/// </summary>
		private float RefreshTimer = 30f;

		/// <summary>
		/// Safety timer for dropping all items; has to be held for a little bit, essentially making sure that
		/// the player confirms they want to drop everything instead of pressing the key accidentally
		/// </summary>
		private float DropAllTimer = 3f;

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

		/// <summary>
		/// Loaded from "yaim_hud.unity3d" as an embedded resource.
		/// First loaded in Setup.PreLoad, fully unpacked and unloaded in Setup.OnLoad.
		/// </summary>
		private AssetBundle AssetBundle;

		private void Mod_Settings()
		{
			KeybindToggleGUI = Keybind.Add("toggleGUI", "Open inventory", KeyCode.X);
			KeybindPickUp = Keybind.Add("pickUp", "Pick up an item", KeyCode.E);
			KeybindDropSelected = Keybind.Add("dropSelected", "Drop selected item", KeyCode.Y);
			KeybindDropAll = Keybind.Add("dropAll", "Drop all items", KeyCode.Y, KeyCode.LeftControl);
			KeybindScrollUp = Keybind.Add("scrollUp", "Scroll up", KeyCode.None);
			KeybindScrollDown = Keybind.Add("scrollDown", "Scroll down", KeyCode.None);

			Color headingColor = new Color(0.1f, 0.1f, 0.1f);

			Settings.AddText("Inventory limits like max weight and length are only set once, during game load. This button forces the inventory to re-initialize, which will update them without needing to save and exit.");
			Settings.AddButton("Re-initialize inventory", RefreshValues);

			Settings.AddHeader("System", headingColor, Color.white);
			SettingShowMessages = Settings.AddCheckBox("showMessages", "Show messages when failing to pick something up", true);
			SettingPlaySounds = Settings.AddCheckBox("playSounds", "Play a sound when opening or closing the inventory", true);
			// it's actually set to 20 in the code but nobody's gonna check NYEHEH
			SettingsPageLimit = Settings.AddTextBox("pageEntries", "Items shown per page (minimum 2, max 15)", "10", "Enter a valid number. Default: 10", UnityEngine.UI.InputField.ContentType.IntegerNumber);

			Settings.AddHeader("Balance", headingColor, Color.white);
			Settings.AddText("For true misery, cut the default values by three quarters to simulate jeans pockets.");
			SettingWeightLimit = Settings.AddTextBox("weightLimitString", "Weight capacity (kg)", "16", "Enter a value. Default: 16", UnityEngine.UI.InputField.ContentType.DecimalNumber);
			SettingLengthLimit = Settings.AddTextBox("lengthLimitString", "Max item length (cm)", "40", "Enter a value. Default: 40", UnityEngine.UI.InputField.ContentType.DecimalNumber);

			Settings.AddHeader("Legacy mode", headingColor, Color.white);
			SettingLegacyMode = Settings.AddCheckBox("legacyMode", "Enable legacy mode", false);
			Settings.AddText("Capacity is determined by a flat number of items, rather than weight limit. Minimum value 1. Maximum value 100, but setting it too high may cause stability issues.");
			SettingMaxSlots = Settings.AddTextBox("maxSlots", "Max items", "10", "Enter a valid number. Default: 10", UnityEngine.UI.InputField.ContentType.IntegerNumber);

			Settings.AddHeader("Logging", headingColor, Color.white);
			Settings.AddText("If you're running into bugs, these settings will put extra info into your log that'll help the author diagnose the issues. Keep them all off for regular play, but please turn them on when submitting a bug report!");
			SettingLogSystem = Settings.AddCheckBox("logSystem", "Log system messages", false);
			SettingLogSaveLoad = Settings.AddCheckBox("logSaveLoad", "Log save/load logic", false);
			SettingLogPickupAndDrop = Settings.AddCheckBox("logPickups", "Log pickup and drop events", false);
			SettingLogPickupLogic = Settings.AddCheckBox("logPickupLogic", "Log pickup logic", false);

			Settings.AddHeader("Debug", headingColor, true);
			Settings.AddText("These options are all for debugging purposes only! Turning them on WILL cause instability!");
			SettingVisibleLoadCatcher = Settings.AddCheckBox("visibleLoadCatcher", "Render load catcher", false);
			SettingPersistLoadCatcher = Settings.AddCheckBox("persistLoadCatcher", "Persist load catcher", false);

			Settings.AddHeader("Danger zone", Color.red, Color.white, true);
			SettingDisableBlacklist = Settings.AddCheckBox("disableBlacklist", "Disable blacklist", false);
			Settings.AddText("Certain objects are blacklisted for stability purposes to ensure things don't break, like the Jonnez. If this option is enabled, that blacklist will be ignored. Don't use this unless you're comfortable risking a broken save.");
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
			
			Singleton = this; // There's almost certainly a built-in way to reference the mod instance in MSCLoader, but I don't know of it. teehee!
			PrintToConsole("Loading asset bundle...", ConsoleMessageScope.System);
			AssetBundle = LoadAssets.LoadBundle("YAIM.AssetBundles.yaim_hud.unity3d");
			PrintToConsole("Initializing load catcher...", ConsoleMessageScope.System);
			LoadedColliders = new HashSet<GameObject>();
			LoadCatcher = Ceres.YAIM.LoadCatcher.Create(InventoryHandler.TempPosition, AssetBundle);
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
			GameObject hudObject = AssetBundle.LoadAsset<GameObject>("YAIM HUD.prefab");
			List<AudioClip> closeSounds = new List<AudioClip>();
			List<AudioClip> openSounds = new List<AudioClip>();
			foreach (var asset in AssetBundle.LoadAllAssets<AudioClip>())
			{
				if (asset.name.Contains("close"))
					closeSounds.Add(asset);
				else
					openSounds.Add(asset);
			}
			PrintToConsole("Unloading asset bundle...", ConsoleMessageScope.System);
			AssetBundle.Unload(false);

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
			PrintToConsole($"Enabled logging levels: {SettingLogSystem.GetValue()}, {SettingLogSaveLoad.GetValue()}, {SettingLogPickupLogic.GetValue()}, {SettingLogPickupAndDrop.GetValue()}", ConsoleMessageScope.Core);
		}

		/// <summary>
		/// Iterates through <c><see cref="LoadedColliders"/></c> to add every stored item to the inventory.
		/// This is a solution to variable hardware and mod setups; maintaining the load catcher until the inventory is manually opened
		/// gives plenty of time for it to catch any items that it might otherwise miss between loading phases.
		/// </summary>
		internal void UnpackCachedColliders()
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
			LoadedColliders.Clear();

			if (!SettingPersistLoadCatcher.GetValue())
			{
				PrintToConsole("Destroying load catcher...", ConsoleMessageScope.System);
				GameObject.Destroy(LoadCatcher);
			}
			else
				ModConsole.LogError("[YAIM] Load catcher is NOT BEING DELETED due to an enabled debugging setting. If you're a regular player, TURN THAT SETTING OFF or it will cause problems!!");
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
			{
				if (LoadCatcher != null)
					UnpackCachedColliders();
				UIHandler.Singleton.Toggle();
			}
			if (!UIHandler.Singleton.gameObject.activeSelf)
				return;
			if (KeybindDropAll.GetKeybind() && InventoryHandler.Singleton.Items.Count > 0)
			{
				DropAllTimer -= Time.deltaTime;
				ThrowMessage("Dropping all items...", 0.3f);
				if (DropAllTimer <= 0f)
				{
					InventoryHandler.Singleton.DropAll();
					DropAllTimer = 3f;
					FailMessageTimer = 0f;
				}
			}
			else
			{
				DropAllTimer = 3f;
				if (KeybindPickUp.GetKeybindDown())
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
				else if (KeybindScrollDown.GetKeybindDown())
					UIHandler.Singleton.AdjustActiveIndex(true); // Since the list goes up instead of down, this is technically inverted
				else if (KeybindScrollUp.GetKeybindDown())
					UIHandler.Singleton.AdjustActiveIndex(false);
			}
		}

		/// <summary>
		/// Throws a readable failure message with the provided contents for 1 second.
		/// </summary>
		/// <param name="Message">The message to display.</param>
		internal static void ThrowMessage(string Message, float Timer = 1f)
		{
			if (!SettingShowMessages.GetValue())
				return;
			FailMessage = Message;
			FailMessageTimer = Timer;
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
