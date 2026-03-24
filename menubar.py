#!/usr/bin/env python3
"""
menubar.py — Windows 11 Custom AppBar
A macOS-style menu bar with Windows 11 / WinUI 3 theming.
Features: battery, network, media controls, accent color, settings.
"""

import sys
import os
import math
import json
import asyncio
import ctypes
import ctypes.wintypes
import subprocess
import traceback
import logging
import winreg
from datetime import datetime
from ctypes import windll, wintypes
from pathlib import Path

# ── Logging ───────────────────────────────────────────────────
_LOG = Path(__file__).parent / "menubar.log"
logging.basicConfig(filename=str(_LOG), filemode="w", level=logging.DEBUG,
                    format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger("menubar")
log.addHandler(logging.StreamHandler(sys.stdout))
log.info("Starting")

import psutil
from PyQt6.QtWidgets import (
    QApplication, QWidget, QHBoxLayout, QVBoxLayout,
    QLabel, QFrame, QSizePolicy,
)
from PyQt6.QtCore import Qt, QTimer, QPoint, QRect, QRectF
from PyQt6.QtGui import QFont, QColor, QPainter, QPen, QFontDatabase, QLinearGradient

# ── Media (optional — graceful fallback) ──────────────────────
try:
    from winrt.windows.media.control import (
        GlobalSystemMediaTransportControlsSessionManager as MediaSessionManager,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus as PlaybackStatus,
    )
    _HAS_MEDIA = True
    log.info("winrt media control: OK")
except ImportError:
    _HAS_MEDIA = False
    log.warning("winrt not installed — media controls disabled")

# ─────────────────────────────────────────────────────────────
#  Settings
# ─────────────────────────────────────────────────────────────
_SETTINGS_PATH = Path(__file__).parent / "settings.json"
_DEFAULT_SETTINGS = {
    "bar_height": 28,
    "show_battery": True,
    "show_network": True,
    "show_clock": True,
    "show_media": True,
    "show_title": True,
    "clock_24h": False,
    "use_accent_color": True,
    "show_windows_logo": True,
}

def _load_settings() -> dict:
    cfg = dict(_DEFAULT_SETTINGS)
    if _SETTINGS_PATH.exists():
        try:
            with open(_SETTINGS_PATH, "r") as f:
                user = json.load(f)
            cfg.update(user)
            log.info("Settings loaded from %s", _SETTINGS_PATH)
        except Exception as e:
            log.warning("Settings load error: %s", e)
    else:
        _save_settings(cfg)
        log.info("Default settings written to %s", _SETTINGS_PATH)
    return cfg

def _save_settings(cfg: dict):
    with open(_SETTINGS_PATH, "w") as f:
        json.dump(cfg, f, indent=4)

SETTINGS = _load_settings()

# ─────────────────────────────────────────────────────────────
#  Win32 constants + structures
# ─────────────────────────────────────────────────────────────
ABM_NEW, ABM_REMOVE, ABM_QUERYPOS, ABM_SETPOS = 0, 1, 2, 3
ABE_TOP = 1
SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN, SM_CXVIRTUALSCREEN = 76, 77, 78
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_SYSTEMBACKDROP_TYPE     = 38
DWMSBT_MAINWINDOW             = 2
DWMWA_MICA_EFFECT             = 1029
WCA_ACCENT_POLICY             = 19
ACCENT_ENABLE_ACRYLIC         = 4
HWND_TOPMOST = -1
SWP_NOSIZE, SWP_NOMOVE, SWP_NOZORDER = 0x0001, 0x0002, 0x0004
SWP_NOACTIVATE, SWP_SHOWWINDOW = 0x0010, 0x0040
SWP_FRAMECHANGED, SWP_NOOWNERZORDER = 0x0020, 0x0200
VK_LWIN, VK_N, KEYEVENTF_KEYUP = 0x5B, 0x4E, 0x0002
VK_MEDIA_PLAY_PAUSE = 0xB3
VK_MEDIA_NEXT_TRACK = 0xB0
VK_MEDIA_PREV_TRACK = 0xB1
GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_APPWINDOW = 0x00040000

# Power broadcast
WM_POWERBROADCAST      = 0x0218
PBT_APMSUSPEND         = 0x0004
PBT_APMRESUMEAUTOMATIC = 0x0012
PBT_APMRESUMESUSPEND   = 0x0007

class APPBARDATA(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.DWORD), ("hWnd", wintypes.HWND),
                ("uCallbackMessage", wintypes.UINT), ("uEdge", wintypes.UINT),
                ("rc", wintypes.RECT), ("lParam", wintypes.LPARAM)]
class MARGINS(ctypes.Structure):
    _fields_ = [("l", ctypes.c_int), ("r", ctypes.c_int),
                ("t", ctypes.c_int), ("b", ctypes.c_int)]
class ACCENT_POLICY(ctypes.Structure):
    _fields_ = [("AccentState", ctypes.c_int), ("AccentFlags", ctypes.c_int),
                ("GradientColor", ctypes.c_int), ("AnimationId", ctypes.c_int)]
class WCAD(ctypes.Structure):
    _fields_ = [("Attribute", ctypes.c_int), ("Data", ctypes.c_void_p),
                ("SizeOfData", ctypes.c_size_t)]

# ─────────────────────────────────────────────────────────────
#  AppBar registration
# ─────────────────────────────────────────────────────────────
def register_appbar(hwnd, height, register=True):
    l, t, w = _virtual_screen_metrics()
    abd = APPBARDATA()
    abd.cbSize = ctypes.sizeof(APPBARDATA)
    abd.hWnd = hwnd; abd.uCallbackMessage = 0x0401; abd.uEdge = ABE_TOP
    abd.rc.left, abd.rc.top, abd.rc.right, abd.rc.bottom = l, t, l + w, t + height
    if register:
        windll.shell32.SHAppBarMessage(ABM_NEW, ctypes.byref(abd))
    windll.shell32.SHAppBarMessage(ABM_QUERYPOS, ctypes.byref(abd))
    abd.rc.bottom = abd.rc.top + height
    windll.shell32.SHAppBarMessage(ABM_SETPOS, ctypes.byref(abd))
    log.info("[AppBar] (%d,%d)->(%d,%d)", abd.rc.left, abd.rc.top, abd.rc.right, abd.rc.bottom)

def unregister_appbar(hwnd):
    abd = APPBARDATA()
    abd.cbSize = ctypes.sizeof(APPBARDATA); abd.hWnd = hwnd
    windll.shell32.SHAppBarMessage(ABM_REMOVE, ctypes.byref(abd))

def _hide_window_from_alt_tab(hwnd):
    try:
        user32 = windll.user32
        get_long = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
        set_long = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
        exstyle = get_long(hwnd, GWL_EXSTYLE)
        exstyle = (exstyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW
        set_long(hwnd, GWL_EXSTYLE, exstyle)
        user32.SetWindowPos(
            hwnd, 0, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER |
            SWP_NOOWNERZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE,
        )
        log.info("[Window] Alt-Tab hidden")
    except Exception as e:
        log.warning("[Window] Failed to hide from Alt-Tab: %s", e)

# ─────────────────────────────────────────────────────────────
#  Backdrop (Mica -> acrylic cascade)
# ─────────────────────────────────────────────────────────────
def apply_backdrop(hwnd):
    dwm = windll.dwmapi
    try:
        m = MARGINS(-1, -1, -1, -1)
        dwm.DwmExtendFrameIntoClientArea(hwnd, ctypes.byref(m))
        dk = ctypes.c_int(1)
        dwm.DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ctypes.byref(dk), ctypes.sizeof(dk))
        bd = ctypes.c_int(DWMSBT_MAINWINDOW)
        if dwm.DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ctypes.byref(bd), ctypes.sizeof(bd)) == 0:
            log.info("[Backdrop] Mica"); return True
        mic = ctypes.c_int(1)
        if dwm.DwmSetWindowAttribute(hwnd, DWMWA_MICA_EFFECT, ctypes.byref(mic), ctypes.sizeof(mic)) == 0:
            log.info("[Backdrop] Mica legacy"); return True
    except Exception as e:
        log.warning("[Backdrop] DWM: %s", e)
    try:
        ac = ACCENT_POLICY(); ac.AccentState = ACCENT_ENABLE_ACRYLIC; ac.GradientColor = 0xCC141414
        wd = WCAD(); wd.Attribute = WCA_ACCENT_POLICY
        wd.Data = ctypes.cast(ctypes.pointer(ac), ctypes.c_void_p); wd.SizeOfData = ctypes.sizeof(ac)
        windll.user32.SetWindowCompositionAttribute(hwnd, ctypes.pointer(wd))
        log.info("[Backdrop] Acrylic"); return True
    except Exception as e:
        log.warning("[Backdrop] Acrylic: %s", e)
    return False

