positions = {
    "M1": (1000, 330),
    "M8": (850, 330),
    "M8mid": (970, 330),
    "H1": (500, 330),
    "1STR": (380, 330),
    "M2": (150, 330),
    "M6H2": (445, 230),
    "M10": (850, 430),
    "H3": (390, 430),
    "M6": (300, 230),
    "H2": (620, 230),
    "1": (760, 330),
    "2": (760, 230),
    "3": (760, 430),
    "4": (760, 130),
    "H4": (620, 130),
    "past4": (970, 130),
    "CH": (80, 330),
    "past2": (970, 230),
    "pastM1": (1090, 330),
    "beforeM6": (260, 230),
    "M2H1_mid": (260, 330),
    "M2H1_third": (340, 330),
    "ALB_Sect0": (80, 500),
    "ALB_Sect1": (325, 500),
    "ALB_Sect2": (175,500),
    "ALB_Sect1-2":(250,500),
    "ALB_Sect1-2_2":(250,500)
}
segments = [
    ("pastM1", "M1"),
    ("M8mid", "M8"),
    ("M8mid", "M1"),
    ("M8", "H1"),
    ("M2", "CH"),
    ("past2", "H2"),
    ("H2", "M6H2"),
    ("M6", "M6H2"),
    ("M2", "M2H1_mid"),
    ("M2H1_mid", "M2H1_third"),
    ("H1", "M2H1_third"),
    ("M10", "H3"),
    ("past4", "H4"),
    ("M6", "beforeM6"),
    ("ALB_Sect2", "ALB_Sect0"),
    ("ALB_Sect1", "ALB_Sect1-2"),
    ("ALB_Sect1-2", "ALB_Sect2"),
]

SEGMENT_ORDER = [
    ("M1", "pastM1"),  # бит 0
    ("M8mid", "M1"),   # бит 1
    ("M8", "H1"),      # бит 2
    ("M2", "M2H1_mid"), # бит 3
    ("M2", "CH"),      # бит 4
    ("past2", "H2"),   # бит 5
    ("H2", "M6H2"),    # бит 6
    ("past4", "H4"),
]
segment_groups = {
    "block_M2_H1": [
        ("M2","M2H1_mid"),
        ("M2H1_mid", "M2H1_third"),
        ("M2H1_third","H1")
    ],
    "block_M6_H2": [
        ("H2", "M6H2"),
        ("M6", "M6H2"),
    ],
    "block_M8_M1":[
        ("M8mid", "M8"),
        ("M8mid", "M1"),
    ],

}
split_parts_map = {
    "ALB_Turn4-6": {
        "partA": "ALB_Turn6",
        "partB": "ALB_Turn4"
    }
}
switch_list = ["ALB_Turn1", "ALB_Turn2", "ALB_Turn8", "ALB_Turn4-6"]

default_switch_mode = {
    "ALB_Turn1": "left",
    "ALB_Turn2": "left",
    "ALB_Turn8":  "left",
    "ALB_Turn4-6":  "left",
}
segment_to_signal = {
    ('M8', 'M8mid'): "M8",
    ('M1', 'M8mid'): "M8",
    ("M8", "H1"): "H1",
    ('M1', 'M8mid'): "M1",
    ('M8mid', 'M8'): "M1",
    #("pastM1", "M1"): "M1",
    #("M10", "H3"): "H3",
    ("M2", "M2H1_mid"): "M2",
    ("M2H1_mid", "M2H1_third"): "M2",
    ("M2H1_third", "H1"): "M2",
    ("H2", "M6H2"): "H2",
    ("M6", "M6H2"): "M6",
}

