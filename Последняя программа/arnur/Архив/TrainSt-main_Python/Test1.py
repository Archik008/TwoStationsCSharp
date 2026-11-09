"""
GUI-станция на Tkinter для управления/визуализации маршрутов, стрелок, сегментов и состояний (занято/свободно).
"""

import time
import tkinter as tk
from tkinter import ttk
from tkinter.messagebox import showinfo

import serial
import serial.tools.list_ports
import tkinter
from serial import SerialException
from serial.tools import list_ports  # используется дальше для авто-поиска COM-порта


# ------------------------------- UI: базовое окно и холст ---------------------------------

root = tk.Tk()
root.title("Станция")

CANVAS_W = 1150
CANVAS_H = 600

canvas = tk.Canvas(root, width=CANVAS_W, height=CANVAS_H, bg="white")
canvas.pack()


def showInfo(title: str, msg: str) -> None:
    """Удобная обёртка над messagebox.showinfo (единая точка вызова)."""
    showinfo(title=title, message=msg)


# ---------------------------------- Графика: тупики --------------------------------------

def drawDeadEnd(name: str, direction: str, offset: int) -> None:
    """
    Рисует тупик (упор) в указанной точке.

    name: ключ из positions
    direction: "right" или "left" (куда отрисовать линию тупика)
    offset: длина линии тупика от точки
    """
    # positions должны быть объявлены выше по файлу перед вызовами drawDeadEnd()
    x, y = positions[name]

    if direction == "right":
        canvas.create_line(x, y, x + offset, y, width=4, fill="black")
        canvas.create_line(x + offset, y - 15, x + offset, y + 15, width=6, fill="black")
    elif direction == "left":
        canvas.create_line(x, y, x - offset, y, width=4, fill="black")
        canvas.create_line(x - offset, y - 15, x - offset, y + 15, width=6, fill="black")
    else:
        # Явная защита от опечаток, чтобы не молча ничего не рисовалось
        raise ValueError(f"drawDeadEnd: неизвестное направление '{direction}'")


# --------------------------------- Геометрия: узлы/точки ---------------------------------

positions = {
    # Основные точки
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

    # Поездные цели/пункты (условные)
    "1": (760, 330),
    "2": (760, 230),
    "3": (760, 430),
    "4": (760, 130),

    # Доп. точки
    "H4": (620, 130),
    "past4": (970, 130),
    "CH": (80, 330),
    "past2": (970, 230),
    "pastM1": (1090, 330),
    "beforeM6": (260, 230),
    "M2H1_mid": (260, 330),
    "M2H1_third": (340, 330),
}

# Список “прямых” линий схемы (сегменты).
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

# Порядок битов от Arduino → какой сегмент они “занимают”.
# Важно: смысл битов у тебя инвертируется дальше (1 = нажата = занято).
SEGMENT_ORDER = [
    ("M1", "pastM1"),     # бит 0
    ("M8mid", "M1"),      # бит 1
    ("M8", "H1"),         # бит 2
    ("M2", "M2H1_mid"),   # бит 3
    ("M2", "CH"),         # бит 4
    ("past2", "H2"),      # бит 5
    ("H2", "M6H2"),       # бит 6
    ("past4", "H4"),      # бит 7 (в комментарии не было, но фактически это 7-й индекс)
]


# ---------------------------- Глобальные хранилища состояния UI ---------------------------

selected_nodes = []
MAX_SELECTED = 2

# canvas-id объектов
node_ids = {}          # name -> canvas item id (текст узла)
switch_ids = {}        # name -> canvas item id (строка в таблице стрелок)
segment_ids = {}       # (a,b) -> canvas line id (оба направления на один id)
diag_ids = {}          # name -> [line_id, line_id, line_id] (стрелка = 3 линии)
signal_ids = {}        # name -> [oval ids...] (лампы светофора)

# Для группировки сегментов в “блоки” (если один занят — весь блок красный)
segment_to_block = {}  # (a,b) -> block_name

segment_groups = {
    # Блок "M2—H1" состоит из трёх последовательных сегментов
    "block_M2_H1": [
        ("M2", "M2H1_mid"),
        ("M2H1_mid", "M2H1_third"),
        ("M2H1_third", "H1"),
    ],
    # Блок "M6—H2"
    "block_M6,H2": [
        ("H2", "M6H2"),
        ("M6", "M6H2"),
    ],
    # Блок "M8mid—(M8,M1)"
    "block_M8_M1": [
        ("M8mid", "M8"),
        ("M8mid", "M1"),
    ],
}

# Если у тебя есть “составные” стрелки — тут планировалось разложение на части.
# В текущем коде (в этом чанке) это не используется.
diag_parts = {
    "ALB_Turn4-6": ["ALB_Turn4", "ALB_Turn6"]
}

# Активные маршруты и счётчик ID
active_routes = {}  # route_id -> {"start": a, "end": b, "segments": [...]}
route_counter = 1   # уникальные номера маршрутов

# Занятости (логические)
occupied_segments = set()   # сегменты, занятые активными маршрутами (желтые)
occupied_diagonals = set()  # стрелки, занятые активными маршрутами (желтые)

# Текущее положение каждой стрелки: "left"/"right"/"both" (у тебя используется left/right)
diagonal_modes = {}

# Мини-таблица стрелок (правый нижний угол)
switch_text_ids = {}        # name -> id текста "+" / "-" / "None"
switch_indicator_ids = {}   # name -> id прямоугольника-индикатора
switch_list = ["ALB_Turn1", "ALB_Turn2", "ALB_Turn8", "ALB_Turn4-6"]

# Для миганий
blinking_diags = set()
blinking_routes = set()

# Нормальное (плюсовое) положение стрелок
default_switch_mode = {
    "ALB_Turn1": "left",
    "ALB_Turn2": "left",
    "ALB_Turn8": "left",
    "ALB_Turn4-6": "left",
}

# Результаты последней проверки стрелок для маршрута
last_switch_check = {}

# Текущий режим UI: "maneuver" / "train"
current_mode = "maneuver"
btn_maneuver = None
btn_train = None

# Arduino/Serial (важно: ниже по файлу у тебя будет второй init_arduino — это потенциальный конфликт имени)
arduino = None
arduino_status_label = None

ser = None
last_bits = None

# Если понадобится посылать команды в Arduino при установке маршрута
route_to_arduino_cmd = {
    ("CH", "M1"): "A",
    ("M1", "CH"): "A",
}