# ─────────────────────────────────────────────────────────────
#  Windows accent color
# ─────────────────────────────────────────────────────────────
_accent_color = None  # set in main()
_BAR_INSTANCE = None
ENABLE_NATIVE_INTEGRATION = True

def _read_accent_color() -> QColor | None:
    try:
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\DWM")
        val, _ = winreg.QueryValueEx(key, "AccentColor")
        winreg.CloseKey(key)
        val = val & 0xFFFFFFFF
        # Registry stores as ABGR
        r = val & 0xFF
        g = (val >> 8) & 0xFF
        b = (val >> 16) & 0xFF
        log.info("[Accent] #%02x%02x%02x", r, g, b)
        return QColor(r, g, b)
    except Exception as e:
        log.warning("[Accent] %s", e)
        return None

def _read_personalize_dword(name: str, default: int = 0) -> int:
    try:
        key = winreg.OpenKey(
            winreg.HKEY_CURRENT_USER,
            r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
        )
        val, _ = winreg.QueryValueEx(key, name)
        winreg.CloseKey(key)
        return int(val)
    except Exception:
        return default

# ─────────────────────────────────────────────────────────────
#  System info
# ─────────────────────────────────────────────────────────────
def get_active_window_title():
    hwnd = windll.user32.GetForegroundWindow()
    if not hwnd: return ""
    n = windll.user32.GetWindowTextLengthW(hwnd)
    if n == 0: return ""
    buf = ctypes.create_unicode_buffer(n + 1)
    windll.user32.GetWindowTextW(hwnd, buf, n + 1)
    t = buf.value
    return (t[:52] + "\u2026") if len(t) > 52 else t

def get_battery_info():
    b = psutil.sensors_battery()
    if b is None:
        return {"has_battery": False, "percent": None, "charging": False,
                "plugged": True, "secs_left": None}
    sl = b.secsleft
    return {
        "has_battery": True,
        "percent":     b.percent,
        "charging":    b.power_plugged and b.percent < 99,
        "plugged":     b.power_plugged,
        "secs_left":   None if sl in (psutil.POWER_TIME_UNLIMITED, psutil.POWER_TIME_UNKNOWN) else sl,
    }

def _parse_netsh():
    out = {}
    try:
        r = subprocess.run(["netsh", "wlan", "show", "interfaces"],
                           capture_output=True, text=True, timeout=3,
                           creationflags=subprocess.CREATE_NO_WINDOW)
        for line in r.stdout.splitlines():
            if ":" in line:
                k, _, v = line.partition(":")
                out[k.strip()] = v.strip()
    except Exception:
        pass
    return out

def get_network_info():
    connected = False
    try:
        for name, stats in psutil.net_if_stats().items():
            lname = name.lower()
            if not stats.isup:
                continue
            if lname.startswith("loopback") or "loopback" in lname:
                continue
            if "virtual" in lname or "vethernet" in lname:
                continue
            connected = True
            break
    except Exception:
        pass

    nd = _parse_netsh()
    is_wifi   = "connected" in nd.get("State", "").lower()
    wifi_name = nd.get("SSID", "")
    signal    = 3
    rx_mbps   = nd.get("Receive rate (Mbps)", "")
    tx_mbps   = nd.get("Transmit rate (Mbps)", "")

    sig_str = nd.get("Signal", "")
    if sig_str:
        try:
            pct    = int(sig_str.replace("%", ""))
            signal = 3 if pct >= 66 else (2 if pct >= 33 else 1)
        except Exception:
            pass

    return {
        "connected": connected, "is_wifi": is_wifi,
        "wifi_name": wifi_name, "signal": signal,
        "rx_mbps": rx_mbps, "tx_mbps": tx_mbps,
    }

def sleep_system():
    subprocess.Popen(["rundll32.exe", "powrprof.dll,SetSuspendState", "0,1,0"],
                     creationflags=subprocess.CREATE_NO_WINDOW)
def restart_system():
    subprocess.Popen(["shutdown", "/r", "/t", "0"],
                     creationflags=subprocess.CREATE_NO_WINDOW)
def shutdown_system():
    subprocess.Popen(["shutdown", "/s", "/t", "0"],
                     creationflags=subprocess.CREATE_NO_WINDOW)

def open_notification_center():
    u = windll.user32
    u.keybd_event(VK_LWIN, 0, 0, 0)
    u.keybd_event(VK_N, 0, 0, 0)
    u.keybd_event(VK_N, 0, KEYEVENTF_KEYUP, 0)
    u.keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0)

# ─────────────────────────────────────────────────────────────
#  Media info + controls
# ─────────────────────────────────────────────────────────────
def _send_media_key(vk):
    windll.user32.keybd_event(vk, 0, 0, 0)
    windll.user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)

def media_play_pause(): _send_media_key(VK_MEDIA_PLAY_PAUSE)
def media_next():       _send_media_key(VK_MEDIA_NEXT_TRACK)
def media_prev():       _send_media_key(VK_MEDIA_PREV_TRACK)

def get_media_info() -> dict | None:
    if not _HAS_MEDIA:
        return None
    try:
        async def _inner():
            manager = await MediaSessionManager.request_async()
            session = manager.get_current_session()
            if not session:
                return None
            info = await session.try_get_media_properties_async()
            playback = session.get_playback_info()
            return {
                "title":   info.title or "",
                "artist":  info.artist or "",
                "playing": playback.playback_status == PlaybackStatus.PLAYING,
            }
        return asyncio.run(_inner())
    except Exception as e:
        log.debug("Media info error: %s", e)
        return None

