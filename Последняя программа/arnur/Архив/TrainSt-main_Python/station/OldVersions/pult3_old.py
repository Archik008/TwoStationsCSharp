import tkinter
import tkinter as tk
import time
from tkinter.messagebox import showinfo
import serial
import serial.tools.list_ports
from serial import SerialException
from serial.tools import list_ports
from tkinter import ttk

root = tk.Tk()
root.title("Станция")
canvas = tk.Canvas(root, width=1150, height=600, bg="white")
canvas.pack()

CANVAS_W = 1150
CANVAS_H = 600

def showInfo(title, msg):
    showinfo(title=title, message=msg)
#########################################        ФУНКЦИЯ ТУПИКОВ                ##############################################
def drawDeadEnd(name, direction, offset):
    x = positions[name][0]
    y = positions[name][1]
    if direction == "right":
        canvas.create_line(x, y, x + offset, y, width=4, fill="black")
        canvas.create_line(x + offset, y - 15, x + offset, y + 15, width=6)
    elif direction == "left":
        canvas.create_line(x, y, x - offset, y, width=4, fill="black")
        canvas.create_line(x - offset, y - 15, x - offset, y + 15, width=6, fill="black")

#########################################        ПОЗИЦИОНИРОВАНИЕ                ##############################################
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

#########################################        МАССИВЫ ЭЛЕМЕНТОВ               ##############################################
selected_nodes = []
MAX_SELECTED = 2
node_ids = {}
switch_ids = {}
segment_ids = {}
diag_ids = {}
signal_ids = {}
segment_to_block = {}

segment_groups = {
    "block_M2_H1": [
        ("M2","M2H1_mid"),
        ("M2H1_mid", "M2H1_third"),
        ("M2H1_third","H1")
    ],
    "block_M6,H2": [
        ("H2", "M6H2"),
        ("M6", "M6H2"),
    ],
    "block_M8_M1":[
        ("M8mid", "M8"),
        ("M8mid", "M1"),
    ],

}

split_diag_ids = {}

split_parts_map = {
    "ALB_Turn4-6": {
        "partA": "ALB_Turn6",
        "partB": "ALB_Turn4"
    }
}


active_routes = {}  # route_id -> {"start": a, "end": b, "segments": [...]}
route_counter = 1   # уникальные номера маршрутов

occupied_segments = set()
occupied_diagonals = set()
diagonal_modes = {}

switch_text_ids = {} # name = id текста
switch_indicator_ids = {}          # name = id прямоугольника
switch_list = ["ALB_Turn1", "ALB_Turn2", "ALB_Turn8", "ALB_Turn4-6"]

blinking_diags = set()
blinking_routes = set()
# нормальное (плюсовое) положение стрелок
default_switch_mode = {
    "ALB_Turn1": "left",
    "ALB_Turn2": "left",
    "ALB_Turn8":  "left",
    "ALB_Turn4-6":  "left",
}


last_switch_check = {}

current_mode = "maneuver"
btn_maneuver = None
btn_train = None

arduino = None
arduino_status_label = None

ser = None
last_bits = None

route_to_arduino_cmd = {
    ("CH", "M1"): 'A',
    ("M1", "CH"): 'A',
}

seg_occ_train = {
    ("M1", "pastM1"): 1,
    ("M8mid", "M8"): 1,
    ("M8mid", "M1"): 1,
    ("M8", "H1"): 1,
    ("M2", "CH"): 1,
    ("past2", "H2"): 1,
    ("M2", "M2H1_mid"): 1,
    ("M2H1_mid", "H1"): 1,
    ("H2", "M6H2"): 1,
    ("M6H2", "M6"): 1,
    ("M10", "H3"): 1,
    ("past4", "H4"): 1,
    ("M6", "beforeM6"): 1,
}
diag_occ_train = {
    "ALB_Turn1": 1,
    "ALB_Turn2": 1,
    "ALB_Turn8": 1,
    "ALB_Turn4": 1,
    "ALB_Turn6": 1,
}


for block, segs in segment_groups.items():
    for seg in segs:
        segment_to_block[seg] = block
        segment_to_block[(seg[1], seg[0])] = block

#########################################        КОНФИГ ДИАГОНАЛЕЙ               ##############################################
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