# --------------------------- Состояния занятости “по датчикам” ----------------------------
# 1 = свободно
# 0 = занято
# (Инверсия от raw_bit делается позже в apply_bits_to_segments)
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
    "ALB_Turn4-6": 1,
}

# Заполняем segment_to_block для обоих направлений (a,b) и (b,a)
for block, segs in segment_groups.items():
    for seg in segs:
        segment_to_block[seg] = block
        segment_to_block[(seg[1], seg[0])] = block


# -------------------------------- Конфиг “стрелок” (геометрия) ----------------------------

diagonal_config = {
    # connected/disconnected используются ниже для сдвига линий, визуализируя “соединено/разомкнуто”
    "ALB_Turn1": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
    "ALB_Turn2": {
        "left":  {"exists": True, "connected": 0,  "disconnected": 0},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
    "ALB_Turn8": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": 0,  "disconnected": 0},
        "default": "both",
    },
    "ALB_Turn4-6": {
        "left":  {"exists": True, "connected": -5, "disconnected": +5},
        "right": {"exists": True, "connected": -5, "disconnected": +5},
        "default": "both",
    },
}


# -------------------------------- Конфиг светофоров ---------------------------------------

signals_config = {
    # mount: "top"/"bottom" (где стойка)
    # pack_side: "left"/"right" (куда “пачка” ламп)
    "CH": {"mount": "bottom", "pack_side": "right", "count": 5,
           "colors": ["white", "yellow", "red", "green", "yellow"]},
    "M2": {"mount": "bottom", "pack_side": "right", "count": 2,
           "colors": ["blue", "white"]},

    "H1": {"mount": "top", "pack_side": "left", "count": 4,
           "colors": ["white", "red", "green", "yellow"]},
    "H2": {"mount": "top", "pack_side": "left", "count": 4,
           "colors": ["white", "red", "green", "yellow"]},
    "H3": {"mount": "top", "pack_side": "left", "count": 4,
           "colors": ["white", "red", "green", "yellow"]},
    "H4": {"mount": "top", "pack_side": "left", "count": 4,
           "colors": ["white", "red", "green", "yellow"]},

    "M6": {"mount": "bottom", "pack_side": "right", "count": 2,
           "colors": ["red", "white"]},
    "M8": {"mount": "bottom", "pack_side": "right", "count": 2,
           "colors": ["red", "white"]},
    "M10": {"mount": "bottom", "pack_side": "right", "count": 2,
            "colors": ["red", "white"]},
    "M1": {"mount": "top", "pack_side": "left", "count": 2,
           "colors": ["white", "red"]},
}


# ----------------------------------- Маршруты (манёвры) ----------------------------------

