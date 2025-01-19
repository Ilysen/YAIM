using HutongGames.PlayMaker;
using System;
using System.Collections.Generic;
using UnityEngine;
using YAIM.UI;

namespace YAIM
{
	internal class Inventory : MonoBehaviour
	{
		internal static Inventory Singleton;

		/// <summary>
		/// Objects whose names are in this list will be prevented from being added to storage, even if other conditions fit.
		/// This is exclusively for stability - these objects can and will break things if picked up.
		/// </summary>
		private readonly string[] Blacklist = new string[]
		{
			"JONNEZ ES(Clone)",
			"doorl",
			"doorr",
			"doorear",
			"house_door1"
		};

		/// <summary>
		/// If an object's name is in this dictionary, it will use its associated value as its longest side, regardless of mesh data.
		/// This is used for objects like electrics, lining, etc. that could easily be folded, wound up, etc. to significantly reduced their size.
		/// These values are very much just ballparks, not actual measurements :p
		/// </summary>
		private readonly Dictionary<string, float> LengthOverrides = new Dictionary<string, float>()
		{
			{ "electrics(Clone)", 6f },
			{ "fuel strainer(Clone)", 6f },
			{ "brake lining(Clone)", 4f },
			{ "clutch lining(Clone)", 4f },
			{ "radiator hose1(Clone)", 6f },
			{ "radiator hose2(Clone)", 6f },
			{ "radiator hose3(Clone)", 8f },
			{ "parts magazine(itemx)", 18f } // not something you can usually pick up, of course - this is for compat. with pickable parts catalog
		};

		/// <summary>
		/// Objects are placed at this set of coordinates when they're "in" storage.
		/// </summary>
		internal static readonly Vector3 TempPosition = new Vector3(0.0f, -1000.0f, 0.0f);

		internal byte MaxSlots;
		internal List<GameObject> Items;

		internal bool SufferingMode;

		internal float Mass, MassCapacity;
		internal float LengthCapacity;

		public Inventory()
		{
			YAIM.PrintToConsole("Attempting to create inventory script...", YAIM.ConsoleMessageScope.System);
			Items = new List<GameObject>();
			MaxSlots = Math.Min((byte)15, byte.Parse(YAIM.ItemLimit.GetValue()));
			SetupValues();
			Singleton = this;
			YAIM.PrintToConsole("Created inventory script.", YAIM.ConsoleMessageScope.System);
		}

		private void SetupValues()
		{
			SufferingMode = YAIM.EnableSufferingMode.GetValue();
			MassCapacity = float.Parse(YAIM.WeightLimit.GetValue());
			LengthCapacity = float.Parse(YAIM.LengthLimit.GetValue()) / 100f;
		}

