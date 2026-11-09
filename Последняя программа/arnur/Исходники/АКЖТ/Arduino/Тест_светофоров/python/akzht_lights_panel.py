# -*- coding: utf-8 -*-
"""
Тестер светофоров АКЖТ: сетка байт×бит с нуля.
Клик = вкл/выкл этого бита (можно несколько сразу).
Station.exe закрыть. Прошивка: AKZHT_Lights_Test.ino
"""
from __future__ import annotations

import time
import tkinter as tk
from tkinter import messagebox, ttk

import serial
from serial.tools import list_ports

from akzht_lights_map import FRAME_BYTES, HW_MAP, empty_frame, frame_to_l_command

BAUD = 9600

BIT_NAME: dict[tuple[int, int], str] = {
    pos: f"{sig}.{lamp}" for (sig, lamp), pos in HW_MAP.items()
}

ON_BG = "#90EE90"
OFF_BG = "#e8e8e8"


def list_com() -> list[str]:
    return [p.device for p in list_ports.comports()]


def guess_port() -> str:
    for p in list_ports.comports():
        d = (p.description or "").lower()
        if any(k in d for k in ("ftdi", "ch340", "arduino", "usb serial")):
            return p.device
    ports = list_com()
    return ports[0] if ports else "COM5"


class App(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("АКЖТ — биты вкл/выкл (несколько сразу)")
        self.geometry("980x720")
        self.ser: serial.Serial | None = None
        self.frame = empty_frame()
        self.bit_btns: dict[tuple[int, int], tk.Button] = {}
        self._build()
        self.after(200, self.refresh_ports)

    def _build(self) -> None:
        top = ttk.Frame(self, padding=6)
        top.pack(fill="x")
        ttk.Label(top, text="COM").pack(side="left")
        self.port_var = tk.StringVar(value=guess_port())
        self.port_box = ttk.Combobox(top, textvariable=self.port_var, width=22, values=[])
        self.port_box.pack(side="left", padx=4)
        ttk.Button(top, text="Обновить", command=self.refresh_ports).pack(side="left")
        ttk.Button(top, text="Подключить", command=self.connect).pack(side="left", padx=4)
        ttk.Button(top, text="Отключить", command=self.disconnect).pack(side="left")
        ttk.Button(top, text="Всё гасить", command=self.all_off).pack(side="left", padx=12)
        ttk.Button(top, text="Всё горит", command=self.all_on).pack(side="left")
        self.status = tk.StringVar(value="Клик = вкл/выкл. Можно несколько зелёных сразу.")
        ttk.Label(top, textvariable=self.status).pack(side="left", padx=10)

        ttk.Label(
            self,
            text="Ряд = байт 0…12. Кнопки = бит 0…7. Зелёная = горит, серая = выкл. Повторный клик гасит.",
            padding=4,
        ).pack(fill="x")

        canvas = tk.Canvas(self, highlightthickness=0)
        scroll = ttk.Scrollbar(self, orient="vertical", command=canvas.yview)
        canvas.configure(yscrollcommand=scroll.set)
        scroll.pack(side="right", fill="y")
        canvas.pack(side="left", fill="both", expand=True)

        grid = ttk.Frame(canvas, padding=8)
        win = canvas.create_window((0, 0), window=grid, anchor="nw")

        def _cfg(_e=None):
            canvas.configure(scrollregion=canvas.bbox("all"))
            canvas.itemconfigure(win, width=canvas.winfo_width())

        grid.bind("<Configure>", _cfg)
        canvas.bind("<Configure>", _cfg)

        ttk.Label(grid, text="байт\\бит", width=8).grid(row=0, column=0, padx=2, pady=2)
        for bit in range(8):
            ttk.Label(grid, text=str(bit), width=6, anchor="center").grid(row=0, column=bit + 1)

        for b in range(FRAME_BYTES):
            ttk.Label(grid, text=f"байт {b}", width=8).grid(row=b + 1, column=0, sticky="e", padx=4)
            for bit in range(8):
                name = BIT_NAME.get((b, bit), "")
                short = (
                    name.replace("yellow1", "Ж↓")
                    .replace("yellow", "Ж")
                    .replace("green", "З")
                    .replace("red", "К")
                    .replace("white", "Б")
                    .replace("blue", "С")
                )
                txt = f"{bit}" + (f"\n{short}" if short else "")
                btn = tk.Button(
                    grid,
                    text=txt,
                    width=8,
                    height=2,
                    relief="raised",
                    bg=OFF_BG,
                    command=lambda bb=b, bt=bit: self.toggle_bit(bb, bt),
                )
                btn.grid(row=b + 1, column=bit + 1, padx=2, pady=2)
                self.bit_btns[(b, bit)] = btn

        self.log = tk.Text(self, height=6, wrap="word")
        self.log.pack(fill="x", padx=8, pady=4)
        self._log("Несколько ламп сразу: жми нужные биты (зелёный=вкл). Ещё раз — выкл.")

    def _log(self, msg: str) -> None:
        self.log.insert("end", msg + "\n")
        self.log.see("end")

    def is_on(self, b: int, bit: int) -> bool:
        return (self.frame[b] & (1 << bit)) == 0

    def refresh_btn(self, b: int, bit: int) -> None:
        on = self.is_on(b, bit)
        btn = self.bit_btns[(b, bit)]
        btn.configure(bg=ON_BG if on else OFF_BG, relief="sunken" if on else "raised")

    def refresh_all_btns(self) -> None:
        for b, bit in self.bit_btns:
            self.refresh_btn(b, bit)

    def push_frame(self) -> None:
        if not self.ser or not self.ser.is_open:
            messagebox.showwarning("COM", "Сначала Подключить")
            return
        self.ser.write(frame_to_l_command(self.frame))
        self.ser.flush()

    def toggle_bit(self, b: int, bit: int) -> None:
        if self.is_on(b, bit):
            self.frame[b] |= 1 << bit  # выкл (1)
            state = "ВЫКЛ"
        else:
            self.frame[b] &= ~(1 << bit)  # вкл (0)
            state = "ВКЛ"
        self.refresh_btn(b, bit)
        self.push_frame()
        who = BIT_NAME.get((b, bit), "???")
        self._log(f"{state}  {b}.{bit}  {who}")

    def refresh_ports(self) -> None:
        ports = list_com()
        self.port_box["values"] = ports
        if self.port_var.get() not in ports and ports:
            self.port_var.set(guess_port())

    def connect(self) -> None:
        self.disconnect()
        port = self.port_var.get().strip()
        try:
            self.ser = serial.Serial(port, BAUD, timeout=0.3)
            time.sleep(2.0)
            banner = self.ser.read(200).decode("ascii", errors="ignore")
            self.status.set(f"OK {port}")
            self._log(f"{port}: {banner.strip() or '(нет LIGHTS TEST — залей скетч)'}")
            self.all_off()
        except Exception as e:
            self.ser = None
            messagebox.showerror("COM", str(e))

    def disconnect(self) -> None:
        if self.ser:
            try:
                self.ser.close()
            except Exception:
                pass
        self.ser = None
        self.status.set("отключено")

    def all_off(self) -> None:
        self.frame = empty_frame()
        self.refresh_all_btns()
        if self.ser and self.ser.is_open:
            self.ser.write(b"A")
            self.ser.flush()
        self._log("всё гасить")

    def all_on(self) -> None:
        self.frame = bytearray([0x00] * FRAME_BYTES)
        self.refresh_all_btns()
        if self.ser and self.ser.is_open:
            self.ser.write(b"Z")
            self.ser.flush()
        self._log("всё горит")

    def on_close(self) -> None:
        self.disconnect()
        self.destroy()


if __name__ == "__main__":
    app = App()
    app.protocol("WM_DELETE_WINDOW", app.on_close)
    app.mainloop()