# Важно: id сегмента – кортеж, как в segment_ids.
# В каждом шаге маршрута:
# - {"type": "segment", "id": ("A","B")}
# - {"type": "diag", "name": "ALB_TurnX"}
routes = {
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
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4-6"},
        {"type": "segment", "id": ("H2", "M6H2")},
    ],
    ("M2", "H4"): [
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "diag", "name": "ALB_Turn4-6"},
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "diag", "name": "ALB_Turn8"},
    ],
    ("H2", "M6"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("H2", "M2"): [
        {"type": "segment", "id": ("H2", "M6H2")},
        {"type": "diag", "name": "ALB_Turn4-6"},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
    ],
    ("H4", "M6"): [
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "segment", "id": ("M6H2", "M6")},
    ],
    ("H4", "M2"): [
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "diag", "name": "ALB_Turn4-6"},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
    ],
    ("M6", "H4"): [
        {"type": "segment", "id": ("M6H2", "M6")},
        {"type": "diag", "name": "ALB_Turn8"},
        {"type": "segment", "id": ("past4", "H4")},
    ],
    ("M6", "H2"): [
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


# ----------------------------------- Маршруты (поездные) ---------------------------------

train_routes = {
    ("CH", "4"): [
        {"type": "segment", "id": ("CH", "M2")},
        {"type": "segment", "id": ("M2H1_mid", "M2H1_third")},
        {"type": "segment", "id": ("M2", "M2H1_mid")},
        {"type": "diag", "name": "ALB_Turn4-6"},
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
        {"type": "diag", "name": "ALB_Turn4-6"},
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


# -------------------------- Требуемые положения стрелок для маршрутов ---------------------
# Важно: если маршрут задан в обратном направлении, ты ниже проверяешь (a,b) и (b,a).

route_switch_modes = {
    ("H2", "M6"): {"ALB_Turn8": "left", "ALB_Turn4-6": "left"},
    ("H4", "M6"): {"ALB_Turn8": "right", "ALB_Turn4-6": "left"},
    ("M2", "H3"): {"ALB_Turn2": "right"},
    ("M2", "M10"): {"ALB_Turn2": "right"},
    ("H3", "M1"): {"ALB_Turn1": "right"},
    ("H3", "M10"): {},
    ("M10", "M1"): {"ALB_Turn1": "right"},
    ("M2", "H1"): {"ALB_Turn2": "left", "ALB_Turn4-6": "left"},
    ("M2", "M8"): {"ALB_Turn2": "left", "ALB_Turn4-6": "left"},
    ("M2", "M1"): {"ALB_Turn1": "left", "ALB_Turn2": "left", "ALB_Turn4-6": "left"},
    ("M1", "M8"): {"ALB_Turn1": "left"},
    ("M1", "H1"): {"ALB_Turn1": "left"},
    ("M2", "H2"): {"ALB_Turn4-6": "right", "ALB_Turn8": "left", "ALB_Turn2": "left"},
    ("H1", "M8"): {},
    ("CH", "4"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "right"},
    ("CH", "3"): {"ALB_Turn2": "right"},
    ("CH", "2"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "left"},
    ("CH", "1"): {"ALB_Turn4-6": "left", "ALB_Turn2": "left"},
    ("M2", "H4"): {"ALB_Turn2": "left", "ALB_Turn4-6": "right", "ALB_Turn8": "right"},
    ("H2", "M2"): {"ALB_Turn8": "left", "ALB_Turn4-6": "right", "ALB_Turn2": "left"},
}

# ---------------------------------------------------------------------------------------
# ВСПОМОГАТЕЛЬНОЕ: форматирование списка маршрутов для popup-окна
# ---------------------------------------------------------------------------------------


def format_routes(routes_dict):
    """
    Превращает словарь маршрутов вида {(A,B): [steps...], ...} в читаемый список строк:
    A → B

    routes_dict: dict
    return: str
    """
    if not routes_dict:
        return "Маршруты не заданы."

    # seen защищает от дублей, если где-то случайно окажутся повторяющиеся ключи
    seen = set()
    lines = []

    for a, b in routes_dict.keys():
        if (a, b) in seen:
            continue
        seen.add((a, b))
        lines.append(f"{a} \u2192 {b}")  # стрелка →

    return "\n".join(lines)


def show_maneuver_routes():
    """
    Переключает режим на маневровый и показывает список доступных маневровых маршрутов.
    settingRoute=True означает, что сейчас идёт установка маршрута (мигания/переводы стрелок),
    и пользователь не должен менять режим/маршрут-список.
    """
    global settingRoute
    if settingRoute is True:
        return

    set_mode("maneuver")
    msg = "Маневровые маршруты:\n\n" + format_routes(routes)
    showInfo("МАНЕВРОВЫЕ", msg)


def show_train_routes():
    """Аналогично show_maneuver_routes(), но для поездных маршрутов."""
    global settingRoute
    if settingRoute is True:
        return

    set_mode("train")
    msg = "Поездные маршруты:\n\n" + format_routes(train_routes)
    showInfo("ПОЕЗДНЫЕ", msg)


# ---------------------------------------------------------------------------------------
# ОТРИСОВКА СВЕТОФОРОВ
# ---------------------------------------------------------------------------------------

def drawSignal(name, mount="bottom", pack_side="right", count=3, colors=None):
    """
    Рисует светофор в точке positions[name].

    mount:
        "top"    - стойка/лампы рисуются вверх (dy_sign=-1)
        "bottom" - стойка/лампы вниз (dy_sign=+1)

    pack_side:
        "right" - блок ламп вправо
        "left"  - блок ламп влево

    count:
        количество ламп (кружков)

    colors:
        список цветов (по одному на лампу), если None - лампы пустые (без заливки)
    """
    x, y = positions[name]

    r = 4                 # радиус лампы
    gap = 2 * r + 2       # расстояние между лампами
    stand_len = 15        # длина стойки
    bar_len = 10          # длина горизонтальной планки

    # направление “стойки”
    dy_sign = -1 if mount == "top" else 1
    sx, sy = x, y + dy_sign * stand_len

    # стойка
    canvas.create_line(x, y, sx, sy, width=2, fill="black")

    # направление “планки”
    hx_sign = 1 if pack_side == "right" else -1

    hx0, hy0 = sx, sy
    hx1, hy1 = sx + hx_sign * bar_len, sy
    canvas.create_line(hx0, hy0, hx1, hy1, width=2, fill="black")

    ids = []
    start_cx = hx1 + hx_sign * (r + 1)

    for i in range(count):
        cx = start_cx + hx_sign * i * gap
        cy = sy

        # если colors задан — берём цвет по индексу
        fill_color = ""
        if colors is not None and i < len(colors):
            fill_color = colors[i]

        oid = canvas.create_oval(
            cx - r, cy - r,
            cx + r, cy + r,
            outline="black", width=1, fill=fill_color
        )
        ids.append(oid)

    # сохранили oval-id ламп, чтобы потом можно было управлять сигналами
    signal_ids[name] = ids


# ---------------------------------------------------------------------------------------
# ОТРИСОВКА/ПЕРЕКРАСКА МАРШРУТОВ (сегменты + стрелки)
# ---------------------------------------------------------------------------------------

def paint_diagonal(name, color):
    """Перекрашивает все линии, из которых состоит стрелка."""
    # FIX: лишние скобки diag_ids[(name)] не нужны; оставил нормально читаемо
    for line_id in diag_ids[name]:
        canvas.itemconfig(line_id, fill=color)


def paint_segment(key, color):
    """
    Перекрашивает сегмент (линию) по ключу (a,b).
    segment_ids хранит и (a,b) и (b,a) на один и тот же id.
    """
    seg_id = segment_ids.get(key)
    if seg_id is None:
        return
    canvas.itemconfig(seg_id, fill=color)


def paint_route(start, end, color="yellow"):
    """
    Перекрашивает весь маршрут start->end в заданный цвет.
    Если маршрут задан в словаре в обратном направлении — пробует end->start.

    current_mode управляет тем, из какого словаря брать маршрут: routes/train_routes.
    """
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


# ---------------------------------------------------------------------------------------
# ВКЛ/ОТКЛ СТРЕЛОК: физически двигаем линии на Canvas, чтобы показать положение стрелки
# ---------------------------------------------------------------------------------------

def setBranchRight(nameDiag, offset):
    """
    Сдвигает "правую ветку" стрелки на offset (по Y) — для визуализации.
    """
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][0])
    canvas.coords(diag_ids[nameDiag][0], x1, y1 + offset, x2, y2 + offset)

    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
    canvas.coords(diag_ids[nameDiag][2], x1, y1 + offset, x2, y2)


def setBranchLeft(nameDiag, offset):
    """Сдвигает "левую ветку" стрелки на offset (по Y)."""
    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][1])
    canvas.coords(diag_ids[nameDiag][1], x1, y1 + offset, x2, y2 + offset)

    x1, y1, x2, y2 = canvas.coords(diag_ids[nameDiag][2])
    canvas.coords(diag_ids[nameDiag][2], x1, y1, x2, y2 + offset)


def branchWidth(namediag, width):
    """Меняет толщину линий стрелки (3 линии)."""
    for line_id in diag_ids[namediag]:
        canvas.itemconfig(line_id, width=width)


def apply_diagonal_mode(nameDiag, mode):
    """
    Применяет положение стрелки.
    mode: "left" / "right" / "both" (у тебя по факту используются left/right).
    """
    cfg = diagonal_config.get(nameDiag)
    if cfg is None:
        print(f"No config for {nameDiag}")
        return

    # --- левая часть стрелки
    left_cfg = cfg["left"]
    if left_cfg["exists"]:
        if mode in ("left", "both"):
            setBranchLeft(nameDiag, left_cfg["connected"])
            branchWidth(nameDiag, 6)

            # Особая визуализация для ALB_Turn8 (завязка с одним из сегментов)
            # NOTE: segment_ids должен уже быть заполнен перед вызовом этой функции.
            if nameDiag == "ALB_Turn8":
                canvas.itemconfig(segment_ids[("M6H2", "H2")], width=6)
        else:
            setBranchLeft(nameDiag, left_cfg["disconnected"])
            branchWidth(nameDiag, 2)

            if nameDiag == "ALB_Turn2":
                canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=2)
                canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=2)

    # --- правая часть стрелки
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

                # FIX (без смены логики): switch_text_ids["ALB_Turn2"] должен существовать.
                # Он создаётся в create_switch_table(), которая у тебя вызывается до set_diagonal_mode().
                text = canvas.itemcget(switch_text_ids["ALB_Turn2"], "text")
                if text == "-":
                    canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=2)

            if nameDiag == "ALB_Turn1":
                canvas.itemconfig(segment_ids[("M8mid", "M8")], width=6)


