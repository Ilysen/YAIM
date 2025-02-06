using UnityEngine;

namespace Ceres.YAIM
{
	/// <summary>
	/// <para>Effectively, this aggressively grabs everything in its range (which should only ever be the temp position)
	/// and adds it to <see cref="YAIM.LoadedColliders"/> to be loaded in <see cref="YAIM.Mod_Load"/>.</para>
	/// 
	/// <para>This is the way we handle loading saved items and it's fairly awkward. More testing needed to see if it's consistent.</para>
	/// </summary>
	internal class LoadCatcher : MonoBehaviour
	{
		private void OnTriggerEnter(Collider other) => CacheObj(other.gameObject);

		private void OnTriggerStay(Collider other) => CacheObj(other.gameObject);

		private void OnTriggerExit(Collider other) => CacheObj(other.gameObject);

		private void OnDestroy() => YAIM.PrintToConsole("Load catcher destroyed", YAIM.ConsoleMessageScope.SaveLoad);

		private void CacheObj(GameObject Object)
		{
			if (!YAIM.LoadedColliders.Contains(Object))
			{
				YAIM.LoadedColliders.Add(Object);
				YAIM.PrintToConsole($"Detected an object named {Object.name} in the load catcher. Caching.", YAIM.ConsoleMessageScope.SaveLoad);
			}
		}

		/// <summary>
		/// Creates a <see cref="GameObject"/> at the provided position with a <see cref="LoadCatcher"/> component to grab everything within a 25m radius.
		/// </summary>
		/// <param name="Position">The position to create the new object.</param>
		/// <returns>The newly-created <see cref="GameObject"/>.</returns>
		internal static GameObject Create(Vector3 Position)
		{
			GameObject loadCatcher = new GameObject
			{
				name = "LOAD CATCHER"
			};
			loadCatcher.transform.localPosition = InventoryHandler.TempPosition;
			Collider c = loadCatcher.AddComponent<SphereCollider>();
			c.bounds.Expand(25f);
			c.isTrigger = true;
			loadCatcher.AddComponent<LoadCatcher>();
			return loadCatcher;
		}
	}
}