diag_to_signal = {
    "ALB_Turn1": "M10",
    "ALB_Turn8": "H4",
    "ALB_Turn2": "H3",

}
diagonal_config = {
    "ALB_Turn1": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both"
    },
    "ALB_Turn2": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both"
    },

    "ALB_Turn8": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": 0,  "disconnected": 0},
        "default": "both"
    },

    "ALB_Turn4-6": {
        "left":  {"exists": True, "connected": +5, "disconnected": 0},
        "right": {"exists": True, "connected": +5, "disconnected": 0},
        "default": "both"
    }
}
signals_config = {
    "CH": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 5,
        "colors": ["white", "yellow", "red", "green", "yellow1"],
    },
    "M2": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 2,
        "colors": ["blue", "white"],
    },

    "H1": {
        "mount": "top",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },
    "H2": {
        "mount": "top",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },
    "H3": {
        "mount": "top",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },
    "H4": {
        "mount": "top",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },

    "M6": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 2,
        "colors": ["red", "white"],
    },
    "M8": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 2,
        "colors": ["red", "white"],
    },
    "M10": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 2,
        "colors": ["red", "white"],
    },
    "M1": {
        "mount": "top",
        "pack_side": "left",
        "count": 2,
        "colors": ["white", "red"],
    },
    "ALB_Sect1-2": {
        "mount": "top",
        "pack_side": "left",
        "count": 3,
        "colors": ["yellow", "green", "red"],
    },
    "ALB_Sect1-2_2": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 3,
        "colors": ["yellow", "green", "red"],
    },
    "ALB_Sect2": {
        "mount": "top",
        "pack_side": "left",
        "count": 5,
        "colors": ["yellow", "green", "red", "black", "white"],
    }
}
ROUTE_SIGNAL_MAP: dict[tuple[str, str], dict[str, dict[str, object]]] = {
    ("M1", "M8"): {
        "M1": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M1", "H1"): {
        "M1": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M1", "M10"): {
        "M1": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M2", "H3"): { "M2": { "lamps": { "white": {"on": True, "blink": False}, } },

    },
    ("M2", "M10"): { "M2": {"lamps": { "white": {"on": True, "blink": False},} },
                    "H3": { "lamps": { "green": {"on": True, "blink": False}, } },
    },
    ("M2", "H1"):{
        "M2": {"lamps": { "white": {"on": True, "blink": False}, } },
    },
    ("M2", "M8"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }}, },
    ("M2", "M1"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }, },
        "M8": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M2", "H2"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M2", "H1"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }},
    },
    ("M2", "2"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }},
    },
    ("M2", "4"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }},
    },
    ("M2", "1"): {
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }},
    },
    ("CH", "1"): {
         "CH": { "lamps": { "yellow1": {"on": True, "blink": False},} },
        "H1": { "lamps": { "green": {"on": True, "blink": False},} },
    },
    ("CH", "2"): { "CH": { "lamps": { "yellow": {"on": True, "blink": False}, "yellow1": {"on": True, "blink": False},} },
                    "H2": {"lamps": { "green": {"on": True, "blink": False}, } },
    },
    ("M6", "H2"): {
        "M6": {"lamps": {"white": {"on": True, "blink": False},} },},
    ("M6", "H4"): {
        "M6": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("M8", "M1"): {
        "M8": { "lamps": { "white": {"on": True, "blink": False}, "red": {"on": False, "blink": False},} },
    },

    ("M10", "M1"): {
        "M10": {"lamps": {"white": {"on": True, "blink": False}, "red": {"on": False, "blink": False}, }}, },
    ("M1", "H3"): {
        "M1": {"lamps": {"white": {"on": True, "blink": False}, "red": {"on": False, "blink": False}, }}, },

    ("H1", "M2"): {
        "H1": {"lamps": {"green": {"on": True, "blink": False}, }, },
        "M2": {"lamps": {"white": {"on": True, "blink": False}, }, },
    },
    ("H2", "M2"):{
        "H2": {"lamps": { "green": {"on": True, "blink": False}, "yellow": {"on": True, "blink": False}, }, },
        "M2": {"lamps": {"white": {"on": True, "blink": False},},},
    },
    ("H2", "M6"):{
        "H2": {"lamps": { "green": {"on": True, "blink": False} }, },
    },
    ("H4", "M6"):{
        "H4": {"lamps": { "green": {"on": True, "blink": False}, "yellow": {"on": True, "blink": False}, }, },
        "M6": {"lamps": {"white": {"on": True, "blink": False},},},
    },
    ("H3", "M2"): {
        "H3": {"lamps": {"green": {"on": True, "blink": False}, "yellow": {"on": True, "blink": False}, }, },
    },
}
routes = {
    # МАНЕВРОВЫЕ
    ("M2", "H3"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "ALB_Turn2"},
    ],
    ("M2", "H1"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2H1_third", "H1")},
    ],
    ("M2", "M8"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2H1_third", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
    ("M2", "M1"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2H1_third", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M2", "M10"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn2"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("M2", "H2"): [
        {"type": "segment", "id": ("M2","M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("H2", "M6H2")},
    ],
    ("M2", "H4"): [
        {"type": "segment", "id": ("M2","M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "diag", "name": "ALB_Turn8"},

    ],
    ("M2", "1"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("H1", "M2H1_third")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
    ("M2", "2"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("H2", "past2")},
    ],
    ("M2", "4"): [
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("past4", "H4")},
    ],
    ("H2", "M6"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "segment", "id": ("M6", "beforeM6")},
    ],
    ("H2", "M2"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2","M2H1_mid")}
    ],
    ("H4", "M6"): [
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "segment", "id": ("M6", "beforeM6")},
    ],
    ("H4", "M2"): [
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
    ],
    ("M6", "H4"):[
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "diag", "name": "ALB_Turn8"},
    ],
    ("M6", "H2"):[
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "segment", "id": ("M6H2", "H2")},
    ],
    ("H3", "M10"): [
        {"type": "segment", "id": ("H3", "M10")},
        {"type": "diag", "name": "ALB_Turn1"},

    ],
    ("H1", "M2"): [
        {"type": "segment", "id": ("M2H1_third", "H1")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
    ],
    ("H3", "M1"): [
        {"type": "segment", "id": ("H3", "M10")},
        {"type": "diag", "name": "ALB_Turn1"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M10", "M1"): [
        {"type": "diag", "name": "ALB_Turn1"},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M1", "M8"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M8mid", "M8")},
    ],
    ("M1", "H3"): [
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "diag", "name": "ALB_Turn1"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("M8", "M1"): [
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M1", "H1"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8", "H1")},
    ],
    ("M1", "M10"): [
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "diag", "name": "ALB_Turn1"},
    ],
    ("H3", "M2"): [
        {"type": "diag", "name": "ALB_Turn2"},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
    ]
}

