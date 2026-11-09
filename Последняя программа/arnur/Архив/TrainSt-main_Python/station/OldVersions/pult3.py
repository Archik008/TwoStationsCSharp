import tkinter
import tkinter as tk
import time
from tkinter.messagebox import showinfo
import serial
import serial.tools.list_ports
from serial import SerialException
from serial.tools import list_ports

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
    "M2H1_mid": (350, 330),
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
    ("H1", "M2H1_mid"),

    ("M10", "H3"),

    ("past4", "H4"),

    ("M6", "beforeM6"),
]

SEGMENT_ORDER = [
    ("M1", "pastM1"),      # бит 0
    ("M8mid", "M1"),       # бит 1
    ("M8", "H1"),          # бит 2
    ("M2", "M2H1_mid"),    # бит 3
    ("M2", "CH"),          # бит 4
    ("past2", "H2"),       # бит 5
    ("H2", "M6H2"),        # бит 6
]

#########################################        МАССИВЫ ЭЛЕМЕНТОВ               ##############################################
selected_nodes = []
MAX_SELECTED = 2
node_ids = {}
segment_ids = {}
diag_ids = {}
signal_ids = {}
segment_to_block = {}

segment_groups = {
    "block_M2_H1": [
        ("M2", "M2H1_mid"),
        ("M2H1_mid", "H1"),
    ],
    "block_M6,H2": [
        ("H2", "M6H2"),
        ("M6", "M6H2"),
    ],
    "block_M8_M1": [
        ("M8mid", "M8"),
        ("M8mid", "M1"),
    ],
}

active_routes = {}  # route_id -> {"start": a, "end": b, "segments": [...]}
route_counter = 1   # уникальные номера маршрутов

occupied_segments = set()
occupied_diagonals = set()
diagonal_modes = {}  # name -> "left"/"right"/"both"/"none"

switch_indicator_ids = {}          # name -> id прямоугольника
switch_list = ["M1M10", "M2H3", "H42", "2T1"]

blinking_routes = set()
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

arduino_status_label = None

ser = None
last_bits = None

route_to_arduino_cmd = {
    ("CH", "M1"): 'A',
    ("M1", "CH"): 'A',
}

seg_occ_train = {
    ("M1", "pastM1"): 1,
    ("M8", "M8mid"): 1,
    ("M1", "M8mid"): 1,
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
    "M1M10": 1,
    "M2H3": 1,
    "H42": 1,
    "2T1": 1,
}

for block, segs in segment_groups.items():
    for seg in segs:
        segment_to_block[seg] = block
        segment_to_block[(seg[1], seg[0])] = block

#########################################        КОНФИГ ДИАГОНАЛЕЙ               ##############################################
diagonal_config = {
    "M1M10": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
    "M2H3": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
    "H42": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": 0,  "disconnected": 0},
        "default": "both",
    },
    "2T1": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
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

#########################################  ЛОГИКА ПОЕЗДНЫХ СВЕТОФОРОВ  #########################################

TRAIN_SIGNALS = {"CH", "H1", "H2", "H3", "H4"}

signal_lamp_roles: dict[str, dict[str, int]] = {}
signal_aspects: dict[str, tuple[set[int], set[int]]] = {}

signal_blink_phase = False
SIGNAL_OFF_COLOR = "#202020"


def init_signal_roles():
    """
    Маппим индексы огней (в signals_config) в роли: красный/зелёный/жёлтые.
    """
    for name in TRAIN_SIGNALS:
        cfg = signals_config.get(name)
        if not cfg:
            continue
        cols = cfg["colors"]
        roles: dict[str, int] = {}

        if "red" in cols:
            roles["red"] = cols.index("red")
        if "green" in cols:
            roles["green"] = cols.index("green")

        ys = [i for i, c in enumerate(cols) if c == "yellow"]
        if ys:
            roles["yellow_low"] = ys[0]
            if len(ys) > 1:
                roles["yellow_high"] = ys[-1]
            else:
                roles["yellow"] = ys[0]

        if "white" in cols:
            roles["white"] = cols.index("white")

        signal_lamp_roles[name] = roles


