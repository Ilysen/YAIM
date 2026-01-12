using System.Diagnostics;
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
		/// <summary>
		/// Keeps track of how long this load catcher has existed for. This is only used for debug.
		/// </summary>
		public Stopwatch TimeElapsed { get; private set; }

		private void OnTriggerEnter(Collider other) => CacheObj(other.gameObject);

		private void OnTriggerStay(Collider other) => CacheObj(other.gameObject);

		private void OnTriggerExit(Collider other) => CacheObj(other.gameObject);

		private void OnDestroy()
		{
			TimeElapsed.Stop();
			YAIM.PrintToConsole($"Load catcher destroyed after {TimeElapsed.Elapsed:s\\.ff} second(s) active.", YAIM.ConsoleMessageScope.SaveLoad);
		}

		internal void CacheObj(GameObject Object)
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
		internal static GameObject Create(Vector3 Position, AssetBundle AssetBundle)
		{
			YAIM.PrintToConsole($"Creating load catcher at position: {Position}", YAIM.ConsoleMessageScope.SaveLoad);
			GameObject loadCatcher = AssetBundle.LoadAsset<GameObject>("YAIM LOAD CATCHER.prefab");
			loadCatcher = UnityEngine.GameObject.Instantiate(loadCatcher);
			loadCatcher.transform.localPosition = Position;
			bool render = YAIM.SettingVisibleLoadCatcher.GetValue();
			foreach (Renderer r in loadCatcher.GetComponentsInChildren<Renderer>())
				r.enabled = render;
			var lc = loadCatcher.AddComponent<LoadCatcher>();
			lc.TimeElapsed = new Stopwatch();
			lc.TimeElapsed.Start();
			YAIM.PrintToConsole($"Load catcher created at position: {loadCatcher.transform.localPosition}", YAIM.ConsoleMessageScope.SaveLoad);
			return loadCatcher;
		}
	}
}
