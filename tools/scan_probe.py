"""
Prototype: measure a MakeIt MOPA colour-test card from two flatbed scans (card at 0 and
90 degrees) and report how much the measured colour depends on scan orientation.

Throwaway research code for designing the app's scan reader - not part of the product.
Grid geometry is seeded from hand-read label positions, then refined from the brightness
profile of the bright (high-power) squares.
"""
import numpy as np
from PIL import Image, ImageDraw
from scipy.signal import find_peaks
from scipy.ndimage import uniform_filter1d

SCANS = r"D:\DevHome\MOPAChroma Atlas\samples\scans"
N_FREQ, N_POW = 10, 20
FREQ = np.linspace(150, 350, N_FREQ)   # kHz, index 0 = 150
POWER = np.linspace(40, 90, N_POW)     # %,   index 0 = 40


# ---------------------------------------------------------------- colour maths
def srgb_to_lab(rgb):
    c = np.asarray(rgb, float) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124564, 0.3575761, 0.1804375],
                  [0.2126729, 0.7151522, 0.0721750],
                  [0.0193339, 0.1191920, 0.9503041]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    e, k = 216 / 24389, 24389 / 27
    f = np.where(xyz > e, np.cbrt(xyz), (k * xyz + 16) / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], -1)


def de2000(l1, l2):
    L1, a1, b1 = np.moveaxis(l1, -1, 0)
    L2, a2, b2 = np.moveaxis(l2, -1, 0)
    C1, C2 = np.hypot(a1, b1), np.hypot(a2, b2)
    Cb7 = ((C1 + C2) / 2) ** 7
    G = 0.5 * (1 - np.sqrt(Cb7 / (Cb7 + 25 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1p = np.degrees(np.arctan2(b1, a1p)) % 360
    h2p = np.degrees(np.arctan2(b2, a2p)) % 360
    dLp, dCp = L2 - L1, C2p - C1p
    dh = h2p - h1p
    dh = np.where(dh > 180, dh - 360, np.where(dh < -180, dh + 360, dh))
    dh = np.where(C1p * C2p == 0, 0, dh)
    dHp = 2 * np.sqrt(C1p * C2p) * np.sin(np.radians(dh / 2))
    Lbp, Cbp = (L1 + L2) / 2, (C1p + C2p) / 2
    hs = h1p + h2p
    hbp = np.where(C1p * C2p == 0, hs,
                   np.where(np.abs(h1p - h2p) <= 180, hs / 2,
                            np.where(hs < 360, (hs + 360) / 2, (hs - 360) / 2)))
    T = (1 - 0.17 * np.cos(np.radians(hbp - 30)) + 0.24 * np.cos(np.radians(2 * hbp))
         + 0.32 * np.cos(np.radians(3 * hbp + 6)) - 0.20 * np.cos(np.radians(4 * hbp - 63)))
    dT = 30 * np.exp(-(((hbp - 275) / 25) ** 2))
    Rc = 2 * np.sqrt(Cbp ** 7 / (Cbp ** 7 + 25 ** 7))
    Sl = 1 + 0.015 * (Lbp - 50) ** 2 / np.sqrt(20 + (Lbp - 50) ** 2)
    Sc, Sh = 1 + 0.045 * Cbp, 1 + 0.015 * Cbp * T
    Rt = -np.sin(np.radians(2 * dT)) * Rc
    return np.sqrt((dLp / Sl) ** 2 + (dCp / Sc) ** 2 + (dHp / Sh) ** 2 + Rt * (dCp / Sc) * (dHp / Sh))


# ---------------------------------------------------------------- grid finding
def load(name):
    return np.asarray(Image.open(f"{SCANS}\\{name}").convert("RGB")).astype(float)


def axis_centres(img, axis, box, seed_pos, seed_index, step_sign, n, pitch_guess=83):
    """Centres of the n cells along one axis.

    box = (x0, y0, x1, y1) around bright squares only. axis 0 = along x, 1 = along y.
    seed_pos/seed_index: one hand-read label position and its cell index; step_sign is
    +1 if the index grows with the coordinate, -1 if it shrinks.
    """
    x0, y0, x1, y1 = box
    region = img[y0:y1, x0:x1].sum(-1)
    prof = region.mean(axis=0) if axis == 0 else region.mean(axis=1)
    prof = uniform_filter1d(prof, 9)
    origin = x0 if axis == 0 else y0

    # Pitch from autocorrelation, then peaks at least 0.6 pitch apart.
    p = prof - prof.mean()
    ac = np.correlate(p, p, "full")[len(p) - 1:]
    lo, hi = int(pitch_guess * 0.8), int(pitch_guess * 1.2)
    pitch = lo + int(np.argmax(ac[lo:hi]))
    peaks, _ = find_peaks(prof, distance=int(pitch * 0.6), prominence=prof.std() * 0.3)
    pos = peaks + origin

    idx = seed_index + step_sign * np.round((pos - seed_pos) / pitch).astype(int)
    keep = (idx >= 0) & (idx < n)
    a, b = np.polyfit(idx[keep], pos[keep], 1)          # pos = a*idx + b
    resid = pos[keep] - (a * idx[keep] + b)
    return a * np.arange(n) + b, abs(a), len(pos[keep]), float(np.abs(resid).max())


def fit_grid(img, guess, pitch, cells, iters=3):
    """Refine a grid from its own squares. guess(fi, pi) -> (x, y) approximate centres.

    For each listed (bright) cell, threshold a pitch-sized window halfway between its
    darkest and brightest pixels and take the centroid of the bright part - that is the
    square. Then least-squares fit an affine grid (handles offset, tilt, scale) through
    those centroids and apply it to every cell. Returns centre(fi, pi) and RMS residual.
    """
    lum = img.sum(-1)
    H, W = lum.shape
    cur = guess
    for _ in range(iters):
        rows, pts = [], []
        for fi, pi in cells:
            cx, cy = cur(fi, pi)
            h = int(pitch * 0.5)
            x0, y0 = int(cx) - h, int(cy) - h
            if x0 < 0 or y0 < 0 or x0 + 2 * h > W or y0 + 2 * h > H:
                continue
            win = lum[y0:y0 + 2 * h, x0:x0 + 2 * h]
            thr = (np.percentile(win, 5) + np.percentile(win, 95)) / 2
            ys, xs = np.nonzero(win > thr)
            if len(xs) < (pitch * 0.4) ** 2:
                continue
            pts.append((x0 + xs.mean(), y0 + ys.mean()))
            rows.append((pi, fi, 1.0))
        A = np.array(rows)
        P = np.array(pts)
        coef, *_ = np.linalg.lstsq(A, P, rcond=None)
        resid = np.sqrt(((A @ coef - P) ** 2).sum(1))
        cur = (lambda c: (lambda fi, pi: tuple(np.array([pi, fi, 1.0]) @ c)))(coef)
    return cur, float(np.sqrt((resid ** 2).mean())), len(P)


def sample(img, cx, cy, half):
    patch = img[int(cy - half):int(cy + half), int(cx - half):int(cx + half)].reshape(-1, 3)
    return np.median(patch, 0), patch.std(0).mean()


def measure(img, xs_for, ys_for, pitch):
    """xs_for(fi, pi), ys_for(fi, pi) give cell centres; returns rgb[10,20,3], spread[10,20]."""
    half = pitch * 0.2
    rgb = np.full((N_FREQ, N_POW, 3), np.nan)
    spread = np.full((N_FREQ, N_POW), np.nan)
    H, W = img.shape[:2]
    for fi in range(N_FREQ):
        for pi in range(N_POW):
            cx, cy = xs_for(fi, pi), ys_for(fi, pi)
            if half < cx < W - half and half < cy < H - half:
                rgb[fi, pi], spread[fi, pi] = sample(img, cx, cy, half)
    return rgb, spread


def overlay(img, xs_for, ys_for, pitch, out):
    im = Image.fromarray(img.astype(np.uint8))
    d = ImageDraw.Draw(im)
    h = pitch * 0.2
    for fi in range(N_FREQ):
        for pi in range(N_POW):
            cx, cy = xs_for(fi, pi), ys_for(fi, pi)
            d.rectangle([cx - h, cy - h, cx + h, cy + h], outline=(255, 0, 255), width=3)
    im.thumbnail((1400, 1400))
    im.save(out, quality=88)


# ---------------------------------------------------------------- main
if __name__ == "__main__":
    out = {}

    # Card at 90 deg: frequency runs left->right 350..150, power top->bottom 90..40.
    im90 = load("90 degrees.png")
    fx, p1, n1, r1 = axis_centres(im90, 0, (540, 400, 1400, 1290), 588, 9, -1, N_FREQ)
    py, p2, n2, r2 = axis_centres(im90, 1, (560, 380, 1380, 1320), 437, 19, -1, N_POW)
    print(f"90: freq pitch {p1:.1f}px ({n1} peaks, max resid {r1:.1f}); power pitch {p2:.1f}px ({n2} peaks, max resid {r2:.1f})")
    pitch90 = (p1 + p2) / 2
    X90 = lambda fi, pi: fx[fi]
    Y90 = lambda fi, pi: py[pi]
    bright = [(fi, pi) for fi in range(N_FREQ) for pi in range(9, N_POW)]
    c90, rms90, n90 = fit_grid(im90, lambda fi, pi: (fx[fi], py[pi]), pitch90, bright)
    print(f"90: grid fit through {n90} squares, rms {rms90:.1f}px")
    X90 = lambda fi, pi: c90(fi, pi)[0]
    Y90 = lambda fi, pi: c90(fi, pi)[1]
    out[90] = measure(im90, X90, Y90, pitch90)
    overlay(im90, X90, Y90, pitch90, f"{SCANS}\\_check_90.jpg")

    # Card at 0 deg: power runs left->right 40..90, frequency top->bottom 350..150.
    im0 = load("0 degrees.png")
    # Three corner squares, hand-read (the app will take these as three clicks):
    # (350 kHz, 40 %), (350 kHz, 90 %), (150 kHz, 40 %). Handles a tilted card.
    P00, P0N, PM0 = np.array([555., 517.]), np.array([2167., 469.]), np.array([569., 1279.])
    ucol, urow = (P0N - P00) / (N_POW - 1), (PM0 - P00) / (N_FREQ - 1)
    pitch0 = (np.linalg.norm(ucol) + np.linalg.norm(urow)) / 2
    lum = im0.sum(-1)

    def centre(fi, pi, off):
        return P00 + off + pi * ucol + (N_FREQ - 1 - fi) * urow

    def score(off):
        h = int(pitch0 * 0.2)
        s = 0.0
        for fi in range(N_FREQ):
            for pi in range(9, N_POW):
                cx, cy = centre(fi, pi, off).astype(int)
                s += lum[cy - h:cy + h, cx - h:cx + h].mean()
        return s

    tilt = np.degrees(np.arctan2(ucol[1], ucol[0]))
    print(f" 0: pitch {pitch0:.1f}px, tilt {tilt:+.2f} deg (from corner clicks)")
    bright = [(fi, pi) for fi in range(N_FREQ) for pi in range(9, N_POW)]
    c0, rms0, n0 = fit_grid(im0, lambda fi, pi: tuple(centre(fi, pi, np.zeros(2))), pitch0, bright)
    print(f" 0: grid fit through {n0} squares, rms {rms0:.1f}px")
    X0 = lambda fi, pi: c0(fi, pi)[0]
    Y0 = lambda fi, pi: c0(fi, pi)[1]
    out[0] = measure(im0, X0, Y0, pitch0)
    overlay(im0, X0, Y0, pitch0, f"{SCANS}\\_check_0.jpg")

    # White paper, same sheet in both scans: scanner consistency check.
    w0 = np.median(im0[1620:1680, 1180:1240].reshape(-1, 3), 0)
    w90 = np.median(im90[2550:2600, 1000:1060].reshape(-1, 3), 0)
    lw0, lw90 = srgb_to_lab(w0), srgb_to_lab(w90)
    print(f"paper  0: rgb {w0}  Lab {np.round(lw0, 1)}")
    print(f"paper 90: rgb {w90}  Lab {np.round(lw90, 1)}  dE00 {float(de2000(lw0, lw90)):.2f}")

    lab0, lab90 = srgb_to_lab(out[0][0]), srgb_to_lab(out[90][0])
    ok = ~np.isnan(lab0[..., 0]) & ~np.isnan(lab90[..., 0])
    de = de2000(lab0, lab90)
    print(f"\nsquares compared: {ok.sum()} of {N_FREQ * N_POW}")
    for name, m in [("all", ok),
                    ("dark  (L*<20 in 90deg)", ok & (lab90[..., 0] < 20)),
                    ("light (L*>=20 in 90deg)", ok & (lab90[..., 0] >= 20))]:
        v = de[m]
        if v.size:
            print(f"  {name:26s} n={v.size:3d}  median dE00 {np.median(v):5.2f}  90th {np.percentile(v, 90):5.2f}  max {v.max():5.2f}")
    dL = (lab90[..., 0] - lab0[..., 0])[ok]
    print(f"  mean L* shift (90 minus 0): {dL.mean():+.2f}")

    worst = np.dstack(np.unravel_index(np.argsort(np.where(ok, de, -1), axis=None)[::-1][:6], de.shape))[0]
    print("\nworst squares:")
    for fi, pi in worst:
        print(f"  {FREQ[fi]:5.0f} kHz {POWER[pi]:5.1f}%  0deg {np.round(lab0[fi, pi], 1)}  90deg {np.round(lab90[fi, pi], 1)}  dE00 {de[fi, pi]:.1f}")

    print("\nL* / C* (90 deg scan), rows = frequency 350..150, cols = power 40..90")
    C = np.hypot(lab90[..., 1], lab90[..., 2])
    for fi in range(N_FREQ - 1, -1, -1):
        print(f"{FREQ[fi]:4.0f} " + " ".join(f"{lab90[fi, pi, 0]:3.0f}/{C[fi, pi]:2.0f}" for pi in range(N_POW)))
    np.save(f"{SCANS}\\_lab90.npy", lab90)
    np.save(f"{SCANS}\\_lab0.npy", lab0)