def find_active_entry_route_from_CH():
    """
    Ищем активный поездной маршрут CH -> 1/2/3/4.
    Возвращает '1', '2', '3', '4' или None.
    """
    for rid, data in active_routes.items():
        a = data["start"]
        b = data["end"]

        if (a, b) not in train_routes and (b, a) not in train_routes:
            continue

        if a == "CH" and b in ("1", "2", "3", "4"):
            return b
        if b == "CH" and a in ("1", "2", "3", "4"):
            return a

    return None


#########################################   ОТПРАВКА БАЙТОВ СИГНАЛОВ В ARDUINO  #########################################

# 0 в бите = лампа ВКЛ, 1 = ВЫКЛ.
# Значения ниже — примеры, которые нужно подогнать под реальную распайку.

# 0 в бите = лампа ВКЛ, 1 = ВЫКЛ
# формат байта для регистра светофора CH такой же, как в тестовом скетче.
# СЮДА ты подставляешь проверенные в Arduino IDE значения.
CH_595_PATTERNS = {
    # один красный (как в твоём тесте с 0b00000111)
    "red":         0b11011111,

    # два жёлтых (пока пример — поставишь свой байт, когда измеришь на железе)
    "two_yellow":  0b10110111,

    # один жёлтый (не мигающий) — тоже пример
    "one_yellow":  0b10111111,

    "green": 0b11101111,



    # всё выключено (если понадобится)
    "off":  0b11111111,
}

MAN_595_PATTERNS = {
    "off":            0b11111111,  # всё выключено
    "H3_M10_white":   0b01111111,  # белый при маршруте H3–M10
}

# какие аспекты должны быть у светофоров при разных маршрутах CH
# ключ None — когда маршрут с CH не установлен
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
# Если на любом из этих участков есть поезд (детектор занятости = 0),
# то CH должен гореть красным, независимо от маршрута.
ROUTE_PROTECT_SEGMENTS_FOR_CH = {
    None: [],
    "1": [("M2", "CH")],  # маршрут CH→1: защищаем участок М2–CH
    "2": [("M2", "CH")],  # CH→2
    "3": [("M2", "CH")],  # CH→3
    "4": [("M2", "CH")],  # CH→4
}

def is_route_occupied_for_CH(dest: str | None) -> bool:
    """
    Проверяет, есть ли поезд на защищаемых для данного направления участках.
    dest = '1'/'2'/'3'/'4' или None.
    Использует seg_occ_train: 0 = занято, 1 = свободно.
    """

    if dest is None:
        return False

    segs = ROUTE_PROTECT_SEGMENTS_FOR_CH.get(dest, [])
    for a, b in segs:
        # проверяем в обе стороны, на всякий случай
        if seg_occ_train.get((a, b), 1) == 0:
            return True
        if seg_occ_train.get((b, a), 1) == 0:
            return True

    return False

ch_route_passed = {
    "1": False,
    "2": False,
    "3": False,
    "4": False,
}



# ================== МАППИНГ АСПЕКТА -> ЛАМПЫ В GUI ==================
def get_gui_lamps_for_aspect(name: str, aspect: str):
    """
    По имени светофора и аспекту возвращаем:
      lit   – индексы ламп, которые горят постоянно
      blink – индексы ламп, которые мигают
    """
    roles = signal_lamp_roles.get(name, {})
    lit = set()
    blink = set()

    if aspect == "red":
        idx = roles.get("red")
        if idx is not None:
            lit.add(idx)

    elif aspect == "yellow":
        idx = roles.get("yellow")
        if idx is not None:
            lit.add(idx)

    elif aspect == "two_yellow":
        low_idx = roles.get("yellow_low") or roles.get("yellow")
        high_idx = roles.get("yellow_high") or low_idx
        if low_idx is not None:
            lit.add(low_idx)
        if high_idx is not None:
            lit.add(high_idx)

    elif aspect == "one_yellow":
        idx = roles.get("yellow_low") or roles.get("yellow")
        if idx is not None:
            lit.add(idx)

    elif aspect == "one_yellow_blink":
        idx = roles.get("yellow_low") or roles.get("yellow")
        if idx is not None:
            lit.add(idx)
            blink.add(idx)

    # "off" или любой неизвестный аспект -> всё погашено
    return lit, blink

