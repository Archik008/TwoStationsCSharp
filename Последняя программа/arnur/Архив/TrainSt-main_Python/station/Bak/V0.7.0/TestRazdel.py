import tkinter
import tkinter as tk
import serial
from tkinter.messagebox import showinfo
from serial.tools import list_ports
from tkinter import ttk
from configs import *
from ArduinoCode import *
import time
from serial import SerialException
from tkinter import messagebox


root = tk.Tk()
root.title("Станция")

#canvas = tk.Canvas(root, width=1250, height=600, bg="#7AA49A")

scheme_frame = tk.Frame(root, bg="gray")
scheme_frame.pack(fill="both", expand=True)

canvas = tk.Canvas(
    scheme_frame,
    width=1920,
    height=1080,
    bg="#7AA49A"
)
canvas.pack(fill="both", expand=True)

def quit_function():
    #response = tkinter.messagebox.askyesno('Exit', 'Are you sure you want to exit?')
    #if response:
        exit()

CANVAS_W = 1920
CANVAS_H = 10801

root.resizable(False, False)

class SignalManager():
    SIMPLE_SIGNAL_ASPECTS = {
        "RED": ["red", "grey"],
        "DOUBLE_YELLOW": ["yellow", "yellow"],
        "WHITE_YELLOW": ["grey", "yellow"],
        "WHITE_GREEN": ["grey", "green"],
        "GREEN_SECOND": ["green", "yellow"],
        "INVITE": ["white", "red"],
    }
    CH_CURRENT_ASPECT = ""
    COLOR_MAP = {
        "red": "red",
        "green": "green",
        "yellow": "yellow",
        "white": "white",
        "grey": "grey",
    }
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
    signals_state_simple = {
        "CH": {
            "lamps": {
                "red": {"on": False, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "M2": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "H1": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "H2": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "H3": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "H4": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "M6": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "M8": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "M10": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "M1": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "ALB_Sect1-2": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "ALB_Sect1-2_2": {
            "lamps": {
                "grey": {"on": True, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },
        "ALB_Sect2": {
            "lamps": {
                "red": {"on": False, "blink": False},
                "white": {"on": False, "blink": False},
            },
            "manual": False,
        },

    }
    active_signal_routes = {}
    def __init__(self, canvas, root):
        self.canvas = canvas
        self.root = root
        self.signal_ids = {}
        self.signal_ids_simple = {}
        self.signal_blink_phase = False
        self.route_manager = None
        self.signal_id_map = {}
        self.signal_id_map_simple = {}
    simple_vis_mode = True
    def set_dependencies(self, route_manager):
        self.route_manager = route_manager

    def make_signal_state(self, name, colors):
        self.signals_state[name] = {
            "lamps": {
                color: {"on": False, "blink": False}
                for color in colors
            }
        }

    def make_signal_state_simple(self, name, colors):
        self.signals_state_simple[name] = {
            "lamps": {
                color: {"on": False, "blink": False}
                for color in colors
            }
        }


    def get_simple_state(self):
        return self.simple_vis_mode

    def drawSignal(self, offsety, name, mount="bottom", pack_side="right", count=3, colors=None):
        x, y = positions[name]
        r = 5
        gap = 2 * r + 2
        stand_len = 15
        bar_len = 10

        dy_sign = -1 if mount == "top" else 1
        sx, sy = x, y + dy_sign * stand_len

        canvas.create_line(x, y, sx, sy + offsety, width=2, fill="white")

        hx_sign = 1 if pack_side == "right" else -1

        hx0, hy0 = sx, sy
        hx1, hy1 = sx + hx_sign * bar_len, sy
        canvas.create_line(hx0, hy0 + offsety, hx1, hy1 + offsety, width=2, fill="white")

        ids = []
        start_cx = hx1 + hx_sign * (r + 1)

        for i in range(count):
            cx = start_cx + hx_sign * i * gap
            cy = sy

            fill_color = ""
            if colors is not None and i < len(colors):
                fill_color = colors[i]
            oid = canvas.create_oval(
                cx - r, cy - r + offsety,
                cx + r, cy + r + offsety,
                outline="#F5F5F5", width=0.25, fill=fill_color
            )
            ids.append(oid)


        self.make_signal_state(name, colors)
        self.signal_ids[name] = ids
        config_colors = self.get_lamp_colors(name)
        self.signal_id_map[name] = dict(zip(list(config_colors.keys()), ids))

    def set_signals_to_route(self, rid):
        if rid != None:
            data = route_manager.get_active_routes(rid)
            a = data.get("start")
            b = data.get("end")
            key = (a, b)
            cfg = ROUTE_SIGNAL_MAP.get(key)
            for name in cfg:
                for color, lamp_cfg in cfg[name]["lamps"].items():
                    self.set_signal(name, color, lamp_cfg["on"])
                    for colors in self.get_lamp_colors(name):
                        if colors == "white" and name == "CH":
                            continue
                        if colors in list(cfg[name]['lamps'].keys()):
                            continue
                        else:
                            self.set_signal(name, colors, False)

    def clear_signal(self, name):
        if name not in self.signals_state:
            return

        for color in self.signals_state[name]["lamps"]:
            self.signals_state[name]["lamps"][color]["on"] = False
            self.signals_state[name]["lamps"][color]["blink"] = False

    def set_signal_red(self, name):
        self.clear_signal(name)
        if self.if_color_in_lamp(name, "red"):
            self.set_signal(name, "red", True)
        else:
            self.set_signal(name, "blue", True)

    def set_signal_white(self, name):
        self.clear_signal(name)
        self.set_signal(name, "white", True)

    def get_first_segment_from_route_steps(self, route_steps):
        for step in route_steps:
            if step.get("type") == "segment":
                return step.get("id")

        return None

    def is_route_steps_free(self, route_steps):
        for step in route_steps:
            if step.get("type") != "segment":
                continue

            seg = step.get("id")

            if self.is_segment_occupied(seg):
                return False

        return True

    def is_segment_occupied(self, seg):
        if seg is None:
            return False

        a, b = seg
        rev = (b, a)

        return (
                seg_occ_train.get(seg, 1) == 0
                or seg_occ_train.get(rev, 1) == 0
        )

    def update_maneuver_signal_logic(self, name):
        rid = SignalManage.active_signal_routes.get(name)


        if rid is None:
            self.set_signal_red(name)
            return

        route = route_manager.active_routes.get(rid)

        if route is None:
            self.set_signal_red(name)
            return

        route_steps = route.get("segments") or []

        if not route_steps:
            self.set_signal_red(name)
            return

        first_seg = self.get_first_segment_from_route_steps(route_steps)

        if first_seg is None:
            self.set_signal_red(name)
            return

        if route.get("started", False):
            self.set_signal_red(name)
            return

        if self.is_segment_occupied(first_seg):
            route["started"] = True
            self.set_signal_red(name)
            return

        if not self.is_route_steps_free(route_steps):
            self.set_signal_red(name)
            return

        self.set_signal_white(name)


    def sync_sinple_CH_with_debug(self):
        name = "CH"
        if self.get_lamp_state(name, "white"):
            self.set_simple_signal_aspect(name, "INVITE")
        elif self.get_lamp_state(name, 'red'):
            self.set_simple_signal_aspect(name, "RED")
        elif self.get_lamp_state(name, 'yellow') and self.get_lamp_state(name, 'yellow1'):
           self.set_simple_signal_aspect(name, "DOUBLE_YELLOW")


    def get_ch_current_apect(self):
        return self.CH_CURRENT_ASPECT

    def sync_simple_and_debug(self):
        for name in signals_config.keys():
            if name == "CH":
                continue
            if name == 'ALB_Sect1-2':
                self.set_signal_simple(name, 'grey',True)
                continue
            elif name == 'ALB_Sect2':
                self.set_signal_simple(name, 'red',True)
                continue
            elif name == 'ALB_Sect1-2_2':
                continue
            config_count = signals_config_simple[name]["count"]
            if self.if_color_in_lamp(name, 'red'):
                if self.get_lamp_state(name, 'red'):
                    for colors in self.get_lamp_colors_simple(name):
                        if config_count == 1:
                            if not colors == 'grey':
                                self.set_signal_simple(name, colors, False)
                            else:
                                self.set_signal_simple(name, colors, True)
                else:
                    for colors in self.get_lamp_colors_simple(name):
                        if config_count == 1:
                            if not colors == 'grey':
                                self.set_signal_simple(name, colors, True)
                            else:
                                self.set_signal_simple(name, colors, False)
            elif self.if_color_in_lamp(name, 'blue'):
                if self.get_lamp_state(name, 'blue'):
                    for colors in self.get_lamp_colors_simple(name):
                        if colors == 'grey':
                            self.set_signal_simple(name, colors, True)
                        else:
                            self.set_signal_simple(name, colors, False)
                else:
                    for colors in self.get_lamp_colors_simple(name):
                        if not colors == 'grey':
                            self.set_signal_simple(name, colors, True)
                        else:
                            self.set_signal_simple(name, colors, False)
            #print(name)

    def set_simple_signal_aspect(self, name, aspect):

        ids = self.signal_ids_simple.get(name, [])
        lamp_colors = self.SIMPLE_SIGNAL_ASPECTS.get(aspect)

        if not lamp_colors:
            print(f"Неизвестный аспект: {aspect}")
            return

        for i, lamp_id in enumerate(ids):
            color_name = lamp_colors[i] if i < len(lamp_colors) else "grey"
            hex_color = self.COLOR_MAP.get(color_name, self.COLOR_MAP["grey"])
            self.canvas.itemconfig(lamp_id, fill=hex_color)

    def after_train_passed_seg(self, seg, rev):
        signal_segment = segment_to_signal.get(seg)
        if signal_segment == None:
            signal_segment = segment_to_signal.get(rev)
        if self.has_signal_in_states(signal_segment):
            for colors in self.get_lamp_colors(signal_segment):
                if colors == "red":
                    self.set_signal(signal_segment, colors, onStatus=True)
                elif colors == "blue":
                    self.set_signal(signal_segment, colors, onStatus=True)
                else:
                    self.set_signal(signal_segment, colors, onStatus=False)



    def is_signal_used_by_other_routes(self, signal_name, exclude_rid):
        for other_rid, data in route_manager.get_active_routes_items():
            if other_rid == exclude_rid:
                continue
            for data_in_segment in route_manager.get_active_routes_other_id(other_rid):
                if data_in_segment['type'] == 'segment':
                    segment = data_in_segment['id']
                    if any(segment == seg for seg in segment_to_signal):
                        signal_cfg = segment_to_signal[segment]
                        if signal_name == signal_cfg:
                            return True

                elif data_in_segment['type'] == 'diag':
                    diag = data_in_segment['name']
                    if any(diag == diagonal for diagonal in diag_to_signal):
                        diag_cfg = diag_to_signal[diag]
                        if signal_name == diag_cfg:
                            return True
        return False

    def return_to_red_after_finishing(self, rid):
        AdditionalSignals = ["ALB_Sect1-2", "ALB_Sect1-2_2", "ALB_Sect2"]
        if rid != None:
            data = route_manager.get_active_routes(rid)
            a = data.get("start")
            b = data.get("end")
            key = (a, b)
            print(key)
            cfg = ROUTE_SIGNAL_MAP.get(key)
            for name in cfg:
                if name in AdditionalSignals:
                    continue
                if self.is_signal_used_by_other_routes(name, rid):
                    continue
                for lamp_name, lamp in self.signals_state[name]["lamps"].items():
                    if lamp_name == "white" and name == "CH":
                        continue
                    self.set_signal(name, lamp_name, onStatus=False)
                    lamp["blink"] = False
                if self.if_color_in_lamp(name, "red"):
                    self.set_signal(name, 'red', onStatus=True)
                elif self.if_color_in_lamp(name, "blue"):
                    self.set_signal(name, 'blue', onStatus=True)
                else:
                    for colors in self.get_lamp_colors(name):
                        self.set_signal(name, colors, onStatus=False)

    def check_Sect1_2_2(self):
        if self.get_lamp_state("CH", "green"):
            for colors in self.get_lamp_colors("ALB_Sect1-2_2"):
                if colors == "green":
                    self.set_signal("ALB_Sect1-2_2", "green", True )
                else:
                    self.set_signal("ALB_Sect1-2_2", colors, False)
            for colors in self.get_lamp_colors_simple("ALB_Sect1-2_2"):
                if colors == "white":
                    self.set_signal_simple("ALB_Sect1-2_2", "white", True)
                else:
                    self.set_signal_simple("ALB_Sect1-2_2", colors, False)
        else:
            for colors in self.get_lamp_colors("ALB_Sect1-2_2"):
                if colors == "yellow":
                    self.set_signal("ALB_Sect1-2_2", "yellow", True)
                else:
                    self.set_signal("ALB_Sect1-2_2", colors, False)
            for colors in self.get_lamp_colors_simple("ALB_Sect1-2_2"):
                if colors == "grey":
                    self.set_signal_simple("ALB_Sect1-2_2", "grey", True)
                else:
                    self.set_signal_simple("ALB_Sect1-2_2", colors, False)

    def update_all_signals(self):
        self.calculate_dynamic_signals()

        if SignalManage.get_simple_state():
            self.sync_simple_and_debug()

        self.update_signals()


        root.after(350, self.update_all_signals)


    def update_signals(self):
        for name in signals_config.keys():
            if name not in self.signal_ids:
                continue
            #print(name, signal_ids)
            #print(cfg_colors)
            #print(self.signal_id_map[name])
            self.sync_simple_and_debug()
            if SignalManage.get_simple_state():
                config_count = signals_config_simple[name]["count"]
                if config_count == 2:
                    self.sync_sinple_CH_with_debug()
                elif config_count == 1:
                    mapping = self.signal_id_map_simple[name]
                    lamp_id = list(mapping.values())[0]
                    final_color = SIGNAL_OFF_COLOR
                    for color in mapping.keys():
                        if self.get_lamp_state_simple(name, color):
                            final_color = color
                            break
                    self.canvas.itemconfig(lamp_id, fill=final_color)

            else: # это отладочный режим
                for color, lamp_id in self.signal_id_map[name].items():
                    if self.get_lamp_state(name,color):
                        self.canvas.itemconfig(lamp_id, fill=color)
                    else:
                        self.canvas.itemconfig(lamp_id, fill=SIGNAL_OFF_COLOR)
        self.check_Sect1_2_2()



    def _indices_for_color(self, sig_name: str, color: str) -> list[int]:
        cols = signals_config[sig_name]["colors"]
        return [i for i, c in enumerate(cols) if c == color]

    def invite_signal_on_off(self, signalName):
        if self.get_lamp_state(signalName, 'white'):
            self.set_signal(signalName, 'white', onStatus=False)
            self.sync_sinple_CH_with_debug()
        elif not self.get_lamp_state(signalName, 'white'):
            self.set_signal(signalName, 'white', onStatus=True)
            self.sync_sinple_CH_with_debug()

    def set_all_to_red(self):
        color = 'red'
        bannedSignals = ['ALB_Sect2', 'ALB_Sect1-2']
        for name in self.signals_state:
            if name in bannedSignals:
                continue
            if self.if_color_in_lamp(name, color):
                self.set_signal(name, color , onStatus=True)
            elif self.if_color_in_lamp(name, "blue"):
                self.set_signal(name, 'blue', onStatus=True)
        """
        for name in self.signals_state_simple:
            if name in bannedSignals:
                continue
            if self.if_color_in_lamp_simple(name, 'grey'):
                self.set_signal_simple(name, 'grey', onStatus=True)
            if self.if_color_in_lamp_simple(name, color):
                self.set_signal_simple(name, color, onStatus=True)
        """

    def set_signal(self, name, color, onStatus):
        self.signals_state[name]["lamps"][color]["on"] = onStatus

    def set_signal_simple(self, name, color, onStatus):
        self.signals_state_simple[name]["lamps"][color]["on"] = onStatus

    def set_blink(self, name, color, blinkstatus):
        self.signals_state[name]["lamps"][color]["blink"] = blinkstatus

    def if_color_in_lamp(self, name, color):
        if color in self.signals_state[name]["lamps"]:
            return True
        else: return False

    def if_color_in_lamp_simple(self, name, color):
        if color in self.signals_state_simple[name]["lamps"]:
            return True
        else: return False

    def has_signal_in_states(self, name):
        if name in self.signals_state:
            return True
        else:
            return False

    def get_lamp_state(self, name, color):
        return self.signals_state[name]["lamps"][color]["on"]

    def get_lamp_state_simple(self, name, color):
        return self.signals_state_simple[name]["lamps"][color]["on"]



    def get_lamp_colors(self, name):
        return self.signals_state[name]["lamps"]

    def get_lamp_colors_simple(self, name):
        return self.signals_state_simple[name]["lamps"]


    def on_CH_click(self, event):
        name = interface_manager.get_node_name_from_event(event)
        if name == "CH":
            menu = tk.Menu(root, tearoff=0)
            menu.add_command(
                label="Пригласительный",
                command=lambda: self.invite_signal_on_off(name)
            )
            menu.tk_popup(event.x_root, event.y_root)

    def bind_invite_button(self):
        canvas.tag_bind("node", "<Button-3>", self.on_CH_click)

    def drawAllSignals(self):
        for name, cfg in signals_config.items():
            self.drawSignal(
                0,
                name,
                cfg['mount'],
                cfg["pack_side"],
                cfg["count"],
                cfg.get('colors'),
            )
    def disable_simple_mode(self):
        for name in self.signal_ids:
            for oid in self.signal_ids.get(name):
                canvas.itemconfig(oid, state="normal")
        for name in self.signal_ids_simple:
            for oid in self.signal_ids_simple.get(name):
                canvas.itemconfig(oid, state="hidden")

    def change_simple_mode(self, state):
        self.simple_vis_mode = state

    def enable_simple_mode(self):
        allowedNames = []#['ALB_Sect1-2', 'ALB_Sect1-2_2', 'ALB_Sect2']
        for name in self.signal_ids_simple:
            for oid in self.signal_ids_simple.get(name):
                canvas.itemconfig(oid, state="normal")
        for name in self.signal_ids:
            if name in allowedNames:
                continue
            else:
                for oid in self.signal_ids.get(name):
                    canvas.itemconfig(oid, state="hidden")

    def calculate_dynamic_signals(self):
        for name, cfg in signals_config.items():
            if name not in self.signals_state:
                continue

            signal_type = cfg.get("type")

            if signal_type == "maneuver":
                self.update_maneuver_signal_logic(name)

            elif signal_type == "train":
                pass
                #self.update_train_signal_logic(name)
            else:
                self.set_signal_red(name)

    def drawAllSignals_Simple(self):
        for name, cfg in signals_config_simple.items():
            self.drawSignal_simple(
                0,
                name,
                cfg['mount'],
                cfg["pack_side"],
                cfg["count"],
                cfg.get('colors'),
            )

    def initialize_signals(self):
        self.drawAllSignals()
        self.drawAllSignals_Simple()
        self.checkMode_on_start()
        self.set_all_to_red()
        self.update_all_signals()




    def checkMode_on_start(self):
        SignalManage.enable_simple_mode()
        SignalManage.change_simple_mode(True)
        button_visual_change.config(text="Выключить упрощённый")

    def drawSignal_simple(self, offsety, name, mount="bottom", pack_side="right", count=0, colors=None):
        x, y = positions[name]
        r = 8
        gap = 2 * r + 2
        stand_len = 15
        bar_len = 10

        dy_sign = -1 if mount == "top" else 1
        sx, sy = x, y + dy_sign * stand_len

        canvas.create_line(x, y, sx, sy + offsety, width=2, fill="white")

        hx_sign = 1 if pack_side == "right" else -1

        hx0, hy0 = sx, sy
        hx1, hy1 = sx + hx_sign * bar_len, sy
        canvas.create_line(hx0, hy0 + offsety, hx1, hy1 + offsety, width=2, fill="white")

        ids = []
        start_cx = hx1 + hx_sign * (r + 1)

        for i in range(count):
            cx = start_cx + hx_sign * i * gap
            cy = sy

            fill_color = ""
            if colors is not None and i < len(colors):
                fill_color = colors[i]
            oid = canvas.create_oval(
                cx - r, cy - r + offsety,
                cx + r, cy + r + offsety,
                outline="#F5F5F5", width=0.25, fill=fill_color
            )
            ids.append(oid)

        self.make_signal_state_simple(name, colors)
        self.signal_ids_simple[name] = ids
        if count > 1:
            """
            config_colors = self.get_lamp_colors_simple(name)
            self.signal_id_map_simple[name] = dict(zip(list(config_colors.keys()), ids))
            """
            self.signal_ids_simple[name] = ids
        else:

            main_lamp_id = ids[0]
            config_colors = self.get_lamp_colors_simple(name)
            self.signal_id_map_simple[name] = {
                color: main_lamp_id for color in list(config_colors.keys())
            }

class OccupancyManager:
    isOccupied = False
    wasOccupied = False

    def __init__(
            self,
            canvas,
            root,
    ):
        self.canvas = canvas
        self.root = root
        self.route_manager = None
        self.signal_manager = None
        self.interface_manager = None
        # self.segment_ids = segment_ids
        # self.diag_ids = diag_ids
        # self.segment_groups = segment_groups
        # self.segment_to_block = segment_to_block
        # self.diag_to_signal = diag_to_signal

    def set_dependencies(self, interface_manager, route_manager, signal_manager):
        self.interface_manager = interface_manager
        self.route_manager = route_manager
        self.signal_manager = signal_manager

    def update_paint_segments(self):
        for (a, b), seg_id in segment_ids.items():
            seg = (a,b)
            if not route_manager.if_seg_in_counter_list(seg):
                seg = (b,a)
            else:
                seg = (a,b)
            rev = (b,a)
            block = segment_to_block.get(seg)
            if block:
                if route_manager.is_block_occupied(block):
                    for s in segment_groups[block]:
                        if s["type"] == 'segment':
                            interface_manager.paint_segment(s['id'], "red")
                        elif s["type"] == 'diag':
                            pass
                    continue
            if seg_occ_train.get((a, b), 1) == 0 or seg_occ_train.get((b, a), 1) == 0:
                interface_manager.paint_segment((a, b), "red")
                continue
            if route_manager.if_seg_in_counter_list(seg) and route_manager.get_segment_counter(seg) > 0:
                interface_manager.paint_segment((a, b), "yellow")
                continue
            self.interface_manager.paint_segment((a, b), interface_manager.line_color_main)

    def update_paint_diagonals(self):
        for diag_name, lines in diag_ids.items():
            if diag_occ_train.get(diag_name, 1) == 0:
                interface_manager.paint_diagonal(diag_name, "red")
                continue
            if self.route_manager.if_diag_in_counter_list(diag_name) and route_manager.get_diag_counter(diag_name) > 0:
                interface_manager.paint_diagonal(diag_name, "yellow")
                continue
            interface_manager.paint_diagonal(diag_name, interface_manager.line_color_main)

    def update_routes_diagonals(self):
        for diag in diag_occ_train:
            if diag_occ_train.get(diag, 1) == 0:
                signal_diag = diag_to_signal.get(diag)
                if self.signal_manager.has_signal_in_states(signal_diag):
                    for colors in self.signal_manager.get_lamp_colors(signal_diag):
                        if colors == "red":
                            self.signal_manager.set_signal(signal_diag, colors, onStatus=True)
                        elif colors == "blue":
                            self.signal_manager.set_signal(signal_diag, colors, onStatus=True)
                        else:
                            self.signal_manager.set_signal(signal_diag, colors, onStatus=False)

                self.route_manager.minus_from_counter_diag(diag)

                self.route_manager.check_if_route_finished(seg="", rev="", diag=diag)


    prev_seg_state = {}

    def update_routes_segment(self):
        for seg in seg_occ_train:
            rev = (seg[1], seg[0])
            current = seg_occ_train.get(seg, 1)
            prev = self.prev_seg_state.get(seg, 1)

            if prev == 1 and current == 0:
                SignalManage.after_train_passed_seg(seg, rev)
                block = segment_to_block.get(seg)
                self.route_manager.check_if_route_finished(seg, rev, diag="")
                if block:
                    segs_in_block = segment_groups[block]
                    for s in segs_in_block:
                        if s["type"] == "segment":
                            rev = (s['id'][1], s['id'][0])
                            if route_manager.if_seg_in_counter_list(s['id']):
                                route_manager.minus_from_counter_segment(s['id'])
                            elif route_manager.if_seg_in_counter_list(rev):
                                route_manager.minus_from_counter_segment(rev)
                        elif s["type"] == "diag":
                            if route_manager.if_diag_in_counter_list(s['name']):
                                route_manager.minus_from_counter_diag(s['name'])

                else:

                    if route_manager.if_seg_in_counter_list(seg):
                        route_manager.minus_from_counter_segment(seg)
                    elif route_manager.if_seg_in_counter_list(rev):
                        route_manager.minus_from_counter_segment(rev)


            self.prev_seg_state[seg] = current

    def update_all_occupancy(self):

        self.update_routes_segment()
        self.update_paint_segments()
        self.update_paint_diagonals()
        self.update_routes_diagonals()


        self.root.after(100, self.update_all_occupancy)

class RouteManager:
    current_mode = "maneuver"
    active_routes = {}
    route_counter = 1
    graph = {}
    segments_active_counter = {
        ("pastM1", "M1"): 0,
        ("M8mid", "M8"): 0,
        ("M8mid", "M1"): 0,
        ("M8", "H1"): 0,
        ("M2", "CH"): 0,
        ("past2", "H2"): 0,
        ("H2", "M6H2"): 0,
        ("M6", "M6H2"): 0,
        ("M2", "M2H1_mid"): 0,
        ("M2H1_mid", "M2H1_third"): 0,
        ("H1", "M2H1_third"): 0,
        ("M10", "H3"): 0,
        ("past4", "H4"): 0,
        ("M6", "beforeM6"): 0,
    }
    diag_active_counter = {
        'ALB_Turn2': 0,
        'ALB_Turn1': 0,
        'ALB_Turn8': 0,
        'ALB_Turn4': 0,
        'ALB_Turn6': 0,
    }

    def __init__(self):
        self.route_counter = 1
        self.interface_manager = None
        self.switch_manager = None
        self.build_graph()

    def minus_from_counter_segment(self, segment):
        if self.segments_active_counter[segment] > 0:
            self.segments_active_counter[segment] -= 1

    def get_segment_counter(self, seg):
        return self.segments_active_counter[seg]

    def minus_from_counter_diag(self, diag):
        if self.diag_active_counter[diag] > 0:
            self.diag_active_counter[diag] -= 1

    def get_diag_counter(self, diag):
        return self.diag_active_counter[diag]

    def if_diag_in_counter_list(self, diag):
        if diag in self.diag_active_counter:
            return True
        else:
            return False

    def is_block_occupied(self, block):
        for s in segment_groups[block]:
            if s["type"] == 'segment':
                if seg_occ_train.get(s['id'], 1) == 0:
                    return True
            elif s["type"] == 'diag':
                if diag_occ_train.get(s['name'], 1) == 0:
                    return True
        return False

    def if_seg_in_counter_list(self, seg):
        if seg in self.segments_active_counter:
            return True
        else:
            return False

    def check_if_route_finished(self, seg, rev, diag):
        for rid in list(self.active_routes.keys()):
             data = self.active_routes[rid]
             last_segment = data["segments"][-1]
             block = segment_to_block.get(seg)
             if seg == last_segment["id"] or rev == last_segment["id"]:
                self.release_route(rid)
             elif block:
                 for s in segment_groups[block]:
                     if s == last_segment["id"]:
                         self.release_route(rid)

    part_to_split = {}

    for split_name in split_parts_map:
        for part, logic_name in split_parts_map[split_name].items():
            part_to_split[logic_name] = (split_name, part)

    def set_mode(self, mode):
        self.current_mode = mode

    def next_route_id(self):
        rid = self.get_route_counter()
        self.add_to_route_id(1)
        return rid

    def add_to_route_id(self, add_num):
        if add_num > 0:
            self.route_counter += add_num
        elif add_num < 0:
            self.route_counter -= add_num

    def get_active_routes(self, rid):
        return self.active_routes.get(rid)

    def get_active_routes_items(self):
        return self.active_routes.items()

    def get_active_routes_other_id(self, other_rid):
        return self.active_routes[other_rid]["segments"]

    def check_visual_mode(self):
        if self.interface_manager.get_btn_maneuver() is not None and self.interface_manager.get_btn_train() is not None:
            if self.get_currnet_mode() == "maneuver":
                self.interface_manager.btn_maneuver.config(bg="#3996D5", fg="white")
                self.interface_manager.btn_train.config(bg="grey", fg="white")
            else:
                self.interface_manager.btn_train.config(bg="#3996D5", fg="white")
                self.interface_manager.btn_maneuver.config(bg="grey", fg="white")

        self.interface_manager.selected_nodes.clear()
        self.interface_manager.apply_mode_visuals()

    def is_segment_occupied(self, seg):
        a, b = seg
        rev = (b, a)
        if self.if_seg_in_counter_list((a,b)):
            seg_counter = self.get_segment_counter((a,b))
            if seg_counter > 0:
                return True
            else:
                return False
        elif self.if_seg_in_counter_list(rev):
            seg_counter = self.get_segment_counter(rev)
            if seg_counter > 0:
                return True
            else:
                return False
        return False


    def is_diagonal_occupied(self, name):
        return any(d == name for d, _ in occupied_diagonals)

    def get_route(self,start, end):
        if self.current_mode == "maneuver":
            key = (start, end)
            if key in routes:
                return routes[key]
            return None
        if self.current_mode == "train":
            key = (start, end)
            if key in train_routes:
                return train_routes[key]
            return None

    def has_switch_conflict(self, a, b):
        key = (a, b)
        if key not in route_switch_modes:
            key = (b, a)
            if key not in route_switch_modes:
                return False

        needed = route_switch_modes[key]

        for rid, data in self.active_routes.items():

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
                    print(f"КОНФЛИКТ: стрелка {diag_name} уже занята маршрутом #{rid}")
                    return True

        return False

    def create_graph(self):
        bannedNames = [("ALB_Sect2", "ALB_Sect0"), ("ALB_Sect1", "ALB_Sect1-2"), ("ALB_Sect1-2", "ALB_Sect2"),]
        for a, b in segments:
            if (a,b) in bannedNames:
                continue
            self.graph.setdefault(a, []).append({"to": b, "type": "track"})
            self.graph.setdefault(b, []).append({"to": a, "type": "track"})

        return self.graph

    def is_edge_available(self, edge):
        if edge["type"] == "track":
            return True

        if edge["type"] == "switch":
            current_state = self.get_switch_state(edge["switch"])
            return current_state == edge["state"]

        return False

    def get_switch_state(self, switch_name):
        return canvas.itemcget(switch_text_ids[switch_name], "text")

    def build_graph(self):
        graph = self.create_graph()
        graph = self.add_switch_edges(graph)
        return graph

    def path_to_segments(self, path):
        result = []

        if not path or len(path) < 2:
            return result

        for i in range(len(path) - 1):
            result.append((path[i], path[i + 1]))

        return result

    def remove_edge(self, graph, a, b):
        if a in graph:
            graph[a] = [edge for edge in graph[a] if edge["to"] != b]

    def add_switch_edges(self, graph):
        self.remove_edge(graph, "M2H1_mid", "M2H1_third")
        self.remove_edge(graph, "M2H1_third", "M2H1_mid")
        self.remove_edge(graph, "M2H1_third", "H1")
        self.remove_edge(graph, "H1", "M2H1_third")
        self.remove_edge(graph, "M6H2", "H2")
        self.remove_edge(graph, "H2", "M6H2")
        self.remove_edge(graph, "M8mid", "M8")
        self.remove_edge(graph, "M8", "M8mid")

        graph.setdefault("M2H1_mid", []).append({
            "to": "M2H1_third",
            "type": "switch",
            "switch": "ALB_Turn2",
            "state": "+"
        })
        graph.setdefault("M2H1_third", []).append({
            "to": "M2H1_mid",
            "type": "switch",
            "switch": "ALB_Turn2",
            "state": "+"
        })

        graph.setdefault("M2H1_third", []).append({
            "to": "H1",
            "type": "switch",
            "switch": "ALB_Turn4-6",
            "state": "+"
        })
        graph.setdefault("H1", []).append({
            "to": "M2H1_third",
            "type": "switch",
            "switch": "ALB_Turn4-6",
            "state": "+"
        })

        graph.setdefault("M6H2", []).append({
            "to": "H2",
            "type": "switch",
            "switch": "ALB_Turn8",
            "state": "+"
        })
        graph.setdefault("H2", []).append({
            "to": "M6H2",
            "type": "switch",
            "switch": "ALB_Turn8",
            "state": "+"
        })

        graph.setdefault("M2H1_third", []).append({
            "to": "M6H2",
            "type": "switch",
            "switch": "ALB_Turn4-6",
            "state": "-"
        })
        graph.setdefault("M6H2", []).append({
            "to": "M2H1_third",
            "type": "switch",
            "switch": "ALB_Turn4-6",
            "state": "-"
        })

        graph.setdefault("M6H2", []).append({
            "to": "H4",
            "type": "switch",
            "switch": "ALB_Turn8",
            "state": "-"
        })
        graph.setdefault("H4", []).append({
            "to": "M6H2",
            "type": "switch",
            "switch": "ALB_Turn8",
            "state": "-"
        })

        graph.setdefault("M10", []).append({
            "to": "M8mid",
            "type": "switch",
            "switch": "ALB_Turn1",
            "state": "-"
        })
        graph.setdefault("M8mid", []).append({
            "to": "M10",
            "type": "switch",
            "switch": "ALB_Turn1",
            "state": "-"
        })

        graph.setdefault("M8mid", []).append({
            "to": "M8",
            "type": "switch",
            "switch": "ALB_Turn1",
            "state": "+"
        })

        graph.setdefault("M8", []).append({
            "to": "M8mid",
            "type": "switch",
            "switch": "ALB_Turn1",
            "state": "+"
        })

        return graph

    def find_path(self, start, end):
        visited = set()

        def dfs(current, path):
            if current == end:
                return path

            visited.add(current)

            for edge in self.graph.get(current, []):
                if not self.is_edge_available(edge):
                    continue

                neighbor = edge["to"]

                if neighbor in visited:
                    continue

                result = dfs(neighbor, path + [neighbor])
                if result is not None:
                    return result

            return None

        return dfs(start, [start])

    def check_route_conflict(self, start, end):
        if self.get_currnet_mode() == "maneuver":
            if self.has_switch_conflict(start, end):
                return True
            for step in routes.get((start, end)):
                if step["type"] == "segment":
                    a, b = step["id"]
                    seg = (a, b)
                    if self.is_segment_occupied(seg) or seg_occ_train.get((a, b),1) == 0 or seg_occ_train.get((b, a), 1) == 0:
                        return True
                elif step["type"] == "diag":
                    if self.is_diagonal_occupied(step["name"]) or diag_occ_train.get(step["name"],1) == 0:
                        return True
            return False

        if self.get_currnet_mode() == "train":
            if self.has_switch_conflict(start, end):
                return True
            for step in train_routes.get((start, end)):
                if step["type"] == "segment":
                    a, b = step["id"]
                    seg = (a, b)
                    if self.is_segment_occupied(seg) or seg_occ_train.get((a, b),1) == 0 or seg_occ_train.get((b, a), 1) == 0:
                        return True
                elif step["type"] == "diag":
                    if self.is_diagonal_occupied(step["name"]) or diag_occ_train.get(step["name"],1) == 0:
                        return True
            return False

    def get_route_counter(self):
        return self.route_counter

    def register_route(self, start, end):
        rid = route_manager.get_route_counter()
        self.add_to_route_id(1)
        if self.get_currnet_mode() == "maneuver":
            for step in routes.get((start, end)):
                if step["type"] == "segment":
                    a, b = step["id"]
                    if self.if_seg_in_counter_list((a,b)):
                        self.segments_active_counter[(a,b)] +=1
                    elif self.if_seg_in_counter_list((b,a)):
                        self.segments_active_counter[(b, a)] += 1
                    occupied_segments.add(((a, b), rid))
                    occupied_segments.add(((b, a), rid))
                elif step["type"] == "diag":
                    occupied_diagonals.add((step["name"], rid))
                    self.diag_active_counter[step["name"]] += 1

            self.active_routes[rid] = {
                "start": start,
                "end": end,
                "segments": routes.get((start, end)),
                "started": False
            }
            SignalManage.active_signal_routes[start] = rid
            return rid
        if self.get_currnet_mode() == "train":
            for step in train_routes.get((start, end)):
                if step["type"] == "segment":
                    a, b = step["id"]
                    if self.if_seg_in_counter_list((a,b)):
                        self.segments_active_counter[(a,b)] +=1
                    else:
                        self.segments_active_counter[(b, a)] += 1
                    occupied_segments.add(((a, b), rid))
                    occupied_segments.add(((b, a), rid))
                elif step["type"] == "diag":
                    occupied_diagonals.add((step["name"], rid))
                    self.diag_active_counter[step["name"]] += 1
            self.active_routes[rid] = {
                "start": start,
                "end": end,
                "segments": train_routes.get((start, end)),
            }
            return rid

    def release_route(self, route_id):
        if route_id not in self.active_routes:
            return
        data = self.active_routes[route_id]
        for step in data["segments"]:
            if step["type"] == "segment":
                a, b = step["id"]
                if any(seg == (a, b) and rid == route_id for seg, rid in occupied_segments):
                    interface_manager.paint_segment((a, b), interface_manager.line_color_main)
                if self.if_seg_in_counter_list((a, b)):
                    self.segments_active_counter[(a, b)] -= 1
                elif self.if_seg_in_counter_list((b, a)):
                    self.segments_active_counter[(b, a)] -= 1
                occupied_segments.discard(((a, b), route_id))
                occupied_segments.discard(((b, a), route_id))
            elif step["type"] == "diag":
                if any(diag == step["name"] and rid == route_id for diag, rid in occupied_diagonals):
                    interface_manager.paint_diagonal(step["name"], interface_manager.line_color_main)
                occupied_diagonals.discard((step["name"], route_id))
                self.diag_active_counter[step["name"]] -= 1
        # recalc_signals_to_red(route_id)
        #SignalManage.recalc_signals_to_red(route_id)
        #SignalManage.return_to_red_after_finishing(route_id)
        start_signal = data.get("start")
        if SignalManage.active_signal_routes.get(start_signal) == route_id:
            del SignalManage.active_signal_routes[start_signal]
        del self.active_routes[route_id]
        self.interface_manager.comboboxDelete(route_id)

    def set_dependencies(self, interface_manager, switch_manager):
        self.interface_manager = interface_manager
        self.switch_manager = switch_manager

    def get_currnet_mode(self):
        return self.current_mode

class SwitchManager:
    settingRoute = False
    def __init__(self):
        self.interface_manager = None
        self.route_manager = None
        self.changingSwitches = False
        self.settingRoute = False


    def initialize_switches(self):
        self.set_diagonal_mode("ALB_Turn1", "left")
        self.set_diagonal_mode("ALB_Turn2", "left")
        self.set_diagonal_mode("ALB_Turn8", "left")
        self.set_diagonal_mode("ALB_Turn4-6", "left")

    def on_switch_mode_selected(self, name, mode):
        text = canvas.itemcget(switch_text_ids[name], "text")
        if mode == 0 and text == "+":
            messagebox.showinfo("Switch Mode", "Стрелка уже в положении '+'")
            return
        elif mode == 1 and text == "-":
            messagebox.showinfo("Switch Mode", "Стрелка уже в положении '-'")
            return
        if self.changingSwitches:
            self.interface_manager.showInfo("Ошибка", "Одна из стерок меняется!")
            return
        if diag_occ_train.get(name, 1) == 0:
            self.interface_manager.showInfo("Ошибка", "Стрелка занята!")
            return

        textALB4_6 = canvas.itemcget(switch_text_ids["ALB_Turn4-6"], "text")
        ALB_Turn1banned = [("M8mid", "M8"), ("M8", "M8_mid")]
        ALB_Turn8banned = [("M6", "M6H2"), ("H2", "M6H2"), ("M6H2", "M6"), ("M6H2", "H2")]
        ALB_Turn4_6banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2"), ("M6", "M6H2"), ("M6H2", "M6"), ("H2", "M6H2"),
                             ("M6H2", "H2")]
        ALB_Turn2banned = [("M2", "M2H1_mid"), ("M2H1_mid", "M2")]

        for num in self.route_manager.active_routes:
            for step in self.route_manager.active_routes[num]["segments"]:
                if name == 'ALB_Turn1':
                    if step["type"] == "segment":
                        if step["id"] in ALB_Turn1banned:
                            self.interface_manager.showInfo("Ошибка", "Стрелка на готовом маршруте!")
                            return
                if name == 'ALB_Turn2':
                    if step["type"] == "segment":
                        if step["id"] in ALB_Turn2banned:
                            self.interface_manager.showInfo("Ошибка", "Стрелка на готовом маршруте!")
                            return
                if name == "ALB_Turn8":
                    if step["type"] == "segment":
                        if step["id"] in ALB_Turn8banned:
                            self.interface_manager.showInfo("Ошибка", "Стрелка на готовом маршруте!")
                            return
                if name == "ALB_Turn4-6":
                    if step["type"] == "segment":
                        if step["id"] in ALB_Turn4_6banned:
                            self.interface_manager.showInfo("Ошибка", "Стрелка на готовом маршруте!")
                            return
                if step["type"] == "diag":
                    if step["name"] == name:
                        self.interface_manager.showInfo("Ошибка", "Используемая стрелка!")
                        return
        if self.settingRoute:
            self.interface_manager.showInfo("Ошибка", "Невозможно сменить стрелку")
            return
        self.changingSwitches = True
        blink_diag(name, duration_ms=2000, interval_ms=200)
        blink_switches([name], duration_ms=2000, interval_ms=200)

        def finalize():
            if mode == 0 and text != "+":
                if name == "ALB_Turn2" and textALB4_6 == "+":
                    canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=6)
                self.set_diagonal_mode(name, "left")
                self.changingSwitches = False
            elif mode == 1 and text != "-":
                self.set_diagonal_mode(name, "right")
                self.changingSwitches = False
            else:
                self.changingSwitches = False
                return

        root.after(2100, finalize)

    def set_diagonal_mode(self, nameDiag, mode):
        self.interface_manager.apply_diagonal_mode( nameDiag=nameDiag, mode=mode)

        diagonal_modes[nameDiag] = mode
        update_switch_indicator(nameDiag)


    def set_dependencies(self, route_manager, interface_manager ):
        self.interface_manager = interface_manager
        self.route_manager = route_manager

    def is_settingRoute(self):
        return self.settingRoute

    def set_settingRoute(self, boolean_variable):
        self.settingRoute = boolean_variable

class interface_manager:
    line_color_main = "white"
    def __init__(self):
        self.line_color_main = "white"
        self.canvas = canvas
        self.node_ids = {}
        self.selected_nodes = []
        self.MAX_SELECTED = 2
        self.switch_manager = None
        self.route_manager = None
        self.drawDeadEnd("pastM1", "right", 0)
        self.drawDeadEnd("past2", "right", 0)
        self.drawDeadEnd("past4", "right", 0)
        self.drawDeadEnd("beforeM6", "left", 0)

        self.signalNodes = []
        button = tkinter.Button(root, text="Отменить маршрут", command=self.snos, relief="flat", bg="#D50063", fg="white",
                                font=("Bahnschrift", 10), )
        button.place(x=1120, y=25)
        buttonAll = tkinter.Button(root, text="Убрать всё", command=self.snosAll, relief="flat", bg="#D50063", fg="white",
                                   font=("Bahnschrift", 10))
        buttonAll.place(x=1250, y=25)
        button = tkinter.Button(root, text="Проверка", command=self.check, relief="flat", bg="#D50063", fg="white",
                                font=("Bahnschrift", 10))
        button.place(x=1340, y=25)
        canvas.tag_bind("node", "<Button-1>", self.on_node_click)
        canvas.tag_bind("node", "<Enter>", self.on_enter)
        canvas.tag_bind("node", "<Leave>", self.on_leave)

        canvas.tag_bind("switch", "<Button-1>", self.on_switch_click)
        canvas.tag_bind("switch", "<Enter>", switch_on_enter)
        canvas.tag_bind("switch", "<Leave>", switch_on_leave)
        self.btn_maneuver = tkinter.Button(
            root,
            text="МАНЕВРОВЫЕ",
            font=("Bahnschrift", 15),
            bg="#3996D5",
            fg="white",
            width=20,
            height=2,
            relief="flat",
            command=self.show_maneuver_routes
        )
        self.btn_train = tkinter.Button(
            root,
            text="ПОЕЗДНЫЕ",
            font=("Bahnschrift", 15),
            bg="grey",
            fg="white",
            relief="flat",
            width=20,
            height=2,
            command=self.show_train_routes
        )
        self.btn_maneuver.place(x=center_x + offset - 100, y=buttons_y)
        self.btn_train.place(x=center_x - offset - 170, y=buttons_y)

        bannedNames = ["pastM1", "beforeM6", "past2", "1STR", "past4", "M6H2", "M2H1_mid",
                       "M8mid", "M2H1_third", "ALB_Sect1-2", "ALB_Sect1", "ALB_Sect2",
                       "ALB_Sect0", "ALB_Sect1-2_2"]

        for name, (x, y) in positions.items():
            if name in bannedNames:
                continue
            node = canvas.create_text(x, y - 28, text=name, tags=(f"node_{name}", "node"), fill=self.line_color_main,
                                      font=("Bahnschrift SemiBold", 14))
            self.node_ids[name] = node



    def showInfo(self, title, msg):
        showinfo(title=title, message=msg)

    def get_btn_maneuver(self):
        return self.btn_maneuver

    def get_btn_train(self):
        return self.btn_train

    def paint_diagonal(self, name, color):
        if name in split_diag_ids:
            return
        for line_id in diag_ids[name]:
            canvas.itemconfig(line_id, fill=color)


    def paint_segment(self, key, color):
        seg_id = segment_ids.get(key)
        if seg_id is None:
            return
        canvas.itemconfig(seg_id, fill=color)


    def drawDeadEnd(self, name, direction, offset):
        x = positions[name][0]
        y = positions[name][1]
        if direction == "right":
            canvas.create_line(x, y, x + offset, y, width=4, fill=self.line_color_main)
            canvas.create_line(x + offset, y - 15, x + offset, y + 15, width=6)
        elif direction == "left":
            canvas.create_line(x, y, x - offset, y, width=4, fill=self.line_color_main)
            canvas.create_line(x - offset, y - 15, x - offset, y + 15, width=6, fill="black")

    def format_routes(self, routes_dict):
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

    def show_maneuver_routes(self):
        if switch_manager.is_settingRoute():
            return
        self.route_manager.set_mode("maneuver")
        self.route_manager.check_visual_mode()
        msg = "Маневровые маршруты:\n\n" + self.format_routes(routes)
        #self.showInfo("МАНЕВРОВЫЕ", msg)

    def show_train_routes(self):
        if switch_manager.is_settingRoute():
            return
        self.route_manager.set_mode("train")
        self.route_manager.check_visual_mode()
        msg = "Поездные маршруты:\n\n" + self.format_routes(train_routes)
        #self.showInfo("ПОЕЗДНЫЕ", msg)

    def setBranchRight(self, nameDiag, offset):
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

    def setBranchLeft(self, nameDiag, offset):
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

    def branchWidth(self, namediag, width):
        if namediag in split_diag_ids.keys():
            for part, lines in split_diag_ids[namediag].items():
                for line_id in lines:
                    canvas.itemconfig(line_id, width=width)
        else:
            for lines in range(len(diag_ids[(namediag)])):
                canvas.itemconfig(diag_ids[namediag][lines], width=width)

    def apply_diagonal_mode(self, nameDiag, mode):
        cfg = diagonal_config.get(nameDiag)
        if cfg is None:
            print(f"No config for {nameDiag}")
            return

        left_cfg = cfg["left"]
        if left_cfg["exists"]:
            if mode in ("left", "both"):
                self.setBranchLeft(nameDiag, left_cfg["connected"])
                self.branchWidth(nameDiag, 6)
                if nameDiag == "ALB_Turn8":
                    canvas.itemconfig(segment_ids[("M6H2", "H2")], width=6)
            else:
                self.setBranchLeft(nameDiag, left_cfg["disconnected"])
                self.branchWidth(nameDiag, 2)
                if nameDiag == "ALB_Turn2":
                    canvas.itemconfig(segment_ids[("M2H1_mid", "M2H1_third")], width=2)

        right_cfg = cfg["right"]
        if right_cfg["exists"]:
            if mode in ("right", "both"):
                self.setBranchRight(nameDiag, right_cfg["connected"])
                self.branchWidth(nameDiag, 6)

                if nameDiag == "ALB_Turn4-6":
                    canvas.itemconfig(segment_ids[("H1", "M2H1_third")], width=2)
                    canvas.itemconfig(segment_ids[("M6", "M6H2")], width=2)
                if nameDiag == "ALB_Turn1":
                    canvas.itemconfig(segment_ids[("M8mid", "M8")], width=2)
                if nameDiag == "ALB_Turn8":
                    canvas.itemconfig(segment_ids[("M6H2", "H2")], width=2)
            else:
                self.setBranchRight(nameDiag, right_cfg["disconnected"])
                self.branchWidth(nameDiag, 2)
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

    def on_switch_click(self, event):
        name = get_switch_name_from_event(event)
        menu = tk.Menu(root, tearoff=0)
        menu.add_command(
            label="-",
            command=lambda: self.switch_manager.on_switch_mode_selected(name, 1)
        )
        menu.add_command(
            label="+",
            command=lambda: self.switch_manager.on_switch_mode_selected(name, 0)
        )
        menu.tk_popup(event.x_root, event.y_root)

    def comboboxDelete(self, ids):
        options = list(combobox1['values'])
        options.remove(str(ids))
        combobox1["values"] = options

    def snos(self):
        selected_item = combobox1.get()
        if selected_item == "":
            return
        num = int(selected_item)
        self.route_manager.release_route(num)
        combobox1.set('')

    def snosAll(self):
        for active in list(self.route_manager.active_routes.keys()):
            self.route_manager.release_route(active)
        combobox1.set('')


    def check(self):
        print(all_keys)
        #print(canvas.coords(switch_indicator_ids.get('ALB_Turn1')))
        #print(canvas.coords(switch_indicator_ids)
        #print("Активные маршруты")
        #print(self.route_manager.active_routes)
        #print("------------------")
        #print("список маршрутов с счётчиком:")
        #print(self.route_manager.segments_active_counter)

    def visualSwitch(self, key):
        list = [("M2", "H1"), ("M2", "M8"), ("M2", "M1"), ("H1", "M2"), ("M1", "M2")]
        needRoutes = [('H1', 'M2H1_third')]
        if key in list:
            canvas.itemconfig(segment_ids[needRoutes[0]], width=6)

    def on_two_nodes_selected(self, a, b):
        if self.route_manager.check_route_conflict(a, b):
            #self.showInfo("Ошибка построения", "Маршрут конфликтует с уже установленными!")
            print("Маршрут конфликтует с уже установленными!")
            #self.reset_node_selection()
            #return
        if switch_manager.is_settingRoute():
            self.reset_node_selection()
            return
        key = (a, b)
        if key not in route_switch_modes:
            key = (b, a)
        if key not in route_switch_modes:
            print("Для этого маршрута нет настроек стрелок")
            self.reset_node_selection()
            return

        route_cfg = route_switch_modes[key]
        last_switch_check = {}
        changed = []
        main_diag = None
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
                self.switch_manager.set_diagonal_mode(diag_name, need_mode)
                changed.append(f"{diag_name}: {current_mode} -> {need_mode}")

        if main_diag is None and route_cfg:
            main_diag = next(iter(route_cfg.keys()))
        self.switch_manager.set_settingRoute(True)
        self.paint_route(a, b, "cyan")
        blink_route(a, b, duration_ms=2000, interval_ms=200)

        if main_diag is not None:
            blink_switches([main_diag], duration_ms=2000, interval_ms=200)
        if changed:
            print("Изменены стрелки:")
            for line in changed:
                print("  ", line)

        self.reset_node_selection()
        self.visualSwitch(key)

        def finalize():
            rid = self.route_manager.register_route(a, b)

            self.paint_route(a, b, "yellow")
            self.switch_manager.set_settingRoute(False)
            current_values = list(combobox1["values"])
            current_values.append(rid)
            combobox1["values"] = tuple(current_values)
            #SignalManage.set_signals_to_route(rid)
            #SignalManage.recalc_signals_from_active_routes((a, b))

        root.after(2100, finalize)

    def highlight_possible_targets(self, start):
        possible = set()
        if route_manager.get_currnet_mode() == "maneuver":
            for (a, b) in routes.keys():
                if a == start:
                    possible.add(b)
        if self.route_manager.get_currnet_mode() == "train":
            for (a, b) in train_routes.keys():
                if a == start:
                    possible.add(b)
        for name, item_id in self.node_ids.items():
            if name == start:
                canvas.itemconfig(item_id, fill="yellow")
                continue
            if name in possible:
                canvas.itemconfig(item_id, fill="#4BFFA7")
                canvas.itemconfig(item_id, state="normal")
            else:
                canvas.itemconfig(item_id, fill="grey")
                canvas.itemconfig(item_id, state="disabled")

        for name in possible:
            canvas.itemconfig(f"node_{name}", state="normal")

    def reset_node_selection(self):
        for name, item_id in self.node_ids.items():
            canvas.itemconfig(item_id, fill=self.line_color_main, state="normal")
        self.selected_nodes.clear()

    def disable_all_except_selected(self):
        for name, item in self.node_ids.items():
            if name in self.selected_nodes:
                canvas.itemconfig(item, state="normal")
            else:
                canvas.itemconfig(item, fill="grey", state="disabled")

    def on_node_click(self, event):
        name = self.get_node_name_from_event(event)
        if name is None:
            return

        if name in self.selected_nodes:
            self.selected_nodes.remove(name)
            canvas.itemconfig(self.node_ids[name], fill=self.line_color_main)
            if len(self.selected_nodes) == 0:
                self.reset_node_selection()
            if len(self.selected_nodes) == 1:
                self.highlight_possible_targets(self.selected_nodes[0])
            return

        if len(self.selected_nodes) >= self.MAX_SELECTED:
            return

        self.selected_nodes.append(name)
        canvas.itemconfig(self.node_ids[name], fill="cyan")

        if len(self.selected_nodes) == 1:
            self.highlight_possible_targets(name)
        if len(self.selected_nodes) == 2:
            first = self.selected_nodes[0]
            second = self.selected_nodes[1]
            self.disable_all_except_selected()
            self.on_two_nodes_selected(first, second)

    def apply_mode_visuals(self):
        for name, item_id in self.node_ids.items():
            color = "white"
            state = "normal"
            if self.route_manager.get_currnet_mode() == "maneuver" and name == "CH":
                color = "grey"
                state = "disabled"
            canvas.itemconfig(item_id, fill=color, state=state)

    def set_dependencies(self, route_manager, switch_manager):
        self.switch_manager = switch_manager
        self.route_manager = route_manager

    def on_enter(self, event):
        name = self.get_node_name_from_event(event)
        if name is None:
            return

        if name not in self.selected_nodes:
            canvas.itemconfig(self.node_ids[name], fill="#e01dcd")

    def on_leave(self, event):
        name = self.get_node_name_from_event(event)
        if name is None:
            return
        if name not in self.selected_nodes:
            if len(self.selected_nodes) == 1:
                canvas.itemconfig(self.node_ids[name], fill="#4BFFA7")
            else:
                canvas.itemconfig(self.node_ids[name], fill=self.line_color_main)

    def get_node_name_from_event(self, event):
        items = canvas.find_withtag("current")
        if not items:
            return None

        item = items[0]
        tags = canvas.gettags(item)

        for t in tags:
            if t.startswith("node_"):
                return t.replace("node_", "")
        return None

    def paint_route(self, start, end, color="yellow"):
        key = (start, end)
        if route_manager.get_currnet_mode() == "maneuver":
            if key not in routes:
                key = (end, start)
                if key not in routes:
                    print("Маршрут не найден")
                    return

            for step in routes[key]:
                if step["type"] == "segment":
                    interface_manager.paint_segment(step["id"], color)

                elif step["type"] == "diag":
                    interface_manager.paint_diagonal(step["name"], color)

                else:
                    print("Неизвестный тип шага:", step)
        if route_manager.get_currnet_mode() == "train":
            if key not in train_routes:
                key = (end, start)
                if key not in train_routes:
                    print("Маршрут не найден")
                    return

            for step in train_routes[key]:
                if step["type"] == "segment":
                    self.paint_segment(step["id"], color)
                elif step["type"] == "diag":
                    self.paint_diagonal(step["name"], color)
                else:
                    print("Неизвестный тип шага:", step)

blinking_diags = set()
blinking_routes = set()
diag_ids = {}
diagonal_modes = {}

last_switch_check = {}

occupied_segments = set()
occupied_diagonals = set()

switch_ids = {}
segment_ids = {}

split_diag_ids = {}
switch_text_ids = {}
switch_indicator_ids = {}


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
    ("M2H1_third","H1") : 1,
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
    for element in segs:
        if element["type"] == "segment":
            seg = element["id"]
            segment_to_block[seg] = block
            segment_to_block[(seg[1], seg[0])] = block
            segment_to_block_type[seg] = element["type"]
            segment_to_block_type[(seg[1], seg[0])] = element["type"]
        elif element["type"] == "diag":
            diag = element["name"]
            segment_to_block[diag] = block
            segment_to_block_type[diag] = element["type"]




SIGNAL_OFF_COLOR = "#202020"
signal_blink_phase = False
DEBUG_SIGNALS_FRAME = False

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
        return "red" # неизвестный режим
    if mode == normal:
        return "#538c65"    # плюс, нормальное положение
    else:
        return "#71a2bd"   # переведена

def update_switch_indicator(name):
    rect = switch_indicator_ids.get(name)
    labelSwitch = switch_text_ids.get(name)
    if rect is None:
        return
    color = get_switch_state_color(name)
    text = get_switch_state_num(name)
    canvas.itemconfig(rect, fill=color)
    canvas.itemconfig(labelSwitch, text=text)


def create_switch_table():
    w = int(canvas["width"])
    h = int(canvas["height"])

    dy = 25
    total_height = dy * len(switch_list)
    y_start = h - total_height - 520

    x_text = w - 980
    x_rect = w - 980

    for i, name in enumerate(switch_list, start=1):
        y = y_start + (i - 1) * dy
        switch = canvas.create_text(x_text, y, text=f"{i}. {name}", anchor="w", font=("Bahnschrift SemiBold", 13), tags=(f"switch_{name}", "switch"), fill="white" )
        switch_ids[name] = switch
        label = canvas.create_text(x_rect+90, y, text="0", font=("Bahnschrift SemiBold", 14), fill="white",  tags=(f"switch_{name}", "switch"))

        rect = canvas.create_rectangle(
            x_rect + 110, y - 9, x_rect + 130, y + 9,
            outline="black", fill="grey",  tags=(f"switch_{name}", "switch")
        )
        switch_text_ids[name] = label
        switch_indicator_ids[name] = rect
        update_switch_indicator(name)

create_switch_table()

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

def blink_switches_table(diags, duration_ms=2000, interval_ms=200):
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
                canvas.itemconfig(rect, fill="#4c86a6" if state else final_colors[d])
        root.after(interval_ms, _step, not state)

    _step(True)

def AddDiagonal(x1, y1, x2, y2, offsetleft, offsetright, nameDiag):
    l1 = canvas.create_line(x1, y1, x1 - offsetleft, y1, width=3, fill=interface_manager.line_color_main)
    l2 = canvas.create_line(x2, y2, x2 + offsetright, y2, width=3, fill=interface_manager.line_color_main)
    l3 = canvas.create_line(x1, y1, x2, y2, width=3, fill=interface_manager.line_color_main)
    diag_ids[(nameDiag)] = [l1, l2, l3]

def AddSplitDiagonal(x1, y1, x2, y2,
                     x3, y3,offset_left,
                     offset_right, nameDiag, namePart1, namePart2):
    l2 = canvas.create_line(x1, y1, x2, y2, width=3, fill=interface_manager.line_color_main)
    l3 = canvas.create_line(x2, y2, x3, y3, width=3, fill=interface_manager.line_color_main)
    l1 = canvas.create_line(x1, y1, x1 - offset_left, y1, width=3, fill=interface_manager.line_color_main)
    l4 = canvas.create_line(x3, y3, x3 + offset_right, y3, width=3, fill=interface_manager.line_color_main)
    split_diag_ids[nameDiag] = {
        'partA': [l1, l2],
        'partB': [l3, l4]
    }
    diag_ids[(namePart1)] = [l1, l2]
    diag_ids[(namePart2)] = [l3, l4]

for a, b in segments:
    x1, y1 = positions[a]
    x2, y2 = positions[b]
    a_and_b = (a,b)
    BlockSegments = [("M1","M8mid")]
    BlockSeg2 = [("CH", "M2")]
    if a_and_b in BlockSegments:
        seg = canvas.create_line(x1, y1, x2-5, y2, width=6, fill=interface_manager.line_color_main)
    elif a_and_b in BlockSeg2:
        seg = canvas.create_line(x1 -1, y1, x2 -13, y2, width=6, fill=interface_manager.line_color_main)
    else:
        seg = canvas.create_line(x1 - 5, y1, x2 + 5, y2, width=6, fill=interface_manager.line_color_main)
    segment_ids[(a, b)] = seg
    segment_ids[(b, a)] = seg


AddDiagonal(260, 330, 350, 430, 20, 38, "ALB_Turn2")
AddDiagonal(965, 330, 890, 430, -22, -37, "ALB_Turn1")
AddDiagonal(560, 130, 470, 230, -57, -20, "ALB_Turn8")
AddSplitDiagonal(430, 230, 390, 280,350, 330, -30, -30, "ALB_Turn4-6", "ALB_Turn4", "ALB_Turn6")

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

def switch_on_enter(event):
    name = get_switch_name_from_event(event)
    canvas.itemconfig(switch_ids[name], fill="pink")

def switch_on_leave(event):
    name = get_switch_name_from_event(event)
    canvas.itemconfig(switch_ids[name], fill=interface_manager.line_color_main)

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
            interface_manager.paint_route(start, end, interface_manager.line_color_main)
            return

        color = "#75CEFF" if state else interface_manager.line_color_main
        interface_manager.paint_route(start, end, color)
        root.after(interval_ms, _step, not state)
    _step(True)

def blink_diag(name, duration_ms=2000, interval_ms=200):
    print(name)
    blinking_diags.add(name)
    end_time = time.time() + duration_ms / 1000.0

    def _step(state=True):
        if time.time() >= end_time:
            if name == "ALB_Turn4-6":
                interface_manager.paint_diagonal("ALB_Turn4", interface_manager.line_color_main)
                interface_manager.paint_diagonal("ALB_Turn6", interface_manager.line_color_main)
            else:
                interface_manager.paint_diagonal(name, interface_manager.line_color_main)
            return

        color = "#75CEFF" if state else interface_manager.line_color_main
        if name == "ALB_Turn4-6":
            interface_manager.paint_diagonal("ALB_Turn4", color)
            interface_manager.paint_diagonal("ALB_Turn6", color)
        else:
            interface_manager.paint_diagonal(name, color)
        root.after(interval_ms, _step, not state)
    _step(True)

def checkOccupied():
    print(occupied_segments)



def init_arduino():
    global arduino, arduino_status_label

    try:
        ports = list(serial.tools.list_ports.comports())
        arduino_port = None

        for p in ports:
            if "Arduino" in p.description or "CH340" in p.description:
                arduino_port = p.device
                break

        if arduino_port is None:

            arduino_port = "COM3"

        arduino = serial.Serial(arduino_port, 9600, timeout=1)
        time.sleep(2)

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
    global arduino  # Должно совпадать с именем в init_arduino()

    if arduino is not None and arduino.is_open:
        try:
            while arduino.in_waiting > 0:
                # Читаем строку целиком
                line = arduino.readline().decode('utf-8', errors='ignore').strip()

                if line:
                    # Выводим в консоль для контроля (ты это уже видишь)
                    print(f"ПОЛУЧЕНО: {line}")
                    # Скармливаем строку парсеру
                    parse_arduino_string(line, seg_occ_train, diag_occ_train)
        except Exception as e:
            print(f"Ошибка чтения порта: {e}")

    # Запуск следующей итерации опроса
    root.after(100, poll_arduino)

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

def signal_visual_change():
    if not SignalManage.get_simple_state():
        SignalManage.enable_simple_mode()
        SignalManage.change_simple_mode(True)
        button_visual_change.config(text="Выключить упрощённый")
    else:
        SignalManage.disable_simple_mode()
        SignalManage.change_simple_mode(False)
        button_visual_change.config(text="Включить упрощённый")


arduino_status_label = tkinter.Label(root, text="Arduino: проверка...", fg="orange",  font=("Bahnschrift bold", 12))
arduino_status_label.place(x=300, y=16)

n = tkinter.StringVar()
combobox1 = ttk.Combobox(root, width = 25, height=25, textvariable = n, state='readonly', font=("Bahnschrift bold", 9))
combobox1.place(x=510,y=20)

# button = tkinter.Button(root, text="Проверка occupied", command=checkOccupied, relief="flat", bg="#D50063", fg="white", font=("Bahnschrift", 10))
# button.place(x=880, y=40)

button_visual_change = tkinter.Button(root, text="Упрощённый: ", command=signal_visual_change, relief="flat", bg="#D50063", fg="white", font=("Bahnschrift", 10))
button_visual_change.place(x=1450, y=25)
buttons_y = CANVAS_H - 350

center_x = CANVAS_W // 2
offset = 140

all_keys = list(seg_occ_train.keys()) + list(diag_occ_train.keys())

def do(button_id):
    if button_id < len(seg_occ_train):
        seg = list(seg_occ_train.keys())[button_id]
        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s['type'] == "segment":
                    seg_occ_train[s['id']] = 1 if seg_occ_train[s['id']] == 0 else 0
                elif s["type"] == "diag":
                    diag_occ_train[s['name']] = 1 if diag_occ_train[s['name']] == 0 else 0
        else:
            seg_occ_train[seg] = 1 if seg_occ_train[seg_occ_dict] == 0 else 0
    else:
        seg = list(diag_occ_train.keys())[button_id - len(seg_occ_train)]
        block = segment_to_block.get(seg)
        if block:
            for s in segment_groups[block]:
                if s['type'] == "segment":
                    seg_occ_train[s['id']] = 1 if seg_occ_train[s['id']] == 0 else 0
                elif s["type"] == "diag":
                    diag_occ_train[s['name']] = 1 if diag_occ_train[s['name']] == 0 else 0
        else:
            diag_occ_train[seg] = 1 if diag_occ_train[seg] == 0 else 0

for i, key in enumerate(all_keys):
    button69 = tkinter.Button(
        root,
        text=str(key),
        command=lambda id=i: do(id),
        relief="flat", font=("Bahnschrift light", 12),
    )
    button69.place(x=1700, y=120 + i * 35)


init_arduino()
poll_arduino()
occupancy_manager = OccupancyManager(canvas, root)
SignalManage = SignalManager(canvas, root)
SignalManage.initialize_signals()
SignalManage.bind_invite_button()
route_manager = RouteManager()
SignalManage.set_dependencies(route_manager)
switch_manager = SwitchManager()
interface_manager = interface_manager()
switch_manager.set_dependencies(route_manager, interface_manager)
route_manager.set_dependencies(interface_manager, switch_manager)
interface_manager.set_dependencies(route_manager, switch_manager)
occupancy_manager.set_dependencies(interface_manager, route_manager, SignalManage)
occupancy_manager.update_all_occupancy()
switch_manager.initialize_switches()
route_manager.check_visual_mode()
root.protocol('WM_DELETE_WINDOW', quit_function)
canvas.scale("all", 0, 0, 1.5, 1.5)
canvas.pack()

root.mainloop()