#########################################        КОНФИГ СВЕТОФОРОВ               ##############################################
signals_config = {
    "CH": {
        "mount": "bottom",
        "pack_side": "right",
        "count": 5,
        "colors": ["white", "yellow", "red", "green", "yellow"],
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
}

#########################################        МАРШРУТЫ                ##############################################
# ВАЖНО: id сегмента – кортеж, как в segment_ids
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
    ("H2", "M6"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6H2", "M6")},
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
        {"type": "segment", "id": ("past4", "H4")},
    ],
    ("M6", "H2"):[
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "segment", "id": ("M6H2", "H2")},
        {"type": "segment", "id": ("H2", "past2")},
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

# какие положения стрелок нужны для маршрута (можно подправить под реальную схему)
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



def format_routes(routes_dict):
    if not routes_dict:
        return "Маршруты не заданы."
    seen = set()
    lines = []
    for a, b in routes_dict.keys():
        if (a, b) in seen:
            continue
        seen.add((a, b))
        lines.append(f"{a} \u2192 {b}")
    return "\n".join(lines)

def show_maneuver_routes():
    global settingRoute
    if settingRoute == True:
        return
    set_mode("maneuver")
    msg = "Маневровые маршруты:\n\n" + format_routes(routes)
    showInfo("МАНЕВРОВЫЕ", msg)

def show_train_routes():
    global settingRoute
    if settingRoute == True:
        return
    set_mode("train")
    msg = "Поездные маршруты:\n\n" + format_routes(train_routes)
    showInfo("ПОЕЗДНЫЕ", msg)

#########################################       ОТРИСОВКА СВЕТОФОРОВ                ##############################################
def drawSignal(name, mount="bottom", pack_side="right", count=3, colors=None):
    x, y = positions[name]

    r = 4
    gap = 2 * r + 2
    stand_len = 15
    bar_len = 10

    dy_sign = -1 if mount == "top" else 1
    sx, sy = x, y + dy_sign * stand_len

    canvas.create_line(x, y, sx, sy, width=2, fill="black")

    hx_sign = 1 if pack_side == "right" else -1

    hx0, hy0 = sx, sy
    hx1, hy1 = sx + hx_sign * bar_len, sy
    canvas.create_line(hx0, hy0, hx1, hy1, width=2, fill="black")

    ids = []
    start_cx = hx1 + hx_sign * (r + 1)

    for i in range(count):
        cx = start_cx + hx_sign * i * gap
        cy = sy

        fill_color = ""
        if colors is not None and i < len(colors):
            fill_color = colors[i]

        oid = canvas.create_oval(
            cx - r, cy - r,
            cx + r, cy + r,
            outline="black", width=1, fill=fill_color
        )
        ids.append(oid)

    signal_ids[name] = ids

#########################################        ФУНКЦИИ МАРШРУТОВ                ##############################################


def paint_diagonal(name, color):
    if name in split_diag_ids:
        return
    for line_id in diag_ids[name]:
        canvas.itemconfig(line_id, fill=color)


def paint_segment(key, color):
    seg_id = segment_ids.get(key)
    if seg_id is None:
        return
    canvas.itemconfig(seg_id, fill=color)

def paint_route(start, end, color="yellow"):
    key = (start, end)
    if current_mode == "maneuver":
        if key not in routes:
            key = (end, start)
            if key not in routes:
                print("Маршрут не найден")
                return

        for step in routes[key]:
            if step["type"] == "segment":
                paint_segment(step["id"], color)
            elif step["type"] == "diag":
                paint_diagonal(step["name"], color)
            else:
                print("Неизвестный тип шага:", step)
    if current_mode == "train":
        if key not in train_routes:
            key = (end, start)
            if key not in train_routes:
                print("Маршрут не найден")
                return

        for step in train_routes[key]:
            if step["type"] == "segment":
                paint_segment(step["id"], color)
            elif step["type"] == "diag":
                paint_diagonal(step["name"], color)
            else:
                print("Неизвестный тип шага:", step)

#########################################        ФУНКЦИИ ВКЛ/ОТКЛ СТРЕЛОК               ##############################################
def setBranchRight(nameDiag, offset):
    if nameDiag in split_diag_ids.keys():
        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][0])
        canvas.coords(split_diag_ids[nameDiag]['partA'][0], x1, y1 - offset, x2, y2 - offset)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][1])
        canvas.coords(split_diag_ids[nameDiag]['partA'][1], x1, y1 - offset, x2, y2)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][0])
        canvas.coords(split_diag_ids[nameDiag]['partB'][0], x1, y1, x2, y2 + offset + 1)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][1])
        canvas.coords(split_diag_ids[nameDiag]['partB'][1], x1, y1 + offset + 1, x2, y2 + offset + 1)
    else:
        x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][0])
        canvas.coords(diag_ids[nameDiag][0], x1, y1 + offset, x2, y2 + offset)
        x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
        canvas.coords(diag_ids[nameDiag][2], x1, y1 + offset, x2, y2)

def setBranchLeft(nameDiag, offset):
    if nameDiag in split_diag_ids.keys():
        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][0])  #1
        canvas.coords(split_diag_ids[nameDiag]['partA'][0], x1, y1 + offset, x2, y2 + offset)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][1])   #3
        canvas.coords(split_diag_ids[nameDiag]['partA'][1], x1, y1 + offset, x2, y2)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][0])  #2
        canvas.coords(split_diag_ids[nameDiag]['partB'][0], x1, y1, x2, y2- offset-1)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][1])  #4
        canvas.coords(split_diag_ids[nameDiag]['partB'][1], x1, y1 - offset-1, x2, y2 - offset-1)
    else:
        x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][1])
        canvas.coords(diag_ids[nameDiag][1], x1, y1 + offset, x2, y2 + offset)
        x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
        canvas.coords(diag_ids[nameDiag][2], x1, y1, x2, y2 + offset)


def branchWidth(namediag, width):
    if namediag in split_diag_ids.keys():
        for part, lines in split_diag_ids[namediag].items():
            for line_id in lines:
                canvas.itemconfig(line_id, width=width)
    else:
        for lines in range(len(diag_ids[(namediag)])):
            canvas.itemconfig(diag_ids[namediag][lines], width=width)