def get_current_ch_aspect() -> str:
    """
    Выдаёт текущий аспект для входного светофора CH
    с учётом:
      - выбранного маршрута (ROUTE_SIGNAL_ASPECTS)
      - факта, что поезд уже вошёл на маршрут (ch_route_passed)
      - занятости защищаемых участков (is_route_occupied_for_CH)
    """
    global ch_route_passed

    dest = find_active_entry_route_from_CH()
    route_cfg = ROUTE_SIGNAL_ASPECTS.get(dest, ROUTE_SIGNAL_ASPECTS[None])

    # Если вообще нет активного поездного маршрута из CH → просто то, что в None
    if dest is None:
        return route_cfg.get("CH", "red")

    # Если для этого направления мы следим за памятью
    if dest in ch_route_passed:
        # если сейчас на защищаемых участках есть поезд – запоминаем,
        # что поезд прошёл под светофором
        if is_route_occupied_for_CH(dest):
            ch_route_passed[dest] = True

        # Если поезд уже проходил под этим маршрутом – CH ТОЛЬКО КРАСНЫЙ
        if ch_route_passed[dest]:
            return "red"

    # Иначе – обычная логика из таблицы маршрутов
    return route_cfg.get("CH", "red")


# ================== ВЫБОР БАЙТА ДЛЯ ARDUINO ==================
def build_signal_byte_for_arduino_by_route() -> int:
    """
    Байt для 74HC595, который управляет входным светофором CH.
    Аспект берём через get_current_ch_aspect() -> CH_595_PATTERNS.
    """
    aspect_ch = get_current_ch_aspect()
    return CH_595_PATTERNS.get(aspect_ch, CH_595_PATTERNS["red"])

def build_maneuver_signal_byte() -> int:
    """
    Байt для 2-го 74HC595 (маневровый светофор).
    Пока правило одно:
      – если активен маршрут H3–M10 (или M10–H3) -> белый
      – иначе всё выключено
    """
    if is_maneuver_route_H3_M10_active():
        return MAN_595_PATTERNS["H3_M10_white"]
    else:
        return MAN_595_PATTERNS["off"]

# ================== ПЕРЕСЧЁТ СВЕТОФОРОВ (GUI + ARDUINO) ==================
def recalc_signal_aspects():
    """
    1) Ставим огни в GUI по ROUTE_SIGNAL_ASPECTS + "память" для CH
    2) Отправляем байт в Arduino (входной светофор CH)
    """
    signal_aspects.clear()

    dest = find_active_entry_route_from_CH()
    route_cfg = ROUTE_SIGNAL_ASPECTS.get(dest, ROUTE_SIGNAL_ASPECTS[None])

    # аспект для CH считаем отдельно, с учётом ch_route_passed + занятости
    ch_aspect = get_current_ch_aspect()

    # --- GUI: все TRAIN_SIGNALS по таблице ---
    for name in TRAIN_SIGNALS:
        if name == "CH":
            aspect = ch_aspect
        else:
            aspect = route_cfg.get(name, "red")

        lit, blink = get_gui_lamps_for_aspect(name, aspect)
        signal_aspects[name] = (lit, blink)

    # --- Arduino: строим байты для CH и маневровых светофоров ---
    byte_ch  = build_signal_byte_for_arduino_by_route()
    byte_man = build_maneuver_signal_byte()

    send_signal_bytes(byte_ch, byte_man)

def is_maneuver_route_H3_M10_active() -> bool:
    """
    Проверяем, есть ли активный МАНЕВРОВЫЙ маршрут H3–M10 или M10–H3.
    """
    for rid, data in active_routes.items():
        a = data["start"]
        b = data["end"]

        # это вообще маневровый маршрут?
        if (a, b) not in routes and (b, a) not in routes:
            continue

        # интересует только H3–M10
        if (a == "H3" and b == "M10") or (a == "M10" and b == "H3"):
            return True

    return False




