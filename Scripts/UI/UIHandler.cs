using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ceres.YAIM.UI
{
	/// <summary>
	/// Controller script for the inventory's GUI. Handles visuals, sounds, and other front-end stuff.
	/// </summary>
	internal class UIHandler : MonoBehaviour
	{
		/// <summary>
		/// The sole <see cref="UIHandler"/> instance.
		/// </summary>
		internal static UIHandler Singleton;

		#region Fields
		/// <summary>
		/// The big "INVENTORY" text at the bottom of the list.
		/// Updates to keep track of stored and max mass in suffering mdoe, or item slots in regular mode.
		/// </summary>
		private readonly Text Header;

		/// <summary>
		/// The parent <see cref="Transform"/> that all item entry <see cref="GameObject"/>s are children to.
		/// </summary>
		private readonly Transform EntriesGroup;

		/// <summary>
		/// A list of entries for each item stored in the inventory.
		/// New entries are dynamically cloned or destroyed as needed.
		/// </summary>
		private readonly List<GameObject> Entries;

		/// <summary>
		/// The color to be used for all of the GUI's text.
		/// <see cref="Color.yellow"/> isn't the right one, so we use this!
		/// </summary>
		private readonly Color DefaultTextColor = new Color(255, 255, 0);

		/// <summary>
		/// The index of whatever item is currently highlighted in the interface.
		/// </summary>
		internal int CurIndex = 0;

		/// <summary>
		/// The <see cref="AudioSource"/> used to play open and close sounds.
		/// </summary>
		internal AudioSource Audio;
		/// <summary>
		/// A list of sounds to be randomly picked from when opening the interface.
		/// </summary>
		internal List<AudioClip> OpenSounds;
		/// <summary>
		/// As <see cref="OpenSounds"/>, but for closing instead.
		/// </summary>
		internal List<AudioClip> CloseSounds;
		#endregion

		#region Functions
		public UIHandler()
		{
			YAIM.PrintToConsole("Attempting to create canvas...", YAIM.ConsoleMessageScope.System);

			Header = gameObject.transform.FindChild("Canvas/UI/Header").GetComponent<Text>();

			EntriesGroup = gameObject.transform.Find("Canvas/UI/Entries");
			Entries = new List<GameObject>() { gameObject.transform.FindChild("Canvas/UI/Entries/1").gameObject };

			gameObject.SetActive(false);
			var go = new GameObject();
			go.transform.parent = GameObject.Find("PLAYER").transform;
			Audio = go.AddComponent<AudioSource>();
			Audio.spatialBlend = 0f;
			Refresh();
			Singleton = this;
			YAIM.PrintToConsole("Created canvas script and linked to UI object.", YAIM.ConsoleMessageScope.System);
		}

		/// <summary>
		/// Refreshes all of the GUI's information to reflect the current state of <see cref="InventoryHandler"/> singleton.
		/// When the GUI isn't visible, this is skipped entirely in order to save on performance.
		/// </summary>
		internal void Refresh()
		{
			if (!gameObject.activeSelf)
				return;
			YAIM.PrintToConsole("Now refreshing inventory GUI", YAIM.ConsoleMessageScope.System);
			InventoryHandler inventory = InventoryHandler.Singleton;
			if (!inventory.SufferingMode)
				UpdateText(Header, $"INVENTORY ({inventory.Items.Count}/{inventory.MaxSlots})", DefaultTextColor);
			else
				UpdateText(Header, $"INVENTORY ({Math.Round(inventory.Mass, 2)} KG / {inventory.MassCapacity} KG)", DefaultTextColor);

			// Calculate and handle changes in the number of entry objects
			int difference = inventory.Items.Count - Entries.Count;
			if (difference != 0)
			{
				YAIM.PrintToConsole($"Difference in entries equals {difference}. Equalizing.", YAIM.ConsoleMessageScope.System);
				for (byte i = 0; i < Math.Abs(difference); i++)
				{
					// If adding new objects, we make clones from the first object in the list...
					if (difference > 0)
					{
						YAIM.PrintToConsole("Creating new entry...", YAIM.ConsoleMessageScope.System);
						GameObject newEntry = GameObject.Instantiate(Entries[0].gameObject);
						newEntry.transform.SetParent(EntriesGroup, false);
						Entries.Add(newEntry);
					}
					// ...and if removing objects, we destroy entries starting from the end of the list
					else
					{
						if (Entries.Count > 1) // And we never remove the first one; that one will change to display "N/A" if there's no items in the list
						{
							YAIM.PrintToConsole("Removing excess entry...", YAIM.ConsoleMessageScope.System);
							GameObject entry = Entries[Entries.Count - 1];
							Entries.Remove(entry);
							Destroy(entry);
						}
					}
				}
			}
			for (byte i = 0; i < Entries.Count; i++)
			{
				Text entryAt = Entries[i].GetComponent<Text>();
				if (inventory.Items.Count == 0)
				{
					UpdateText(entryAt, "N/A", DefaultTextColor);
					break;
				}
				UpdateText(entryAt, DisplayName(inventory.Items[i]), i == CurIndex ? Color.white : DefaultTextColor);
			}
			YAIM.PrintToConsole("Inventory GUI refreshed", YAIM.ConsoleMessageScope.System);
		}

		/// <summary>
		/// Toggles the interface on and off, playing appropriate sounds in the process (if enabled). Automatically refreshes after.
		/// </summary>
		internal void Toggle()
		{
			YAIM.PrintToConsole($"Toggling inventory GUI to {!gameObject.activeSelf}", YAIM.ConsoleMessageScope.System);
			gameObject.SetActive(!gameObject.activeSelf);
			var listToUse = gameObject.activeSelf ? OpenSounds : CloseSounds;
			if (YAIM.SettingPlaySounds.GetValue())
			{
				AudioClip toPlay = listToUse[UnityEngine.Random.Range(0, listToUse.Count)];
				Audio.pitch = UnityEngine.Random.Range(90, 111) / 100f;
				Audio.PlayOneShot(toPlay);
			}
			Refresh();
		}

		/// <summary>
		/// Updates the provided <see cref="Text"/> component with new contents and color.
		/// </summary>
		/// <param name="Text">The <see cref="Text"/> to modify.</param>
		/// <param name="Contents">The new contents of the string.</param>
		/// <param name="Color">The new color of the string.</param>
		internal void UpdateText(Text Text, string Contents, Color Color)
		{
			// Vanilla MSC appears to handle a "drop shadow" effect by having a second text object beneath the first, just colored differently
			// We mirror this behavior in the prefab; but since each entry can change dynamically, we need to manually check for child text objects
			// and alter their own text as a result.
			//
			// The dark secret here is that, due to rendering order, the text object itself *is* actually the shadow;
			// the colored text that's meant to actually be read is the child object!
			Text.gameObject.name = Contents;
			Text.text = Contents;
			var textBody = Text.transform.FindChild("Body")?.GetComponent<Text>();
			if (textBody != null)
			{
				textBody.text = Contents;
				textBody.color = Color;
			}
		}

		/// <summary>
		/// Increments or decrements <see cref="CurIndex"/> based on the provided argument.
		/// </summary>
		/// <param name="Up">If true, <see cref="CurIndex"/> will be increased by 1; otherwise, it will be decreased by 1 instead.</param>
		internal void AdjustActiveIndex(bool Up)
		{
			CurIndex += Up ? 1 : -1;
			if (CurIndex < 0)
				CurIndex = InventoryHandler.Singleton.Items.Count - 1;
			else if (CurIndex >= InventoryHandler.Singleton.Items.Count)
				CurIndex = 0;
			Refresh();
		}

		/// <summary>
		/// Returns a readable display name for the provided object.
		/// This effectively strips out Unity tags (i.e. "(Clone)") and makes the resulting string all-caps.
		/// </summary>
		/// <param name="Object">The <see cref="GameObject"/> to get a display name for.</param>
		/// <returns>A readable name for the provided <see cref="GameObject"/>.</returns>
		internal static string DisplayName(GameObject Object) => Object.name.
			Replace("(Clone)", string.Empty).
			Replace("(itemx)", string.Empty).
			Replace("(xxxxx)", string.Empty).
			ToUpper();
		#endregion
	}
}