# ─────────────────────────────────────────────────────────────
#  Background colour — sample screen just below the bar
# ─────────────────────────────────────────────────────────────
def _sample_bar_bg(phys_y: int) -> QColor:
    gdi = windll.gdi32
    u   = windll.user32
    sw  = u.GetSystemMetrics(SM_CXVIRTUALSCREEN)
    hdc = u.GetDC(0)

    rs = gs = bs = n = 0
    step = max(1, sw // 10)
    for x in range(step, sw - step, step):
        c = gdi.GetPixel(hdc, x, phys_y)
        if 0 <= c <= 0x00FFFFFF:
            rs += c & 0xFF
            gs += (c >> 8) & 0xFF
            bs += (c >> 16) & 0xFF
            n  += 1

    u.ReleaseDC(0, hdc)
    if n == 0:
        return QColor(20, 20, 20)
    return QColor(rs // n, gs // n, bs // n)

def _sample_taskbar_color() -> QColor | None:
    try:
        user32 = windll.user32
        hwnd = user32.FindWindowW("Shell_TrayWnd", None)
        if not hwnd:
            return None

        rect = wintypes.RECT()
        if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
            return None

        left, top, right, bottom = rect.left, rect.top, rect.right, rect.bottom
        width = max(0, right - left)
        height = max(0, bottom - top)
        if width < 20 or height < 20:
            return None

        gdi = windll.gdi32
        hdc = user32.GetDC(0)
        try:
            rs = gs = bs = n = 0
            if width >= height:
                y_offsets = [max(3, height // 8), max(6, height // 5)]
                x_fracs = (0.08, 0.18, 0.28, 0.72, 0.82)
                points = [
                    (left + int(width * frac), top + yoff)
                    for yoff in y_offsets for frac in x_fracs
                ]
            else:
                x_offsets = [max(3, width // 8), max(6, width // 5)]
                y_fracs = (0.08, 0.18, 0.28, 0.72, 0.82)
                points = [
                    (left + xoff, top + int(height * frac))
                    for xoff in x_offsets for frac in y_fracs
                ]

            for x, y in points:
                c = gdi.GetPixel(hdc, x, y)
                if 0 <= c <= 0x00FFFFFF:
                    rs += c & 0xFF
                    gs += (c >> 8) & 0xFF
                    bs += (c >> 16) & 0xFF
                    n += 1
        finally:
            user32.ReleaseDC(0, hdc)

        if n == 0:
            return None
        sampled = QColor(rs // n, gs // n, bs // n)
        log.info("[Taskbar] sampled #%02x%02x%02x", sampled.red(), sampled.green(), sampled.blue())
        return sampled
    except Exception as e:
        log.warning("[Taskbar] sample failed: %s", e)
        return None


def _color_dist(a: QColor, b: QColor) -> int:
    return abs(a.red()-b.red()) + abs(a.green()-b.green()) + abs(a.blue()-b.blue())

_SHELL_CLASSES = {'Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd'}

# ─────────────────────────────────────────────────────────────
#  Icon font — prefer Segoe Fluent Icons (Win11), fall back to MDL2
# ─────────────────────────────────────────────────────────────
_ICON_FONT = "Segoe MDL2 Assets"  # updated in main() if Fluent available

ICON_WIFI_ON   = "\uE701"
ICON_WIFI_OFF  = "\uEB55"
ICON_ETHERNET  = "\uE839"
ICON_BATT      = "\uE83F"
ICON_BATT_CHG  = "\uEA93"
ICON_POWER     = "\uE7E8"

# ─────────────────────────────────────────────────────────────
#  WinUI 3 design tokens & constants
# ─────────────────────────────────────────────────────────────
EXTRA_PAD  = 10
ICON_PT    = 11
TEXT_PT    = 10

TEXT_COL        = "#FFFFFF"
TEXT_SEC_COL    = "rgba(255,255,255,139)"
TEXT_DIM_COL    = "rgba(255,255,255,87)"
TEXT_FAINT_COL  = "rgba(255,255,255,64)"

HOVER_BG        = QColor(255, 255, 255, 15)
PRESS_BG        = QColor(255, 255, 255, 10)
POPUP_BG        = QColor(44, 44, 44, 252)
POPUP_BORDER    = QColor(255, 255, 255, 21)
POPUP_RADIUS    = 10
BAR_EDGE        = QColor(255, 255, 255, 24)
BAR_HIGHLIGHT   = QColor(255, 255, 255, 14)
BAR_ACCENT_GLOW = QColor(118, 185, 255, 60)
_DEFAULT_BAR_COLOR = QColor(28, 34, 42, 240)

_TEXT_FONT_FAMILY = "Segoe UI Variable"

def _bar_height() -> int:
    try:
        return max(24, min(56, int(SETTINGS.get("bar_height", 28))))
    except Exception:
        return 28

BAR_HEIGHT = _bar_height()

def _rgba(color: QColor) -> str:
    return f"rgba({color.red()},{color.green()},{color.blue()},{color.alpha()})"

def _mix(c1: QColor, c2: QColor, amount: float) -> QColor:
    amt = max(0.0, min(1.0, amount))
    inv = 1.0 - amt
    return QColor(
        int(c1.red() * inv + c2.red() * amt),
        int(c1.green() * inv + c2.green() * amt),
        int(c1.blue() * inv + c2.blue() * amt),
        int(c1.alpha() * inv + c2.alpha() * amt),
    )

def _accent_brush() -> QColor:
    if SETTINGS.get("use_accent_color") and _accent_color:
        return QColor(_accent_color.red(), _accent_color.green(), _accent_color.blue(), 88)
    return QColor(118, 185, 255, 60)

def _refresh_design_tokens():
    global BAR_HEIGHT, TEXT_COL, TEXT_SEC_COL, TEXT_DIM_COL, TEXT_FAINT_COL
    global HOVER_BG, PRESS_BG, POPUP_BG, POPUP_BORDER, POPUP_RADIUS
    global BAR_EDGE, BAR_HIGHLIGHT, BAR_ACCENT_GLOW, _DEFAULT_BAR_COLOR

    BAR_HEIGHT = _bar_height()
    apps_light = bool(_read_personalize_dword("AppsUseLightTheme", 0))
    neutral_surface = QColor(244, 246, 249, 244) if apps_light else QColor(35, 39, 46, 244)
    neutral_popup = QColor(252, 253, 255, 248) if apps_light else QColor(40, 44, 52, 248)

    BAR_ACCENT_GLOW = _accent_brush()
    sampled_taskbar = _sample_taskbar_color()

    if sampled_taskbar:
        base_surface = _mix(neutral_surface, sampled_taskbar, 0.78)
    elif SETTINGS.get("use_accent_color") and _accent_color:
        accent_surface = QColor(_accent_color.red(), _accent_color.green(), _accent_color.blue(), 244)
        base_surface = _mix(neutral_surface, accent_surface, 0.16)
    else:
        base_surface = neutral_surface

    if SETTINGS.get("use_accent_color") and _accent_color:
        accent_surface = QColor(_accent_color.red(), _accent_color.green(), _accent_color.blue(), 248)
        popup_surface = _mix(neutral_popup, accent_surface, 0.1)
    else:
        popup_surface = neutral_popup

    _DEFAULT_BAR_COLOR = base_surface
    BAR_EDGE = QColor(255, 255, 255, 18) if apps_light else QColor(255, 255, 255, 12)
    BAR_HIGHLIGHT = QColor(255, 255, 255, 0)
    HOVER_BG = QColor(0, 0, 0, 14) if apps_light else QColor(255, 255, 255, 18)
    PRESS_BG = QColor(0, 0, 0, 8) if apps_light else QColor(255, 255, 255, 10)
    POPUP_BG = popup_surface
    POPUP_BORDER = QColor(0, 0, 0, 18) if apps_light else QColor(255, 255, 255, 28)
    POPUP_RADIUS = 12
    TEXT_COL = "#1A1C20" if apps_light else "#F5F7FA"
    TEXT_SEC_COL = "rgba(26,28,32,170)" if apps_light else "rgba(245,247,250,170)"
    TEXT_DIM_COL = "rgba(26,28,32,118)" if apps_light else "rgba(245,247,250,118)"
    TEXT_FAINT_COL = "rgba(26,28,32,72)" if apps_light else "rgba(245,247,250,72)"

_refresh_design_tokens()

def _ifont(pt=ICON_PT) -> QFont:
    return QFont(_ICON_FONT, pt)

def _ufont(pt=TEXT_PT, bold=False) -> QFont:
    f = QFont(_TEXT_FONT_FAMILY, pt)
    if bold:
        f.setWeight(QFont.Weight.DemiBold)
    return f

def _lbl(text, font, col=TEXT_COL, parent=None) -> QLabel:
    w = QLabel(text, parent)
    w.setFont(font)
    w.setStyleSheet(f"color:{col}; background:transparent;")
    w.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignHCenter)
    return w

def _all_screens_rect() -> QRect:
    r = QRect()
    for s in QApplication.screens():
        r = r.united(s.geometry())
    return r if not r.isNull() else QApplication.primaryScreen().geometry()

def _virtual_screen_metrics():
    u = windll.user32
    return (
        u.GetSystemMetrics(SM_XVIRTUALSCREEN),
        u.GetSystemMetrics(SM_YVIRTUALSCREEN),
        u.GetSystemMetrics(SM_CXVIRTUALSCREEN),
    )

def _place_popup_below(anchor: QWidget, popup: QWidget, right_align=True, gap=8):
    popup.adjustSize()
    ar = anchor.rect()
    bottom_right = anchor.mapToGlobal(QPoint(ar.right(), ar.bottom()))
    bottom_left = anchor.mapToGlobal(QPoint(ar.left(), ar.bottom()))

    if right_align:
        x = bottom_right.x() - popup.width()
    else:
        x = bottom_left.x()
    y = bottom_right.y() + gap

    bounds = _all_screens_rect()
    x = max(bounds.left() + 8, min(x, bounds.right() - popup.width() - 8))
    y = max(bounds.top() + 8, min(y, bounds.bottom() - popup.height() - 8))
    popup.move(x, y)
    popup.show()

# ─────────────────────────────────────────────────────────────
#  Windows logo — drawn with QPainter (no font glyph exists)
# ─────────────────────────────────────────────────────────────
class WinLogo(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(14, 14)

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        w, h = self.width(), self.height()
        g = 2
        s = (min(w, h) - g) // 2
        ox = (w - (2 * s + g)) // 2
        oy = (h - (2 * s + g)) // 2
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(255, 255, 255, 230))
        p.drawRect(ox,         oy,         s, s)
        p.drawRect(ox + s + g, oy,         s, s)
        p.drawRect(ox,         oy + s + g, s, s)
        p.drawRect(ox + s + g, oy + s + g, s, s)
        p.end()

# ─────────────────────────────────────────────────────────────
#  WiFi + Battery icons — shared height for alignment
# ─────────────────────────────────────────────────────────────
_ICON_WIDGET_H = 14

class WifiIcon(QWidget):
    W = 16

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(self.W, _ICON_WIDGET_H)
        self._connected = True
        self._signal    = 3

    def set_state(self, connected: bool, signal: int):
        self._connected = connected
        self._signal    = signal
        self.update()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)

        cx = self.W / 2
        cy = (_ICON_WIDGET_H - 9.1) / 2 + 7.5

        ON  = QColor(255, 255, 255, 220)
        OFF = QColor(255, 255, 255, 50)

        ARC_START = 30 * 16
        ARC_SPAN  = 120 * 16

        for i, (r, pw) in enumerate([(7.5, 1.6), (5.0, 1.6), (2.5, 1.6)]):
            level  = 3 - i
            active = self._connected and self._signal >= level
            pen = QPen(ON if active else OFF, pw)
            pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            p.setPen(pen)
            rect = QRectF(cx - r, cy - r, r * 2, r * 2)
            p.drawArc(rect, ARC_START, ARC_SPAN)

        dot_r = 1.6
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(ON if self._connected else OFF)
        p.drawEllipse(QRectF(cx - dot_r, cy - dot_r, dot_r * 2, dot_r * 2))
        p.end()


class BatteryDrawn(QWidget):
    W = 22

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedSize(self.W, _ICON_WIDGET_H)
        self._pct      = 80.0
        self._charging = False

    def set_state(self, pct: float, charging: bool):
        self._pct      = pct
        self._charging = charging
        self.update()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)

        ww, hh = self.width(), self.height()
        body_w  = ww - 4.0
        body_h  = hh - 4.0
        body_x  = 0.5
        body_y  = (hh - body_h) / 2

        nub_w = max(1.5, ww * 0.09)
        nub_h = body_h * 0.42
        nub_x = body_x + body_w + 0.5
        nub_y = (hh - nub_h) / 2

        if self._pct > 50:
            fill_col = QColor(255, 255, 255, 220)
        elif self._pct > 20:
            fill_col = QColor(255, 185, 0)
        else:
            fill_col = QColor(196, 43, 28)

        outline_col = QColor(255, 255, 255, 180)

        p.setPen(QPen(outline_col, 1.2))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(QRectF(body_x, body_y, body_w, body_h), 2.5, 2.5)

        pad    = 2.0
        iw     = body_w - pad * 2
        ih     = body_h - pad * 2
        fill_w = max(0.0, iw * self._pct / 100.0)
        if fill_w > 0:
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(fill_col)
            p.drawRoundedRect(QRectF(body_x + pad, body_y + pad, fill_w, ih), 1, 1)

        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(outline_col)
        p.drawRoundedRect(QRectF(nub_x, nub_y, nub_w, nub_h), 1, 1)

        if self._charging:
            from PyQt6.QtGui import QPainterPath
            sc = body_h / 10.0
            bx = body_x + body_w * 0.55
            t = body_y + 1.5 * sc
            b = body_y + body_h - 1.5 * sc
            mid = (t + b) / 2
            bolt = QPainterPath()
            bolt.moveTo(bx + 2 * sc,    t)
            bolt.lineTo(bx - 1 * sc,    mid - 0.5 * sc)
            bolt.lineTo(bx + 0.5 * sc,  mid - 0.5 * sc)
            bolt.lineTo(bx - 2 * sc,    b)
            bolt.lineTo(bx + 1 * sc,    mid + 0.5 * sc)
            bolt.lineTo(bx - 0.5 * sc,  mid + 0.5 * sc)
            bolt.closeSubpath()
            p.setBrush(QColor(30, 30, 30, 200))
            p.drawPath(bolt)

        p.end()

# ─────────────────────────────────────────────────────────────
#  Info popup base — WinUI 3 flyout style
# ─────────────────────────────────────────────────────────────
class InfoPopup(QWidget):
    W = 260

    def __init__(self):
        super().__init__(None, Qt.WindowType.Popup | Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setFixedWidth(self.W)
        self._vbox = QVBoxLayout(self)
        self._vbox.setContentsMargins(16, 14, 16, 14)
        self._vbox.setSpacing(4)

    def _clear(self):
        while self._vbox.count():
            item = self._vbox.takeAt(0)
            if w := item.widget(): w.deleteLater()
            del item

    def _title(self, text):
        w = QLabel(text, self)
        w.setFont(_ufont(11, bold=True))
        w.setStyleSheet(f"color:{TEXT_COL}; background:transparent;")
        self._vbox.addWidget(w)

    def _body(self, text, dim=False):
        w = QLabel(text, self)
        w.setFont(_ufont(10))
        col = TEXT_DIM_COL if dim else TEXT_COL
        w.setStyleSheet(f"color:{col}; background:transparent;")
        w.setWordWrap(True)
        self._vbox.addWidget(w)

    def _sep(self):
        f = QFrame(self)
        f.setFrameShape(QFrame.Shape.HLine)
        f.setFixedHeight(1)
        f.setStyleSheet(f"background: {_rgba(BAR_EDGE)};")
        self._vbox.addWidget(f)
        self._vbox.addSpacing(2)

    def _row_pair(self, left_text, right_text, left_dim=False, right_dim=False):
        row = QWidget(self); row.setStyleSheet("background:transparent;")
        rl  = QHBoxLayout(row); rl.setContentsMargins(0, 0, 0, 0); rl.setSpacing(4)
        lw  = QLabel(left_text, row)
        rw  = QLabel(right_text, row)
        for w, dim in ((lw, left_dim), (rw, right_dim)):
            w.setFont(_ufont(10))
            col = TEXT_DIM_COL if dim else TEXT_COL
            w.setStyleSheet(f"color:{col}; background:transparent;")
        rw.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        rl.addWidget(lw); rl.addStretch(); rl.addWidget(rw)
        self._vbox.addWidget(row)

    def show_below(self, widget: QWidget):
        _place_popup_below(widget, self)

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        r = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        p.setPen(QPen(POPUP_BORDER, 1))
        p.setBrush(POPUP_BG)
        p.drawRoundedRect(r, POPUP_RADIUS, POPUP_RADIUS)
        glow = QRectF(r).adjusted(1, 1, -1, -r.height() * 0.52)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(_mix(POPUP_BG, BAR_ACCENT_GLOW, 0.22))
        p.drawRoundedRect(glow, POPUP_RADIUS, POPUP_RADIUS)
        p.end()


class BatteryPopup(InfoPopup):
    def load(self, info: dict):
        self._clear()
        self._title("Battery")
        self._sep()

        if not info["has_battery"]:
            self._body("AC power \u2014 no battery detected", dim=True)
            self.adjustSize(); return

        pct  = info["percent"]
        chg  = info["charging"]
        plug = info["plugged"]
        sl   = info["secs_left"]

        row = QWidget(self); row.setStyleSheet("background:transparent;")
        rl  = QHBoxLayout(row); rl.setContentsMargins(0, 4, 0, 4); rl.setSpacing(10)
        ic  = BatteryDrawn(row)
        ic.setFixedSize(32, 18)
        ic.set_state(pct, chg)
        pct_lbl = QLabel(f"{int(pct)}%", row)
        pct_lbl.setFont(_ufont(16, bold=True))
        pct_lbl.setStyleSheet(f"color:{TEXT_COL}; background:transparent;")
        rl.addWidget(ic, 0, Qt.AlignmentFlag.AlignVCenter)
        rl.addWidget(pct_lbl); rl.addStretch()
        self._vbox.addWidget(row)

        if chg:
            status = "Charging\u2026"
        elif plug:
            status = "Plugged in, not charging"
        else:
            status = "On battery power"
        self._body(status, dim=True)

        if sl is not None:
            h = sl // 3600; m = (sl % 3600) // 60
            time_str = (f"{h}h {m}m remaining" if h else f"{m}m remaining")
            self._sep()
            self._row_pair("Time remaining", time_str, left_dim=True)
        elif plug and not chg:
            self._sep()
            self._body("Fully charged", dim=True)

        self.adjustSize()


class NetworkPopup(InfoPopup):
    def load(self, info: dict):
        self._clear()
        self._title("Network")
        self._sep()

        if not info["connected"]:
            self._body("Not connected", dim=True)
            self.adjustSize(); return

        if info["is_wifi"]:
            ssid = info["wifi_name"] or "Wi-Fi"
            sig_labels = {1: "Weak", 2: "Fair", 3: "Strong"}
            self._body(ssid)
            self._body(f"Wi-Fi  \u00B7  {sig_labels.get(info['signal'], '')}", dim=True)
            self._sep()
            rx = info.get("rx_mbps", "")
            tx = info.get("tx_mbps", "")
            if rx:
                self._row_pair("Link speed (\u2193)", f"{rx} Mbps", left_dim=True)
            if tx and tx != rx:
                self._row_pair("Link speed (\u2191)", f"{tx} Mbps", left_dim=True)
        else:
            self._body("Ethernet")
            self._body("Connected", dim=True)
            try:
                for nm, st in psutil.net_if_stats().items():
                    if st.isup and st.speed > 0:
                        self._sep()
                        self._row_pair("Link speed", f"{st.speed} Mbps", left_dim=True)
                        break
            except Exception:
                pass

        self.adjustSize()


# ─────────────────────────────────────────────────────────────
#  Media popup — transport controls drawn with QPainter
# ─────────────────────────────────────────────────────────────
class _MediaBtn(QWidget):
    """Round icon button for media transport controls."""
    def __init__(self, draw_fn, click_fn, parent=None):
        super().__init__(parent)
        self.setFixedSize(36, 36)
        self._draw = draw_fn
        self._click = click_fn
        self._hov = self._prs = False
        self.setCursor(Qt.CursorShape.ArrowCursor)

    def enterEvent(self, e):  self._hov = True;  self.update()
    def leaveEvent(self, e):  self._hov = self._prs = False; self.update()
    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton: self._prs = True; self.update()
    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton and self._prs:
            self._prs = False; self.update(); self._click()

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        r = QRectF(self.rect()).adjusted(1, 1, -1, -1)
        if self._prs:
            p.setPen(Qt.PenStyle.NoPen); p.setBrush(PRESS_BG)
            p.drawRoundedRect(r, 18, 18)
        elif self._hov:
            p.setPen(Qt.PenStyle.NoPen); p.setBrush(HOVER_BG)
            p.drawRoundedRect(r, 18, 18)
        self._draw(p, self.rect())
        p.end()


def _draw_prev(p: QPainter, rect: QRect):
    cx, cy = rect.center().x(), rect.center().y()
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(QColor(255, 255, 255, 210))
    from PyQt6.QtGui import QPolygonF
    from PyQt6.QtCore import QPointF
    # Bar + triangle pointing left
    p.drawRect(QRectF(cx - 6, cy - 5, 2, 10))
    tri = QPolygonF([QPointF(cx - 3, cy), QPointF(cx + 6, cy - 5), QPointF(cx + 6, cy + 5)])
    p.drawPolygon(tri)

def _draw_play(p: QPainter, rect: QRect):
    cx, cy = rect.center().x(), rect.center().y()
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(QColor(255, 255, 255, 210))
    from PyQt6.QtGui import QPolygonF
    from PyQt6.QtCore import QPointF
    tri = QPolygonF([QPointF(cx - 4, cy - 6), QPointF(cx - 4, cy + 6), QPointF(cx + 6, cy)])
    p.drawPolygon(tri)

def _draw_pause(p: QPainter, rect: QRect):
    cx, cy = rect.center().x(), rect.center().y()
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(QColor(255, 255, 255, 210))
    p.drawRoundedRect(QRectF(cx - 5, cy - 5, 3.5, 10), 1, 1)
    p.drawRoundedRect(QRectF(cx + 1.5, cy - 5, 3.5, 10), 1, 1)

def _draw_next(p: QPainter, rect: QRect):
    cx, cy = rect.center().x(), rect.center().y()
    p.setPen(Qt.PenStyle.NoPen)
    p.setBrush(QColor(255, 255, 255, 210))
    from PyQt6.QtGui import QPolygonF
    from PyQt6.QtCore import QPointF
    tri = QPolygonF([QPointF(cx - 6, cy - 5), QPointF(cx - 6, cy + 5), QPointF(cx + 3, cy)])
    p.drawPolygon(tri)
    p.drawRect(QRectF(cx + 4, cy - 5, 2, 10))


class MediaPopup(InfoPopup):
    W = 280

    def __init__(self):
        super().__init__()
        self.setFixedWidth(self.W)

    def load(self, info: dict | None):
        self._clear()
        self._title("Now Playing")
        self._sep()

        if not info or (not info["title"] and not info["artist"]):
            self._body("Nothing playing", dim=True)
            self.adjustSize()
            return

        if info["title"]:
            w = QLabel(info["title"], self)
            w.setFont(_ufont(12, bold=True))
            w.setStyleSheet(f"color:{TEXT_COL}; background:transparent;")
            w.setWordWrap(True)
            self._vbox.addWidget(w)

        if info["artist"]:
            self._body(info["artist"], dim=True)

        self._vbox.addSpacing(6)

        # Transport controls row
        row = QWidget(self); row.setStyleSheet("background:transparent;")
        rl = QHBoxLayout(row)
        rl.setContentsMargins(0, 0, 0, 0)
        rl.setSpacing(8)
        rl.setAlignment(Qt.AlignmentFlag.AlignCenter)

        rl.addWidget(_MediaBtn(_draw_prev, media_prev, row))

        is_playing = info.get("playing", False)
        self._play_btn = _MediaBtn(
            _draw_pause if is_playing else _draw_play,
            self._toggle_play, row)
        self._is_playing = is_playing
        rl.addWidget(self._play_btn)

        rl.addWidget(_MediaBtn(_draw_next, media_next, row))
        self._vbox.addWidget(row)

        self.adjustSize()

    def _toggle_play(self):
        media_play_pause()
        self._is_playing = not self._is_playing
        self._play_btn._draw = _draw_pause if self._is_playing else _draw_play
        self._play_btn.update()


# ─────────────────────────────────────────────────────────────
#  Hover-highlight clickable base — WinUI 3 SubtleFill states
# ─────────────────────────────────────────────────────────────
class MenuExtra(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setFixedHeight(BAR_HEIGHT)
        self._hov = self._prs = False
        self.setCursor(Qt.CursorShape.ArrowCursor)

    def enterEvent(self, e):   self._hov = True;  self.update(); super().enterEvent(e)
    def leaveEvent(self, e):   self._hov = self._prs = False; self.update(); super().leaveEvent(e)
    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton: self._prs = True; self.update()
        super().mousePressEvent(e)
    def mouseReleaseEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton and self._prs:
            self._prs = False; self.update(); self._on_click()
        super().mouseReleaseEvent(e)
    def _on_click(self): pass

    def paintEvent(self, _):
        if not (self._hov or self._prs): return
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(PRESS_BG if self._prs else HOVER_BG)
        p.drawRoundedRect(QRectF(self.rect()).adjusted(2, 4, -2, -4), 6, 6)
        p.end()

    def sync_metrics(self):
        self.setFixedHeight(BAR_HEIGHT)

# ─────────────────────────────────────────────────────────────
#  Right-side extras
# ─────────────────────────────────────────────────────────────
class BatteryExtra(MenuExtra):
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)
        lay.setSpacing(5)
        self._icon = BatteryDrawn(self)
        self._txt  = _lbl("", _ufont(TEXT_PT), TEXT_COL, self)
        self._txt.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft)
        lay.addWidget(self._icon, 0, Qt.AlignmentFlag.AlignVCenter)
        lay.addWidget(self._txt,  0, Qt.AlignmentFlag.AlignVCenter)
        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)
        self._info = {}
        self._popup = None

    def refresh(self, info: dict):
        self._info = info
        if info["has_battery"]:
            self._icon.set_state(info["percent"], info["charging"])
            self._txt.setText(f'{int(info["percent"])}%')
            self._icon.show()
        else:
            self._icon.hide()
            self._txt.setText("AC")
        self.adjustSize()

    def sync_metrics(self):
        super().sync_metrics()
        self.layout().setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)

    def _on_click(self):
        if self._popup and self._popup.isVisible():
            self._popup.close(); return
        if self._popup:
            self._popup.deleteLater()
        self._popup = BatteryPopup()
        self._popup.load(self._info)
        self._popup.show_below(self)


class NetworkExtra(MenuExtra):
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)
        lay.setSpacing(0)
        self._icon = WifiIcon(self)
        lay.addWidget(self._icon, 0, Qt.AlignmentFlag.AlignVCenter)
        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)
        self._info = {}
        self._popup = None

    def refresh(self, info: dict):
        self._info = info
        self._icon.set_state(info["connected"], info["signal"] if info["is_wifi"] else 3)
        self.adjustSize()

    def sync_metrics(self):
        super().sync_metrics()
        self.layout().setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)

    def _on_click(self):
        if self._popup and self._popup.isVisible():
            self._popup.close(); return
        if self._popup:
            self._popup.deleteLater()
        self._popup = NetworkPopup()
        self._popup.load(self._info)
        self._popup.show_below(self)


