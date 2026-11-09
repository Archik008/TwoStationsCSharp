import tkinter
import tkinter as tk
import time
from tkinter.messagebox import showinfo

root = tk.Tk()
root.title("Станция")

# фиксированный размер канвы, без фуллскрина
CANVAS_W = 1150
CANVAS_H = 600

canvas = tk.Canvas(root, width=CANVAS_W, height=CANVAS_H, bg="white")
canvas.pack()


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
}

segments = [
    ("M1", "pastM1"),
    ("M1", "M8"),
    ("M8", "H1"),
    ("H1", "M2"),
    ("M2", "CH"),

    ("past2", "H2"),
    ("H2", "M6H2"),
    ("M6H2", "M6"),

    ("M10", "H3"),

    ("past4", "H4"),

    ("M6", "beforeM6"),
]

#########################################        МАССИВЫ ЭЛЕМЕНТОВ               ##############################################
selected_nodes = []
MAX_SELECTED = 2
node_ids = {}
segment_ids = {}
diag_ids = {}
signal_ids = {}

active_routes = {}  # route_id -> {"start": a, "end": b, "segments": [...], "route_type": "maneuver"/"train"}
route_counter = 1

occupied_segments = set()
occupied_diagonals = set()
diagonal_modes = {}

switch_indicator_ids = {}
switch_list = ["M1M10", "M2H3", "H42", "2T1"]

default_switch_mode = {
    "M1M10": "left",
    "M2H3": "left",
    "H42":  "left",
    "2T1":  "left",
}

last_switch_check = {}

current_mode = "maneuver"
btn_maneuver = None
btn_train = None


#########################################        КОНФИГ ДИАГОНАЛЕЙ               ##############################################
diagonal_config = {
    "M1M10": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both"
    },
    "M2H3": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both"
    },

    "H42": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": 0,  "disconnected": 0},
        "default": "both"
    },

    "2T1": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
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
        "mount": "bottom",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },
    "H2": {
        "mount": "bottom",
        "pack_side": "left",
        "count": 4,
        "colors": ["white", "red", "green", "yellow"],
    },
    "H3": {
        "mount": "bottom",
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
        "mount": "top",
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
maneuver_routes = {
    ("H2", "M6"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("H4", "M6"): [
        {"type": "diag", "name": "H42"},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("M2", "H3"): [
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "diag", "name": "M2H3"},
    ],
    ("M2", "H1"): [
        {"type": "segment", "id": ("M2", "H1")},
    ],
    ("M2", "M8"): [
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
    ("M2", "M1"): [
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
        {"type": "segment", "id": ("M8", "M1")},
    ],
    ("H3", "M10"): [
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("H3", "M1"): [
        {"type": "segment", "id": ("H3", "M10")},
        {"type": "diag", "name": "M1M10"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M2", "M10"): [
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "diag", "name": "M2H3"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("M10", "M1"): [
        {"type": "diag", "name": "M1M10"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M1", "M8"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M8", "M1")},
    ],
    ("M8", "M1"): [
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M1", "H1"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M1", "M8")},
        {"type": "segment", "id": ("M8", "H1")},
    ],
    ("M2", "H2"): [
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "diag", "name": "2T1"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6", "M6H2")},
    ],
}

train_routes = {
    ("CH", "M1"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
        {"type": "segment", "id": ("M8", "M1")},
    ],
}


def add_train_route(start, end, steps):
    train_routes[(start, end)] = steps


#########################################        КОНФИГ СТРЕЛОК ДЛЯ МАРШРУТОВ   ##############################################
route_switch_modes = {
    ("H2", "M6"): {"H42":  "left", "2T1":  "left"},
    ("H4", "M6"): {"H42":  "right", "2T1":  "left"},
    ("M2", "H3"): {"M2H3": "right"},
    ("M2", "M10"): {"M2H3": "right"},
    ("H3", "M1"): {"M1M10": "right"},
    ("H3", "M10"): {},
    ("M10", "M1"): {"M1M10": "right"},
    ("M2", "H1"): {"M2H3": "left", "2T1": "left"},
    ("M2", "M8"): {"M2H3": "left"},
    ("M2", "M1"): {"M1M10": "left", "M2H3": "left", "2T1": "left"},
    ("M1", "M8"): {"M1M10": "left"},
    ("M1", "H1"): {"M1M10": "left"},
    ("M2", "H2"): {"2T1": "right", "H42": "left", "M1M10": "right"},
    ("CH", "M1"): {"M2H3": "left", "M1M10": "left"},
}


#########################################        ПОИСК МАРШРУТА                ##############################################
def find_route(start, end):
    def _find(dct):
        key = (start, end)
        if key in dct:
            return dct[key]
        key = (end, start)
        if key in dct:
            return dct[key]
        return None

    if current_mode == "maneuver":
        steps = _find(maneuver_routes)
        if steps is not None:
            return "maneuver", steps
        return None, None

    if current_mode == "train":
        steps = _find(train_routes)
        if steps is not None:
            return "train", steps
        return None, None

    return None, None


#########################################        СПИСОК МАРШРУТОВ (КНОПКИ)       ##############################################
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
    set_mode("maneuver")
    msg = "Маневровые маршруты:\n\n" + format_routes(maneuver_routes)
    showInfo("МАНЕВРОВЫЕ", msg)


def show_train_routes():
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
            cx - r, cy - r, cx + r, cy + r,
            outline="black", width=1, fill=fill_color
        )
        ids.append(oid)

    signal_ids[name] = ids


#########################################        ФУНКЦИИ МАРШРУТОВ                ##############################################
def paint_diagonal(name, color):
    for l in range(len(diag_ids[name])):
        canvas.itemconfig(diag_ids[name][l], fill=color)


def paint_segment(key, color):
    seg_id = segment_ids.get(key)
    if seg_id is None:
        return
    canvas.itemconfig(seg_id, fill=color)


def paint_route(start, end, color="yellow"):
    route_type, steps = find_route(start, end)
    if steps is None:
        print("Маршрут не найден")
        return
    for step in steps:
        if step["type"] == "segment":
            paint_segment(step["id"], color)
        elif step["type"] == "diag":
            paint_diagonal(step["name"], color)


#########################################        ФУНКЦИИ ВКЛ/ОТКЛ СТРЕЛОК               ##############################################
def setBranchRight(nameDiag, offset):
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][0])
    canvas.coords(diag_ids[nameDiag][0], x1, y1 + offset, x2, y2 + offset)
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
    canvas.coords(diag_ids[nameDiag][2], x1, y1 + offset, x2, y2)