		internal bool CanPickUp(GameObject o, bool bypassLimit = false)
		{
			if (!YAIM.DisableBlacklist.GetValue() && Array.Exists(Blacklist, e => e == o.name))
			{
				YAIM.PrintToConsole($"Failed to pick up {o.name}; blacklisted", YAIM.ConsoleMessageScope.PickupAndDrop);
				YAIM.ThrowMessage($"IT WON'T FIT, SILLY! (BLACKLISTED)");
				return false;
			}

			Rigidbody rb = o.GetComponent<Rigidbody>();
			// Item doesn't have a rigid body
			if (rb == null)
			{
				YAIM.PrintToConsole($"Failed to pick up {o.name}; no RigidBody", YAIM.ConsoleMessageScope.PickupAndDrop);
				return false;
			}

			// Item is installed on or bolted to the car
			// Loop through all PlayMakerFSM components
			foreach (PlayMakerFSM c in o.GetComponents<PlayMakerFSM>())
			{
				// Part is installed if component is "Data" or "Use" and "Installed" is true
				if (c.FsmName == "Data" || c.FsmName == "Use")
				{
					FsmBool v = c.FsmVariables.FindFsmBool("Installed");
					if (v != null && v.Value)
					{
						YAIM.PrintToConsole($"Failed to pick up {o.name}; part is currently installed", YAIM.ConsoleMessageScope.RejectedPickups);
						return false;
					}
				}

				// Part is bolted if component is "BoltCheck" and "Tightness" is greater than 0
				if (c.FsmName == "BoltCheck")
				{
					FsmFloat v = c.FsmVariables.FindFsmFloat("Tightness");
					if (v != null && v.Value > 0.0f)
					{
						YAIM.PrintToConsole($"Failed to pick up {o.name}; part is currently bolted", YAIM.ConsoleMessageScope.RejectedPickups);
						return false;
					}
				}
			}

			// Object is not a part or item
			if (!(o.layer == 16 || o.layer == 19))
			{
				YAIM.PrintToConsole($"Failed to pick up {o.name}; invalid layer ({o.layer} - valid is 16 or 19)", YAIM.ConsoleMessageScope.PickupAndDrop);
				return false;
			}

			if (!bypassLimit)
			{
				if (!SufferingMode)
				{
					if (Items.Count >= MaxSlots)
					{
						YAIM.PrintToConsole($"Failed to pick up {o.name}; list is full", YAIM.ConsoleMessageScope.RejectedPickups);
						YAIM.ThrowMessage($"INVENTORY IS FULL ({Items.Count} / {MaxSlots})");
						return false;
					}
				}
				else
				{
					// Item is too heavy
					if (!bypassLimit && MassCapacity - Mass < rb.mass)
					{
						YAIM.PrintToConsole($"Failed to pick up {o.name}; too heavy (weighs {rb.mass}, can only store {MassCapacity - Mass})", YAIM.ConsoleMessageScope.PickupAndDrop);
						YAIM.ThrowMessage($"TOO HEAVY (WEIGHS {Math.Round(rb.mass, 2)} KG, ONLY {MassCapacity - Mass} KG OF STORAGE)");
						return false;
					}

					// Check item length - objects that are too long won't fit
					float longestSide = 0f;
					if (LengthOverrides.ContainsKey(o.name))
						longestSide = LengthOverrides[o.name];
					else
					{
						MeshFilter meshFilter = o.GetComponent<MeshFilter>();
						if (meshFilter != null)
						{
							Mesh mesh = meshFilter.mesh;
							if (mesh != null)
							{
								for (int i = 0; i < 3; i++)
									if (meshFilter.mesh.bounds.size[i] > longestSide)
										longestSide = meshFilter.mesh.bounds.size[i];
							}
						}
						else
						{
							MeshFilter[] meshFilters = o.GetComponentsInChildren<MeshFilter>(true);
							if (meshFilters.Length >= 1)
							{
								foreach (MeshFilter filter in meshFilters)
								{
									for (int i = 0; i < 3; i++)
										if (filter.mesh.bounds.size[i] > longestSide)
											longestSide = filter.mesh.bounds.size[i];
								}
							}
						}
						if (!bypassLimit && longestSide > LengthCapacity)
						{
							YAIM.PrintToConsole($"Failed to pick up {o.name}; too long (longest side is {longestSide} m, can only fit 0.5 m)", YAIM.ConsoleMessageScope.PickupAndDrop);
							YAIM.ThrowMessage($"TOO LONG (LONGEST SIDE IS {Math.Round(longestSide * 100, 2)} CM, LIMIT IS {LengthCapacity * 100} CM)");
							return false;
						}
					}
				}
			}

			// Otherwise, the item can be picked up
			YAIM.PrintToConsole($"Item can be picked up - returning true", YAIM.ConsoleMessageScope.PickupLogic);
			return true;
		}

		internal bool PickUp(GameObject o, bool bypassLimit = false)
		{
			if (!CanPickUp(o))
				return false;
			PlayMakerFSM.BroadcastEvent("PROCEED Drop");
			Items.Add(o);
			o.transform.parent = null;
			Rigidbody rb = o.GetComponent<Rigidbody>();
			rb.isKinematic = true;
			o.transform.position = TempPosition;
			UIHandler.Singleton.CurIndex = Math.Max(0, Math.Min(UIHandler.Singleton.CurIndex, Items.Count));
			UpdateMass();
			string suffix = $"now at {Items.Count} / {MaxSlots} items";
			if (SufferingMode)
				suffix = $"weighs {rb.mass}, now at {Mass} kg / {MassCapacity} kg";
			YAIM.PrintToConsole($"Added {o.name} to storage ({suffix})", YAIM.ConsoleMessageScope.PickupAndDrop);
			return true;
		}

		internal bool DropCurrent()
		{
			GameObject go = Items[UIHandler.Singleton.CurIndex];
			Rigidbody rb = go.GetComponent<Rigidbody>();
			rb.isKinematic = false;
			go.transform.position = Camera.main.transform.position + (Camera.main.transform.forward * 1.0f);
			Items.Remove(go);
			UIHandler.Singleton.CurIndex = Math.Max(0, Math.Min(UIHandler.Singleton.CurIndex, Items.Count));
			UpdateMass();
			return true;
		}

		private void UpdateMass()
		{
			if (!SufferingMode)
				return;
			Mass = 0f;
			foreach (GameObject go in Items)
			{
				Rigidbody rb = go.GetComponent<Rigidbody>();
				Mass += rb.mass;
			}
		}
	}
}
