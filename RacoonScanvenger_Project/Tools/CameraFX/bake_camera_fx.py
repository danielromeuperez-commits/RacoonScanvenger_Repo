"""
Horneado de los VFX de cámara (CameraFX): simula el fuego de la racha y lo "graba" en un flipbook PNG, para que
en el juego solo se reproduzca (sin coste de simulación); y dibuja los sprites (rayo, viruta, destello).
El aura de la bebida energética no se hornea: es procedural (shader Racoon/UI/CameraFXWaves).

    python bake_camera_fx.py

Estilo: cartoon / cel-shading, a juego con el HUD. Formas redondeadas y simples, tres bandas de color planas
(sin degradados) y contorno grueso.

Salida: Assets/_Racoon/Visuals/VFX/CameraFX/Resources/CameraFX/ (todas sin sRGB)
  - T_CamFX_Fire.png     flipbook 4x4 (256 px por fotograma): llama cartoon que nace del borde inferior.
  - T_CamFX_Bolt.png     icono de rayo.
  - T_CamFX_Ember.png    viruta (bolita).
  - T_CamFX_Sparkle.png  destello de cuatro puntas.

Codificación (la lee el shader Racoon/UI/CameraFXRamp): R = banda (0 = contorno, 1/3 = sombra, 2/3 = base,
1 = luz) y A = alfa. El color no se hornea: el juego pone el contorno del HUD y saca sombra, base y luz del color
de cada personaje, así la misma llama sirve para todos.

Simulación: ruido fractal (fbm) sobre un toro, con deformación de dominio. Como el ruido es periódico y el tiempo
recorre exactamente un periodo, el bucle de 16 fotogramas empalma sin salto. Se simula al doble de resolución, se
suaviza (puntas redondas, sin detalle «realista»), se corta en bandas y se reduce (bordes antialias).
Si cambias FRAMES o GRID, cambia también las constantes del flipbook en CameraFX.cs.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Racoon", "Visuals", "VFX", "CameraFX",
                                    "Resources", "CameraFX"))
SIZE = 256
SS = 2                      # Supermuestreo.
BIG = SIZE * SS
GRID = 4
FRAMES = GRID * GRID
OUTLINE_PX = 7              # Grosor del contorno a resolución BIG (≈3,5 px por fotograma).
RNG = np.random.default_rng(20261028)

OUTLINE, SHADOW, BASE, LIGHT = 0.0, 1 / 3, 2 / 3, 1.0


# ---------------- Ruido periódico ----------------

class TorusNoise:
    """Ruido de valor suavizado, periódico en u y v (periodo 1)."""

    def __init__(self, cells):
        self.cells = cells
        self.grid = RNG.random((cells, cells))

    def __call__(self, u, v):
        n = self.cells
        x = (u % 1.0) * n
        y = (v % 1.0) * n
        x0 = np.floor(x).astype(int)
        y0 = np.floor(y).astype(int)
        fx = x - x0
        fy = y - y0
        fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10)
        fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10)
        x0 %= n
        y0 %= n
        x1 = (x0 + 1) % n
        y1 = (y0 + 1) % n
        g = self.grid
        a = g[y0, x0] * (1 - fx) + g[y0, x1] * fx
        b = g[y1, x0] * (1 - fx) + g[y1, x1] * fx
        return a * (1 - fy) + b * fy


class Fbm:
    def __init__(self, base, octaves, gain=0.5):
        self.layers = [TorusNoise(base * 2 ** k) for k in range(octaves)]
        self.gain = gain

    def __call__(self, u, v):
        total = np.zeros_like(u)
        amp, norm = 1.0, 0.0
        for layer in self.layers:
            total += layer(u, v) * amp
            norm += amp
            amp *= self.gain
        return total / norm


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def contrast(n, k):
    return np.clip((n - 0.5) * k + 0.5, 0, 1)


# Coordenadas a resolución BIG: x 0-1 (izquierda a derecha), y 0-1 (abajo a arriba).
_ys, _xs = np.mgrid[0:BIG, 0:BIG]
X = (_xs + 0.5) / BIG
Y = 1.0 - (_ys + 0.5) / BIG


# ---------------- Cel-shading ----------------

def blur(field, radius):
    """Desenfoque gaussiano separable; los bordes se extienden (la base de la llama sigue tocando el borde)."""
    k = np.exp(-0.5 * (np.arange(-3 * radius, 3 * radius + 1) / radius) ** 2)
    k /= k.sum()
    pad = 3 * radius
    f = np.pad(field, pad, mode="edge")
    f = np.apply_along_axis(lambda r: np.convolve(r, k, mode="same"), 1, f)
    f = np.apply_along_axis(lambda c: np.convolve(c, k, mode="same"), 0, f)
    return f[pad:-pad, pad:-pad]


def dilate(mask, px):
    img = Image.fromarray((mask * 255).astype(np.uint8), "L")
    return np.asarray(img.filter(ImageFilter.MaxFilter(px * 2 + 1))) > 127


def cel(body, base, light):
    """Bandas planas con contorno a partir de máscaras (cuerpo ⊇ base ⊇ luz). Devuelve (banda, alfa)."""
    band = np.full(body.shape, OUTLINE)
    band[body] = SHADOW
    band[body & base] = BASE
    band[body & light] = LIGHT
    outer = dilate(body, OUTLINE_PX)
    return band, outer.astype(np.float64)


def downsample(a):
    return a.reshape(SIZE, SS, SIZE, SS).mean(axis=(1, 3))


# ---------------- Fuego cartoon ----------------

fire_warp = Fbm(2, 2)
fire_detail = Fbm(3, 2)
fire_sway = Fbm(2, 2)

# Lenguas de la llama: (centro x, altura, anchura, desfase del vaivén).
FIRE_LOBES = [(0.50, 0.95, 0.20, 0.00), (0.29, 0.66, 0.16, 0.33), (0.71, 0.74, 0.16, 0.66),
              (0.14, 0.40, 0.12, 0.15), (0.86, 0.46, 0.12, 0.85)]


def fire_frame(t):
    """Llama cartoon: lenguas en forma de gota que nacen del borde inferior, con gotas que se desprenden."""
    warp = fire_warp(X, Y - t) - 0.5
    body = np.zeros_like(X)
    for cx, height, width, phase in FIRE_LOBES:
        y = Y / height
        sway = (fire_sway(X * 0 + phase, Y * 0.5 - t) - 0.5) * 0.35 * y
        # Gota: ancha abajo, punta fina arriba.
        w = width * (np.clip(1 - y, 0, 1) ** 1.1) * (1 + 0.6 * np.exp(-((y - 0.15) / 0.25) ** 2)) + 0.003
        lobe = np.clip(1 - np.abs(X - cx - sway - 0.06 * warp * y) / w, 0, 1) ** 0.45
        body = np.maximum(body, lobe * np.clip(1 - y, 0, 1) ** 0.15)
    n = contrast(fire_detail(X + 0.35 * warp, Y - 2 * t + 0.3 * warp), 2.2)
    heat = body * 1.2 - n * (0.1 + 0.7 * Y)
    heat = blur(heat, 4)                                          # Puntas redondas.
    heat *= smoothstep(0.0, 0.08, X) * smoothstep(1.0, 0.92, X) * smoothstep(1.0, 0.90, Y)
    # Llama dentro de la llama: base y luz cada vez más bajas y estrechas.
    core = blur(body * (1.1 - 0.9 * Y) - n * 0.25, 4)
    return cel(heat > 0.18, core > 0.45, core > 0.75)


# ---------------- Flipbook ----------------

def save(band, alpha, name):
    band = downsample(band) if band.shape[0] == BIG else band
    alpha = downsample(alpha) if alpha.shape[0] == BIG else alpha
    r = (np.clip(band, 0, 1) * 255 + 0.5).astype(np.uint8)
    a = (np.clip(alpha, 0, 1) * 255 + 0.5).astype(np.uint8)
    return np.dstack([r, r, r, a])


def bake_flipbook(frame_fn, name):
    sheet = np.zeros((SIZE * GRID, SIZE * GRID, 4), dtype=np.uint8)
    for i in range(FRAMES):
        band, alpha = frame_fn(i / FRAMES)
        row, col = divmod(i, GRID)  # Fila 0 = arriba.
        sheet[row * SIZE:(row + 1) * SIZE, col * SIZE:(col + 1) * SIZE] = save(band, alpha, name)
    Image.fromarray(sheet, "RGBA").save(os.path.join(OUT, name))
    print("ok", name, sheet.shape[:2])


# ---------------- Sprites ----------------

def sprite_from_mask(fill, highlight, name, size):
    """Sprite cel-shaded: relleno base, zona de luz y contorno, a partir de máscaras a resolución grande."""
    s = fill.shape[0] // size
    band = np.full(fill.shape, OUTLINE)
    band[fill] = BASE
    band[fill & highlight] = LIGHT
    outer = dilate(fill, max(2, int(OUTLINE_PX * fill.shape[0] / BIG * 0.9)))
    alpha = outer.astype(np.float64)
    band = band.reshape(size, s, size, s).mean(axis=(1, 3))
    alpha = alpha.reshape(size, s, size, s).mean(axis=(1, 3))
    r = (band * 255 + 0.5).astype(np.uint8)
    Image.fromarray(np.dstack([r, r, r, (alpha * 255 + 0.5).astype(np.uint8)]), "RGBA").save(os.path.join(OUT, name))
    print("ok", name, (size, size))


def rounded_polygon(points, big, radius):
    img = Image.new("L", (big, big), 0)
    ImageDraw.Draw(img).polygon([(x * big, y * big) for x, y in points], fill=255)
    # Esquinas redondas: desenfoque y umbral.
    img = img.filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(img) > 127


def bake_bolt(name, size=128):
    big = size * 4
    points = [(0.60, 0.06), (0.22, 0.56), (0.47, 0.56), (0.36, 0.94), (0.80, 0.42), (0.55, 0.42), (0.72, 0.06)]
    points = [(0.5 + (x - 0.5) * 0.84, 0.5 + (y - 0.5) * 0.84) for x, y in points]
    fill = rounded_polygon(points, big, big * 0.012)
    ys, xs = np.mgrid[0:big, 0:big]
    highlight = (xs / big + ys / big) < 0.86          # Mitad superior izquierda, más clara.
    sprite_from_mask(fill, highlight, name, size)


def bake_ember(name, size=64):
    big = size * 4
    ys, xs = np.mgrid[0:big, 0:big]
    u, v = (xs + 0.5) / big - 0.5, (ys + 0.5) / big - 0.5
    fill = np.hypot(u, v) < 0.34
    highlight = np.hypot(u + 0.1, v + 0.1) < 0.14       # Brillo arriba a la izquierda.
    sprite_from_mask(fill, highlight, name, size)


def bake_sparkle(name, size=64):
    big = size * 4
    ys, xs = np.mgrid[0:big, 0:big]
    u, v = np.abs((xs + 0.5) / big - 0.5) * 2, np.abs((ys + 0.5) / big - 0.5) * 2
    # Estrella de cuatro puntas con lados curvos.
    star = (np.sqrt(u) + np.sqrt(v)) < 0.95
    highlight = (np.sqrt(u) + np.sqrt(v)) < 0.55
    sprite_from_mask(star, highlight, name, size)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    bake_flipbook(fire_frame, "T_CamFX_Fire.png")
    bake_bolt("T_CamFX_Bolt.png")
    bake_ember("T_CamFX_Ember.png")
    bake_sparkle("T_CamFX_Sparkle.png")
