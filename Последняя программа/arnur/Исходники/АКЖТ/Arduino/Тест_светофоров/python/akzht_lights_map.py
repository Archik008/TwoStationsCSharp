# -*- coding: utf-8 -*-
"""
Карта ламп АКЖТ + путевой пост → (байт, бит) в кадре 'L' + 13 байт.
Active-Low: 0 = горит, 1 = выкл.

Источник: Excel «Распределение_светофоров_по_битам_регистровбайтов_ВСЕ_СТАНЦИИ_АКЖТ (2).xlsx»
блок «АКЖТ + промежуточная» (27.10.2026). Должна совпадать с:
  Station/.../AkzhtHardware.cs
  Station/.../Post/PostHardware.cs
"""
from __future__ import annotations
from typing import Dict, Tuple

# (имя_в_программе, лампа) → (byte, bit)
# Имена АКЖТ без префикса «А:» — как на схеме; в C# к ним добавляется «А:».

COLOR = {"Ж": "yellow", "З": "green", "К": "red", "Б": "white", "С": "blue", "Ж1": "yellow1"}

HW_MAP: Dict[Tuple[str, str], Tuple[int, int]] = {
    # ---- АКЖТ (байты 0..7.2) ----
    ("М6", "white"): (0, 0), ("М6", "blue"): (0, 1),
    ("М10", "red"): (0, 2), ("М10", "white"): (0, 3),
    ("М8", "white"): (0, 4), ("М8", "blue"): (0, 5),
    ("Н5", "yellow"): (0, 6), ("Н5", "green"): (0, 7),
    ("Н5", "red"): (1, 0), ("Н5", "white"): (1, 1),
    ("Н3", "yellow"): (1, 2), ("Н3", "green"): (1, 3),
    ("Н3", "red"): (1, 4), ("Н3", "white"): (1, 5),
    ("Н1", "yellow"): (1, 6), ("Н1", "green"): (1, 7),
    ("Н1", "red"): (2, 0), ("Н1", "white"): (2, 1),
    ("Н2", "yellow"): (2, 2), ("Н2", "green"): (2, 3),
    ("Н2", "red"): (2, 4), ("Н2", "white"): (2, 5),
    ("Н4", "yellow"): (2, 6), ("Н4", "green"): (2, 7),
    ("Н4", "red"): (3, 0), ("Н4", "white"): (3, 1),
    ("Ч5", "yellow"): (3, 2), ("Ч5", "green"): (3, 3),
    ("Ч5", "red"): (3, 4), ("Ч5", "white"): (3, 5),
    ("Ч3", "yellow"): (3, 6), ("Ч3", "green"): (3, 7),
    ("Ч3", "red"): (4, 0), ("Ч3", "white"): (4, 1),
    ("Ч1", "yellow"): (4, 2), ("Ч1", "green"): (4, 3),
    ("Ч1", "red"): (4, 4), ("Ч1", "white"): (4, 5),
    ("Ч2", "yellow"): (4, 6), ("Ч2", "green"): (4, 7),
    ("Ч2", "red"): (5, 0), ("Ч2", "white"): (5, 1),
    ("Ч4", "yellow"): (5, 2), ("Ч4", "green"): (5, 3),
    ("Ч4", "red"): (5, 4), ("Ч4", "white"): (5, 5),
    ("М7", "white"): (5, 6), ("М7", "blue"): (5, 7),
    ("М5", "white"): (6, 0), ("М5", "blue"): (6, 1),
    ("М1", "white"): (6, 2), ("М1", "blue"): (6, 3),
    ("М3", "white"): (6, 4), ("М3", "blue"): (6, 5),
    ("Н", "yellow"): (6, 6), ("Н", "green"): (6, 7),
    ("Н", "red"): (7, 0), ("Н", "yellow1"): (7, 1), ("Н", "white"): (7, 2),
    ("М9", "red"): (8, 7), ("М9", "white"): (9, 0),  # «под вопросом»

    # ---- проходные / пост (PostHardware) ----
    ("1-I", "red"): (7, 3), ("1-I", "yellow"): (7, 4), ("1-I", "green"): (7, 5),
    ("6-II", "red"): (7, 6), ("6-II", "yellow"): (7, 7), ("6-II", "green"): (8, 0),
    ("3-I", "red"): (8, 1), ("3-I", "yellow"): (8, 2), ("3-I", "green"): (8, 3),
    ("4-II", "red"): (8, 4), ("4-II", "yellow"): (8, 5), ("4-II", "green"): (8, 6),
    ("5-I", "red"): (9, 1), ("5-I", "yellow"): (9, 2), ("5-I", "green"): (9, 3),  # [ИСПРАВЛЕНО 28.10.2026] в таблице К Ж З
    ("2-II", "red"): (9, 4), ("2-II", "yellow"): (9, 5), ("2-II", "green"): (9, 6),  # [ИСПРАВЛЕНО 28.10.2026] в таблице К Ж З
    ("Ч", "yellow"): (9, 7), ("Ч", "green"): (10, 0), ("Ч", "red"): (10, 1),
    ("Ч", "yellow1"): (10, 2), ("Ч", "white"): (10, 3),
    ("Н_пост", "yellow"): (10, 4), ("Н_пост", "green"): (10, 5),
    ("Н_пост", "red"): (10, 6), ("Н_пост", "white"): (10, 7),
    ("1-ОП", "red"): (11, 0), ("1-ОП", "yellow"): (11, 1), ("1-ОП", "green"): (11, 2),
    ("2-ОП", "red"): (11, 3), ("2-ОП", "yellow"): (11, 4), ("2-ОП", "green"): (11, 5),
    ("НА", "yellow"): (11, 6), ("НА", "yellow1"): (11, 7),
    ("НА", "red"): (12, 0), ("НА", "white"): (12, 1),
}

FRAME_BYTES = 13


def empty_frame() -> bytearray:
    return bytearray([0xFF] * FRAME_BYTES)


def set_lamp(frame: bytearray, sig: str, lamp: str, on: bool) -> None:
    pos = HW_MAP.get((sig, lamp))
    if pos is None:
        return
    b, bit = pos
    if on:
        frame[b] &= ~(1 << bit)
    else:
        frame[b] |= (1 << bit)


def frame_to_l_command(frame: bytearray) -> bytes:
    return b"L" + bytes(frame)


if __name__ == "__main__":
    # быстрая проверка: нет пересечений битов
    seen = {}
    for key, pos in HW_MAP.items():
        if pos in seen:
            print("CONFLICT", seen[pos], key, pos)
        else:
            seen[pos] = key
    print("lamps", len(HW_MAP), "unique bits", len(seen), "frame", FRAME_BYTES)