class ClockExtra(MenuExtra):
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD + 2, 0)
        self._lbl = _lbl("", _ufont(TEXT_PT), TEXT_COL, self)
        self._lbl.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignHCenter)
        lay.addWidget(self._lbl)
        self.setSizePolicy(QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed)
        self.tick()

    def tick(self):
        now = datetime.now()
        if SETTINGS.get("clock_24h"):
            self._lbl.setText(now.strftime("%m/%d/%Y  %H:%M"))
        else:
            hour = now.strftime("%I").lstrip("0") or "12"
            self._lbl.setText(now.strftime(f"%m/%d/%Y  {hour}:%M %p"))
        self.adjustSize()

    def _on_click(self):
        open_notification_center()

    def sync_metrics(self):
        super().sync_metrics()
        self.layout().setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD + 2, 0)


class MediaExtra(MenuExtra):
    """Shows currently playing media — click for transport controls popup."""
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)
        lay.setSpacing(5)

        # Playing indicator dot (uses accent color)
        self._dot = QWidget(self)
        self._dot.setFixedSize(6, 6)
        self._dot.setStyleSheet("background: transparent; border-radius: 3px;")
        lay.addWidget(self._dot, 0, Qt.AlignmentFlag.AlignVCenter)

        self._txt = _lbl("", _ufont(TEXT_PT - 1), TEXT_SEC_COL, self)
        self._txt.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft)
        self._txt.setMaximumWidth(200)
        lay.addWidget(self._txt, 0, Qt.AlignmentFlag.AlignVCenter)

        self.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)
        self._info = None
        self._popup = None
        self.hide()

    def refresh(self, info: dict | None):
        self._info = info
        if not info or (not info["title"] and not info["artist"]):
            self.hide()
            return

        parts = []
        if info["artist"]:
            parts.append(info["artist"])
        if info["title"]:
            parts.append(info["title"])
        display = " \u2014 ".join(parts)
        if len(display) > 45:
            display = display[:44] + "\u2026"
        self._txt.setText(display)

        # Accent-colored dot when playing
        if info.get("playing"):
            self._dot.setStyleSheet(
                "background: rgb(106,196,91); border-radius: 3px;")
        else:
            self._dot.setStyleSheet(
                "background: rgba(255,255,255,60); border-radius: 3px;")

        self.show()
        self.adjustSize()

    def sync_metrics(self):
        super().sync_metrics()
        self.layout().setContentsMargins(EXTRA_PAD, 0, EXTRA_PAD, 0)

    def _on_click(self):
        if self._popup and self._popup.isVisible():
            self._popup.close(); return
        if self._popup:
            self._popup.deleteLater()
        self._popup = MediaPopup()
        self._popup.load(self._info)
        self._popup.show_below(self)