def apply_diagonal_mode(nameDiag, mode):
    cfg = diagonal_config.get(nameDiag)
    if cfg is None:
        print(f"No config for {nameDiag}")
        return

    left_cfg = cfg["left"]
    if left_cfg["exists"]:
        if mode in ("left", "both"):
            setBranchLeft(nameDiag, left_cfg["connected"])
            branchWidth(nameDiag, 6)
            if nameDiag == "ALB_Turn8":
                canvas.itemconfig(segment_ids[("M6H2", "H2")], width=6)
        else:
            setBranchLeft(nameDiag, left_cfg["disconnected"])
            branchWidth(nameDiag, 2)
            if nameDiag == "ALB_Turn2":
                canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=2)
                #canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=2)

    right_cfg = cfg["right"]
    if right_cfg["exists"]:
        if mode in ("right", "both"):
            setBranchRight(nameDiag, right_cfg["connected"])
            branchWidth(nameDiag, 6)

            if nameDiag == "ALB_Turn4-6":
                canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=2)
                canvas.itemconfig(segment_ids[("M6", "M6H2")], width=2)
            if nameDiag == "ALB_Turn1":
                canvas.itemconfig(segment_ids[("M8mid", "M8")], width=2)
            if nameDiag == "ALB_Turn8":
                canvas.itemconfig(segment_ids[("M6H2", "H2")], width=2)
        else:
            setBranchRight(nameDiag, right_cfg["disconnected"])
            branchWidth(nameDiag, 2)
            if nameDiag == "ALB_Turn2":
                canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=6)
            if nameDiag == "ALB_Turn4-6":
                canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=6)
                canvas.itemconfig(segment_ids[("M6", "M6H2")], width=6)
                text = canvas.itemcget(switch_text_ids["ALB_Turn2"], "text")
                if text == "-":
                    canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=2)

            if nameDiag == "ALB_Turn1":
                canvas.itemconfig(segment_ids[("M8mid", "M8")], width=6)

def get_switch_state_num(name):
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")
    if mode is None:
        return "None"
    if mode == normal:
        return "+"
    else:
        return "-"

def get_switch_state_color(name):
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")
    if mode is None:
        return "grey"
    if mode == normal:
        return "green"    # плюс, нормальное положение
    else:
        return "yellow"   # переведена

def update_switch_indicator(name):
    rect = switch_indicator_ids.get(name)
    labelSwitch = switch_text_ids.get(name)
    if rect is None:
        return
    color = get_switch_state_color(name)
    text = get_switch_state_num(name)
    canvas.itemconfig(rect, fill=color)
    canvas.itemconfig(labelSwitch, text=text)

#########################################  МИНИ-Таблица стрелок (правый нижний угол)  #########################################
def create_switch_table():
    w = int(canvas["width"])
    h = int(canvas["height"])

    dy = 25
    total_height = dy * len(switch_list)
    y_start = h - total_height - 20

    x_text = w - 220
    x_rect = w - 60

    for i, name in enumerate(switch_list, start=1):
        y = y_start + (i - 1) * dy
        switch = canvas.create_text(x_text, y, text=f"{i}. {name}", anchor="w", font=("Bahnschrift SemiBold", 12), tags=(f"switch_{name}", "switch"))
        switch_ids[name] = switch
        label = canvas.create_text(x_rect-30, y+1, text="0", font=("Bahnschrift SemiBold", 12))

        rect = canvas.create_rectangle(
            x_rect - 8, y - 8, x_rect + 8, y + 8,
            outline="black", fill="grey"
        )
        switch_text_ids[name] = label
        switch_indicator_ids[name] = rect
        update_switch_indicator(name)
create_switch_table()

def set_diagonal_mode(nameDiag, mode):
    apply_diagonal_mode(nameDiag, mode)
    diagonal_modes[nameDiag] = mode
    update_switch_indicator(nameDiag)



def blink_switches(diags, duration_ms=2000, interval_ms=200):
    if not diags:
        return

    end_time = time.time() + duration_ms / 1000.0
    final_colors = {d: get_switch_state_color(d) for d in diags}

    def _step(state=True):
        if time.time() >= end_time:
            for d in diags:
                rect = switch_indicator_ids.get(d)
                if rect is not None:
                    canvas.itemconfig(rect, fill=final_colors[d])
            return

        for d in diags:
            rect = switch_indicator_ids.get(d)
            if rect is not None:
                canvas.itemconfig(rect, fill="cyan" if state else final_colors[d])

        root.after(interval_ms, _step, not state)

    _step(True)
#########################################        СТРЕЛКИ/ДИАГОНАЛИ               ##############################################
def AddDiagonal(x1, y1, x2, y2, offsetleft, offsetright, nameDiag):
    l1 = canvas.create_line(x1, y1, x1 - offsetleft, y1, width=3, fill="black")
    l2 = canvas.create_line(x2, y2, x2 + offsetright, y2, width=3, fill="black")
    l3 = canvas.create_line(x1, y1, x2, y2, width=3, fill="black")
    diag_ids[(nameDiag)] = [l1, l2, l3]


def AddSplitDiagonal(x1, y1, x2, y2,
                     x3, y3,offset_left,
                     offset_right, nameDiag, namePart1, namePart2):
    l2 = canvas.create_line(x1, y1, x2, y2, width=3, fill="black")
    l3 = canvas.create_line(x2, y2, x3, y3, width=3, fill="black")
    l1 = canvas.create_line(x1, y1, x1 - offset_left, y1, width=3, fill="black")
    l4 = canvas.create_line(x3, y3, x3 + offset_right, y3, width=3, fill="black")
    split_diag_ids[nameDiag] = {
        'partA': [l1, l2],
        'partB': [l3, l4]
    }
    diag_ids[(namePart1)] = [l1, l2]
    diag_ids[(namePart2)] = [l3, l4]

#########################################       ЛИНИИ              ##############################################
for a, b in segments:
    x1, y1 = positions[a]
    x2, y2 = positions[b]
    seg = canvas.create_line(x1 - 5, y1, x2 + 5, y2, width=6, fill="black")
    segment_ids[(a, b)] = seg
    segment_ids[(b, a)] = seg

