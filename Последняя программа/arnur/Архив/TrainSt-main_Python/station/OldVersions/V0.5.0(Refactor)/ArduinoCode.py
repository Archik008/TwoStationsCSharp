from dataclasses import dataclass
from typing import Dict, List, Sequence
from configs import signals_config, ROUTE_PROTECT_SEGMENTS_FOR_CH, ROUTE_SIGNAL_ASPECTS, train_routes, SEGMENT_ORDER
from TestRazdel import seg_occ_train, active_routes, routes, signal_blink_phase
from serial import SerialException
import time



# ===================== СВЕТОФОРЫ: логика байтов/аспектов =====================

# 1) Какие лампы должны гореть для каждого "аспекта" (сигнала)
#    ВАЖНО: названия ламп здесь — "логические роли", а не позиции.
#    Реальные биты вычисляются из signals_config[name]["colors"].
ASPECT_ROLES = {
    "off":        tuple(),
    "red":        ("red",),
    "green":      ("green",),
    "white":      ("white",),
    "blue":       ("blue",),
    "one_yellow": ("yellow",),
    "two_yellow": ("yellow", "yellow2"),
    "invite":     ("red", "white"),   # белый будет мигать фазой
}

@dataclass
class SignalDef:
    name: str
    lamp_to_bit: Dict[str, int]   # например {"white":7,"yellow":6,"red":5,"green":4,"yellow2":3}
    has: set                      # быстрые проверки наличия ламп

def build_signal_defs(signals_config: dict) -> Dict[str, SignalDef]:
    """
    По signals_config строит для каждого сигнала:
      - lamp_to_bit: какая лампа сидит на каком бите (7..0)
      - has: набор доступных "логических ламп" (white/red/yellow/yellow2/...)
    Правило: i-я лампа в colors -> бит (7 - i)
    """
    defs: Dict[str, SignalDef] = {}

    for name, cfg in signals_config.items():
        colors: List[str] = cfg["colors"]

        # если цвет повторяется (например yellow, yellow), делаем ключи yellow2, yellow3...
        counts: Dict[str, int] = {}
        lamp_keys: List[str] = []
        for c in colors:
            counts[c] = counts.get(c, 0) + 1
            lamp_keys.append(c if counts[c] == 1 else f"{c}{counts[c]}")

        lamp_to_bit: Dict[str, int] = {}
        for i, lamp in enumerate(lamp_keys):
            bit = 7 - i
            lamp_to_bit[lamp] = bit

        defs[name] = SignalDef(name=name, lamp_to_bit=lamp_to_bit, has=set(lamp_to_bit.keys()))

    return defs

def make_byte_on(lamps: Sequence[str], lamp_to_bit: Dict[str, int]) -> int:
    """
    Active-low: 0 = ON, 1 = OFF
    Включить лампу = сбросить её бит в 0.
    """
    b = 0xFF
    for lamp in lamps:
        bit = lamp_to_bit.get(lamp)
        if bit is None:
            continue
        b &= ~(1 << bit)
    return b & 0xFF

def stop_aspect_for_signal(sig_def: SignalDef) -> str:
    """
    Базовый "запрещающий" сигнал:
      - если есть красный -> red
      - иначе если есть синий -> blue
      - иначе off
    """
    if "red" in sig_def.has:
        return "red"
    if "blue" in sig_def.has:
        return "blue"
    return "off"

def encode_signal_byte(sig_def: SignalDef, aspect: str, blink: bool, blink_phase: bool) -> int:
    """
    Возвращает один байт (8 бит) для 74HC595 данного сигнала.
    """
    # неизвестный аспект => STOP
    if aspect not in ASPECT_ROLES:
        aspect = stop_aspect_for_signal(sig_def)

    if aspect == "off":
        return 0xFF

    # invite: красный всегда + белый мигает фазой
    if aspect == "invite":
        lamps = ("red", "white") if blink_phase else ("red",)
        return make_byte_on(lamps, sig_def.lamp_to_bit)

    lamps = ASPECT_ROLES[aspect]

    # если blink=True — мигает весь аспект: в "выключенной фазе" гасим всё
    if blink and not blink_phase:
        return 0xFF

    return make_byte_on(lamps, sig_def.lamp_to_bit)

def build_frame(signals_config: dict,
                signal_defs: Dict[str, SignalDef],
                signals_state: dict,
                blink_phase: bool) -> List[int]:
    """
    Собирает список байтов по всем сигналам в порядке signals_config.keys().
    Это и есть "кадр" для цепочки регистров.
    """
    regs: List[int] = []
    for name in signals_config.keys():  # порядок dict = порядок цепочки
        st = signals_state.get(name, {"aspect": "off", "blink": False})
        sd = signal_defs[name]
        b = encode_signal_byte(sd, st["aspect"], st.get("blink", False), blink_phase)
        regs.append(b)
    return regs

# 2) СТРОИМ ОПИСАНИЯ СИГНАЛОВ (после того как ВСЕ def-ы выше уже объявлены)
signal_defs = build_signal_defs(signals_config)