# ---------------------------------------------------------------------------------------
# ТЕКУЩЕЕ СОСТОЯНИЕ СТРЕЛКИ: номер (+/-/None) и цвет индикатора
# ---------------------------------------------------------------------------------------

def get_switch_state_num(name):
    """
    Возвращает текст-индикатор:
    - "None" если режим стрелки не определён
    - "+" если в нормальном положении (default_switch_mode)
    - "-" если переведена
    """
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")

    if mode is None:
        return "None"
    return "+" if mode == normal else "-"


def get_switch_state_color(name):
    """
    Возвращает цвет прямоугольника:
    - grey: неизвестно
    - green: в нормальном положении
    - yellow: переведена
    """
    mode = diagonal_modes.get(name)
    normal = default_switch_mode.get(name, "left")

    if mode is None:
        return "grey"
    return "green" if mode == normal else "yellow"


def update_switch_indicator(name):
    """
    Обновляет мини-индикатор стрелки:
    - прямоугольник (цвет)
    - текст (+/-/None)
    """
    rect = switch_indicator_ids.get(name)
    labelSwitch = switch_text_ids.get(name)

    # FIX: если labelSwitch не создан — тоже выходим, иначе itemconfig упадёт.
    if rect is None or labelSwitch is None:
        return

    canvas.itemconfig(rect, fill=get_switch_state_color(name))
    canvas.itemconfig(labelSwitch, text=get_switch_state_num(name))


# ---------------------------------------------------------------------------------------
# МИНИ-ТАБЛИЦА СТРЕЛОК (правый нижний угол): название + индикатор
# ---------------------------------------------------------------------------------------

def create_switch_table():
    """
    Рисует список стрелок справа снизу:
    '1. ALB_Turn1'   [rect][+/−]
    и сохраняет canvas-id для последующих обновлений.
    """
    w = int(canvas["width"])
    h = int(canvas["height"])

    dy = 25
    total_height = dy * len(switch_list)
    y_start = h - total_height - 20

    x_text = w - 220
    x_rect = w - 60

    for i, name in enumerate(switch_list, start=1):
        y = y_start + (i - 1) * dy

        # строка списка (на неё навешан tag "switch" для клика/hover)
        switch = canvas.create_text(
            x_text, y,
            text=f"{i}. {name}",
            anchor="w",
            font=("Bahnschrift SemiBold", 12),
            tags=(f"switch_{name}", "switch"),
        )
        switch_ids[name] = switch

        # текстовый индикатор "+" / "-" / "None"
        label = canvas.create_text(
            x_rect - 30, y + 1,
            text="0",  # начальное значение — будет обновлено ниже
            font=("Bahnschrift SemiBold", 12),
        )

        # прямоугольник-индикатор
        rect = canvas.create_rectangle(
            x_rect - 8, y - 8, x_rect + 8, y + 8,
            outline="black", fill="grey",
        )

        switch_text_ids[name] = label
        switch_indicator_ids[name] = rect

        # сразу обновим на основе diagonal_modes/default_switch_mode
        update_switch_indicator(name)


# Важно: таблица создаётся один раз при старте.
create_switch_table()


def set_diagonal_mode(nameDiag, mode):
    """
    Единая точка установки режима стрелки:
    - применяем визуально (apply_diagonal_mode)
    - сохраняем в diagonal_modes
    - обновляем мини-табличку
    """
    apply_diagonal_mode(nameDiag, mode)
    diagonal_modes[nameDiag] = mode
    update_switch_indicator(nameDiag)


def blink_switches(diags, duration_ms=2000, interval_ms=200):
    """
    Мигает индикаторами стрелок в мини-таблице (прямоугольник становится cyan).
    Используется, чтобы подсветить “главную” стрелку при установке маршрута.
    """
    if not diags:
        return

    end_time = time.time() + duration_ms / 1000.0
    final_colors = {d: get_switch_state_color(d) for d in diags}

    def _step(state=True):
        # завершение мигания
        if time.time() >= end_time:
            for d in diags:
                rect = switch_indicator_ids.get(d)
                if rect is not None:
                    canvas.itemconfig(rect, fill=final_colors[d])
            return

        # шаг мигания
        for d in diags:
            rect = switch_indicator_ids.get(d)
            if rect is not None:
                canvas.itemconfig(rect, fill="cyan" if state else final_colors[d])

        root.after(interval_ms, _step, not state)

    _step(True)


# ---------------------------------------------------------------------------------------
# ГРАФИКА: добавление стрелки (3 линии) и отрисовка сегментов (линий)
# ---------------------------------------------------------------------------------------

def AddDiagonal(x1, y1, x2, y2, offsetleft, offsetright, nameDiag):
    """
    Рисует стрелку как 3 линии:
    l1: “хвост” слева
    l2: “хвост” справа
    l3: “диагональ”
    """
    l1 = canvas.create_line(x1, y1, x1 - offsetleft, y1, width=3, fill="black")
    l2 = canvas.create_line(x2, y2, x2 + offsetright, y2, width=3, fill="black")
    l3 = canvas.create_line(x1, y1, x2, y2, width=3, fill="black")
    diag_ids[nameDiag] = [l1, l2, l3]  # FIX: лишние скобки вокруг ключа убраны


# Сначала рисуем все сегменты (прямые линии между точками)
for a, b in segments:
    x1, y1 = positions[a]
    x2, y2 = positions[b]

    seg = canvas.create_line(x1 - 5, y1, x2 + 5, y2, width=6, fill="black")

    # В обе стороны кладём один и тот же id (удобно красить по (a,b) или (b,a))
    segment_ids[(a, b)] = seg
    segment_ids[(b, a)] = seg