# ─────────────────────────────────────────────────────────────
#  Windows logo button + power menu — WinUI 3 context menu
# ─────────────────────────────────────────────────────────────
def _menu_ss():
    return f"""
QMenu {{
    background: {_rgba(POPUP_BG)};
    border: 1px solid {_rgba(POPUP_BORDER)};
    border-radius: {POPUP_RADIUS}px;
    padding: 6px 0;
    color: {TEXT_COL};
}}
QMenu::item {{
    padding: 9px 36px 9px 12px;
    border-radius: 6px;
    margin: 2px 6px;
    font-family: "{_TEXT_FONT_FAMILY}";
    font-size: 12px;
}}
QMenu::item:selected {{
    background: {_rgba(HOVER_BG)};
}}
QMenu::separator {{
    height: 1px;
    background: {_rgba(BAR_EDGE)};
    margin: 6px 10px;
}}
"""

class WinLogoExtra(MenuExtra):
    def __init__(self, parent=None):
        super().__init__(parent)
        lay = QHBoxLayout(self)
        lay.setContentsMargins(10, 0, 10, 0)
        lay.setSpacing(0)
        lay.addWidget(WinLogo(self), 0,
                      Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignHCenter)
        self.setFixedWidth(34)

    def _on_click(self):
        from PyQt6.QtWidgets import QMenu
        m = QMenu(self)
        m.setStyleSheet(_menu_ss())
        m.setWindowFlags(m.windowFlags()
                         | Qt.WindowType.FramelessWindowHint
                         | Qt.WindowType.NoDropShadowWindowHint)
        m.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        a_settings = m.addAction("Settings")
        m.addSeparator()
        sl = m.addAction("Sleep")
        rs = m.addAction("Restart")
        m.addSeparator()
        sd = m.addAction("Shut down")
        for a in m.actions():
            a.setFont(_ufont(11))
        ch = m.exec(self.mapToGlobal(QPoint(0, self.height() + 4)))
        if ch == a_settings:
            os.startfile("ms-settings:")
        elif ch == sl: sleep_system()
        elif ch == rs: restart_system()
        elif ch == sd: shutdown_system()

    def sync_metrics(self):
        super().sync_metrics()
        self.layout().setContentsMargins(12, 0, 12, 0)