def debug_print_frame(regs: list[int]) -> None:
    # Печать “какой байт на какой сигнал” в порядке цепочки signals_config
    names = list(signals_config.keys())
    line = " | ".join(f"{n}:{b:08b}" for n, b in zip(names, regs))
    print("[FRAME]", line)

#########################################        СТРЕЛКИ/СЕРВО (ARDUINO)              ##############################################
def send_arduino_cmd(cmd: str):
    """
    Отправка одиночной команды (один символ) в Arduino.
    """
    global ser
    if ser is None or not ser.is_open:
        print(f"[PY] Arduino not connected, cmd={cmd!r} пропущена")
        return

    try:
        # шлём один байт-символ
        ser.write(cmd.encode("ascii"))
        print(f"[PY] SEND -> {cmd!r}")
    except SerialException as e:
        print(f"[PY] Ошибка отправки команды {cmd!r} в Arduino: {e}")

def send_servo_for_switch(nameDiag: str, mode: str):
    """
    nameDiag: 'ALB_Turn1', 'ALB_Turn2-4', 'ALB_Turn6-8', 'ALB_Turn4-6'
    mode: 'left' или 'right'

    Логика:
      left  -> ПЛЮС (первое положение)
      right -> МИНУС (второе положение)
    """

    if mode not in ("left", "right"):
        return

    # по договору:
    # A / a – M1M10 (ALB_Turn1)
    # D / d – M2H3  (ALB_Turn2-4)
    # B / b – H42   (ALB_Turn6-8)
    # C / c – 2T1A  (первый привод 2T1)
    # E / e – 2T1B  (второй привод 2T1)

    # плюс = большая буква, минус = маленькая
    cmds: list[str] = []

    if nameDiag == "ALB_Turn1":          # M1M10
        cmds.append('A' if mode == "left" else 'a')

    elif nameDiag == "ALB_Turn2-4":      # M2H3
        cmds.append('D' if mode == "left" else 'd')

    elif nameDiag == "ALB_Turn6-8":      # H42
        cmds.append('B' if mode == "left" else 'b')

    elif nameDiag == "ALB_Turn4-6":      # 2T1: ДВА СЕРВО ОДНОВРЕМЕННО
        # первый привод
        cmds.append('C' if mode == "left" else 'c')
        # второй привод
        cmds.append('E' if mode == "left" else 'e')

    # отправляем все команды по очереди
    for c in cmds:
        send_arduino_cmd(c)



####################################################################################################


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

    elif aspect == "green":
        idx = roles.get("green")
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

    elif aspect == "white":
        idx = roles.get("white")
        if idx is not None:
            lit.add(idx)

    elif aspect == "white_blink":
        idx = roles.get("white")
        if idx is not None:
            lit.add(idx)
            blink.add(idx)

    elif aspect == "two_yellow":
        low_idx = roles.get("yellow_low") or roles.get("yellow")
        high_idx = roles.get("yellow_high") or low_idx
        if low_idx is not None:
            lit.add(low_idx)
        if high_idx is not None:
            lit.add(high_idx)

    elif aspect == "invite":
        # красный горит постоянно
        ridx = roles.get("red")
        if ridx is not None:
            lit.add(ridx)

        # белый мигает
        widx = roles.get("white")
        if widx is not None:
            lit.add(widx)
            blink.add(widx)

    return lit, blink




def get_current_ch_aspect() -> str:
    """
    Выдаёт текущий аспект для CH с учётом:
      - выбранного маршрута (ROUTE_SIGNAL_ASPECTS)
      - факта, что поезд уже вошёл на маршрут (ch_route_passed)
      - занятости защищаемых участков (is_route_occupied_for_CH)
    """
    dest = find_active_entry_route_from_CH()
    route_cfg = ROUTE_SIGNAL_ASPECTS.get(dest, ROUTE_SIGNAL_ASPECTS[None])

    global invite_until
    if time.time() < invite_until:
        return "invite"
    # <-- ДО СЮДА

    if dest is None:
        return route_cfg.get("CH", "red")

    if invite_until > 0:
        if time.time() < invite_until:
            return "white_blink"
        else:
            invite_until = 0.0

    if dest is None:
        return route_cfg.get("CH", "red")

    if dest in ch_route_passed:
        if is_route_occupied_for_CH(dest):
            ch_route_passed[dest] = True

        if ch_route_passed[dest]:
            return "red"

    return route_cfg.get("CH", "red")

def is_maneuver_route_H3_M10_active() -> bool:
    """
    Проверяем, есть ли активный МАНЕВРОВЫЙ маршрут H3–M10 или M10–H3.
    """
    for rid, data in active_routes.items():
        a = data["start"]
        b = data["end"]

        if (a, b) not in routes and (b, a) not in routes:
            continue

        if (a == "H3" and b == "M10") or (a == "M10" and b == "H3"):
            return True

    return False