# Потом рисуем стрелки/диагонали
AddDiagonal(260, 330, 350, 430, 20, 38, "ALB_Turn2")
AddDiagonal(965, 330, 890, 430, -22, -37, "ALB_Turn1")
AddDiagonal(560, 130, 470, 230, -57, -20, "ALB_Turn8")
AddDiagonal(420, 230, 350, 330, -30, -30, "ALB_Turn4-6")


# Начальное положение стрелок (нормальное — left)
set_diagonal_mode("ALB_Turn1", "left")
set_diagonal_mode("ALB_Turn2", "left")
set_diagonal_mode("ALB_Turn8", "left")
set_diagonal_mode("ALB_Turn4-6", "left")


# ---------------------------------------------------------------------------------------
# АВТО-СНЯТИЕ МАРШРУТА: если “последний сегмент” маршрута освободился/сработал
# ---------------------------------------------------------------------------------------

def check_if_route_finished(seg, rev):
    """
    Проверяет: является ли текущий сегмент (или его обратный) последним сегментом
    одного из активных маршрутов. Если да — освобождаем маршрут (release_route).

    seg: (a,b)
    rev: (b,a) для удобства
    """
    for rid in list(active_routes.keys()):
        data = active_routes[rid]
        segs = data["segments"]

        # вытаскиваем только сегменты маршрута (без стрелок)
        real_segs = [step["id"] for step in segs if step.get("type") == "segment"]

        # FIX: защита от пустых маршрутов (теоретически возможно при ошибке данных)
        if not real_segs:
            continue

        last_seg = real_segs[-1]

        # если текущий сегмент совпал с последним — маршрут завершён
        if seg == last_seg or rev == last_seg:
            release_route(rid)
            continue

        # если сегмент принадлежит блоку, и блок включает последний сегмент — тоже завершаем
        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s == last_seg or rev == last_seg:
                    release_route(rid)
                    break


def set_arduino_status(connected: bool, text: str = ""):
    """
    Обновляет UI-метку статуса Arduino.
    """
    # NOTE: arduino_status_label должен быть создан до вызовов этой функции
    if connected:
        arduino_status_label.config(text=f"Arduino: {text}", bg="green", fg="black")
    else:
        arduino_status_label.config(text="Arduino: not connected", bg="red", fg="white")


# ---------------------------------------------------------------------------------------
# ОБНОВЛЕНИЕ ОКРАСКИ СЕГМЕНТОВ/СТРЕЛОК по состояниям:
# - seg_occ_train / diag_occ_train (датчики: 0 занято, 1 свободно)
# - occupied_segments / occupied_diagonals (маршруты: желтые)
# ---------------------------------------------------------------------------------------

def update_all_occupancy():
    """
    Периодически (root.after) перерисовывает занятости:
    - датчики занятости (красный)
    - активные маршруты (желтый)
    - свободные (чёрный)
    """

    # 1) если датчик показал “занято” (0) — считаем, что активный маршрут может завершиться,
    #    и снимаем жёлтую занятость у соответствующих сегментов/блоков
    for seg in seg_occ_train:
        rev = (seg[1], seg[0])

        if seg_occ_train.get(seg, 1) == 0:
            occupied_segments.discard(seg)
            occupied_segments.discard(rev)

            check_if_route_finished(seg, rev)

            block = segment_to_block.get(seg)
            if block is None:
                continue

            # снимаем занятость со всех сегментов блока
            for s in segment_groups[block]:
                occupied_segments.discard(s)
                occupied_segments.discard((s[1], s[0]))

    # 2) перерисовываем сегменты
    for (a, b), seg_id in segment_ids.items():
        seg = (a, b)
        block = segment_to_block.get(seg)

        if block:
            # если ЛЮБОЙ сегмент блока занят по датчику -> весь блок красный
            if any(seg_occ_train.get(s, 1) == 0 for s in segment_groups[block]):
                paint_segment(seg, "red")
                continue

            # если блок не красный — но маршрут занял часть блока -> жёлтый
            if any(s in occupied_segments for s in segment_groups[block]):
                paint_segment(seg, "yellow")
                continue

            # FIX (важно): у тебя было paint_segment(s, "black") — там переменная s “утекает”
            # из цикла и красится не тот сегмент. Должно быть seg, а не s.
            paint_segment(seg, "black")
            continue

        # если не блок — обычная логика
        if seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0:
            paint_segment((a, b), "red")
            continue

        if (a, b) in occupied_segments or (b, a) in occupied_segments:
            paint_segment((a, b), "yellow")
            continue

        paint_segment((a, b), "black")

    # 3) перерисовываем стрелки
    for diag_name, _lines in diag_ids.items():
        if diag_occ_train.get(diag_name, 1) == 0:
            paint_diagonal(diag_name, "red")
            continue

        if diag_name in occupied_diagonals:
            paint_diagonal(diag_name, "yellow")
            continue

        paint_diagonal(diag_name, "black")

    # цикл обновления
    root.after(100, update_all_occupancy)



#########################################        ПОДСВЕТКА МАРШРУТОВ               ##############################################
def highlight_possible_targets(start):
    """
    Подсвечивает возможные конечные точки для выбранного старта:
    - start: жёлтый
    - возможные цели: зелёные (и доступны для клика)
    - остальные: серые и disabled

    Работает отдельно для maneuver и train режимов.
    """
    possible = set()

    if current_mode == "maneuver":
        for (a, b) in routes.keys():
            if a == start:
                possible.add(b)

    if current_mode == "train":
        for (a, b) in train_routes.keys():
            if a == start:
                possible.add(b)

    # Пробегаем по всем узлам и выставляем цвет/доступность
    for name, item_id in node_ids.items():
        if name == start:
            canvas.itemconfig(item_id, fill="yellow")
            continue

        if name in possible:
            canvas.itemconfig(item_id, fill="green", state="normal")
        else:
            canvas.itemconfig(item_id, fill="grey", state="disabled")

    # Доп. страховка: делаем возможные цели явно активными по тегу
    for name in possible:
        canvas.itemconfig(f"node_{name}", state="normal")


def reset_node_selection():
    """
    Сбрасывает выбор узлов:
    - все узлы -> чёрные и normal
    - selected_nodes очищается
    """
    for name, item_id in node_ids.items():
        canvas.itemconfig(item_id, fill="black", state="normal")
    selected_nodes.clear()


