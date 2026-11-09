# ArduinoCode.py
from __future__ import annotations

from typing import Dict, Tuple, Optional, List, Any

try:
    import serial  # type: ignore
except Exception:
    serial = None  # чтобы модуль не падал при импорте без pyserial

# ---------------------------------------------------------------------
# SERIAL handle (назначаешь извне: AC.ser = ser)
# ---------------------------------------------------------------------
ser = None  # ожидается объект serial.Serial

# ---------------------------------------------------------------------
# ПРОТОКОЛ (как в твоём текущем скетче)
#   'L' + 3 байта  -> применить свет (24 бита)
#   'S' + 1 байт   -> порядок регистров/бит (FLAGS)
#   'R'            -> сброс (all off + resetShift)
#
# FLAGS:
#   bit0=1 => REG_ORDER=b0b1b2 (иначе b2b1b0)
#   bit1=1 => BIT_ORDER=LSB    (иначе MSB)
# ---------------------------------------------------------------------

REG_ORDER = "b2b1b0"  # или "b0b1b2"
BIT_ORDER = "LSB"     # или "LSB"

def _is_serial_ok() -> bool:
    global ser
    return ser is not None and getattr(ser, "is_open", False)

def _write(data: bytes) -> None:
    if not _is_serial_ok():
        return
    try:
        ser.write(data)
    except Exception as e:
        print(f"[AC] Serial write error: {e}")

# ---------------------------------------------------------------------
# Биты из байтов (для кнопок)
# ---------------------------------------------------------------------
def bytes_to_bits(data: bytes, bits_needed: int) -> List[int]:
    bits: List[int] = []
    for byte in data:
        for bit_index in range(8):
            bits.append((byte >> bit_index) & 1)
            if len(bits) >= bits_needed:
                return bits
    while len(bits) < bits_needed:
        bits.append(0)
    return bits

# ---------------------------------------------------------------------
# Управление порядком (S) и сбросом (R)
# ---------------------------------------------------------------------
def set_link_order(reg_order: str = REG_ORDER, bit_order: str = BIT_ORDER, debug: bool = True) -> int:
    """
    reg_order: "b2b1b0" или "b0b1b2"
    bit_order: "MSB" или "LSB"
    Возвращает FLAGS, которые отправили.
    """
    global REG_ORDER, BIT_ORDER
    REG_ORDER = reg_order
    BIT_ORDER = bit_order

    flags = 0
    if reg_order.strip().lower() == "b0b1b2":
        flags |= 0x01
    if bit_order.strip().upper() == "LSB":
        flags |= 0x02

    _write(bytes([ord('S'), flags]))

    if debug:
        print(f"[AC] -> S {flags:#04x}  (REG_ORDER={REG_ORDER}, BIT_ORDER={BIT_ORDER})")
    return flags

def reset_outputs(debug: bool = True) -> None:
    """Жёсткий сброс на Arduino: 'R' """
    _write(b'R')
    if debug:
        print("[AC] -> R  (reset outputs)")

def all_off(debug: bool = True) -> None:
    """Alias под твой вызов AC.all_off()"""
    reset_outputs(debug=debug)

# ---------------------------------------------------------------------
# Карта ламп -> биты (active-low: 0=ON, 1=OFF)
# ТВОЯ текущая железка в скетче: 3 регистра = 24 бита => используем byte0..byte2
# ---------------------------------------------------------------------
# byte_i: 0..2, bit: 0..7
HW_MAP_24: Dict[Tuple[str, str], Tuple[int, int]] = {
    # --- Byte0 (LED 1..8) ---
    ("CH","Y_TOP"): (0, 0),
    ("CH","G"):     (0, 1),
    ("CH","R"):     (0, 2),
    ("CH","Y_BOT"): (0, 3),
    ("CH","W"):     (0, 4),
    ("M2","W"):     (0, 5),
    ("M2","B"):     (0, 6),
    ("H1","Y"):     (0, 7),

    # --- Byte1 (LED 9..16) ---
    ("H1","G"):     (1, 0),
    ("H1","R"):     (1, 1),
    ("H1","W"):     (1, 2),
    ("M8","W"):     (1, 3),
    ("M8","R"):     (1, 4),
    ("M1","W"):     (1, 5),
    ("M1","R"):     (1, 6),
    ("M6","W"):     (1, 7),

    # --- Byte2 (LED 17..24) ---
    ("M6","R"):     (2, 0),
    ("H2","Y"):     (2, 1),
    ("H2","G"):     (2, 2),
    ("H2","R"):     (2, 3),
    ("H2","W"):     (2, 4),
    ("H3","Y"):     (2, 5),
    ("H3","G"):     (2, 6),
    ("H3","R"):     (2, 7),
}

