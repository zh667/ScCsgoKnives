"""One unbiased arm measurement, used on both the CS:MC references and our render.

PCA is biased when the blob is only ~1.5x longer than it is wide, which is exactly
the case for a forearm that leaves the frame.  This instead fits the medial axis:
bin along a provisional axis, take each bin's centroid, fit a line through the
centroids, and repeat.  Width is then measured perpendicular to that line.
"""
import math
import numpy as np
AR = 16/9

def arm_stats(mask, bins=10, iters=3):
    ys, xs = np.nonzero(mask)
    if len(xs) < 200: return None
    H, W = mask.shape
    pts = np.stack([xs/W*AR, ys/H], 1)             # aspect-corrected, y down
    c = pts.mean(0)
    _, _, vt = np.linalg.svd(pts-c, full_matrices=False)
    ax = vt[0]
    if ax[1] < 0: ax = -ax                          # +ax runs down the arm
    for _ in range(iters):
        t = (pts-c)@ax
        lo, hi = np.percentile(t, 1), np.percentile(t, 99)
        cen, tt = [], []
        for k in range(bins):
            a, b = lo+(hi-lo)*k/bins, lo+(hi-lo)*(k+1)/bins
            sel = (t >= a) & (t < b)
            if sel.sum() < 20: continue
            cen.append(pts[sel].mean(0)); tt.append((a+b)/2)
        if len(cen) < 4: break
        cen = np.array(cen); c2 = cen.mean(0)
        _, _, v2 = np.linalg.svd(cen-c2, full_matrices=False)
        ax = v2[0]
        if ax[1] < 0: ax = -ax
        c = c2
    perp = np.array([-ax[1], ax[0]])
    t = (pts-c)@ax; w = (pts-c)@perp
    lo, hi = np.percentile(t, 1), np.percentile(t, 99)
    prof, clear = [], []
    for k in range(bins):
        a, b = lo+(hi-lo)*k/bins, lo+(hi-lo)*(k+1)/bins
        sel = (t >= a) & (t < b)
        if sel.sum() <= 30:
            prof.append(float('nan')); clear.append(False); continue
        prof.append(float((np.percentile(w[sel], 98)-np.percentile(w[sel], 2))/AR))
        # a bin touching the frame edge is cut off, not tapered -- its width is not a measurement
        clear.append(not (xs[sel].min() <= 1 or xs[sel].max() >= W-2
                          or ys[sel].max() >= H-2 or ys[sel].min() <= 1))
    hand = c+ax*lo
    good = [p for p, ok in zip(prof, clear) if ok and p == p]
    if len(good) < 4: good = [p for p in prof if p == p]
    return dict(lean=float(math.degrees(math.atan2(ax[0], ax[1]))),
                hand=[float(hand[0]/AR), float(hand[1])],
                width=float(np.nanmedian(prof)),
                width_hand=float(np.nanmean(good[:3])), width_far=float(np.nanmean(good[-3:])),
                taper=float(np.nanmean(good[-3:])/np.nanmean(good[:3])),
                length=float(hi-lo), prof=prof, px=int(len(xs)))