def build_signal_byte_for_arduino_by_route() -> int:
    aspect_ch = get_current_ch_aspect()

    # 0 = ВКЛ, 1 = ВЫКЛ -> чтобы включить несколько ламп сразу, надо AND-ить
    def comb(*vals: int) -> int:
        out = 0xFF
        for v in vals:
            out &= (v & 0xFF)
        return out

    if aspect_ch == "invite":
        red = CH_595_PATTERNS["red"]
        white = CH_595_PATTERNS["white"]

        # белый мигает, красный постоянный
        return comb(red, white) if signal_blink_phase else red

    return CH_595_PATTERNS.get(aspect_ch, CH_595_PATTERNS["red"])

def send_signal_bytes(byte_ch: int, byte_man: int = 0xFF):
    """
    Отправка состояний светофоров в Arduino.

    Протокол:
      'L', <byte_ch>, <byte_man>

    byte_ch  – байт для транспортного светофора CH
    byte_man – байт для маневрового светофора (H3–M10 и т.п.)
    """
    global ser
    print(f"[PY] SIGNAL -> L {byte_ch:08b} {byte_man:08b}")
    if ser is None or not ser.is_open:
        print("send_signal_bytes: нет соединения с Arduino")
        return
    try:
        ser.write(bytes([ord('L'), byte_ch & 0xFF, byte_man & 0xFF]))
    except SerialException as e:
        print(f"Ошибка отправки байтов сигналов в Arduino: {e}")

def recalc_signal_aspects():
    """
    1) Ставим огни в GUI по ROUTE_SIGNAL_ASPECTS + "память" для CH
    2) Отправляем байты в Arduino (CH + маневровый)
    """
    signal_aspects.clear()

    dest = find_active_entry_route_from_CH()
    route_cfg = ROUTE_SIGNAL_ASPECTS.get(dest, ROUTE_SIGNAL_ASPECTS[None])

    ch_aspect = get_current_ch_aspect()

    for name in TRAIN_SIGNALS:
        if name == "CH":
            aspect = ch_aspect
        else:
            aspect = route_cfg.get(name, "red")

        lit, blink = get_gui_lamps_for_aspect(name, aspect)
        signal_aspects[name] = (lit, blink)

    byte_ch = build_signal_byte_for_arduino_by_route()
    byte_man = build_maneuver_signal_byte()

    send_signal_bytes(byte_ch, byte_man)

def build_maneuver_signal_byte() -> int:
    global man_red_until

    if time.time() < man_red_until:
        return MAN_595_PATTERNS["red"]   # постоянно, без мигания

    if is_maneuver_route_H3_M10_active():
        return MAN_595_PATTERNS["H3_M10_white"]

    return MAN_595_PATTERNS["off"]

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



#########################################   ОТПРАВКА БАЙТОВ СИГНАЛОВ В ARDUINO  #########################################

# 0 в бите = лампа ВКЛ, 1 = ВЫКЛ.
# Эти значения ты уже подгонял на железе (0b11011111 = красный и т.п.).

CH_595_PATTERNS = {
    "red":         0b11011111,  # один красный

    "two_yellow":  0b10110111,  # два жёлтых
    "one_yellow":  0b10111111,  # один жёлтый

    "white": 0b01111111,  # <-- ДОБАВИТЬ СЮДА

    "green":       0b11101111,  # зелёный

    "off":         0b11111111,  # всё выкл
}

# второй регистр – манёвровые/доп. светофоры
MAN_595_PATTERNS = {
    "off":            0b11111111,  # всё выключено
    "H3_M10_white":   0b01111111,  # белый при маршруте H3–M10
    "red":            0b11011111,   # <-- ПОДСТАВЬ СВОЙ БИТ КРАСНОГО
}

invite_until = 0.0        # до какого времени CH белый мигает
man_red_until = 0.0       # до какого времени MAN красный горит постоянно

def start_invite_mode():
    global invite_until, man_red_until
    invite_until = time.time() + 60.0
    man_red_until = time.time() + 60.0
    recalc_signal_aspects()   # применить сразу

def is_route_occupied_for_CH(dest: str | None) -> bool:
    """
    Проверяем, есть ли поезд на защищаемых для данного направления участках.
    dest = '1'/'2'/'3'/'4' или None.
    seg_occ_train: 0 = занято, 1 = свободно.
    """
    if dest is None:
        return False

    segs = ROUTE_PROTECT_SEGMENTS_FOR_CH.get(dest, [])
    for a, b in segs:
        if seg_occ_train.get((a, b), 1) == 0:
            return True
        if seg_occ_train.get((b, a), 1) == 0:
            return True

    return False

# память: поезд уже прошёл под входным светофором по этому направлению
ch_route_passed = { "1": False, "2": False, "3": False, "4": False }


#########################################  ЛОГИКА ПОЕЗДНЫХ СВЕТОФОРОВ  #########################################

TRAIN_SIGNALS = {"CH", "H1", "H2", "H3", "H4"}

signal_lamp_roles: dict[str, dict[str, int]] = {}
signal_aspects: dict[str, tuple[set[int], set[int]]] = {}


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