train_routes = {
    ("CH", "4"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("past4", "H4")},
    ],
    ("CH", "3"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn2"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("CH", "2"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4"},
        {"type": "diag", "name": "ALB_Turn6"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("H2", "past2")},
    ],
    ("CH", "1"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("H1", "M2H1_third")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
}
route_switch_modes = {
    ("H2", "M6"): {"ALB_Turn8":  "left","ALB_Turn4-6":  "left"},
    ("H4", "M6"): {"ALB_Turn8":  "right","ALB_Turn4-6":  "left"},
    ("M2", "H3"): {"ALB_Turn2": "right"},
    ("M2", "M10"): {"ALB_Turn2": "right"},
    ("H3", "M1"): {"ALB_Turn1": "right"},
    ("H3","M10"):{},
    ("M10", "M1"): {"ALB_Turn1": "right"},
    ("M2", "H1"): {"ALB_Turn2": "left","ALB_Turn4-6":  "left"},
    ("M2", "M8"): {"ALB_Turn2": "left", "ALB_Turn4-6":  "left"},
    ("M2", "M1"): {"ALB_Turn1": "left","ALB_Turn2": "left","ALB_Turn4-6":  "left"},
    ("M2", "1"):  {"ALB_Turn4-6": "left", "ALB_Turn2": "left"},
    ("M2", "2"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "left"},
    ("M2", "4"): {"ALB_Turn2": "left","ALB_Turn4-6": "right", "ALB_Turn8": "right"},
    ("M1", "M8"): {"ALB_Turn1": "left"},
    ("M1", "H1"): {"ALB_Turn1": "left"},
    ("M2", "H2"): {"ALB_Turn4-6": "right", "ALB_Turn8":  "left", "ALB_Turn2": "left"},
    ("H1", "M8"): {},
    ("CH", "4"): {"ALB_Turn2": "left","ALB_Turn4-6": "right", "ALB_Turn8": "right"},
    ("CH", "3"): {"ALB_Turn2": "right"},
    ("CH", "2"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "left"},
    ("CH", "1"): {"ALB_Turn4-6": "left", "ALB_Turn2": "left"},
    ("M2", "H4"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "right"},
    ("H2", "M2"): {"ALB_Turn8": "left", "ALB_Turn4-6": "right", "ALB_Turn2": "left"},
}

#Arduino Configs
ROUTE_SIGNAL_ASPECTS = {
    None: {
        "CH": "red",
        "H1": "red",
        "H2": "red",
        "H3": "red",
        "H4": "red",
    },
    "1": {   # CH → 1 (главный путь)
        "CH": "one_yellow",
        "H1": "red",
        "H2": "red",
        "H3": "red",
        "H4": "red",
    },
    "2": {   # CH → 2 (боковой)
        "CH": "two_yellow",
        "H1": "red",
        "H2": "red",
        "H3": "red",
        "H4": "red",
    },
    "3": {   # CH → 3
        "CH": "two_yellow",
        "H1": "red",
        "H2": "red",
        "H3": "red",
        "H4": "red",
    },
    "4": {   # CH → 4
        "CH": "two_yellow",
        "H1": "red",
        "H2": "red",
        "H3": "red",
        "H4": "red",
    },
}
# УЧАСТКИ, КОТОРЫЕ «ЗАЩИЩАЕТ» ВХОДНОЙ СВЕТОФОР CH
ROUTE_PROTECT_SEGMENTS_FOR_CH = {
    None: [],
    "1": [("M2", "CH")],
    "2": [("M2", "CH")],
    "3": [("M2", "CH")],
    "4": [("M2", "CH")],
}