# ─────────────────────────────────────────────────────────────
#  Active window title
# ─────────────────────────────────────────────────────────────
class TitleLabel(QLabel):
    def __init__(self, parent=None):
        super().__init__("Desktop", parent)
        self.setFont(_ufont(10))
        self.setStyleSheet(f"color:{TEXT_SEC_COL}; background:transparent;")
        self.setMaximumWidth(340)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Expanding)
        self.setAlignment(Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft)

    def sync_style(self):
        self.setFont(_ufont(10))
        self.setStyleSheet(f"color:{TEXT_SEC_COL}; background:transparent;")

class MenuBar(QWidget):
    def __init__(self):
        super().__init__()
        self._mica     = False
        self._last_fg  = None
        self._last_bg_hwnd = None
        self._last_bg_sample_at = 0.0
        self._hwnd     = None
        self._phys_h   = BAR_HEIGHT
        self._bg_color = _DEFAULT_BAR_COLOR
        self._timers   = []
        self._root_lay = None
        self._native_ready = False
        self._appbar_registered = False
        self.logo = self.title = self.media = self.net = self.batt = self.clock = None
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint
                            | Qt.WindowType.WindowStaysOnTopHint)
        r = _all_screens_rect()
        self.setGeometry(r.left(), r.top(), r.width(), BAR_HEIGHT)
        self.setFixedHeight(BAR_HEIGHT)
        self.show()
        QTimer.singleShot(0, self._finish_startup)

    def _finish_startup(self):
        try:
            self._apply_settings(initial=True)
            self._poll_title()
            self._poll_status()
            self._poll_media()
        except Exception:
            log.warning("[MenuBar] delayed startup failed:\n%s", traceback.format_exc())
            return
        if not ENABLE_NATIVE_INTEGRATION:
            return
        QTimer.singleShot(40, self._enable_native_features)

    def _enable_native_features(self):
        try:
            self._hwnd = int(self.winId())
            if not self._hwnd:
                return
            _hide_window_from_alt_tab(self._hwnd)
            self._mica = apply_backdrop(self._hwnd)
            self._pin()
            self._native_ready = True
            log.info("[MenuBar] Native integration ready")
        except Exception:
            self._hwnd = None
            self._native_ready = False
            log.warning("[MenuBar] native integration failed:\n%s", traceback.format_exc())

    def _clear_layout(self):
        lay = self.layout()
        if not lay:
            return None
        while lay.count():
            item = lay.takeAt(0)
            if widget := item.widget():
                widget.deleteLater()
            del item
        return lay

    def _build_ui(self):
        lay = self._clear_layout()
        self.setStyleSheet("QWidget { background:transparent; }")
        if lay is None:
            lay = QHBoxLayout()
            self.setLayout(lay)
        lay.setContentsMargins(8, 0, 6, 0)
        lay.setSpacing(2)
        self._root_lay = lay

        self.logo  = WinLogoExtra(self) if SETTINGS.get("show_windows_logo", True) else None
        self.title = TitleLabel(self) if SETTINGS.get("show_title", True) else None
        self.media = MediaExtra(self) if SETTINGS.get("show_media", True) else None
        self.net   = NetworkExtra(self) if SETTINGS.get("show_network", True) else None
        self.batt  = BatteryExtra(self) if SETTINGS.get("show_battery", True) else None
        self.clock = ClockExtra(self) if SETTINGS.get("show_clock", True) else None

        if self.logo:
            lay.addWidget(self.logo)
            lay.addSpacing(2)
        if self.title:
            self.title.sync_style()
            lay.addWidget(self.title)
        lay.addStretch()
        if self.media:
            lay.addWidget(self.media)
        if self.net:
            lay.addWidget(self.net)
        if self.batt:
            lay.addWidget(self.batt)
        if self.clock:
            lay.addWidget(self.clock)

        for widget in (self.logo, self.media, self.net, self.batt, self.clock):
            if widget and hasattr(widget, "sync_metrics"):
                widget.sync_metrics()

    def _clear_timers(self):
        for timer in self._timers:
            timer.stop()
            timer.deleteLater()
        self._timers.clear()

    def _add_timer(self, interval_ms, slot):
        timer = QTimer(self)
        timer.timeout.connect(slot)
        timer.start(interval_ms)
        self._timers.append(timer)
        return timer

    def _setup_timers(self):
        self._clear_timers()
        self._add_timer(500, self._poll_title_safe)
        if self.clock:
            self._add_timer(10_000, self._tick_clock_safe)
        self._add_timer(15_000, self._poll_status_safe)
        if self.media and _HAS_MEDIA:
            self._add_timer(3_000, self._poll_media_safe)
            QTimer.singleShot(500, self._poll_media_safe)
        self._wdog = self._add_timer(60_000, self._health_check)

    def _apply_settings(self, initial=False):
        global _accent_color
        if SETTINGS.get("use_accent_color"):
            _accent_color = _read_accent_color()
        else:
            _accent_color = None
        _refresh_design_tokens()

        r = _all_screens_rect()
        self._phys_h = BAR_HEIGHT
        self.setGeometry(r.left(), r.top(), r.width(), BAR_HEIGHT)
        self.setFixedHeight(BAR_HEIGHT)
        self._bg_color = _DEFAULT_BAR_COLOR
        self._build_ui()
        self._setup_timers()
        self.update()

        if not initial:
            self._last_fg = None
            self._last_bg_hwnd = None
            self._last_bg_sample_at = 0.0
            self._poll_title_safe()
            self._poll_status_safe()
            self._poll_media_safe()
            if self._hwnd and windll.user32.IsWindow(self._hwnd):
                _hide_window_from_alt_tab(self._hwnd)
                self._mica = apply_backdrop(self._hwnd)
                self._pin()

    # ── Guarded timer callbacks ───────────────────────────────
    def _poll_title_safe(self):
        try: self._poll_title()
        except Exception: log.debug("[timer] poll_title:\n%s", traceback.format_exc())

    def _tick_clock_safe(self):
        try: self.clock.tick()
        except Exception: log.debug("[timer] clock.tick:\n%s", traceback.format_exc())

    def _poll_status_safe(self):
        try: self._poll_status()
        except Exception: log.debug("[timer] poll_status:\n%s", traceback.format_exc())

    def _poll_media_safe(self):
        try: self._poll_media()
        except Exception: log.debug("[timer] poll_media:\n%s", traceback.format_exc())

    def _on_resume(self):
        """Re-anchor the bar after sleep/hibernate wake."""
        log.info("[Power] Applying post-resume fixes")
        try:
            if self._hwnd and windll.user32.IsWindow(self._hwnd):
                _hide_window_from_alt_tab(self._hwnd)
                self._mica = apply_backdrop(self._hwnd)
                self._pin()
                self.update()
                self._poll_status_safe()
                if self.media and _HAS_MEDIA:
                    self._poll_media_safe()
        except Exception:
            log.warning("[Power] Resume fix failed:\n%s", traceback.format_exc())

    # ── Screen-change handler ─────────────────────────────────
    def _on_screen_change(self):
        try:
            r = _all_screens_rect()
            self.setGeometry(r.left(), r.top(), r.width(), BAR_HEIGHT)
            self.setFixedHeight(BAR_HEIGHT)
            if self._hwnd and windll.user32.IsWindow(self._hwnd):
                _hide_window_from_alt_tab(self._hwnd)
                self._pin()
        except Exception:
            log.warning("[Screen] Re-pin failed:\n%s", traceback.format_exc())

    # ── Watchdog ──────────────────────────────────────────────
    def _health_check(self):
        """Periodic self-check: ensure bar is visible and pinned correctly."""
        try:
            if not self.isVisible():
                self.show()
            if self._hwnd and self._native_ready:
                if not windll.user32.IsWindow(self._hwnd):
                    log.error("[Watchdog] HWND invalid — skipping re-pin")
                    return
                _hide_window_from_alt_tab(self._hwnd)
                self._pin()
        except Exception:
            log.warning("[Watchdog] Health check error:\n%s", traceback.format_exc())

    def _poll_title(self):
        try:
            fg = windll.user32.GetForegroundWindow()
            buf = ctypes.create_unicode_buffer(256)
            windll.user32.GetClassNameW(fg or 0, buf, 256)
            cls = buf.value

            if _color_dist(_DEFAULT_BAR_COLOR, self._bg_color) > 6:
                self._bg_color = _DEFAULT_BAR_COLOR
                self.update()

            if fg == self._last_fg or fg == self._hwnd:
                return
            self._last_fg = fg
            if self.title:
                self.title.setText(get_active_window_title() or "Desktop")
        except Exception:
            log.debug("[poll_title] %s", traceback.format_exc())

    def _poll_status(self):
        try:
            if self.batt:
                self.batt.refresh(get_battery_info())
        except Exception:
            log.debug("[poll_battery] %s", traceback.format_exc())
        try:
            if self.net:
                self.net.refresh(get_network_info())
        except Exception:
            log.debug("[poll_network] %s", traceback.format_exc())

    def _poll_media(self):
        try:
            if self.media:
                self.media.refresh(get_media_info())
        except Exception:
            log.debug("[poll_media] %s", traceback.format_exc())

    def _pin(self):
        u   = windll.user32
        dpr = self.devicePixelRatioF() or 1.0
        self._phys_h = math.ceil(BAR_HEIGHT * dpr)
        pl, pt, pw = _virtual_screen_metrics()
        logical_rect = _all_screens_rect()
        self.setGeometry(logical_rect.left(), logical_rect.top(), logical_rect.width(), BAR_HEIGHT)
        register_appbar(self._hwnd, self._phys_h, register=not self._appbar_registered)
        self._appbar_registered = True
        u.SetWindowPos(self._hwnd, HWND_TOPMOST, pl, pt, pw, self._phys_h,
                       SWP_NOACTIVATE | SWP_SHOWWINDOW)

    def paintEvent(self, _):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        grad = QLinearGradient(0, 0, 0, self.height())
        grad.setColorAt(0.0, _mix(self._bg_color, QColor(255, 255, 255, 255), 0.015))
        grad.setColorAt(1.0, _mix(self._bg_color, QColor(0, 0, 0, 255), 0.03))
        p.fillRect(self.rect(), grad)
        p.fillRect(QRect(0, self.height() - 1, self.width(), 1), BAR_EDGE)
        p.end()

    def contextMenuEvent(self, e):
        from PyQt6.QtWidgets import QMenu
        m = QMenu(self)
        m.setStyleSheet(_menu_ss())
        m.setWindowFlags(m.windowFlags()
                         | Qt.WindowType.FramelessWindowHint
                         | Qt.WindowType.NoDropShadowWindowHint)
        m.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        a_settings = m.addAction("Open settings")
        a_reload   = m.addAction("Reload settings")
        m.addSeparator()
        a_restart  = m.addAction("Restart bar")
        a_stop     = m.addAction("Stop bar")
        for a in m.actions():
            a.setFont(_ufont(11))

        ch = m.exec(e.globalPos())
        if ch == a_settings:
            os.startfile(str(_SETTINGS_PATH))
        elif ch == a_reload:
            global SETTINGS
            SETTINGS = _load_settings()
            log.info("Settings reloaded")
            self._apply_settings()
        elif ch == a_restart:
            if self._hwnd:
                unregister_appbar(self._hwnd)
                self._appbar_registered = False
            subprocess.Popen([sys.executable] + sys.argv,
                             creationflags=subprocess.CREATE_NO_WINDOW)
            QApplication.quit()
        elif ch == a_stop:
            QApplication.quit()

    def closeEvent(self, e):
        if self._hwnd and self._appbar_registered:
            unregister_appbar(self._hwnd)
            self._appbar_registered = False
        super().closeEvent(e)