def update_signals_visual():
    """
    Обновляет внешний вид поездных светофоров на Canvas.
    """
    global signal_blink_phase

    recalc_signal_aspects()

    for name in TRAIN_SIGNALS:
        if name not in signal_ids:
            continue

        ids = signal_ids[name]
        cfg_colors = signals_config[name]["colors"]
        lit, blink = signal_aspects.get(name, (set(), set()))

        for idx, oid in enumerate(ids):
            is_lit = idx in lit
            is_blink = idx in blink

            if is_lit:
                if is_blink and not signal_blink_phase:
                    fill = SIGNAL_OFF_COLOR
                else:
                    base = cfg_colors[idx] if idx < len(cfg_colors) else "white"
                    fill = base
            else:
                fill = SIGNAL_OFF_COLOR

            canvas.itemconfig(oid, fill=fill)

    signal_blink_phase = not signal_blink_phase
    root.after(500, update_signals_visual)


#########################################        МАРШРУТЫ                ##############################################
routes = {
    # МАНЕВРОВЫЕ
    ("H2", "M6"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("H4", "M6"): [
        {"type": "diag", "name": "H42"},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("M2", "H3"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "M2H3"},
    ],
    ("M2", "H1"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "H1")},
    ],
    ("M2", "M8"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
    ("M2", "M1"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "H1")},
        {"type": "segment", "id": ("H1", "M8")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8mid", "M1")},
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
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "M2H3"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("M10", "M1"): [
        {"type": "diag", "name": "M1M10"},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M1", "pastM1")},
    ],
    ("M1", "M8"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8mid", "M1")},
    ],
    ("M1", "H1"): [
        {"type": "segment", "id": ("M1", "pastM1")},
        {"type": "segment", "id": ("M8mid", "M8")},
        {"type": "segment", "id": ("M8mid", "M1")},
        {"type": "segment", "id": ("M8", "H1")},
    ],
    ("M2", "H2"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "2T1"},
        {"type": "segment", "id": ("H2", "M6H2")},
    ],
}

train_routes = {
    ("CH", "4"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "2T1"},
        {"type": "diag", "name": "H42"},
        {"type": "segment", "id": ("M8", "M1")},
        {"type": "segment", "id": ("past4", "H4")},
    ],
    ("CH", "3"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "M2H3"},
        {"type": "segment", "id": ("H3", "M10")},
    ],
    ("CH", "2"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "2T1"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("H2", "past2")},
    ],
    ("CH", "1"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("H1", "M2H1_mid")},
        {"type": "segment", "id": ("H1", "M8")},
    ],
}