def _hw_key(sig: str, lamp: str) -> Optional[Tuple[str, str]]:
    # CH имеет две жёлтых: yellow / yellow1
    if sig == "CH":
        if lamp == "yellow":
            return ("CH", "Y_TOP")
        if lamp == "yellow1":
            return ("CH", "Y_BOT")
        if lamp == "red":
            return ("CH", "R")
        if lamp == "green":
            return ("CH", "G")
        if lamp == "white":
            return ("CH", "W")
        return None

    conv = {
        "red": "R",
        "green": "G",
        "yellow": "Y",
        "white": "W",
        "blue": "B",
    }
    code = conv.get(lamp)
    if not code:
        return None
    return (sig, code)

def build_hw_frame_from_signals_state(signals_state: Dict[str, Any], blink_phase: bool) -> List[int]:
    """
    Возвращает 3 байта [b0, b1, b2] (active-low: 0=ON).
    """
    frame = [0xFF, 0xFF, 0xFF]

    for sig_name, st in signals_state.items():
        lamps = st.get("lamps", {})
        for lamp_name, cfg in lamps.items():
            if not cfg.get("on", False):
                continue

            if cfg.get("blink", False) and (not blink_phase):
                continue

            key = _hw_key(sig_name, lamp_name)
            if key is None:
                continue

            pos = HW_MAP_24.get(key)
            if pos is None:
                continue

            byte_i, bit = pos
            frame[byte_i] &= ~(1 << bit)  # 0 = ON

    return frame

def _dbg_bytes(prefix: str, b2: int, b1: int, b0: int) -> None:
    print(
        f"{prefix} "
        f"b2=0x{b2:02X}({b2:08b}) "
        f"b1=0x{b1:02X}({b1:08b}) "
        f"b0=0x{b0:02X}({b0:08b})"
    )

# ---------------------------------------------------------------------
# Отправка света на Arduino
# ВАЖНО: в скетче приём: 'L' + 3 байта (b2,b1,b0)
# То есть мы должны отправить именно в этом порядке.
# ---------------------------------------------------------------------
def send_lights_bytes(b2: int, b1: int, b0: int, debug: bool = True) -> None:
    pkt = bytes([ord('L'), b2 & 0xFF, b1 & 0xFF, b0 & 0xFF])
    _write(pkt)
    if debug:
        _dbg_bytes("[AC] -> L", b2, b1, b0)

def send_hw_from_state(signals_state: Dict[str, Any], blink_phase: bool, debug: bool = False) -> List[int]:
    """
    Собирает кадр из signals_state и шлёт.
    Возвращает [b0,b1,b2] (логический кадр до перестановок в скетче).
    """
    b0, b1, b2 = build_hw_frame_from_signals_state(signals_state, blink_phase=blink_phase)

    # В СКЕТЧЕ ожидается порядок (b2,b1,b0)
    send_lights_bytes(b2, b1, b0, debug=debug)

    if debug:
        print(f"[AC] frame(logical) b0=0x{b0:02X}, b1=0x{b1:02X}, b2=0x{b2:02X}")

    return [b0, b1, b2]

def send_reset_on_arduino():
    """
    Команда 'R' — Arduino: resetShift + allOffCH
    """
    global ser
    if ser is None:
        return False
    try:
        ser.write(b'R')
        return True
    except Exception:
        return False


def send_all_off():
    """
    Мягкое выключение: отправляем пакет L + FF FF FF (все выходы 74HC595 = 1 = OFF).
    Работает даже без 'R', но 'R' полезен чтобы гарантированно очистить.
    """
    global ser
    if ser is None:
        return False
    try:
        ser.write(b'L' + bytes([0xFF, 0xFF, 0xFF]))
        return True
    except Exception:
        return False
