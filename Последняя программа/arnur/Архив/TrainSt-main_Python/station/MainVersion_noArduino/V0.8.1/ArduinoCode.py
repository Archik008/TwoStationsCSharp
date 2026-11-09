# ArduinoCode.py
from __future__ import annotations
from typing import Dict, Tuple, Optional, List, Any
import serial
import time
from configs import SEGMENT_ORDER, segment_groups, segment_to_block

# ---------------------------------------------------------------------
# SERIAL handle
# ---------------------------------------------------------------------
ser = None  # Ожидается объект serial.Serial из основного скрипта

# ---------------------------------------------------------------------
# ЖЕЛЕЗНАЯ КАРТА СВЕТОФОРОВ
# Формат: ("Имя_Светофора", "Цвет"): (Индекс_Байта, Индекс_Бита)
# Индексы байтов: 0 (reg5), 1 (reg4), 2 (reg3), 3 (reg2), 4 (reg1)
# ---------------------------------------------------------------------
HW_MAP_40: Dict[Tuple[str, str], Tuple[int, int]] = {
    # Первый байт (reg5)
    ("Ч", "yellow"): (0, 0),  # Верхний
    ("Ч", "green"): (0, 1),
    ("Ч", "red"): (0, 2),
    ("Ч", "yellow1"): (0, 3),  # Нижний
    ("Ч", "white"): (0, 4),
    ("M2", "white"): (0, 5),
    ("M2", "blue"): (0, 6),
    ("H1", "yellow"): (0, 7),

    # Второй байт (reg4)
    ("H1", "green"): (1, 0),
    ("H1", "red"): (1, 1),
    ("H1", "white"): (1, 2),
    ("M8", "white"): (1, 3),
    ("M8", "red"): (1, 4),
    ("M1", "white"): (1, 5),
    ("M1", "red"): (1, 6),

    # Третий байт (reg3)
    ("H2", "yellow"): (2, 1),
    ("H2", "green"): (2, 2),
    ("H2", "red"): (2, 3),
    ("H2", "white"): (2, 4),
    ("H3", "yellow"): (2, 5),
    ("H3", "green"): (2, 6),
    ("H3", "red"): (2, 7),

    # Четвертый байт (reg2)
    ("H3", "white"): (3, 0),
    ("M10", "red"): (4, 4),
    ("M10", "white"): (4, 5),
    ("H4", "yellow"): (3, 3),
    ("H4", "green"): (3, 4),
    ("H4", "red"): (3, 5),
    ("H4", "white"): (3, 6),
    ("ALB_Sect1-2_2", "red"): (3, 7),


    # Пятый байт (reg1)
    ("ALB_Sect1-2_2", "yellow"): (4, 0),
    ("ALB_Sect1-2_2", "green"): (4, 1),
    ("M6", "red"): (4, 2),
    ("M6", "white"): (4, 3),
}


# ---------------------------------------------------------------------
# ПРИЕМ ДАННЫХ ОТ ARDUINO (Датчики занятости)
# ---------------------------------------------------------------------
def parse_arduino_string(line, seg_occ_dict, diag_occ_dict):
    if "Data: " not in line:
        return

    raw_bin = line.replace("Data: ", "").strip()
    bin_str = raw_bin.zfill(24)[::-1]

    for idx, char in enumerate(bin_str):
        if idx >= len(SEGMENT_ORDER):
            break

        seg = SEGMENT_ORDER[idx]
        if seg == "EMPTY" or not isinstance(seg, tuple):
            continue

        is_occupied = (char == '0')

        if is_occupied:
            print(f"[Occupancy] Сработал бит №{idx} для сегмента {seg}")

        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s['type'] == "segment":
                    seg_occ_dict[s['id']] = 0 if is_occupied else 1
                elif s["type"] == "diag":
                    diag_occ_dict[s['name']] = 0 if is_occupied else 1
        else:
            seg_occ_dict[seg] = 0 if is_occupied else 1


# ---------------------------------------------------------------------
# ОТПРАВКА ДАННЫХ НА ARDUINO (Светофоры)
# ---------------------------------------------------------------------
def build_hw_frame(signals_state: Dict[str, Any], blink_phase: bool) -> List[int]:
    """Собирает 5 байт (40 бит). 1 = ВЫКЛ, 0 = ВКЛ (Active-Low)."""
    frame = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF]

    for sig_name, st in signals_state.items():
        lamps = st.get("lamps", {})
        for lamp_name, cfg in lamps.items():
            if not cfg.get("on", False):
                continue

            if cfg.get("blink", False) and not blink_phase:
                continue

            pos = HW_MAP_40.get((sig_name, lamp_name))
            if pos:
                byte_idx, bit_idx = pos
                frame[byte_idx] &= ~(1 << bit_idx)

    return frame


def send_lights_to_arduino(frame: List[int]):
    """Запаковывает 5 байт в пакет 'L' и шлет в Serial."""
    if ser is None or not ser.is_open: return
    try:
        packet = bytearray([ord('L')]) + bytearray(frame)
        ser.write(packet)
        # Отладочный вывод для контроля
       # print(f"DEBUG [{time.strftime('%H:%M:%S')}]: {' '.join(f'{b:08b}' for b in frame)}")
    except Exception as e:
        print(f"[AC] Serial Error: {e}")



def all_off():
    """Гасит все светофоры (все биты в 1)."""
    send_lights_to_arduino([0xFF] * 5)