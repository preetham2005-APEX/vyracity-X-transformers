"""The kit's catalogue sources, shared by make_manifest.py and make_handoff.py so the two documents
cannot drift apart.

`tall` / `low` are the style lists KitbashCatalogBuilder assigns with `Height >= 30f ? tall : low`
(Assets/_Veyra/Editor/KitbashCatalogBuilder.cs). Where a source holds models both above and below 30 m,
both lists carry the same styles so the split cannot matter.
"""
import json
import os

B = os.path.dirname(os.path.abspath(__file__))

SETS = [
    {"report": "report_houses.json", "fit": "houses_fit.json", "source": "Veyra_Original_Suburban",
     "title": "D5 suburban houses", "districts": ["D5"], "tall": ["Suburban"], "low": ["Suburban"],
     "glob": "VC_House_*.fbx", "tex": ("VC_Wall_", "VC_Roof_", "VC_Trim_", "VC_Glass_Window",
                                       "VC_Door_", "VC_Metal_Garage"),
     "records": "1,923 D5 `Suburban`: every one exactly 14 x 11 m, 6.2-8.2 m tall, plus one 8 x 7 "
                "`garage_small` and one 40 x 22 `garage_community`"},
    {"report": "report_towers.json", "fit": "tower_fit.json", "source": "Veyra_Original_Downtown",
     "title": "D1 Crown Downtown towers", "districts": ["D1"],
     "tall": ["GlassModern", "ArtDeco"], "low": ["GlassModern", "ArtDeco"],
     "glob": "VC_Tower_*.fbx", "tex": ("VC_Twr_",),
     "records": "232 D1 `GlassModern/ArtDeco`: 181 near-square (short side 52-57 m), 51 slabs "
                "(long side ~80 or ~115 m), a few 117 x 117 superblocks and ~30 m thin towers; "
                "heights 23-260 m; `box` 207, `stepped` 25"},
    {"report": "report_neon.json", "fit": "neon_fit.json", "source": "Veyra_Original_NeonMile",
     "title": "D6 Neon Mile low-rise", "districts": ["D6"],
     "tall": ["BrickIndustrial"], "low": ["ModernLowrise"],
     "glob": "VC_Neon_*.fbx", "tex": ("VC_Nm_", "VC_Neon_Emissive"),
     "records": "79 `BrickIndustrial` (short ~56 m, long 60 m or 105-125 m, height 24-62) and "
                "37 `ModernLowrise` (short ~57, long ~60 or ~120, height 13-22)"},
    {"report": "report_coast.json", "fit": "coast_fit.json", "source": "Veyra_Original_Coast",
     "title": "D2 Azure Coast stucco", "districts": ["D2"],
     "tall": ["CoastalStucco"], "low": ["CoastalStucco"],
     "glob": "VC_Coast_*.fbx", "tex": ("VC_Cs_",),
     "records": "81 `CoastalStucco`: 65 `villa` at exactly 20 x 15 m (5-12 m tall), 12 `beach_club` "
                "at exactly 24 x 12 m, 4 one-offs (42 x 26, 30 x 18, 36 x 20, 22 x 14)"},
    {"report": "report_town.json", "fit": "town_fit.json", "source": "Veyra_Original_MarketTown",
     "title": "D8 Redwood Valley market town", "districts": ["D8"],
     "tall": ["MarketTownBrick"], "low": ["MarketTownBrick"],
     "glob": "VC_Town_*.fbx", "tex": ("VC_Mt_",),
     "records": "65 `MarketTownBrick`: every one exactly 12 x 12 m, `box`, kind `town`, 6.4-9.8 m tall"},
    {"report": "report_midrise.json", "fit": "midrise_fit.json", "source": "Veyra_Original_Midrise",
     "title": "ResidentialMidrise (D1 + D5)", "districts": ["D1", "D5"],
     "tall": ["ResidentialMidrise"], "low": ["ResidentialMidrise"],
     "glob": "VC_Mid_*.fbx", "tex": ("VC_Mr_",),
     "records": "87 `apartment`, two footprints only: 34 x 18 m (47, all D5, 12-18 m) and "
                "38 x 24 m (40, all D1, 28-75 m)"},
    {"report": "report_ironport.json", "fit": "ironport_fit.json", "source": "Veyra_Original_Ironport",
     "title": "D3 Ironport sheds", "districts": ["D3", "D7"],
     "tall": ["Industrial"], "low": ["Industrial"],
     "glob": "VC_Ind_Shed*.fbx", "tex": ("VC_In_",),
     "records": "41 `Industrial` (39 D3, 2 D7): `warehouse` 35, `rail_shed` 5, `warehouse_hq` 1; "
                "footprints 90 x 50 (18), 64 x 40 (12), 120 x 24 (5), 72 x 42 (4), 80 x 45 (2); "
                "heights 8.7-15.5 m"},
    {"report": "report_ironporttanks.json", "fit": "ironporttanks_fit.json",
     "source": "Veyra_Original_IronportTanks", "title": "D3 Ironport tanks and process columns",
     "districts": ["D3"], "tall": ["IndustrialRefinery"], "low": ["IndustrialTank"],
     "glob": "VC_Ind_Tank*.fbx + VC_Ind_Process*.fbx", "tex": ("VC_In_",),
     "records": "80 `IndustrialTank` (`cylinder`, diameter 28.5-42.8 m, height 12.5-19) and "
                "3 `IndustrialRefinery` `process_column` (8 x 8 m, 47.6-66 m tall)"},
    {"report": "report_alpine.json", "fit": "alpine_fit.json", "source": "Veyra_Original_Alpine",
     "title": "D4 Blackridge alpine", "districts": ["D4"], "tall": ["Alpine"], "low": ["Alpine"],
     "glob": "VC_Alp_*.fbx", "tex": ("VC_Al_",),
     "records": "25 `Alpine`: 24 `chalet` at exactly 12 x 10 m (7.1-9.0 m) and one 42 x 24 `workshop`"},
    {"report": "report_rural.json", "fit": "rural_fit.json", "source": "Veyra_Original_Rural",
     "title": "D8 Redwood Valley rural", "districts": ["D8"], "tall": ["Rural"], "low": ["Rural"],
     "glob": "VC_Rur_*.fbx", "tex": ("VC_Ru_",),
     "records": "25 `Rural`: `barn` 9 at 26 x 14 (h 9), `silo` 8 at 7 x 7 (h 16, `cylinder`), "
                "`farmhouse` 7 at 12 x 10 (h 7), one 64 x 30 barn, one 32 x 18 `mill`"},
    {"report": "report_aviation.json", "fit": "aviation_fit.json", "source": "Veyra_Original_Aviation",
     "title": "D7 Falcon Airfield", "districts": ["D7"], "tall": ["Aviation"], "low": ["Aviation"],
     "glob": "VC_Avi_*.fbx", "tex": ("VC_Av_",),
     "records": "8 `Aviation`: 6 `hangar` at 62 x 74 m (h 17), one 130 x 36 `terminal` (h 12), "
                "one 18 x 150 `grandstand` (h 14)"},
    {"report": "report_retail.json", "fit": "retail_fit.json", "source": "Veyra_Original_Retail",
     "title": "D5 suburban retail", "districts": ["D5"],
     "tall": ["SuburbanRetail"], "low": ["SuburbanRetail"],
     "glob": "VC_Ret_Retail*.fbx", "tex": ("VC_Rt_",),
     "records": "3 `SuburbanRetail`: 40 x 30 (h 7), 120 x 60 (h 10), 24 x 14 (h 5)"},
    {"report": "report_roadside.json", "fit": "roadside_fit.json", "source": "Veyra_Original_Roadside",
     "title": "D4 roadside", "districts": ["D4"], "tall": ["Roadside"], "low": ["Roadside"],
     "glob": "VC_Ret_GasStation.fbx", "tex": ("VC_Rt_",),
     "records": "1 `Roadside`: a 26 x 16 m `gas_station` at 6 m"},
]


def load():
    """Attach each set's model rows and fit result."""
    for s in SETS:
        p = os.path.join(B, s["report"])
        s["rows"] = sorted(json.load(open(p)), key=lambda r: r["name"]) if os.path.exists(p) else []
        fp = os.path.join(B, s["fit"]) if s.get("fit") else None
        s["fitdata"] = json.load(open(fp)) if fp and os.path.exists(fp) else None
    return SETS