#########################################        ДИАГОНАЛИ/СТРЕЛКИ               ##############################################
AddDiagonal(260, 330, 350, 430, 20, 38, "ALB_Turn2")
AddDiagonal(965, 330, 890, 430, -22, -37, "ALB_Turn1")
AddDiagonal(560, 130, 470, 230, -57, -20, "ALB_Turn8")
AddSplitDiagonal(430, 230, 390, 280,350, 330, -30, -30, "ALB_Turn4-6", "ALB_Turn4", "ALB_Turn6")


# начальное положение стрелок
set_diagonal_mode("ALB_Turn1", "left")
set_diagonal_mode("ALB_Turn2", "left")
set_diagonal_mode("ALB_Turn8", "left")
set_diagonal_mode("ALB_Turn4-6", "left")


def check_if_route_finished(seg, rev, diag):
    for rid in list(active_routes.keys()):
        data = active_routes[rid]
        segs = data["segments"]
        all_segs = []
        for steps in segs:
            if steps.get("type") == "diag":
                all_segs.append(steps)
            if steps.get("type") == "segment":
                all_segs.append(steps)
        last_all = all_segs[-1]
        if last_all.get("type") == "segment":
            if seg == last_all["id"] or rev == last_all["id"]:
                release_route(rid)
        if last_all.get("type") == "diag":
            if last_all["name"] == diag:
                release_route(rid)
        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if last_all.get("type") == "segment":
                    if s == last_all["id"] or rev == last_all["id"]:
                        release_route(rid)



def set_arduino_status(connected: bool, text: str = ""):
    if connected:
        arduino_status_label.config(text=f"Arduino: {text}", bg="green", fg="black")
    else:
        arduino_status_label.config(text="Arduino: not connected", bg="red", fg="white")

part_to_split = {}

for split_name in split_parts_map:
    for part, logic_name in split_parts_map[split_name].items():
        part_to_split[logic_name] = (split_name, part)


def update_all_occupancy():
    for seg in seg_occ_train:
        rev = (seg[1], seg[0])
        if seg_occ_train.get(seg, 1) == 0:
            occupied_segments.discard(seg)
            occupied_segments.discard(rev)
            check_if_route_finished(seg, rev, diag="")
            block = segment_to_block.get(seg)
            if block is None:
                continue
            segs_in_block = segment_groups[block]
            for s in segs_in_block:
                occupied_segments.discard(s)
                occupied_segments.discard((s[1], s[0]))
    for diag in diag_occ_train:
        if diag_occ_train.get(diag, 1) == 0:
            occupied_diagonals.discard(diag)
            check_if_route_finished(seg="", rev="", diag=diag)
    for (a, b), seg_id in segment_ids.items():
        seg = (a, b)
        block = segment_to_block.get(seg)

        if block:
            # если ЛЮБОЙ сегмент блока занят → весь блок красный
            if any(seg_occ_train.get(s, 1) == 0 for s in segment_groups[block]):
                paint_segment(seg, "red")
                continue
            for s in segment_groups[block]:
                if s in occupied_segments:
                    paint_segment(seg, "yellow")
                    continue
            paint_segment(s, "black")

        if seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0 :
            paint_segment((a,b), "red")
            continue
        if (a, b) in occupied_segments or (b, a) in occupied_segments:
            paint_segment((a,b), "yellow")
            continue

        paint_segment((a, b), "black")


    for diag_name, lines in diag_ids.items():
        if diag_occ_train.get(diag_name, 1) == 0:
            paint_diagonal(diag_name, "red")
            continue
        if diag_name in occupied_diagonals:
            paint_diagonal(diag_name, "yellow")
            continue
        # 3) свободна -> чёрная
        paint_diagonal(diag_name, "black")

    root.after(100, update_all_occupancy)

#########################################        ПОДСВЕТКА МАРШРУТОВ               ##############################################
def highlight_possible_targets(start):

    possible = set()
    if current_mode == "maneuver":
        for (a, b) in routes.keys():
            if a == start:
                possible.add(b)
    if current_mode == "train":
        for (a, b) in train_routes.keys():
            if a == start:
                possible.add(b)
    for name, item_id in node_ids.items():
        if name == start:
            canvas.itemconfig(item_id, fill="yellow")
            continue

        if name in possible:
            canvas.itemconfig(item_id, fill="green")
            canvas.itemconfig(item_id, state="normal")
        else:
            canvas.itemconfig(item_id, fill="grey")
            canvas.itemconfig(item_id, state="disabled")

    for name in possible:
        canvas.itemconfig(f"node_{name}", state="normal")

def reset_node_selection():
    for name, item_id in node_ids.items():
        canvas.itemconfig(item_id, fill="black", state="normal")
    selected_nodes.clear()

def disable_all_except_selected():
    for name, item in node_ids.items():
        if name in selected_nodes:
            canvas.itemconfig(item, state="normal")
        else:
            canvas.itemconfig(item, fill="grey", state="disabled")

#########################################        ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ УЗЛОВ      ##############################################
def get_node_name_from_event(event):
    items = canvas.find_withtag("current")
    if not items:
        return None

    item = items[0]
    tags = canvas.gettags(item)

    for t in tags:
        if t.startswith("node_"):
            return t.replace("node_", "")
    return None

def get_switch_name_from_event(event):
    items = canvas.find_withtag("current")
    if not items:
        return None
    item = items[0]
    tags = canvas.gettags(item)
    for t in tags:
        if t.startswith("switch_"):
            return t.replace("switch_", "")
    return None

