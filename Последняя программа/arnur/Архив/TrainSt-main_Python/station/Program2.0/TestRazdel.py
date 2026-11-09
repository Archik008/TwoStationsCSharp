import tkinter
import tkinter as tk
import serial
from tkinter.messagebox import showinfo
from serial.tools import list_ports
from tkinter import ttk
from configs import *
import ArduinoCode as AC

REG_ORDER = "b2b1b0"  # варианты: "b2b1b0" или "b0b1b2"
BIT_ORDER = "LSB"  # варианты: "MSB" или "LSB"
print("ArduinoCode file:", AC.__file__)
print("Has send_all_off:", hasattr(AC, "send_all_off"))
print("Has send_hw_from_state:", hasattr(AC, "send_hw_from_state"))
print("Has set_link_order:", hasattr(AC, "set_link_order"))
import time
from serial import SerialException
from tkinter import messagebox

rx_buf = bytearray()

print("AC has:", hasattr(AC, "build_hw_frame_from_signals_state"))

root = tk.Tk()
root.title("Станция")
canvas = tk.Canvas(root, width=1250, height=600, bg="white")


def quit_function():
    # response = tkinter.messagebox.askyesno('Exit', 'Are you sure you want to exit?')
    # if response:
    exit()


CANVAS_W = 1200
CANVAS_H = 600
root.resizable(False, False)


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


#########################################        МАССИВЫ ЭЛЕМЕНТОВ               ##############################################
selected_nodes = []
MAX_SELECTED = 2
node_ids = {}
switch_ids = {}
segment_ids = {}
diag_ids = {}
signal_ids = {}
segment_to_block = {}
split_diag_ids = {}
active_routes = {}
route_counter = 1
occupied_segments = set()
occupied_diagonals = set()
diagonal_modes = {}
switch_text_ids = {}
switch_indicator_ids = {}
blinking_diags = set()
blinking_routes = set()
last_switch_check = {}
current_mode = "maneuver"
btn_maneuver = None
btn_train = None
arduino = None
arduino_status_label = None
ser = None
last_bits = None