def setBranchLeft(nameDiag, offset):
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][1])
    canvas.coords(diag_ids[nameDiag][1], x1, y1 + offset, x2, y2 + offset)
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
    canvas.coords(diag_ids[nameDiag][2], x1, y1, x2, y2 + offset)


def apply_diagonal_mode(nameDiag, mode):
    cfg = diagonal_config.get(nameDiag)
    if cfg is None:
        print(f"No config for {nameDiag}")
        return

    left_cfg = cfg["left"]
    if left_cfg["exists"]:
        if mode in ("left", "both"):
            setBranchLeft(nameDiag, left_cfg["connected"])
        else:
            setBranchLeft(nameDiag, left_cfg["disconnected"])

    right_cfg = cfg["right"]
    if right_cfg["exists"]:
        if mode in ("right", "both"):
            setBranchRight(nameDiag, right_cfg["connected"])
        else:
            setBranchRight(nameDiag, right_cfg["disconnected"])


def get_switch_state_color(name):
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")
    if mode is None:
        return "grey"
    if mode == normal:
        return "green"
    return "yellow"


def update_switch_indicator(name):
    rect = switch_indicator_ids.get(name)
    if rect is None:
        return
    color = get_switch_state_color(name)
    canvas.itemconfig(rect, fill=color)


def set_diagonal_mode(nameDiag, mode):
    apply_diagonal_mode(nameDiag, mode)
    diagonal_modes[nameDiag] = mode
    update_switch_indicator(nameDiag)


#########################################  МИНИ-Таблица стрелок (правый нижний угол)  #########################################
def create_switch_table():
    w = CANVAS_W
    h = CANVAS_H

    dy = 25
    total_height = dy * len(switch_list)
    y_start = h - total_height - 20

    x_text = w - 220
    x_rect = w - 60

    for i, name in enumerate(switch_list, start=1):
        y = y_start + (i - 1) * dy
        canvas.create_text(x_text, y, text=f"{i}. {name}", anchor="w")
        rect = canvas.create_rectangle(
            x_rect - 8, y - 8, x_rect + 8, y + 8,
            outline="black", fill="grey"
        )
        switch_indicator_ids[name] = rect
        update_switch_indicator(name)


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
    l1 = canvas.create_line(x1, y1, x1 - offsetleft, y1, width=4, fill="black")
    l2 = canvas.create_line(x2, y2, x2 + offsetright, y2, width=4, fill="black")
    l3 = canvas.create_line(x1, y1, x2, y2, width=4, fill="black")
    diag_ids[nameDiag] = [l1, l2, l3]


#########################################       ЛИНИИ              ##############################################
for a, b in segments:
    x1, y1 = positions[a]
    x2, y2 = positions[b]
    seg = canvas.create_line(x1 - 5, y1, x2 + 5, y2, width=4, fill="black")
    segment_ids[(a, b)] = seg
    segment_ids[(b, a)] = seg