def on_enter(event):
    name = get_node_name_from_event(event)
    if name is None:
        return

    if name not in selected_nodes:
        canvas.itemconfig(node_ids[name], fill="#e01dcd")

def on_leave(event):
    name = get_node_name_from_event(event)
    if name is None:
        return
    if name not in selected_nodes:
        if len(selected_nodes) == 1:
            canvas.itemconfig(node_ids[name], fill="green")
        else:
            canvas.itemconfig(node_ids[name], fill="black")

def switch_on_enter(event):
    name = get_switch_name_from_event(event)
    canvas.itemconfig(switch_ids[name], fill="pink")

def switch_on_leave(event):
    name = get_switch_name_from_event(event)
    canvas.itemconfig(switch_ids[name], fill="black")

#########################################        КОНФЛИКТЫ МАРШРУТОВ ПОСТРОЕНИЕ НОВЫХ               ##############################################
def next_route_id():
    global route_counter
    rid = route_counter
    route_counter += 1
    return rid

global settingRoute
settingRoute = False

def get_route(start, end):
    if current_mode == "maneuver":
        key = (start, end)
        if key in routes:
            return routes[key]
        return None
    if current_mode == "train":
        key = (start, end)
        if key in train_routes:
            return train_routes[key]
        return None

def has_switch_conflict(a, b):
    """
    Проверяет, можно ли построить маршрут a->b или он ломает существующие стрелки.
    """
    key = (a, b)
    if key not in route_switch_modes:
        key = (b, a)
        if key not in route_switch_modes:
            return False  # маршрут не использует стрелки вообще

    needed = route_switch_modes[key]

    # пробегаем по ВСЕМ активным маршрутам
    for rid, data in active_routes.items():

        other_key = (data["start"], data["end"])
        if other_key not in route_switch_modes:
            other_key = (data["end"], data["start"])
            if other_key not in route_switch_modes:
                continue

        other_needed = route_switch_modes[other_key]

        # теперь сравниваем стрелки
        for diag_name, mode_needed in needed.items():

            # если эта стрелка не используется другим маршрутом — всё норм
            if diag_name not in other_needed:
                continue
            other_mode = other_needed[diag_name]
            # если маршруты требуют РАЗНОЕ положение → конфликт
            if other_mode != mode_needed:
                print(f"КОНФЛИКТ: стрелка {diag_name} уже занята маршрутом #{rid}, "
                      f"она стоит в положении {other_mode}, "
                      f"а требуется {mode_needed}")
                return True

    return False

def check_route_conflict(start, end):
    if current_mode == "maneuver":
        if has_switch_conflict(start, end):
            return True
        for step in routes.get((start,end)):
            if step["type"] == "segment":
                a, b = step["id"]
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0:
                    return True
            elif step["type"] == "diag":
                if step["name"] in occupied_diagonals or diag_occ_train.get(step["name"],1) == 0:
                    return True
        return False
    if current_mode == "train":
        if has_switch_conflict(start, end):
            return True
        for step in train_routes.get((start,end)):
            if step["type"] == "segment":
                a, b = step["id"]
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0:
                    return True
            elif step["type"] == "diag":
                if step["name"] in occupied_diagonals or diag_occ_train.get(step["name"],1) == 0:
                    return True
        return False