seg_occ_train = {
    ("M1", "pastM1"): 1,
    ("M8mid", "M8"): 1,
    ("M8mid", "M1"): 1,
    ("M8", "H1"): 1,
    ("M2", "CH"): 1,
    ("past2", "H2"): 1,
    ("M2", "M2H1_mid"): 1,
    ("M2H1_mid", "M2H1_third"): 1,
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

# 3) ТЕКУЩЕЕ СОСТОЯНИЕ СИГНАЛОВ
signals_state = {
    "CH": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": True, "blink": False},
            "yellow1": {"on": False, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "M2": {
        "lamps": {
            "blue": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "H1": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "H2": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "H3": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "H4": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "M6": {
        "lamps": {
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "M8": {
        "lamps": {
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "M10": {
        "lamps": {
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "M1": {
        "lamps": {
            "red": {"on": True, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "ALB_Sect1-2": {
        "lamps": {
            "red": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "yellow": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "ALB_Sect1-2_2": {
        "lamps": {
            "red": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "yellow": {"on": False, "blink": False},
        },
        "manual": False,
    },
    "ALB_Sect2": {
        "lamps": {
            "yellow": {"on": False, "blink": False},
            "green": {"on": False, "blink": False},
            "red": {"on": False, "blink": False},
            "black": {"on": False, "blink": False},
            "white": {"on": False, "blink": False},
        },
        "manual": False,
    },

}

SIGNAL_OFF_COLOR = "#202020"
signal_blink_phase = False
DEBUG_SIGNALS_FRAME = False


def _indices_for_color(sig_name: str, color: str) -> list[int]:
    cols = signals_config[sig_name]["colors"]
    return [i for i, c in enumerate(cols) if c == color]


def gui_lamps_for_aspect(sig_name: str, aspect: str) -> tuple[set[int], set[int]]:
    """
    Возвращает:
      lit   = какие лампы должны светиться (индексы)
      blink = какие лампы должны мигать (индексы)
    """

    lit: set[int] = set()
    blink: set[int] = set()

    reds = _indices_for_color(sig_name, "red")
    greens = _indices_for_color(sig_name, "green")
    whites = _indices_for_color(sig_name, "white")
    blues = _indices_for_color(sig_name, "blue")
    yellows = _indices_for_color(sig_name, "yellow")

    if aspect == "off":
        return set(), set()

    if aspect == "red" and reds:
        lit.add(reds[0])
    elif aspect == "green" and greens:
        lit.add(greens[0])
    elif aspect == "white" and whites:
        lit.add(whites[0])
    elif aspect == "blue" and blues:
        lit.add(blues[0])
    elif aspect == "one_yellow" and yellows:
        lit.add(yellows[0])
    elif aspect == "two_yellow" and yellows:
        # если жёлтых две (как у CH), зажигаем обе; если одна — зажжётся одна
        for i in yellows:
            lit.add(i)
    elif aspect == "invite":
        # пригласительный: красный постоянный + белый мигает (если белый есть)
        if reds:
            lit.add(reds[0])
        if whites:
            lit.add(whites[0])
            blink.add(whites[0])

    return lit, blink


def gui_lamps_from_state(sig_name: str) -> tuple[set[int], set[int]]:
    lit: set[int] = set()
    blink: set[int] = set()

    st = signals_state.get(sig_name)
    if not st:
        return lit, blink

    lamps = st.get("lamps", {})

    # индексы физических ламп и их цвета
    lamp_indices = {
        color: _indices_for_color(sig_name, color)
        for color in lamps.keys()
    }

    for color, cfg in lamps.items():
        idxs = lamp_indices.get(color, [])
        if not idxs:
            continue

        # если включена — все лампы этого цвета зажигаем
        if cfg.get("on", False):
            for i in idxs:
                lit.add(i)

                # если мигает — добавляем в blink
                if cfg.get("blink", False):
                    blink.add(i)

    return lit, blink


def recalc_signals_to_red(rid) -> None:
    AdditionalSignals = ["ALB_Sect1-2", "ALB_Sect1-2_2", "ALB_Sect2"]
    # включаем красный по умолчанию
    if rid != None:
        data = active_routes.get(rid)
        a = data.get("start")
        b = data.get("end")
        key = (a, b)
        # if key not in ROUTE_SIGNAL_MAP and (b, a) in ROUTE_SIGNAL_MAP:
        # print("Нет маршрута")
        # return
        cfg = ROUTE_SIGNAL_MAP.get(key)
        for name in cfg:
            if name in AdditionalSignals:
                continue

            for lamp in signals_state[name]["lamps"].values():
                lamp["on"] = False
                lamp["blink"] = False
            if "red" in signals_state[name]["lamps"]:
                signals_state[name]["lamps"]["red"]["on"] = True
            elif "blue" in signals_state[name]["lamps"]:
                signals_state[name]["lamps"]["blue"]["on"] = True
            else:
                for colors in signals_state[name]["lamps"]:
                    signals_state[name]["lamps"][colors]["on"] = False
    else:
        for name in signals_state.keys():
            if name in AdditionalSignals:
                continue
            for lamp in signals_state[name]["lamps"].values():
                lamp["on"] = False
                lamp["blink"] = False
            if "red" in signals_state[name]["lamps"]:
                signals_state[name]["lamps"]["red"]["on"] = True
            elif "blue" in signals_state[name]["lamps"]:
                signals_state[name]["lamps"]["blue"]["on"] = True
            else:
                for colors in signals_state[name]["lamps"]:
                    signals_state[name]["lamps"][colors]["on"] = False


def recompute_all_signals():
    """
    Полный пересчёт:
      1) сбросить всё в дефолт (красные/синие)
      2) применить ВСЕ активные маршруты заново
    """
    # 1) сброс дефолта для всех
    recalc_signals_to_red(None)

    # 2) применяем каждый активный маршрут
    for rid, data in active_routes.items():
        a = data.get("start")
        b = data.get("end")
        if not a or not b:
            continue
        recalc_signals_from_active_routes((a, b))


def recalc_signals_from_active_routes(route):
    """
       # 2) применить активные маршруты
    for rid, data in active_routes.items():
        a = data.get("start")
        b = data.get("end")
        if not a or not b:
            continue
    """
    key = route
    # if key not in ROUTE_SIGNAL_MAP and (b, a) in ROUTE_SIGNAL_MAP:
    # print("recalc_signals_from_active_routes")

    cfg = ROUTE_SIGNAL_MAP.get(key)
    # if not cfg:
    # continue

    for name in cfg:

        if name in signals_state:
            for lamp in signals_state[name]["lamps"].values():
                lamp["on"] = False
                lamp["blink"] = False

        for color, lamp_cfg in cfg[name]["lamps"].items():
            if color not in signals_state[name]["lamps"]:
                continue
            signals_state[name]["lamps"][color]["on"] = lamp_cfg.get("on", False)
            signals_state[name]["lamps"][color]["blink"] = lamp_cfg.get("blink", False)


def reset_signals_to_default() -> None:
    AdditionalSignals = ["ALB_Sect1-2", "ALB_Sect1-2_2", "ALB_Sect2"]

    for name in signals_state.keys():
        if name in AdditionalSignals:
            continue

        # всё выключить
        for lamp in signals_state[name]["lamps"].values():
            lamp["on"] = False
            lamp["blink"] = False

        # дефолт: красный (или синий, если красного нет)
        if "red" in signals_state[name]["lamps"]:
            signals_state[name]["lamps"]["red"]["on"] = True
        elif "blue" in signals_state[name]["lamps"]:
            signals_state[name]["lamps"]["blue"]["on"] = True
        # иначе остаётся всё выключено


def apply_route_signals_by_key(key: tuple[str, str]) -> None:
    cfg = ROUTE_SIGNAL_MAP.get(key)
    if cfg is None:
        cfg = ROUTE_SIGNAL_MAP.get((key[1], key[0]))
    if cfg is None:
        return

    for sig_name in cfg:
        if sig_name not in signals_state:
            continue

        # сбросить этот светофор перед применением
        for lamp in signals_state[sig_name]["lamps"].values():
            lamp["on"] = False
            lamp["blink"] = False

        for color, lamp_cfg in cfg[sig_name]["lamps"].items():
            if color not in signals_state[sig_name]["lamps"]:
                continue
            signals_state[sig_name]["lamps"][color]["on"] = bool(lamp_cfg.get("on", False))
            signals_state[sig_name]["lamps"][color]["blink"] = bool(lamp_cfg.get("blink", False))


def recompute_signals_from_all_active_routes() -> None:
    reset_signals_to_default()
    for rid, data in active_routes.items():
        a = data.get("start")
        b = data.get("end")
        if not a or not b:
            continue
        apply_route_signals_by_key((a, b))


def update_signals_visual_v2() -> None:
    global signal_blink_phase

    # 2) (опционально) собрать байты в порядке signals_config — для отладки/будущей отправки в Arduino
    try:
        AC.send_hw_from_state(signals_state, blink_phase=signal_blink_phase, debug=True)
    except Exception as e:
        print("[HW] send error:", e)
    # 3) покрасить все светофорыа
    for name in signals_config.keys():
        if name not in signal_ids:
            continue

        ids = signal_ids[name]
        cfg_colors = signals_config[name]["colors"]

        st = signals_state.get(name, {"aspect": "off", "blink": False})
        aspect = st.get("aspect", "off")
        blink_all = bool(st.get("blink", False))

        lit, blink = gui_lamps_from_state(name)

        for idx, oid in enumerate(ids):
            is_lit = idx in lit
            is_blink = (idx in blink) or (blink_all and is_lit)

            if is_lit:
                # если лампа мигает — гасим её через фазу
                if is_blink and (not signal_blink_phase):
                    fill = SIGNAL_OFF_COLOR
                else:
                    fill = cfg_colors[idx] if idx < len(cfg_colors) else "white"
            else:
                fill = SIGNAL_OFF_COLOR

            canvas.itemconfig(oid, fill=fill)

    signal_blink_phase = not signal_blink_phase
    if signals_state["CH"]["lamps"]["red"]["on"] == True:
        signals_state["ALB_Sect1-2_2"]["lamps"]["yellow"]["on"] = True
        signals_state["ALB_Sect1-2_2"]["lamps"]["green"]["on"] = False
    if signals_state["CH"]["lamps"]["yellow"]["on"] == True or signals_state["CH"]["lamps"]["yellow1"]["on"] == True:
        signals_state["ALB_Sect1-2_2"]["lamps"]["yellow"]["on"] = False
        signals_state["ALB_Sect1-2_2"]["lamps"]["green"]["on"] = True

    root.after(350, update_signals_visual_v2)


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


def make_signal_state(name, colors):
    signals_state[name] = {
        "lamps": {
            color: {"on": False, "blink": False}
            for color in colors
        }
    }


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

    make_signal_state(name, colors)
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
        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][0])  # 1
        canvas.coords(split_diag_ids[nameDiag]['partA'][0], x1, y1 + offset, x2, y2 + offset)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partA'][1])  # 3
        canvas.coords(split_diag_ids[nameDiag]['partA'][1], x1, y1 + offset, x2, y2)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][0])  # 2
        canvas.coords(split_diag_ids[nameDiag]['partB'][0], x1, y1, x2, y2 - offset - 1)

        x1, y1, x2, y2 = canvas.coords(split_diag_ids[nameDiag]['partB'][1])  # 4
        canvas.coords(split_diag_ids[nameDiag]['partB'][1], x1, y1 - offset - 1, x2, y2 - offset - 1)
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
                # canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=2)

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
        return "green"  # плюс, нормальное положение
    else:
        return "yellow"  # переведена


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
        switch = canvas.create_text(x_text, y, text=f"{i}. {name}", anchor="w", font=("Bahnschrift SemiBold", 12),
                                    tags=(f"switch_{name}", "switch"))
        switch_ids[name] = switch
        label = canvas.create_text(x_rect - 30, y + 1, text="0", font=("Bahnschrift SemiBold", 12))

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
                     x3, y3, offset_left,
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
AddSplitDiagonal(430, 230, 390, 280, 350, 330, -30, -30, "ALB_Turn4-6", "ALB_Turn4", "ALB_Turn6")

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


part_to_split = {}

for split_name in split_parts_map:
    for part, logic_name in split_parts_map[split_name].items():
        part_to_split[logic_name] = (split_name, part)


def update_all_occupancy():
    for seg in seg_occ_train:
        rev = (seg[1], seg[0])
        if seg_occ_train.get(seg, 1) == 0:
            occupied_segments.discard(seg)
            signal_segment = segment_to_signal.get(seg)
            if signal_segment == None:
                signal_segment = segment_to_signal.get(rev)
            if signal_segment in signals_state:
                for colors in signals_state[signal_segment]["lamps"]:
                    if colors == "red":
                        signals_state[signal_segment]["lamps"][colors]["on"] = True
                    elif colors == "blue":
                        signals_state[signal_segment]["lamps"][colors]["on"] = True
                    else:
                        signals_state[signal_segment]["lamps"][colors]["on"] = False
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
            signal_diag = diag_to_signal.get(diag)
            if signal_diag in signals_state:
                for colors in signals_state[signal_diag]["lamps"]:
                    if colors == "red":
                        signals_state[signal_diag]["lamps"][colors]["on"] = True
                    elif colors == "blue":
                        signals_state[signal_diag]["lamps"][colors]["on"] = True
                    else:
                        signals_state[signal_diag]["lamps"][colors]["on"] = False

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

        if seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0:
            paint_segment((a, b), "red")
            continue
        if (a, b) in occupied_segments or (b, a) in occupied_segments:
            paint_segment((a, b), "yellow")
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
        for step in routes.get((start, end)):
            if step["type"] == "segment":
                a, b = step["id"]
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a),
                                                                                                             1) == 0:
                    return True
            elif step["type"] == "diag":
                if step["name"] in occupied_diagonals or diag_occ_train.get(step["name"], 1) == 0:
                    return True
        return False
    if current_mode == "train":
        if has_switch_conflict(start, end):
            return True
        for step in train_routes.get((start, end)):
            if step["type"] == "segment":
                a, b = step["id"]
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a),
                                                                                                             1) == 0:
                    return True
            elif step["type"] == "diag":
                if step["name"] in occupied_diagonals or diag_occ_train.get(step["name"], 1) == 0:
                    return True
        return False


def register_route(start, end):
    global route_counter
    rid = route_counter
    route_counter += 1
    if current_mode == "maneuver":
        for step in routes.get((start, end)):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a, b))
                occupied_segments.add((b, a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {
            "start": start,
            "end": end,
            "segments": routes.get((start, end)),
        }
        return rid
    if current_mode == "train":
        for step in train_routes.get((start, end)):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a, b))
                occupied_segments.add((b, a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {
            "start": start,
            "end": end,
            "segments": train_routes.get((start, end)),
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
            paint_segment((a, b), "black")

            occupied_segments.discard((a, b))
            occupied_segments.discard((b, a))
        elif step["type"] == "diag":
            paint_diagonal(step["name"], "black")
            occupied_diagonals.discard(step["name"])
    del active_routes[route_id]
    recompute_all_signals()
    recompute_signals_from_all_active_routes()
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
                if step["id"] == (a, b) or step["id"] == (b, a):
                    return True
    return False


def blink_route(start, end, duration_ms=2000, interval_ms=200):
    blinking_routes.add((start, end))
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


def on_switch_mode_selected(name, mode):
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
    ALB_Turn8banned = [("M6", "M6H2"), ("H2", "M6H2"), ("M6H2", "M6"), ("M6H2", "H2")]
    ALB_Turn4_6banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2"), ("M6", "M6H2"), ("M6H2", "M6"), ("H2", "M6H2"),
                         ("M6H2", "H2")]
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

    route_cfg = route_switch_modes[key]  # только нужные стрелки для ЭТОГО маршрута
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
        recompute_signals_from_all_active_routes()

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


def init_arduino():
    # Поиск порта и подключение к Arduino.

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

        arduino = serial.Serial(arduino_port, 9600, timeout=0)  # <-- 115200 как в Arduino
        time.sleep(2)

        global ser
        ser = arduino  # <-- вот это главное: ser теперь реальный порт
        AC.ser = ser
        AC.set_link_order(REG_ORDER, BIT_ORDER)

        ser.write(b'A')  # должна дернуться стрелка M1M10 (по твоему handleCommand)

        print(f"Arduino подключен к {arduino_port}")
        if arduino_status_label is not None:
            arduino_status_label.config(text=f"Arduino: {arduino_port}", fg="green")

    except Exception as e:
        print("Не удалось подключиться к Arduino:", e)
        arduino = None
        if arduino_status_label is not None:
            arduino_status_label.config(text="Arduino: нет соединения", fg="red")


def set_arduino_status(connected: bool, text: str = ""):
    if connected:
        arduino_status_label.config(text=f"Arduino: {text}", bg="green", fg="black")
    else:
        arduino_status_label.config(text="Arduino: not connected", bg="red", fg="white")


def poll_arduino():
    global ser, rx_buf
    if ser is not None and ser.is_open:
        try:
            rx_buf += ser.read(64)
        except SerialException as e:
            print(f"Ошибка чтения из Arduino: {e}")
            ser = None
            set_arduino_status(False)
            root.after(2000, init_arduino)
            return

        while True:
            if len(rx_buf) < 1:
                break

            tag = rx_buf[0]

            # Кнопки: K + 1 байт
            if tag == ord('K'):
                if len(rx_buf) < 2:
                    break
                btn = rx_buf[1]
                del rx_buf[:2]

                bits = bytes_to_bits(bytes([btn]), bits_needed=len(SEGMENT_ORDER))
                apply_bits_to_segments(bits)
                continue

            # ACK: O + 3 байта
            if tag == ord('O'):
                if len(rx_buf) < 4:
                    break
                ack = bytes(rx_buf[:4])
                del rx_buf[:4]
                # print("ACK:", ack)  # если надо
                continue

            # fallback: старый режим "сырой 1 байт кнопок" (если нет K-префикса)
            # Если байт не похож на буквы протокола — считаем кнопками.
            if tag not in (ord('K'), ord('O'), ord('L')):
                btn = rx_buf[0]
                del rx_buf[:1]
                bits = bytes_to_bits(bytes([btn]), bits_needed=len(SEGMENT_ORDER))
                apply_bits_to_segments(bits)
                continue

            # мусор/текст/остатки — выкинуть 1 байт
            del rx_buf[:1]

    root.after(20, poll_arduino)


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


def apply_bits_to_segments(bits: list[int]) -> None:
    """
    Применяем биты от Arduino к занятости.
    Предположение: bit=1 значит "занято" (у тебя занято = 0), bit=0 значит "свободно" (1).
    Если окажется наоборот — просто поменяй местами 0 и 1 в присваивании.
    """
    for i, bit in enumerate(bits):
        if i >= len(SEGMENT_ORDER):
            break

        item = SEGMENT_ORDER[i]

        # SEGMENT_ORDER должен содержать либо tuple(a,b) для сегментов, либо str для диагоналей
        if isinstance(item, tuple) and len(item) == 2:
            seg_occ_train[item] = 0 if bit == 1 else 1
        else:
            diag_occ_train[item] = 0 if bit == 1 else 1


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


"""
def init_arduino(port=None, baudrate=9600):
    global ser
    if port is None:
        port = find_arduino_port()
        if port is None:
            #set_arduino_status(False)

            return

    try:
        ser = serial.Serial(port, baudrate, timeout=0)
        time.sleep(2)             # даём плате перезапуститься
        ser.reset_input_buffer()  # чистим мусор
        print(f"Arduino подключено на {port}")
        #set_arduino_status(True, text=port)
    except SerialException as e:
        ser = None
        #set_arduino_status(False)
        print(f"Не удалось открыть {port}: {e}")
        print("Попробую переподключиться через 2 секунды.")
"""

#########################################        ТУПИКИ               ##############################################
drawDeadEnd("pastM1", "right", 0)
drawDeadEnd("past2", "right", 0)
drawDeadEnd("past4", "right", 0)
drawDeadEnd("beforeM6", "left", 0)

#########################################        СТАНЦИИ(КРУГИ/ТЕКСТ)             ##############################################
bannedNames = ["pastM1", "beforeM6", "past2", "1STR", "past4", "M6H2", "M2H1_mid",
               "M8mid", "M2H1_third", "ALB_Sect1-2", "ALB_Sect1", "ALB_Sect2",
               "ALB_Sect0", "ALB_Sect1-2_2"]
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
combobox1 = ttk.Combobox(root, width=25, textvariable=n, state='readonly')
combobox1.place(x=510, y=20)

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
        seg = keys[button_id - 13]
        diag_occ_train[seg] = 1 if diag_occ_train[seg] == 0 else 0


for i in range(18):
    button69 = tkinter.Button(root, text=f"{[i]}", command=lambda id=i: do(id))
    button69.place(x=1220, y=40 + i * 25)

#########################################        ЗАПУСК ЦИКЛОВ                    ##############################################         # порт ищется автоматически
init_arduino()
REG_ORDER = "b2b1b0"  # или "b0b1b2"
BIT_ORDER = "LSB"  # или "LSB"
AC.send_all_off()  # должно всё выключить
time.sleep(0.3)
poll_arduino()
update_all_occupancy()
update_signals_visual_v2()  # новое (все сигналы)
recalc_signals_to_red(None)
root.protocol('WM_DELETE_WINDOW', quit_function)
canvas.pack()
root.mainloop()
# ТЕСТ: включим, например, H1 зелёный и CH красный
signals_state["H1"]["lamps"]["green"]["on"] = True
signals_state["CH"]["lamps"]["red"]["on"] = True