#########################################        ДИАГОНАЛИ/СТРЕЛКИ               ##############################################
AddDiagonal(260, 330, 350, 430, 20, 38, "M2H3")
AddDiagonal(965, 330, 890, 430, -22, -37, "M1M10")
AddDiagonal(560, 130, 470, 230, -57, -20, "H42")
AddDiagonal(420, 230, 350, 330, -20, -20, "2T1")

set_diagonal_mode("M1M10", "left")
set_diagonal_mode("M2H3", "left")
set_diagonal_mode("H42", "left")
set_diagonal_mode("2T1", "left")


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
            btn_maneuver.config(bg="#4CAF50", fg="white")
            btn_train.config(bg="#D32F2F", fg="white")
        else:
            btn_train.config(bg="#4CAF50", fg="white")
            btn_maneuver.config(bg="#D32F2F", fg="white")

    selected_nodes.clear()
    apply_mode_visuals()


#########################################        ПОДСВЕТКА МАРШРУТОВ               ##############################################
def highlight_possible_targets(start):
    apply_mode_visuals()
    possible = set()

    routes_sources = []
    if current_mode == "maneuver":
        routes_sources = [maneuver_routes]
    elif current_mode == "train":
        routes_sources = [train_routes]

    for d in routes_sources:
        for (a, b) in d.keys():
            if a == start:
                possible.add(b)

    for name, item_id in node_ids.items():
        if name == start:
            canvas.itemconfig(item_id, fill="yellow")
            continue

        if name in possible:
            canvas.itemconfig(item_id, fill="green", state="normal")
        else:
            if current_mode == "maneuver" and name == "CH":
                canvas.itemconfig(item_id, fill="grey", state="disabled")
            else:
                canvas.itemconfig(item_id, fill="grey", state="disabled")

    for name in possible:
        canvas.itemconfig(f"node_{name}", state="normal")


def reset_node_selection():
    selected_nodes.clear()
    apply_mode_visuals()


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


def on_enter(event):
    name = get_node_name_from_event(event)
    if name is None:
        return
    if name not in selected_nodes:
        if current_mode == "maneuver" and name == "CH":
            return
        canvas.itemconfig(node_ids[name], fill="cyan")


def on_leave(event):
    name = get_node_name_from_event(event)
    if name is None:
        return
    if name not in selected_nodes:
        if current_mode == "maneuver" and name == "CH":
            canvas.itemconfig(node_ids[name], fill="grey")
        else:
            canvas.itemconfig(node_ids[name], fill="black")


#########################################        КОНФЛИКТЫ/РЕГИСТРАЦИЯ МАРШРУТОВ ##############################################
def next_route_id():
    global route_counter
    rid = route_counter
    route_counter += 1
    return rid


def check_route_conflict(start, end):
    route_type, steps = find_route(start, end)
    if steps is None:
        return False
    for step in steps:
        if step["type"] == "segment":
            if step["id"] in occupied_segments:
                return True
        elif step["type"] == "diag":
            if step["name"] in occupied_diagonals:
                return True
    return False