# ─────────────────────────────────────────────────────────────
#  Entry point
# ─────────────────────────────────────────────────────────────
def main():
    log.info("main()")
    try: windll.shcore.SetProcessDpiAwareness(2)
    except Exception: pass
    QApplication.setHighDpiScaleFactorRoundingPolicy(
        Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    app = QApplication(sys.argv)
    app.setApplicationName("Windows11MenuBar")
    app.setQuitOnLastWindowClosed(False)

    global _TEXT_FONT_FAMILY, _ICON_FONT, _accent_color
    fams = QFontDatabase.families()

    if "Segoe Fluent Icons" in fams:
        _ICON_FONT = "Segoe Fluent Icons"
        log.info("Icon font -> Segoe Fluent Icons")
    elif "Segoe MDL2 Assets" in fams:
        _ICON_FONT = "Segoe MDL2 Assets"
        log.info("Icon font -> Segoe MDL2 Assets")
    else:
        log.warning("No icon font found!")

    if "Segoe UI Variable" in fams:
        _TEXT_FONT_FAMILY = "Segoe UI Variable"
    else:
        _TEXT_FONT_FAMILY = "Segoe UI"
    log.info("Text font -> %s", _TEXT_FONT_FAMILY)

    for f in [_ICON_FONT, _TEXT_FONT_FAMILY]:
        log.info("Font '%s': %s", f, "OK" if f in fams else "MISSING")

    if SETTINGS.get("use_accent_color"):
        _accent_color = _read_accent_color()
    else:
        _accent_color = None
    _refresh_design_tokens()

    global _BAR_INSTANCE
    _BAR_INSTANCE = MenuBar()
    bar = _BAR_INSTANCE

    # Re-anchor bar on display topology changes
    app.primaryScreenChanged.connect(bar._on_screen_change)
    app.screenAdded.connect(lambda _: bar._on_screen_change())
    app.screenRemoved.connect(lambda _: bar._on_screen_change())

    sys.exit(app.exec())

if __name__ == "__main__":
    try:
        main()
    except Exception:
        log.critical("CRASH:\n%s", traceback.format_exc())
        (Path(__file__).parent / "menubar_crash.log").write_text(traceback.format_exc())
        raise
