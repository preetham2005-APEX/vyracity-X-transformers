using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// The routes and data the player-side test runner needs. Lives at Resources/Testing/VeyraAutoTestConfig.asset so a
    /// build includes it (and, through its references, road_network_geometry.json and the route assets).
    /// Created and updated by the editor menu Veyra/Test/Create Test Routes.
    /// </summary>
    public sealed class VeyraAutoTestConfig : ScriptableObject
    {
        public const string ResourcePath = "Testing/VeyraAutoTestConfig";

        [Tooltip("Assets/_Veyra/Data/road_network_geometry.json")] public TextAsset RoadNetworkJson;
        public List<VeyraTestRoute> Routes = new List<VeyraTestRoute>();

        public VeyraTestRoute Find(string routeName)
        {
            foreach (var r in Routes) if (r != null && string.Equals(r.name, routeName, System.StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }
    }
}
