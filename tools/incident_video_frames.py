"""Read a local incident video and export labelled contact sheets (no source changes)."""
import sys
from pathlib import Path
import cv2
import numpy as np

path, output = Path(sys.argv[1]), Path(sys.argv[2])
output.mkdir(parents=True, exist_ok=True)
cap = cv2.VideoCapture(str(path))
fps, frames = cap.get(cv2.CAP_PROP_FPS), cap.get(cv2.CAP_PROP_FRAME_COUNT)
if not cap.isOpened() or fps <= 0:
    raise SystemExit('Cannot decode video')
duration = frames / fps
print(f'duration={duration:.3f}s fps={fps} frames={frames}')
for page, start in enumerate(range(0, int(duration) + 1, 12)):
    tiles = []
    for sec in range(start, min(start + 12, int(duration) + 1)):
        cap.set(cv2.CAP_PROP_POS_MSEC, sec * 1000)
        ok, frame = cap.read()
        if not ok: break
        tile = cv2.resize(frame, (512, 288))
        cv2.putText(tile, f'{sec}s', (8, 24), cv2.FONT_HERSHEY_SIMPLEX, .7, (0, 255, 255), 2)
        tiles.append(tile)
    if not tiles: continue
    while len(tiles) % 3: tiles.append(np.zeros_like(tiles[0]))
    sheet = np.vstack([np.hstack(tiles[i:i+3]) for i in range(0, len(tiles), 3)])
    target = output / f'frames-{page:02}.jpg'
    cv2.imencode('.jpg', sheet)[1].tofile(str(target))
    print(target)
cap.release()