def disable_all_except_selected():
    """
    Отключает все узлы, кроме уже выбранных (selected_nodes).
    Нужно после выбора двух точек, чтобы пользователь не кликал по лишним.
    """
    for name, item in node_ids.items():
        if name in selected_nodes:
            canvas.itemconfig(item, state="normal")
        else:
            canvas.itemconfig(item, fill="grey", state="disabled")


#########################################        ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ УЗЛОВ      ##############################################
def get_node_name_from_event(event):
    """
    По событию мыши определяет имя узла по тегу "node_<NAME>".
    Возвращает NAME или None.
    """
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
    """
    Аналогично get_node_name_from_event, но для стрелок в мини-таблице:
    тег "switch_<NAME>".
    """
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
    """Hover по узлу: подсветка cyan (если узел не выбран)."""
    name = get_node_name_from_event(event)
    if name is None:
        return
    if name not in selected_nodes:
        canvas.itemconfig(node_ids[name], fill="cyan")


def on_leave(event):
    """
    Hover out:
    - если выбран 1 узел, то возможные цели зелёные (остаются зелёными)
    - иначе возвращаем чёрный
    """
    name = get_node_name_from_event(event)
    if name is None:
        return

    if name not in selected_nodes:
        if len(selected_nodes) == 1:
            canvas.itemconfig(node_ids[name], fill="green")
        else:
            canvas.itemconfig(node_ids[name], fill="black")


def switch_on_enter(event):
    """Hover по строке стрелки в мини-таблице."""
    name = get_switch_name_from_event(event)
    if name is None:
        return
    canvas.itemconfig(switch_ids[name], fill="pink")


def switch_on_leave(event):
    """Hover out по строке стрелки."""
    name = get_switch_name_from_event(event)
    if name is None:
        return
    canvas.itemconfig(switch_ids[name], fill="black")


#########################################        КОНФЛИКТЫ МАРШРУТОВ / ПОСТРОЕНИЕ НОВЫХ ##############################################
def next_route_id():
    """
    Генератор id маршрутов. У тебя он почти не используется,
    потому что register_route сам берёт route_counter.
    Оставляю как есть.
    """
    global route_counter
    rid = route_counter
    route_counter += 1
    return rid


# NOTE: global на уровне модуля не нужен, но не ломает.
global settingRoute
settingRoute = False


def get_route(start, end):
    """
    Возвращает список шагов маршрута (segments/diags) или None.
    ВАЖНО: здесь берём только прямой (start,end). Проверку reverse делаем снаружи.
    """
    if current_mode == "maneuver":
        key = (start, end)
        return routes.get(key)

    if current_mode == "train":
        key = (start, end)
        return train_routes.get(key)

    return None


