using HutongGames.PlayMaker;
using System;
using System.Collections.Generic;
using UnityEngine;
using Ceres.YAIM.UI;
using MSCLoader;

namespace Ceres.YAIM
{
	/// <summary>
	/// Controller script for the inventory's internal logic.
	/// This is where all the heavy lifting happens.
	/// </summary>
	internal class InventoryHandler : MonoBehaviour
	{
		/// <summary>
		/// The sole <see cref="InventoryHandler"/> instance.
		/// </summary>
		internal static InventoryHandler Singleton;

		#region Fields
		/// <summary>
		/// Objects whose names are in this list will be prevented from being added to storage, even if other conditions fit.
		/// This is exclusively for stability -- these objects can and will break things if picked up.
		/// </summary>
		private readonly string[] Blacklist = new string[]
		{
			"JONNEZ ES(Clone)",
			"doorl",
			"doorr",
			"doorear"
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
		internal static readonly Vector3 TempPosition = new Vector3(0.0f, 1000.0f, 0.0f);

		/// <summary>
		/// A list of every <see cref="GameObject"/> currently held in the inventory.
		/// </summary>
		internal List<GameObject> Items;

		/// <summary>
		/// Determines the foundational behavior of the inventory.
		/// In suffering mode, capacity is determined by object mass, and objects above a specific length can't be picked up.
		/// When not in suffering mode, capacity is simply determined by a maximum number of items. Anything not in the blacklist can be stored.
		/// </summary>
		internal bool SufferingMode;

		/// <summary>
		/// The maximum number of items this inventory can hold.
		/// In suffering mode, this value is ignored in favor of <see cref="Mass"/> and <see cref="MassCapacity"/>.
		/// </summary>
		internal byte MaxSlots;

		/// <summary>
		/// The sum total of mass of all stored objects in the inventory.
		/// This value is cached and is updated whenever something changes.
		/// </summary>
		internal float Mass;

		/// <summary>
		/// The maximum amount of mass this inventory can hold.
		/// </summary>
		internal float MassCapacity;

		/// <summary>
		/// Objects whose longest dimension is above this number can't fit in the inventory.
		/// </summary>
		internal float MaxLength;
		#endregion

		#region Functions
		public InventoryHandler()
		{
			YAIM.PrintToConsole("Attempting to create InventoryHandler...", YAIM.ConsoleMessageScope.System);
			Items = new List<GameObject>();
			SetupValues();
			Singleton = this;
			YAIM.PrintToConsole("InventoryHandler created successfully", YAIM.ConsoleMessageScope.System);
		}

		/// <summary>
		/// Sets up initial parameters for this inventory.
		/// This is called once during setup and can also be manually called again through a button in the mod settings.
		/// </summary>
		internal void SetupValues()
		{
			YAIM.PrintToConsole("InventoryHandler is setting up values...", YAIM.ConsoleMessageScope.System);
			MaxSlots = Math.Min((byte)15, byte.Parse(YAIM.SettingMaxSlots.GetValue()));
			SufferingMode = YAIM.SettingSufferingMode.GetValue();
			MassCapacity = float.Parse(YAIM.SettingWeightLimit.GetValue());
			MaxLength = float.Parse(YAIM.SettingLengthLimit.GetValue()) / 100f;
			YAIM.PrintToConsole($"InventoryHandler has set up values (suffering mode {SufferingMode})", YAIM.ConsoleMessageScope.System);
		}

		/// <summary>
		/// Determine whether or not a provided <see cref="GameObject"/> can be added to the inventory.
		/// To pick up an item, it needs to pass the following list of criteria:<br/>
		/// 1. It's not in the blacklist<br/>
		/// 2. It has a <see cref="Rigidbody"/> component<br/>
		/// 3. If it's a vehicle part, it's not installed or bolted<br/>
		/// 4. It's on layers 16 or 19 (which avoids things like doors and furniture)<br/>
		/// If all of these conditions are true, it then checks for capacity (or weight/length in suffering mode), and then finally returns true.
		/// </summary>
		/// <param name="Target">The <see cref="GameObject"/> that we're trying to pick up.</param>
		/// <param name="BypassLimit">If true, this pickup attempt will skip remaining capacity logic.</param>
		/// <returns>True if the provided <see cref="GameObject"/> is ready to be picked up; false if it isn't.</returns>
		internal bool CanPickUp(GameObject Target, bool BypassLimit = false)
		{
			YAIM.PrintToConsole($"Checking pickup eligibility for object named {Target.name}", YAIM.ConsoleMessageScope.PickupLogic);

			// Before checking criteria, ensure that this wouldn't be a duplicate (this can happen during save-load)
			if (Items.Contains(Target))
			{
				YAIM.PrintToConsole($"Failed to pick up {Target.name}; object already in inventory", YAIM.ConsoleMessageScope.PickupLogic);
				return false;
			}

			// 1. Is it blacklisted?
			if (!YAIM.SettingDisableBlacklist.GetValue() && Array.Exists(Blacklist, e => e == Target.name))
			{
				YAIM.PrintToConsole($"Failed to pick up {Target.name}; blacklisted", YAIM.ConsoleMessageScope.PickupLogic);
				YAIM.ThrowMessage($"IT WON'T FIT, SILLY! (BLACKLISTED)");
				return false;
			}

			// 2. Does it have a Rigidbody?
			Rigidbody rb = Target.GetComponent<Rigidbody>();
			if (rb == null)
			{
				YAIM.PrintToConsole($"Failed to pick up {Target.name}; no RigidBody", YAIM.ConsoleMessageScope.PickupLogic);
				return false;
			}

			// 3. Is it bolted to the car?
			foreach (PlayMakerFSM c in Target.GetComponents<PlayMakerFSM>())
			{
				// Part is installed if component is "Data" or "Use" and "Installed" is true
				if (c.FsmName == "Data" || c.FsmName == "Use")
				{
					FsmBool v = c.FsmVariables.FindFsmBool("Installed");
					if (v != null && v.Value)
					{
						YAIM.PrintToConsole($"Failed to pick up {Target.name}; part is currently installed", YAIM.ConsoleMessageScope.PickupLogic);
						return false;
					}
				}

				// Part is bolted if component is "BoltCheck" and "Tightness" is greater than 0
				if (c.FsmName == "BoltCheck")
				{
					FsmFloat v = c.FsmVariables.FindFsmFloat("Tightness");
					if (v != null && v.Value > 0f)
					{
						YAIM.PrintToConsole($"Failed to pick up {Target.name}; part is currently bolted", YAIM.ConsoleMessageScope.PickupLogic);
						return false;
					}
				}
			}

			// 4. Is it on the right layer?
			if (!(Target.layer == 16 || Target.layer == 19))
			{
				YAIM.PrintToConsole($"Failed to pick up {Target.name}; invalid layer ({Target.layer} - valid is 16 or 19)", YAIM.ConsoleMessageScope.PickupLogic);
				return false;
			}

			// Finally, move to check remaining capacity
			if (!BypassLimit)
			{
				if (!SufferingMode)
				{
					if (Items.Count >= MaxSlots)
					{
						YAIM.PrintToConsole($"Failed to pick up {Target.name}; list is full", YAIM.ConsoleMessageScope.PickupLogic);
						YAIM.ThrowMessage($"INVENTORY IS FULL ({Items.Count} / {MaxSlots})");
						return false;
					}
				}
				else
				{
					// Item is too heavy
					if (!BypassLimit && MassCapacity - Mass < rb.mass)
					{
						YAIM.PrintToConsole($"Failed to pick up {Target.name}; too heavy (weighs {rb.mass}, can only store {MassCapacity - Mass})", YAIM.ConsoleMessageScope.PickupLogic);
						YAIM.ThrowMessage($"TOO HEAVY (WEIGHS {Math.Round(rb.mass, 2)} KG, ONLY {MassCapacity - Mass} KG OF STORAGE)");
						return false;
					}

					// Check item length - objects that are too long won't fit
					float longestSide = 0f;
					if (LengthOverrides.ContainsKey(Target.name))
						longestSide = LengthOverrides[Target.name];
					else
					{
						MeshFilter meshFilter = Target.GetComponent<MeshFilter>();
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
							MeshFilter[] meshFilters = Target.GetComponentsInChildren<MeshFilter>(true);
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
						if (!BypassLimit && longestSide > MaxLength)
						{
							YAIM.PrintToConsole($"Failed to pick up {Target.name}; too long (longest side is {longestSide} m, can only fit 0.5 m)", YAIM.ConsoleMessageScope.PickupLogic);
							YAIM.ThrowMessage($"TOO LONG (LONGEST SIDE IS {Math.Round(longestSide * 100, 2)} CM, LIMIT IS {MaxLength * 100} CM)");
							return false;
						}
					}
				}
			}

			// We can be picked up!
			YAIM.PrintToConsole($"Item can be picked up - returning true", YAIM.ConsoleMessageScope.PickupLogic);
			return true;
		}

		/// <summary>
		/// Attempts to pick up the provided <see cref="GameObject"/> and add it to the inventory.
		/// Picked-up objects have their physics disabled and are stored underneath the map indefinitely.
		/// Also updates mass.
		/// </summary>
		/// <param name="Target">The <see cref="GameObject"/> to pick up.</param>
		/// <param name="BypassLimit">If true, this pickup attempt will skip remaining capacity logic.</param>
		/// <returns>True if the object was successfully picked up, and false if not.</returns>
		internal bool AttemptPickUp(GameObject Target, bool BypassLimit = false)
		{
			YAIM.PrintToConsole($"Attempting to pick up an object named {Target.name}...", YAIM.ConsoleMessageScope.PickupLogic);
			if (!CanPickUp(Target, BypassLimit))
			{
				YAIM.PrintToConsole($"Pickup attempt failed", YAIM.ConsoleMessageScope.PickupLogic);
				return false;
			}
			YAIM.PrintToConsole($"Validation successful. Picking up object...", YAIM.ConsoleMessageScope.PickupLogic);
			PlayMakerFSM.BroadcastEvent("PROCEED Drop");
			Items.Add(Target);
			Target.transform.parent = null;
			Rigidbody rb = Target.GetComponent<Rigidbody>();
			rb.isKinematic = true;
			Target.transform.position = TempPosition;
			UIHandler.Singleton.CurIndex = Math.Max(0, Math.Min(UIHandler.Singleton.CurIndex, Items.Count));
			UpdateMass();
			string suffix = $"now at {Items.Count} / {MaxSlots} items";
			if (SufferingMode)
				suffix = $"weighs {rb.mass}, now at {Mass} kg / {MassCapacity} kg";
			YAIM.PrintToConsole($"Successfully added {Target.name} to the inventory ({suffix})", YAIM.ConsoleMessageScope.PickupAndDrop);
			return true;
		}

		/// <summary>
		/// Drops the item at index <see cref="UIHandler.Singleton.CurIndex"/>.
		/// Physics will immediately resume and the object will be dropped 1m in front of the camera.
		/// Also updates mass.
		/// </summary>
		/// <param name="Position">An optional position that the item will be dropped at.</param>
		internal void DropCurrent(Vector3? Position = null)
		{
			if (Position == null)
				Position = Camera.main.transform.position + (Camera.main.transform.forward * 1.0f);
			GameObject go = Items[UIHandler.Singleton.CurIndex];
			YAIM.PrintToConsole($"Attempting to drop an object named {go.name}...", YAIM.ConsoleMessageScope.PickupLogic);
			Rigidbody rb = go.GetComponent<Rigidbody>();
			rb.isKinematic = false;
			go.transform.position = (Vector3)Position;
			Items.Remove(go);
			UIHandler.Singleton.CurIndex = Math.Max(0, Math.Min(UIHandler.Singleton.CurIndex - 1, Items.Count));
			UpdateMass();
			string suffix = $"now at {Items.Count} / {MaxSlots} items";
			if (SufferingMode)
				suffix = $"weighs {rb.mass}, now at {Mass} kg / {MassCapacity} kg";
			YAIM.PrintToConsole($"Successfully removed {go.name} from the inventory ({suffix})", YAIM.ConsoleMessageScope.PickupAndDrop);
		}

		/// <summary>
		/// Drops all stored items at the provided position. If no position is provided, <see cref="DropCurrent(Vector3?)"/> will use its default fallback.
		/// </summary>
		/// <param name="Position">An optional position that the item(s) will be dropped at.</param>
		internal void DropAll(Vector3? Position = null)
		{
			YAIM.PrintToConsole($"Attempting to drop all objects at position {(Position != null ? Position.ToString() : "NULL")}", YAIM.ConsoleMessageScope.PickupAndDrop);
			foreach (var item in Items.ToArray()) // This sucks but it's less of a headache than writing a for() right now. I'm sleepy.
				DropCurrent(Position);
			if (Items.Count > 0)
				ModConsole.LogError("[YAIM] The inventory attempted to save but not every item was dropped! Report this to the mod author!");
			else
				YAIM.PrintToConsole("Dropped all items.", YAIM.ConsoleMessageScope.PickupAndDrop);
			UIHandler.Singleton.Refresh();
		}

		/// <summary>
		/// Recalculates the inventory's mass by adding up the mass of every <see cref="Rigidbody"/> in its stored objects.
		/// Outside of suffering mode, this is skipped entirely.
		/// </summary>
		private void UpdateMass()
		{
			if (!SufferingMode)
				return;
			YAIM.PrintToConsole($"Updated mass: new value is {Mass}/{MassCapacity} kg", YAIM.ConsoleMessageScope.System);
			Mass = 0f;
			foreach (GameObject go in Items)
			{
				Rigidbody rb = go.GetComponent<Rigidbody>();
				Mass += rb.mass;
			}
		}
		#endregion
	}
}