route_switch_modes = {
    ("H2", "M6"): {"H42": "left", "2T1": "left"},
    ("H4", "M6"): {"H42": "right"},
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
    ("M2", "H2"): {"2T1": "right", "H42": "left"},
    ("H1", "M8"): {},
    ("CH", "4"): {"2T1": "right", "H42": "right"},
    ("CH", "3"): {"M2H3": "right"},
    ("CH", "2"): {"2T1": "right", "H42": "left"},
    ("CH", "1"): {"2T1": "left", "M2H3": "left"},
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
    set_mode("maneuver")
    msg = "Маневровые маршруты:\n\n" + format_routes(routes)
    showInfo("МАНЕВРОВЫЕ", msg)


def show_train_routes():
    set_mode("train")
    msg = "Поездные маршруты:\n\n" + format_routes(train_routes)
    showInfo("ПОЕЗДНЫЕ", msg)


#########################################        ФУНКЦИИ МАРШРУТОВ                ##############################################
def paint_diagonal(name, color):
    for l in range(len(diag_ids[(name)])):
        canvas.itemconfig(diag_ids[name][l], fill=color)


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


def branchWidth(namediag, width):
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
            if nameDiag == "H42":
                canvas.itemconfig(segment_ids[("M6H2", "H2")], width=6)
        else:
            setBranchLeft(nameDiag, left_cfg["disconnected"])
            branchWidth(nameDiag, 2)

    right_cfg = cfg["right"]
    if right_cfg["exists"]:
        if mode in ("right", "both"):
            setBranchRight(nameDiag, right_cfg["connected"])
            branchWidth(nameDiag, 6)
            if nameDiag == "2T1":
                canvas.itemconfig(segment_ids[("H1", "M2H1_mid")], width=2)
                canvas.itemconfig(segment_ids[("M6", "M6H2")], width=2)
            if nameDiag == "M2H3":
                canvas.itemconfig(segment_ids[("H1", "M2H1_mid")], width=2)
            if nameDiag == "M1M10":
                canvas.itemconfig(segment_ids[("M8mid", "M8")], width=2)
            if nameDiag == "H42":
                canvas.itemconfig(segment_ids[("M6H2", "H2")], width=2)
        else:
            setBranchRight(nameDiag, right_cfg["disconnected"])
            branchWidth(nameDiag, 2)
            if nameDiag == "2T1":
                canvas.itemconfig(segment_ids[("H1", "M2H1_mid")], width=6)
                canvas.itemconfig(segment_ids[("M6", "M6H2")], width=6)
            if nameDiag == "M2H3" and nameDiag == "2T1":
                canvas.itemconfig(segment_ids[("H1", "M2H1_mid")], width=6)
                canvas.itemconfig(segment_ids[("M6", "M6H2")], width=6)
            if nameDiag == "M1M10":
                canvas.itemconfig(segment_ids[("M8mid", "M8")], width=6)


def get_switch_state_color(name):
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")
    if mode is None:
        return "grey"
    if mode == normal:
        return "green"
    else:
        return "yellow"


def update_switch_indicator(name):
    rect = switch_indicator_ids.get(name)
    if rect is None:
        return
    color = get_switch_state_color(name)
    canvas.itemconfig(rect, fill=color)


#########################################        ARDUINO: ОТПРАВКА КОМАНД           ##############################################
def send_arduino_cmd(cmd: str):
    """
    Команды для серв/стрелок (A/B/C/D/E и т.д.).
    """
    global ser
    if ser is None or not ser.is_open:
        print("send_arduino_cmd: нет соединения с Arduino")
        return
    try:
        ser.write(cmd.encode("ascii"))
        print(f"--> Arduino: {cmd!r}")
    except SerialException as e:
        print(f"Ошибка отправки в Arduino: {e}")

def send_signal_bytes(byte_ch: int, byte_ped: int = 0xFF):
    """
    Отправка состояний светофоров в Arduino.

    Протокол:
      'L', <byte_ch>, <byte_ped>

    byte_ch  – байт для входного светофора (CH, твой регистр на A1/A2/A3)
    byte_ped – байт для второго регистра (пока можно всегда 0xFF = всё выкл)
    """
    global ser
    print(f"[PY] SIGNAL -> L {byte_ch:08b} {byte_ped:08b}")
    if ser is None or not ser.is_open:
        print("send_signal_bytes: нет соединения с Arduino")
        return
    try:
        ser.write(bytes([ord('L'), byte_ch & 0xFF, byte_ped & 0xFF]))
    except SerialException as e:
        print(f"Ошибка отправки байтов сигналов в Arduino: {e}")


def send_servo_for_switch(nameDiag: str, mode: str):
    """
    nameDiag – 'M1M10', 'M2H3', 'H42', '2T1'
    mode – 'left' или 'right'
    """
    if mode not in ("left", "right"):
        return

    cmd = None

    if nameDiag == "M1M10":
        cmd = 'A' if mode == "left" else 'a'
    elif nameDiag == "H42":
        cmd = 'B' if mode == "left" else 'b'
    elif nameDiag == "2T1":
        cmd = 'C' if mode == "left" else 'c'   # оба сервика внутри Arduino
    elif nameDiag == "M2H3":
        cmd = 'D' if mode == "left" else 'd'

    if cmd is not None:
        send_arduino_cmd(cmd)


def set_diagonal_mode(nameDiag, mode):
    apply_diagonal_mode(nameDiag, mode)
    diagonal_modes[nameDiag] = mode
    update_switch_indicator(nameDiag)
    send_servo_for_switch(nameDiag, mode)


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
    l1 = canvas.create_line(x1, y1, x1 - offsetleft, y1, width=3, fill="black")
    l2 = canvas.create_line(x2, y2, x2 + offsetright, y2, width=3, fill="black")
    l3 = canvas.create_line(x1, y1, x2, y2, width=3, fill="black")
    diag_ids[(nameDiag)] = [l1, l2, l3]


#########################################       ЛИНИИ              ##############################################
for a, b in segments:
    x1, y1 = positions[a]
    x2, y2 = positions[b]
    seg = canvas.create_line(x1 - 5, y1, x2 + 5, y2, width=6, fill="black")
    segment_ids[(a, b)] = seg
    segment_ids[(b, a)] = seg


#########################################        ДИАГОНАЛИ/СТРЕЛКИ               ##############################################
AddDiagonal(260, 330, 350, 430, 20, 38, "M2H3")
AddDiagonal(965, 330, 890, 430, -22, -37, "M1M10")
AddDiagonal(560, 130, 470, 230, -57, -20, "H42")
AddDiagonal(420, 230, 350, 330, -30, -30, "2T1")

set_diagonal_mode("M1M10", "left")
set_diagonal_mode("M2H3", "left")
set_diagonal_mode("H42", "left")
set_diagonal_mode("2T1", "left")


def inblinking(start, end):
    pass


def check_if_route_finished(seg, rev):
    for rid in list(active_routes.keys()):
        data = active_routes[rid]
        segs = data["segments"]
        real_segs = [step["id"] for step in segs if step.get("type") == "segment"]
        if not real_segs:
            continue
        last_seg = real_segs[-1]
        if seg == last_seg or rev == last_seg:
            release_route(rid)
        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s == last_seg or rev == last_seg:
                    release_route(rid)


def set_arduino_status(connected: bool, text: str = ""):
    if connected:
        arduino_status_label.config(text=f"Arduino: {text}", bg="green", fg="black")
    else:
        arduino_status_label.config(text="Arduino: not connected", bg="red", fg="white")


def update_all_occupancy():
    for seg in seg_occ_train:
        rev = (seg[1], seg[0])
        if seg_occ_train.get(seg, 1) == 0:
            occupied_segments.discard(seg)
            occupied_segments.discard(rev)
            check_if_route_finished(seg, rev)
            block = segment_to_block.get(seg)
            if block is None:
                continue
            segs_in_block = segment_groups[block]
            for s in segs_in_block:
                occupied_segments.discard(s)
                occupied_segments.discard((s[1], s[0]))

    for (a, b), seg_id in segment_ids.items():
        seg = (a, b)
        block = segment_to_block.get(seg)
        if block:
            if any(seg_occ_train.get(s, 1) == 0 for s in segment_groups[block]):
                paint_segment(seg, "red")
                continue
            for s in segment_groups[block]:
                if s in occupied_segments:
                    paint_segment(seg, "yellow")
                    break
            else:
                paint_segment(seg, "black")

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
        paint_diagonal(diag_name, "black")

    root.after(10, update_all_occupancy)


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


def on_enter(event):
    name = get_node_name_from_event(event)
    if name is None:
        return

    if name not in selected_nodes:
        canvas.itemconfig(node_ids[name], fill="cyan")


def on_leave(event):
    name = get_node_name_from_event(event)
    if name is None:
        return

    if name not in selected_nodes:
        canvas.itemconfig(node_ids[name], fill="black")


#########################################        КОНФЛИКТЫ МАРШРУТОВ                ##############################################
def next_route_id():
    global route_counter
    rid = route_counter
    route_counter += 1
    return rid


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
    key = (a, b)
    if key not in route_switch_modes:
        key = (b, a)
        if key not in route_switch_modes:
            return False

    needed = route_switch_modes[key]

    for rid, data in active_routes.items():
        other_key = (data["start"], data["end"])
        if other_key not in route_switch_modes:
            other_key = (data["end"], data["start"])
            if other_key not in route_switch_modes:
                continue

        other_needed = route_switch_modes[other_key]

        for diag_name, mode_needed in needed.items():
            if diag_name not in other_needed:
                continue
            other_mode = other_needed[diag_name]
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
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get(
                        (b, a), 1) == 0:
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
                if step["id"] in occupied_segments or seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get(
                        (b, a), 1) == 0:
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

        # Если это поездной маршрут с участием CH – сбрасываем «память» для этого направления
        if start == "CH" and end in ch_route_passed:
            ch_route_passed[end] = False
        if end == "CH" and start in ch_route_passed:
            ch_route_passed[start] = False

        return rid



def release_route(route_id):
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


#########################################        ФУНКЦИЯ ПРИ ВЫБОРЕ ДВУХ ТОЧЕК   ##############################################
def on_two_nodes_selected(a, b):
    global last_switch_check

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
        paint_route(a, b, "yellow")

    root.after(2050, finalize)


def snos():
    for active in list(active_routes.keys()):
        release_route(active)


def check():
    print("Активные маршруты")
    print(active_routes)


#########################################        ТУПИКИ               ##############################################
drawDeadEnd("pastM1", "right", 0)
drawDeadEnd("past2", "right", 0)
drawDeadEnd("past4", "right", 0)
drawDeadEnd("beforeM6", "left", 0)


#########################################        СТАНЦИИ(КРУГИ/ТЕКСТ)             ##############################################
bannedNames = ["pastM1", "beforeM6", "past2", "1STR", "past4", "M6H2", "M2H1_mid", "M8mid"]
for name, (x, y) in positions.items():
    if name in bannedNames:
        continue
    node = canvas.create_text(x, y - 25, text=name, tags=(f"node_{name}", "node"))
    node_ids[name] = node


#########################################        РИСУЕМ ВСЕ СВЕТОФОРЫ            ##############################################
def drawSignal(name, mount="bottom", pack_side="right", count=3, colors=None):
    x, y = positions[name]

    r = 4              # радиус кружочка
    gap = 2 * r + 2    # расстояние между огнями
    stand_len = 15     # длина стойки
    bar_len = 10       # длина горизонтальной перекладины

    # куда ставим светофор — сверху или снизу от пути
    dy_sign = -1 if mount == "top" else 1
    sx, sy = x, y + dy_sign * stand_len

    # стойка
    canvas.create_line(x, y, sx, sy, width=2, fill="black")

    # горизонтальная перекладина
    hx_sign = 1 if pack_side == "right" else -1
    hx0, hy0 = sx, sy
    hx1, hy1 = sx + hx_sign * bar_len, sy
    canvas.create_line(hx0, hy0, hx1, hy1, width=2, fill="black")

    # сами огни
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

    # сохраняем id кружков для дальнейшего управления
    signal_ids[name] = ids

for name, cfg in signals_config.items():
    drawSignal(
        name,
        cfg["mount"],
        cfg["pack_side"],
        cfg["count"],
        cfg.get("colors")
    )

init_signal_roles()
recalc_signal_aspects()
update_signals_visual()


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


#########################################        БИНДЫ И НАЧАЛЬНАЯ ОКРАСКА        ##############################################
canvas.tag_bind("node", "<Button-1>", on_node_click)
canvas.tag_bind("node", "<Enter>", on_enter)
canvas.tag_bind("node", "<Leave>", on_leave)
create_switch_table()

arduino_status_label = tkinter.Label(root, text="Arduino: проверка...", fg="orange")
arduino_status_label.place(x=120, y=3)

button = tkinter.Button(root, text="Снести", command=snos)
button.place(x=1, y=1)
button = tkinter.Button(root, text="Проверка", command=check)
button.place(x=50, y=1)

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


def do():
    if seg_occ_train[("M2", "M2H1_mid")] == 0:
        seg_occ_train[("M2", "M2H1_mid")] = 1
    else:
        seg_occ_train[("M2", "M2H1_mid")] = 0


def do2():
    if seg_occ_train[("M8", "H1")] == 0:
        seg_occ_train[("M8", "H1")] = 1
    else:
        seg_occ_train[("M8", "H1")] = 0


button69 = tkinter.Button(root, text="prost", command=do)
button69.place(x=250, y=2)
button6969 = tkinter.Button(root, text="prost2", command=do2)
button6969.place(x=300, y=2)


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
        time.sleep(2)
        ser.reset_input_buffer()
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
            continue

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
                bits = bytes_to_bits(data, bits_needed=len(SEGMENT_ORDER))
                apply_bits_to_segments(bits)

    root.after(20, poll_arduino)


#########################################        ЗАПУСК ЦИКЛОВ                    ##############################################
init_arduino()
update_all_occupancy()
poll_arduino()
root.mainloop()
