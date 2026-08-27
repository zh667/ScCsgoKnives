#!/usr/bin/env python3
"""Render three dependency-light orthographic OBJ previews for porting checks."""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


def load_obj(path):
    vertices = []
    faces = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("v "):
            vertices.append(tuple(float(value) for value in line.split()[1:4]))
        elif line.startswith("f "):
            faces.append(tuple(int(value.split("/")[0]) - 1 for value in line.split()[1:4]))
    return vertices, faces


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("target", type=Path)
    args = parser.parse_args()

    vertices, faces = load_obj(args.source)
    image = Image.new("RGB", (960, 340), (24, 27, 31))
    draw = ImageDraw.Draw(image)
    views = [
        ("front XY", 0, 1, 2),
        ("side ZY", 2, 1, 0),
        ("top XZ", 0, 2, 1),
    ]
    for column, (label, horizontal, vertical, depth) in enumerate(views):
        panel_left = column * 320
        xs = [point[horizontal] for point in vertices]
        ys = [point[vertical] for point in vertices]
        span = max(max(xs) - min(xs), max(ys) - min(ys), 1e-6)
        scale = 250.0 / span
        center_x = (min(xs) + max(xs)) / 2.0
        center_y = (min(ys) + max(ys)) / 2.0

        def project(point):
            return (
                panel_left + 160 + (point[horizontal] - center_x) * scale,
                175 - (point[vertical] - center_y) * scale,
            )

        ordered = sorted(faces, key=lambda face: sum(vertices[index][depth] for index in face) / 3.0)
        for face in ordered:
            a, b, c = (vertices[index] for index in face)
            ab = tuple(b[i] - a[i] for i in range(3))
            ac = tuple(c[i] - a[i] for i in range(3))
            normal = (
                ab[1] * ac[2] - ab[2] * ac[1],
                ab[2] * ac[0] - ab[0] * ac[2],
                ab[0] * ac[1] - ab[1] * ac[0],
            )
            shade = int(90 + 150 * min(abs(normal[depth]), 1.0))
            color = (shade, min(255, shade + 18), min(255, shade + 28))
            draw.polygon([project(a), project(b), project(c)], fill=color)
        draw.text((panel_left + 12, 12), label, fill=(235, 235, 235))
        draw.rectangle((panel_left + 1, 1, panel_left + 318, 338), outline=(70, 76, 84))

    image.save(args.target)


if __name__ == "__main__":
    main()
