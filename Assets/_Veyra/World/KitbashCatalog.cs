using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>
    /// Phase C (Roadmap Phase 1B step 3): one entry per imported CC-BY kitbash building (Art/Models/Kitbash/&lt;source&gt;/VC_KB_*.fbx),
    /// built by Veyra/Buildings/Build Kitbash Catalog from each pack's kitbash_manifest.json. BuildingAssembler picks entries
    /// by style tag, district and footprint. Every entry carries the licence register id of its source download.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Kitbash Catalog", fileName = "KitbashCatalog")]
    public sealed class KitbashCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Id;
            public GameObject Prefab;
            public string Source;
            [Tooltip("x (width) and z (length) of the footprint, metres, as imported")]
            public Vector2 Footprint;
            public float Height;
            public float FloorHeight = 3.5f;
            public string[] Styles = Array.Empty<string>();
            public string[] Districts = Array.Empty<string>();
            public string LicenceId;
            public int[] Triangles = Array.Empty<int>();
            public string Collider;
            [Tooltip("Source material names in slot order (kitbash_manifest.json); textures in Art/Textures/Imported/<Source>/<name>_baseColor.*")]
            public string[] SourceMaterials = Array.Empty<string>();
            [Tooltip("Excluded from assembly (reason in Note): too small, prop, not a building, monolithic block, oversize")]
            public bool Excluded;
            public string Note;
        }

        public List<Entry> Entries = new List<Entry>();

        public bool HasStyle(Entry e, string style) => Array.IndexOf(e.Styles, style) >= 0;
    }
}