def register_route(start, end):
    global route_counter
    route_type, steps = find_route(start, end)
    if steps is None:
        print("Нельзя зарегистрировать: маршрут не найден")
        return None

    rid = route_counter
    route_counter += 1

    for step in steps:
        if step["type"] == "segment":
            a, b = step["id"]
            occupied_segments.add((a, b))
            occupied_segments.add((b, a))
        elif step["type"] == "diag":
            occupied_diagonals.add(step["name"])

    active_routes[rid] = {
        "start": start,
        "end": end,
        "segments": steps,
        "route_type": route_type,
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
            paint_segment((a, b), "green")
            occupied_segments.discard((a, b))
            occupied_segments.discard((b, a))
        elif step["type"] == "diag":
            paint_diagonal(step["name"], "lime")
            occupied_diagonals.discard(step["name"])
    del active_routes[route_id]
    route_counter -= 1


#########################################        МИГАНИЕ МАРШРУТА               ##############################################
def blink_route(start, end, duration_ms=2000, interval_ms=200):
    end_time = time.time() + duration_ms / 1000.0

    def _step(state=True):
        if time.time() >= end_time:
            # после мигания маршрут жёлтый
            paint_route(start, end, "yellow")
            return
        color = "cyan" if state else "green"
        paint_route(start, end, color)
        root.after(interval_ms, _step, not state)

    _step(True)


#########################################        ОБРАБОТКА КЛИКА ПО УЗЛУ          ##############################################
def on_node_click(event):
    name = get_node_name_from_event(event)
    if name is None:
        return
    if current_mode == "maneuver" and name == "CH":
        return

    if name in selected_nodes:
        selected_nodes.remove(name)
        if current_mode == "maneuver" and name == "CH":
            canvas.itemconfig(node_ids[name], fill="grey")
        else:
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
    elif len(selected_nodes) == 2:
        first, second = selected_nodes
        disable_all_except_selected()
        on_two_nodes_selected(first, second)


#########################################        ФУНКЦИЯ ПРИ ВЫБОРЕ ДВУХ ТОЧЕК   ##############################################
def on_two_nodes_selected(a, b):
    global last_switch_check
    print("Выбраны точки маршрута:", a, "->", b)

    if check_route_conflict(a, b):
        print("Маршрут конфликтует с уже установленными!")
        reset_node_selection()
        return

    key = (a, b)
    if key not in route_switch_modes:
        key = (b, a)
    if key not in route_switch_modes:
        print("Для этого маршрута нет настроек стрелок")
        reset_node_selection()
        return

    route_cfg = route_switch_modes[key]
    last_switch_check = {}
    changed = []
    main_diag = None

    if not route_cfg:
        print("Для этого маршрута стрелки не задействованы.")
    else:
        for diag_name, need_mode in route_cfg.items():
            current_mode_diag = diagonal_modes.get(diag_name)
            ok = (current_mode_diag == need_mode)
            last_switch_check[diag_name] = {
                "needed": need_mode,
                "current": current_mode_diag,
                "ok": ok,
            }
            if not ok:
                if main_diag is None:
                    main_diag = diag_name
                set_diagonal_mode(diag_name, need_mode)
                changed.append(f"{diag_name}: {current_mode_diag} -> {need_mode}")

        if main_diag is None and route_cfg:
            main_diag = next(iter(route_cfg.keys()))

    print("Устанавливаем маршрут")
    paint_route(a, b, "cyan")
    blink_route(a, b, duration_ms=2000, interval_ms=200)

    if main_diag is not None:
        blink_switches([main_diag], duration_ms=2000, interval_ms=200)

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

    def finalize():
        rid = register_route(a, b)
        if rid is not None:
            # после регистрации маршрут остаётся жёлтым
            paint_route(a, b, "yellow")
            print("Активные маршруты:", active_routes)

    root.after(2050, finalize)


def snos():
    for active in list(active_routes.keys()):
        release_route(active)
    print(active_routes)


#########################################        ТУПИКИ               ##############################################
drawDeadEnd("pastM1", "right", 0)
drawDeadEnd("past2", "right", 0)
drawDeadEnd("past4", "right", 0)
drawDeadEnd("beforeM6", "left", 0)


#########################################        СТАНЦИИ(КРУГИ/ТЕКСТ)             ##############################################
bannedNames = ["pastM1", "beforeM6", "past2", "1STR", "past4", "M6H2"]
for name, (x, y) in positions.items():
    if name in bannedNames:
        continue
    node = canvas.create_text(x, y - 25, text=name, tags=(f"node_{name}", "node"))
    node_ids[name] = node


#########################################        РИСУЕМ ВСЕ СВЕТОФОРЫ            ##############################################
for name, cfg in signals_config.items():
    drawSignal(name, cfg["mount"], cfg["pack_side"], cfg["count"], cfg.get("colors"))


#########################################        БИНДЫ, КНОПКИ, НАЧАЛЬНАЯ ОКРАСКА ##############################################
canvas.tag_bind("node", "<Button-1>", on_node_click)
canvas.tag_bind("node", "<Enter>", on_enter)
canvas.tag_bind("node", "<Leave>", on_leave)
create_switch_table()

button_reset = tkinter.Button(root, text="Снести", command=snos)
button_reset.place(x=1, y=1)

buttons_y = CANVAS_H - 80

btn_maneuver = tkinter.Button(
    root,
    text="МАНЕВРОВЫЕ",
    font=("Arial", 14, "bold"),
    bg="#4CAF50",
    fg="white",
    width=15,
    height=2,
    command=show_maneuver_routes
)
btn_train = tkinter.Button(
    root,
    text="ПОЕЗДНЫЕ",
    font=("Arial", 14, "bold"),
    bg="#D32F2F",
    fg="white",
    width=15,
    height=2,
    command=show_train_routes
)

center_x = CANVAS_W // 2
offset = 140

btn_maneuver.place(x=center_x - offset - 80, y=buttons_y)
btn_train.place(x=center_x + offset - 80, y=buttons_y)

for id_ in node_ids:
    canvas.itemconfig(node_ids[id_], fill="black")
for id_ in segment_ids:
    canvas.itemconfig(segment_ids[id_], fill="grey")
for id_ in segment_ids:
    paint_segment(id_, "green")
for id_ in diag_ids:
    paint_diagonal(id_, "lime")

set_mode("maneuver")

root.mainloop()