def has_switch_conflict(a, b):
    """
    Проверяет конфликты стрелок:
    если новый маршрут требует положение стрелки,
    которое противоречит положению, требуемому уже активным маршрутом — конфликт.

    Возвращает True если конфликт есть.
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

        # сравниваем требования по стрелкам
        for diag_name, mode_needed in needed.items():
            # если другая трасса не использует эту стрелку — ок
            if diag_name not in other_needed:
                continue

            other_mode = other_needed[diag_name]

            # если требования разные -> конфликт
            if other_mode != mode_needed:
                print(
                    f"КОНФЛИКТ: стрелка {diag_name} уже занята маршрутом #{rid}, "
                    f"она стоит в положении {other_mode}, "
                    f"а требуется {mode_needed}"
                )
                return True

    return False


def check_route_conflict(start, end):
    """
    Проверяет конфликт построения маршрута:
    - конфликты стрелок (has_switch_conflict)
    - занятые сегменты occupied_segments
    - датчики занятости seg_occ_train (0=занято)
    - занятые стрелки occupied_diagonals
    - датчики занятости diag_occ_train (0=занято)

    """
    if has_switch_conflict(start, end):
        return True

    # берём шаги маршрута с учётом reverse
    steps = get_route(start, end)
    if steps is None:
        steps = get_route(end, start)
    if not steps:
        # маршрута нет -> считаем конфликтом (или можно False, но лучше блокировать)
        return True

    for step in steps:
        if step["type"] == "segment":
            a, b = step["id"]
            if (
                step["id"] in occupied_segments
                or (b, a) in occupied_segments
                or seg_occ_train.get((a, b), 1) == 0
                or seg_occ_train.get((b, a), 1) == 0
            ):
                return True

        elif step["type"] == "diag":
            name = step["name"]
            if name in occupied_diagonals or diag_occ_train.get(name, 1) == 0:
                return True

    return False


def register_route(start, end):
    """
    Регистрирует маршрут как активный:
    - добавляет его сегменты в occupied_segments (в обе стороны)
    - добавляет его стрелки в occupied_diagonals
    - сохраняет в active_routes[rid]

    """
    global route_counter

    rid = route_counter
    route_counter += 1

    if current_mode == "maneuver":
        for step in routes.get((start, end), []):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a, b))
                occupied_segments.add((b, a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {"start": start, "end": end, "segments": routes.get((start, end))}
        return rid

    if current_mode == "train":
        for step in train_routes.get((start, end), []):
            if step["type"] == "segment":
                a, b = step["id"]
                occupied_segments.add((a, b))
                occupied_segments.add((b, a))
            elif step["type"] == "diag":
                occupied_diagonals.add(step["name"])

        active_routes[rid] = {"start": start, "end": end, "segments": train_routes.get((start, end))}
        return rid


def release_route(route_id):
    """
    Снимает маршрут:
    - сегменты/стрелки перекрашивает в black
    - удаляет их из occupied_*
    - удаляет запись из active_routes
    - убирает из combobox

    """
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
    comboboxDelete(route_id)


#########################################        МИГАНИЕ МАРШРУТА               ##############################################
def is_segment_in_blinking_route(seg):
    """
    Проверка: входит ли сегмент в какой-то мигающий маршрут.

    """
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
    """
    Мигает маршрутом (перекраска cyan/black).

    """
    blinking_routes.add((start, end))
    end_time = time.time() + duration_ms / 1000.0

    def _step(state=True):
        if time.time() >= end_time:
            paint_route(start, end, "black")
            blinking_routes.discard((start, end))  # FIX
            return

        color = "cyan" if state else "black"
        paint_route(start, end, color)
        root.after(interval_ms, _step, not state)

    _step(True)


#########################################        ОБРАБОТКА КЛИКА ПО УЗЛУ          ##############################################
def on_node_click(event):
    """
    Логика выбора 2 узлов:
    - клик 1: подсветка и включение возможных целей
    - клик 2: блок остальных и запуск on_two_nodes_selected()
    - повторный клик по выбранному узлу: снимает выбор
    """
    name = get_node_name_from_event(event)
    if name is None:
        return

    # если кликнули по уже выбранному — снимаем
    if name in selected_nodes:
        selected_nodes.remove(name)
        canvas.itemconfig(node_ids[name], fill="black")

        if len(selected_nodes) == 0:
            reset_node_selection()
        elif len(selected_nodes) == 1:
            highlight_possible_targets(selected_nodes[0])

        return

    # не даём выбрать больше 2
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
    """
    Мигает диагональю (стрелкой) на схеме.

    """
    blinking_diags.add(name)
    end_time = time.time() + duration_ms / 1000.0

    def _step(state=True):
        if time.time() >= end_time:
            paint_diagonal(name, "black")
            return

        color = "cyan" if state else "black"
        paint_diagonal(name, color)
        root.after(interval_ms, _step, not state)

    _step(True)


global changingSwitches
changingSwitches = False


def on_switch_mode_selected(name, mode):
    """
    Обработка выбора положения стрелки из контекстного меню:
    mode:
        0 -> "+"
        1 -> "-"

    Блокировки:
    - если сейчас другая стрелка меняется (changingSwitches=True)
    - если стрелка занята по датчику diag_occ_train
    - если стрелка используется в активном маршруте
    - если сейчас идёт установка маршрута (settingRoute=True)
    """
    text = canvas.itemcget(switch_text_ids[name], "text")

    global settingRoute
    global changingSwitches

    if changingSwitches:
        showInfo("Ошибка", "Одна из стрелок меняется!")
        return

    if diag_occ_train.get(name, 1) == 0:
        showInfo("Ошибка", "Стрелка занята!")
        return

    # banned-сегменты: если на маршруте есть эти сегменты, стрелку запрещаем менять.

    ALB_Turn1banned = [("M8mid", "M8"), ("M8", "M8_mid")]
    ALB_Turn8banned = [("M6", "M6H2"), ("H2", "M6H2"), ("M6H2", "M6"), ("M6H2", "H2")]
    ALB_Turn4_6banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2"), ("M6", "M6H2"), ("M6H2", "M6"), ("H2", "M6H2"), ("M6H2", "H2")]
    ALB_Turn2banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2")]

    # проверяем по всем активным маршрутам
    for num in active_routes:
        for step in active_routes[num]["segments"]:
            if name == 'ALB_Turn1':
                if step["type"] == "segment" and step["id"] in ALB_Turn1banned:
                    showInfo("Ошибка", "Стрелка на готовом маршруте!")
                    return

            if name == 'ALB_Turn2':
                if step["type"] == "segment" and step["id"] in ALB_Turn2banned:
                    showInfo("Ошибка", "Стрелка на готовом маршруте!")
                    return

            if name == "ALB_Turn8":
                if step["type"] == "segment" and step["id"] in ALB_Turn8banned:
                    showInfo("Ошибка", "Стрелка на готовом маршруте!")
                    return

            if name == "ALB_Turn4-6":
                if step["type"] == "segment" and step["id"] in ALB_Turn4_6banned:
                    showInfo("Ошибка", "Стрелка на готовом маршруте!")
                    return

            # если сам маршрут использует эту стрелку как diag-step — тоже запрещаем
            if step["type"] == "diag" and step["name"] == name:
                showInfo("Ошибка", "Используемая стрелка!")
                return

    if settingRoute:
        showInfo("Ошибка", "Невозможно сменить стрелку")
        return

    changingSwitches = True
    blink_diag(name, duration_ms=2000, interval_ms=200)

    # дальше у тебя продолжение (finalize и т.д.) — жду следующий чанк
    def finalize():
        """
        Завершение смены стрелки после мигания.
        mode:
          0 -> "+"
          1 -> "-"
        text — текущее отображаемое положение ("+" или "-") из таблицы.
        """
        global changingSwitches

        # mode==0 -> хотим "+"
        if mode == 0 and text != "+":
            # частный визуальный костыль: при переводе Turn2 в нормальное положение
            # делаем один сегмент толще
            if name == "ALB_Turn2":
                canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=6)

            set_diagonal_mode(name, "left")
            changingSwitches = False

        # mode==1 -> хотим "-"
        elif mode == 1 and text != "-":
            set_diagonal_mode(name, "right")
            changingSwitches = False

        else:
            # ничего менять не нужно
            changingSwitches = False
            return

    root.after(2100, finalize)


def on_switch_click(event):
    """
    ПКМ/клик по строке стрелки в мини-таблице: показать меню (+ / -).

    """
    name = get_switch_name_from_event(event)
    if name is None:
        return

    menu = tk.Menu(root, tearoff=0)
    menu.add_command(label="-", command=lambda: on_switch_mode_selected(name, 1))
    menu.add_command(label="+", command=lambda: on_switch_mode_selected(name, 0))

    # tk_popup иногда требует grab_release, чтобы не “залипало”
    try:
        menu.tk_popup(event.x_root, event.y_root)
    finally:
        menu.grab_release()


#########################################        ФУНКЦИЯ ПРИ ВЫБОРЕ ДВУХ ТОЧЕК   ##############################################
def visualSwitch(key):
    """
    Дополнительный “визуальный” хак: для некоторых маршрутов делаем
    сегмент ('H1','M2H1_third') толще.

    """
    route_keys_with_visual = {
        ("M2", "H1"), ("M2", "M8"), ("M2", "M1"),
        ("H1", "M2"), ("M1", "M2"),
    }
    seg_to_bold = ("H1", "M2H1_third")

    if key in route_keys_with_visual:
        canvas.itemconfig(segment_ids[seg_to_bold], width=6)


def on_two_nodes_selected(a, b):
    """
    Основной сценарий:
    1) проверяем конфликты
    2) подгоняем стрелки под маршрут (если нужно)
    3) мигаем маршрутом + стрелкой в таблице
    4) через 2.1с регистрируем маршрут и добавляем в combobox
    """
    global last_switch_check
    global settingRoute

    # 1) Проверка конфликтов по занятым сегментам/стрелкам
    if check_route_conflict(a, b):
        print("Маршрут конфликтует с уже установленными!")
        reset_node_selection()
        return

    if settingRoute:
        reset_node_selection()
        return

    # 2) Ищем настройки стрелок для этого маршрута
    key = (a, b)
    if key not in route_switch_modes:
        key = (b, a)

    if key not in route_switch_modes:
        print("Для этого маршрута нет настроек стрелок")
        reset_node_selection()
        return

    route_cfg = route_switch_modes[key]  # нужные стрелки для ЭТОГО маршрута

    last_switch_check = {}
    changed = []
    main_diag = None  # какая стрелка будет мигать в табличке

    for diag_name, need_mode in route_cfg.items():
        cur_diag_mode = diagonal_modes.get(diag_name)  # FIX: не перекрываем global current_mode
        ok = (cur_diag_mode == need_mode)

        last_switch_check[diag_name] = {
            "needed": need_mode,
            "current": cur_diag_mode,
            "ok": ok,
        }

        if not ok:
            if main_diag is None:
                main_diag = diag_name
            set_diagonal_mode(diag_name, need_mode)
            changed.append(f"{diag_name}: {cur_diag_mode} -> {need_mode}")

    # если ничего не меняли, но стрелки есть — всё равно мигнём одну “главную”
    if main_diag is None and route_cfg:
        main_diag = next(iter(route_cfg.keys()))

    settingRoute = True

    # Визуальная подсветка и мигание
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
        """
        После мигания:
        - регистрируем маршрут
        - красим в yellow
        - снимаем флаг settingRoute
        - добавляем id в combobox

        """
        global settingRoute

        # определяем, в каком направлении реально есть маршрут
        reg_a, reg_b = a, b
        if get_route(a, b) is None and get_route(b, a) is not None:
            reg_a, reg_b = b, a

        rid = register_route(reg_a, reg_b)

        # paint_route сам умеет работать с reverse, оставляем a,b
        paint_route(a, b, "yellow")

        settingRoute = False

        # FIX: combobox лучше хранить строки
        current_values = list(combobox1["values"])
        current_values.append(str(rid))
        combobox1["values"] = tuple(current_values)

    root.after(2100, finalize)


def comboboxDelete(ids):
    """
    Удаляет id маршрута из combobox.

    """
    ids_str = str(ids)
    options = list(combobox1["values"])
    if ids_str in options:
        options.remove(ids_str)
        combobox1["values"] = tuple(options)


def snos():
    """
    Снос выбранного маршрута из combobox.
    """
    selected_item = combobox1.get()
    if selected_item == "":
        return

    num = int(selected_item)
    release_route(num)
    combobox1.set('')


def snosAll():
    """
    Снести все активные маршруты.
    """
    for active in list(active_routes.keys()):
        release_route(active)
    combobox1.set('')


def check():
    """Отладка: печать активных маршрутов."""
    print("Активные маршруты")
    print(active_routes)




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
    node = canvas.create_text(
        x, y - 25, text=name,
        tags=(f"node_{name}", "node"),
        font=("Bahnschrift SemiBold", 12)
    )
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
    """
    Применяет визуальные ограничения режима:
    - в maneuver режиме CH недоступен (серый/disabled)
    - в train режиме всё normal
    """
    for name, item_id in node_ids.items():
        color = "black"
        state = "normal"
        if current_mode == "maneuver" and name == "CH":
            color = "grey"
            state = "disabled"
        canvas.itemconfig(item_id, fill=color, state=state)


def set_mode(mode):
    """
    Переключение режима работы:
    - current_mode обновляется
    - кнопки меняют цвет
    - сбрасываем выбранные узлы и применяем ограничения
    """
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
combobox1 = ttk.Combobox(root, width=25, textvariable=n)
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
    """
    Отладочные кнопки:
    0..12  -> переключаем seg_occ_train по индексу
    13..   -> переключаем diag_occ_train по индексу
    """
    if 0 <= button_id < 13:
        keys = list(seg_occ_train.keys())
        seg = keys[button_id]
        seg_occ_train[seg] = 1 if seg_occ_train[seg] == 0 else 0
    else:
        keys = list(diag_occ_train.keys())
        diag = keys[button_id - 13]
        diag_occ_train[diag] = 1 if diag_occ_train[diag] == 0 else 0


for i in range(17):
    button69 = tkinter.Button(root, text=f"{[i]}", command=lambda id=i: do(id))
    button69.place(x=10, y=40 + i * 25)


#########################################        ARDUINO: ПОИСК ПОРТА И ОПРОС      ##############################################
def find_arduino_port():
    """
    Пытается найти “arduino-подобный” COM порт по описанию.
    Если не найдено — берёт первый доступный.
    """
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
    """
    Подключение к Arduino и подготовка порта.
    global ser — основной объект порта.
    """
    global ser

    if port is None:
        port = find_arduino_port()
        if port is None:
            set_arduino_status(False)
            # FIX: если портов нет, пробуем снова через 2 секунды
            root.after(2000, init_arduino)
            return

    try:
        ser = serial.Serial(port, baudrate, timeout=0)
        time.sleep(2)  # даём плате перезапуститься
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
    """
    Превращает байты в список битов (LSB first).
    bits_needed — сколько бит реально нужно.
    """
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

    # первичная инициализация
    if last_bits is None:
        last_bits = bits[:]
        for idx, (bit_value, seg) in enumerate(zip(bits, SEGMENT_ORDER)):
            occ = 0 if bit_value == 1 else 1
            seg_occ_train[seg] = occ
            print(f"[INIT BTN {idx}] raw_bit={bit_value} segment={seg} -> seg_occ_train={occ}")
        return

    # применяем только изменения
    for idx, (bit_value, seg) in enumerate(zip(bits, SEGMENT_ORDER)):
        old_bit = last_bits[idx]
        if bit_value == old_bit:
            continue

        occ = 0 if bit_value == 1 else 1
        seg_occ_train[seg] = occ
        print(f"[BTN {idx}] raw_bit={bit_value} segment={seg} -> seg_occ_train={occ}")

    last_bits = bits[:]


def poll_arduino():
    """
    Периодический опрос Arduino:
    читаем 1 байт (8 кнопок), раскладываем в биты, применяем к сегментам.
    """
    global ser

    if ser is not None and ser.is_open:
        try:
            data = ser.read(1)  # 1 байт = 8 кнопок
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
init_arduino()  # порт ищется автоматически
update_all_occupancy()
poll_arduino()
root.mainloop()