def register_route(start, end):

    global route_counter
    rid = route_counter
    route_counter += 1
    if current_mode == "maneuver":
        for step in routes.get((start,end)):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a,b))
                occupied_segments.add((b,a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {
            "start": start,
            "end": end,
            "segments": routes.get((start,end)),
        }
        return rid
    if current_mode == "train":
        for step in train_routes.get((start,end)):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a,b))
                occupied_segments.add((b,a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {
            "start": start,
            "end": end,
            "segments": train_routes.get((start,end)),
        }
        return rid

def release_route(route_id):
    global route_counter
    if route_id not in active_routes:
        return
    data = active_routes[route_id]
    for step in data["segments"]:
        if step["type"] == "segment":
            a, b = step["id"]
            paint_segment((a,b), "black")
            occupied_segments.discard((a,b))
            occupied_segments.discard((b, a))
        elif step["type"] == "diag":
            paint_diagonal(step["name"], "black")
            occupied_diagonals.discard(step["name"])
    del active_routes[route_id]
    comboboxDelete(route_id)

#########################################        МИГАНИЕ МАРШРУТА               ##############################################
def is_segment_in_blinking_route(seg):
    a, b = seg
    for (start, end) in blinking_routes:
        route = routes.get((start, end)) or routes.get((end, start))
        if not route:
            continue
        for step in route:
            if step.get("type") == "segment":
                if step["id"] == (a,b) or step["id"] == (b,a):
                    return True
    return False

def blink_route(start, end, duration_ms=2000, interval_ms=200):
    blinking_routes.add((start,end))
    end_time = time.time() + duration_ms / 1000.0

    def _step(state=True):
        if time.time() >= end_time:
            paint_route(start, end, "black")
            return

        color = "cyan" if state else "black"
        paint_route(start, end, color)
        root.after(interval_ms, _step, not state)
    _step(True)
#########################################        ОБРАБОТКА КЛИКА ПО УЗЛУ          ##############################################
def on_node_click(event):
    name = get_node_name_from_event(event)
    if name is None:
        return

    if name in selected_nodes:
        selected_nodes.remove(name)
        canvas.itemconfig(node_ids[name], fill="black")
        if len(selected_nodes) == 0:
            reset_node_selection()
        if len(selected_nodes) == 1:
            highlight_possible_targets(selected_nodes[0])
        return

    if len(selected_nodes) >= MAX_SELECTED:
        return

    selected_nodes.append(name)
    canvas.itemconfig(node_ids[name], fill="cyan")
    print("Выбрано узлов:", len(selected_nodes), "->", selected_nodes)

    if len(selected_nodes) == 1:
        highlight_possible_targets(name)

    if len(selected_nodes) == 2:
        first = selected_nodes[0]
        second = selected_nodes[1]
        disable_all_except_selected()
        on_two_nodes_selected(first, second)

def blink_diag(name, duration_ms=2000, interval_ms=200):
        blinking_diags.add(name)
        end_time = time.time() + duration_ms / 1000.0

        def _step(state=True):
            if time.time() >= end_time:
                if name in split_diag_ids:
                    for split_name in split_diag_ids.keys():
                        for part_name, lines in split_diag_ids[split_name].items():
                            logic_name = split_parts_map[split_name][part_name]
                            if logic_name in diag_ids:
                                   paint_diagonal(logic_name, "black")
                else:
                    paint_diagonal(name, "black")
                return

            color = "cyan" if state else "black"
            if name in split_diag_ids:
                for split_name in split_diag_ids.keys():
                    for part_name, lines in split_diag_ids[split_name].items():
                        logic_name = split_parts_map[split_name][part_name]
                        if logic_name in diag_ids:
                            paint_diagonal(logic_name, color)
            else:
                paint_diagonal(name, color)
            root.after(interval_ms, _step, not state)
        _step(True)

global changingSwitches
changingSwitches = False
def on_switch_mode_selected(name,mode):
    text = canvas.itemcget(switch_text_ids[name], "text")
    global settingRoute
    global changingSwitches
    if changingSwitches:
        showInfo("Ошибка", "Одна из стерок меняется!")
        return
    if diag_occ_train.get(name, 1) == 0:
        showInfo("Ошибка", "Стрелка занята!")
        return

    textALB4_6 = canvas.itemcget(switch_text_ids["ALB_Turn4-6"], "text")
    ALB_Turn1banned = [("M8mid", "M8"), ("M8", "M8_mid")]
    ALB_Turn8banned = [("M6", "M6H2"), ("H2", "M6H2"),("M6H2", "M6") ,("M6H2", "H2")]
    ALB_Turn4_6banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2"), ("M6", "M6H2"), ("M6H2", "M6"), ("H2", "M6H2"), ("M6H2", "H2")]
    ALB_Turn2banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2")]

    for num in active_routes:
        for step in active_routes[num]["segments"]:
            if name == 'ALB_Turn1':
                if step["type"] == "segment":
                    if step["id"] in ALB_Turn1banned:
                        showInfo("Ошибка", "Стрелка на готовом маршруте!")
                        return
            if name == 'ALB_Turn2':
                if step["type"] == "segment":
                    if step["id"] in ALB_Turn2banned:
                        showInfo("Ошибка", "Стрелка на готовом маршруте!")
                        return
            if name == "ALB_Turn8":
                if step["type"] == "segment":
                    if step["id"] in ALB_Turn8banned:
                        showInfo("Ошибка", "Стрелка на готовом маршруте!")
                        return
            if name == "ALB_Turn4-6":
                if step["type"] == "segment":
                    if step["id"] in ALB_Turn4_6banned:
                        showInfo("Ошибка", "Стрелка на готовом маршруте!")
                        return
            if step["type"] == "diag":
                if step["name"] == name:
                    showInfo("Ошибка", "Используемая стрелка!")
                    return
    if settingRoute:
        showInfo("Ошибка", "Невозможно сменить стрелку")
        return
    changingSwitches = True
    blink_diag(name, duration_ms=2000, interval_ms=200)

    def finalize():
        global changingSwitches
        if mode == 0 and text != "+":
            if name == "ALB_Turn2" and textALB4_6 == "+":
                canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=6)
            set_diagonal_mode(name, "left")
            changingSwitches = False
        elif mode == 1 and text != "-":
            set_diagonal_mode(name, "right")
            changingSwitches = False
        else:
            changingSwitches = False
            return
    root.after(2100, finalize)


def on_switch_click(event):
    name = get_switch_name_from_event(event)
    menu = tk.Menu(root, tearoff=0)
    menu.add_command(
        label="-",
        command=lambda: on_switch_mode_selected(name, 1)
    )
    menu.add_command(
        label="+",
        command=lambda: on_switch_mode_selected(name, 0)
    )
    menu.tk_popup(event.x_root, event.y_root)

#########################################        ФУНКЦИЯ ПРИ ВЫБОРЕ ДВУХ ТОЧЕК   ##############################################
def visualSwitch(key):
    list = [("M2", "H1"), ("M2", "M8"), ("M2", "M1"), ("H1", "M2"), ("M1", "M2")]
    needRoutes = [('H1', 'M2H1_third')]
    if key in list:
        canvas.itemconfig(segment_ids[needRoutes[0]], width=6)

def on_two_nodes_selected(a, b):
    global last_switch_check
    global settingRoute

    # 1. Проверка конфликтов по занятым сегментам/стрелкам
    if check_route_conflict(a, b):
        print("Маршрут конфликтует с уже установленными!")
        reset_node_selection()
        return
    if settingRoute == True:
        reset_node_selection()
        return

    # 2. Ищем настройки стрелок для этого маршрута
    key = (a, b)

    if key not in route_switch_modes:
        key = (b, a)

    if key not in route_switch_modes:
        print("Для этого маршрута нет настроек стрелок")
        reset_node_selection()
        return

    route_cfg = route_switch_modes[key]   # только нужные стрелки для ЭТОГО маршрута

    last_switch_check = {}
    changed = []
    main_diag = None  # какая стрелка будет мигать в табличке

    for diag_name, need_mode in route_cfg.items():
        current_mode = diagonal_modes.get(diag_name)
        ok = (current_mode == need_mode)

        last_switch_check[diag_name] = {
            "needed": need_mode,
            "current": current_mode,
            "ok": ok,
        }
        if not ok:
            if main_diag is None:
                main_diag = diag_name
            set_diagonal_mode(diag_name, need_mode)
            changed.append(f"{diag_name}: {current_mode} -> {need_mode}")

    if main_diag is None and route_cfg:
        main_diag = next(iter(route_cfg.keys()))
    settingRoute = True
    paint_route(a, b, "cyan")

    blink_route(a, b, duration_ms=2000, interval_ms=150)

    if main_diag is not None:
        blink_switches([main_diag], duration_ms=2000, interval_ms=150)

    if changed:
        print("Изменены стрелки:")
        for line in changed:
            print("  ", line)
    else:
        if route_cfg:
            print("Стрелки уже стояли как нужно.")
        else:
            print("Маршрут без задействования стрелок.")

    reset_node_selection()
    visualSwitch(key)
    def finalize():
        rid = register_route(a, b)
        paint_route(a, b, "yellow")
        global settingRoute
        settingRoute = False
        current_values = list(combobox1["values"])
        current_values.append(rid)
        combobox1["values"] = tuple(current_values)

    root.after(2100, finalize)

def comboboxDelete(ids):
    options = list(combobox1['values'])
    options.remove(str(ids))
    combobox1["values"] = options

def snos():
    selected_item = combobox1.get()
    if selected_item == "":
        return
    num = int(selected_item)
    release_route(num)
    combobox1.set('')

def snosAll():
    for active in list(active_routes.keys()):
        release_route(active)
    combobox1.set('')

def check():
    print("Активные маршруты")
    print(active_routes)

#########################################        Arduino: функции подключения/отправки ##########################################
def init_arduino():
    """
    Поиск порта и подключение к Arduino.
    """
    global arduino, arduino_status_label

    try:
        ports = list(serial.tools.list_ports.comports())
        arduino_port = None

        for p in ports:
            if "Arduino" in p.description or "CH340" in p.description:
                arduino_port = p.device
                break

        # если не нашли автоматически – выставь свой порт
        if arduino_port is None:
            # ПОДСТАВЬ СВОЙ ПОРТ (например "COM3" или "/dev/ttyACM0")
            arduino_port = "COM3"

        arduino = serial.Serial(arduino_port, 9600, timeout=1)
        time.sleep(2)  # дать Arduino перезапуститься

        print(f"Arduino подключен к {arduino_port}")
        if arduino_status_label is not None:
            arduino_status_label.config(text=f"Arduino: {arduino_port}", fg="green")

    except Exception as e:
        print("Не удалось подключиться к Arduino:", e)
        arduino = None
        if arduino_status_label is not None:
            arduino_status_label.config(text="Arduino: нет соединения", fg="red")

####################################################################################################

#########################################        ТУПИКИ               ##############################################
drawDeadEnd("pastM1", "right", 0)
drawDeadEnd("past2", "right", 0)
drawDeadEnd("past4", "right", 0)
drawDeadEnd("beforeM6", "left", 0)

#########################################        СТАНЦИИ(КРУГИ/ТЕКСТ)             ##############################################
bannedNames = ["pastM1", "beforeM6", "past2", "1STR", "past4", "M6H2", "M2H1_mid", "M8mid", "M2H1_third"]
for name, (x, y) in positions.items():
    if name in bannedNames:
        continue
    node = canvas.create_text(x, y - 25, text=name, tags=(f"node_{name}", "node"), font=("Bahnschrift SemiBold", 12))
    node_ids[name] = node

#########################################        РИСУЕМ ВСЕ СВЕТОФОРЫ            ##############################################
for name, cfg in signals_config.items():
    drawSignal(
        name,
        cfg["mount"],
        cfg["pack_side"],
        cfg["count"],
        cfg.get("colors")
    )
#########################################        ВИЗУАЛ РЕЖИМА                    ##############################################
def apply_mode_visuals():
    for name, item_id in node_ids.items():
        color = "black"
        state = "normal"
        if current_mode == "maneuver" and name == "CH":
            color = "grey"
            state = "disabled"
        canvas.itemconfig(item_id, fill=color, state=state)

def set_mode(mode):
    global current_mode
    current_mode = mode

    if btn_maneuver is not None and btn_train is not None:
        if mode == "maneuver":
            btn_maneuver.config(bg="#65a167", fg="white")
            btn_train.config(bg="#bf4343", fg="white")
        else:
            btn_train.config(bg="#65a167", fg="white")
            btn_maneuver.config(bg="#bf4343", fg="white")

    selected_nodes.clear()
    apply_mode_visuals()

#########################################        БИНДЫ И НАЧАЛЬНАЯ ОКРАСКА        ##############################################
canvas.tag_bind("node", "<Button-1>", on_node_click)
canvas.tag_bind("node", "<Enter>", on_enter)
canvas.tag_bind("node", "<Leave>", on_leave)

canvas.tag_bind("switch", "<Button-1>", on_switch_click)
canvas.tag_bind("switch", "<Enter>", switch_on_enter)
canvas.tag_bind("switch", "<Leave>", switch_on_leave)

# метка статуса Arduino
arduino_status_label = tkinter.Label(root, text="Arduino: проверка...", fg="orange")
arduino_status_label.place(x=360, y=20)
n = tkinter.StringVar()
combobox1 = ttk.Combobox(root, width = 25, textvariable = n, )
combobox1.place(x=510,y=20)
button = tkinter.Button(root, text="Снести", command=snos)
button.place(x=700, y=18)
buttonAll = tkinter.Button(root, text="Убрать всё", command=snosAll)
buttonAll.place(x=770, y=18)
button = tkinter.Button(root, text="Проверка", command=check)
button.place(x=860, y=18)

buttons_y = CANVAS_H - 80

btn_maneuver = tkinter.Button(
    root,
    text="МАНЕВРОВЫЕ",
    font=("Bahnschrift Bold", 15),
    bg="#65a167",
    fg="white",
    width=15,
    height=2,
    command=show_maneuver_routes
)
btn_train = tkinter.Button(
    root,
    text="ПОЕЗДНЫЕ",
    font=("Bahnschrift Bold", 15),
    bg="#bf4343",
    fg="white",
    width=15,
    height=2,
    command=show_train_routes
)

center_x = CANVAS_W // 2
offset = 140

btn_maneuver.place(x=center_x - offset - 80, y=buttons_y)
btn_train.place(x=center_x + offset - 80, y=buttons_y)

def do(button_id):
    if button_id >= 0 and button_id < 13:
        keys = list(seg_occ_train.keys())
        seg = keys[button_id]
        seg_occ_train[seg] = 1 if seg_occ_train[seg] == 0 else 0
    else:
        keys = list(diag_occ_train.keys())
        seg = keys[button_id-13]
        diag_occ_train[seg] = 1 if diag_occ_train[seg] == 0 else 0

for i in range(18):
    button69 = tkinter.Button(root, text=f"{[i]}", command=lambda id=i: do(id))
    button69.place(x=10, y=40 + i * 25)


#########################################        ARDUINO: ПОИСК ПОРТА И ОПРОС      ##############################################
def find_arduino_port():
    ports = list_ports.comports()
    for p in ports:
        desc = p.description.lower()
        if "arduino" in desc or "ch340" in desc or "usb serial" in desc:
            print(f"Найдено Arduino-подобное устройство: {p.device} ({p.description})")
            return p.device
    if ports:
        print(f"Не удалось однозначно определить Arduino, беру первый порт: {ports[0].device}")
        return ports[0].device
    print("COM-порты не найдены вообще.")
    return None

def init_arduino(port=None, baudrate=9600):
    global ser

    if port is None:
        port = find_arduino_port()
        if port is None:
            set_arduino_status(False)
            return

    try:
        ser = serial.Serial(port, baudrate, timeout=0)
        time.sleep(2)             # даём плате перезапуститься
        ser.reset_input_buffer()  # чистим мусор
        print(f"Arduino подключено на {port}")
        set_arduino_status(True, text=port)
    except SerialException as e:
        ser = None
        set_arduino_status(False)
        print(f"Не удалось открыть {port}: {e}")
        print("Попробую переподключиться через 2 секунды.")
        root.after(2000, init_arduino)

def bytes_to_bits(data: bytes, bits_needed: int) -> list[int]:
    bits: list[int] = []
    for byte in data:
        for bit_index in range(8):
            bits.append((byte >> bit_index) & 1)
            if len(bits) >= bits_needed:
                return bits
    while len(bits) < bits_needed:
        bits.append(0)

    return bits

def apply_bits_to_segments(bits: list[int]):
    """
    bits[i] относится к SEGMENT_ORDER[i].
    1 в бите = кнопка НАЖАТА -> сегмент ЗАНЯТ (0)
    0 в бите = кнопка ОТПУЩЕНА -> сегмент СВОБОДЕН (1)
    """
    global last_bits

    if last_bits is None:
        last_bits = bits[:]
        for idx, (bit_value, seg) in enumerate(zip(bits, SEGMENT_ORDER)):
            occ = 0 if bit_value == 1 else 1
            seg_occ_train[seg] = occ
            print(f"[INIT BTN {idx}] raw_bit={bit_value} segment={seg} -> seg_occ_train={occ}")
        return

    for idx, (bit_value, seg) in enumerate(zip(bits, SEGMENT_ORDER)):
        old_bit = last_bits[idx]
        if bit_value == old_bit:
            continue  # ничего не изменилось — пропускаем

        # тут важная инверсия:
        # 1 (нажата) -> 0 (занят)
        # 0 (отпущена) -> 1 (свободен)
        occ = 0 if bit_value == 1 else 1
        seg_occ_train[seg] = occ
        print(f"[BTN {idx}] raw_bit={bit_value} segment={seg} -> seg_occ_train={occ}")

    last_bits = bits[:]


def poll_arduino():
    global ser

    if ser is not None and ser.is_open:
        try:
            data = ser.read(1)  # один байт = 8 кнопок
        except SerialException as e:
            print(f"Ошибка чтения из Arduino: {e}")
            ser = None
            set_arduino_status(False)
            root.after(2000, init_arduino)
        else:
            if data:
                val = data[0]
                bits = bytes_to_bits(data, bits_needed=len(SEGMENT_ORDER))
                apply_bits_to_segments(bits)

    root.after(20, poll_arduino)

#########################################        ЗАПУСК ЦИКЛОВ                    ##############################################
init_arduino()          # порт ищется автоматически
update_all_occupancy()
poll_arduino()
root.mainloop()
