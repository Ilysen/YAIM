using HutongGames.PlayMaker.Actions;
using MSCLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YAIM.UI
{
	internal class UIHandler : MonoBehaviour
	{
		internal static UIHandler Singleton;

		private Text Header;
		private Transform EntriesGroup;
		private List<GameObject> Entries;

		private readonly Color _defaultColor = new Color(255, 255, 0);

		internal AudioSource Audio;

		internal int CurIndex = 0;

		internal List<AudioClip> OpenSounds;
		internal List<AudioClip> CloseSounds;

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

		internal void Refresh()
		{
			if (!gameObject.activeSelf)
				return;
			Inventory inventory = Inventory.Singleton;
			if (!inventory.SufferingMode)
				UpdateText(Header, $"INVENTORY ({inventory.Items.Count}/{inventory.MaxSlots})", _defaultColor);
			else
				UpdateText(Header, $"INVENTORY ({inventory.Mass} KG / {inventory.MassCapacity} KG)", _defaultColor);
			int difference = inventory.Items.Count - Entries.Count;
			if (difference != 0)
			{
				YAIM.PrintToConsole($"Difference in entries equals {difference}. Equalizing.", YAIM.ConsoleMessageScope.System);
				for (byte i = 0; i < Math.Abs(difference); i++)
				{
					if (difference > 0)
					{
						YAIM.PrintToConsole("Creating new entry...", YAIM.ConsoleMessageScope.System);
						GameObject newEntry = GameObject.Instantiate(Entries[0]);
						newEntry.transform.parent = EntriesGroup;
						Entries.Add(newEntry);
					}
					else
					{
						if (Entries.Count > 1) // Never remove the last entry
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
					UpdateText(entryAt, "N/A", _defaultColor);
					break;
				}
				UpdateText(entryAt, DisplayName(inventory.Items[i]), i == CurIndex ? Color.white : _defaultColor);
			}
		}

		internal void AdjustActiveIndex(bool up)
		{
			CurIndex += up ? 1 : -1;
			if (CurIndex < 0)
				CurIndex = Inventory.Singleton.Items.Count - 1;
			else if (CurIndex >= Inventory.Singleton.Items.Count)
				CurIndex = 0;
			Refresh();
		}

		internal void UpdateText(Text text, string contents, Color color)
		{
			text.text = contents;
			var textBody = text.transform.FindChild("Body")?.GetComponent<Text>();
			if (textBody != null)
			{
				textBody.text = contents;
				textBody.color = color;
			}
		}

		internal static string DisplayName(GameObject go) => go.name.Replace("(Clone)", string.Empty).Replace("(itemx)", string.Empty).Replace("(xxxxx)", string.Empty).ToUpper();
	}
}
