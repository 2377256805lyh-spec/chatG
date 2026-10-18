"""制作转头对应点：原画定位后，用 OpenCV 细化发丝、龙角和五官的像素对应。

制作依赖：numpy、Pillow、opencv-python-headless；桌面程序不需要这些依赖。
输入 gaze/pose-00.png 至 pose-15.png 是已对齐衣领和色调的16张原画，
pose-16.png 是待机图；landmarks.json 保存粗定位点，龙角尖端单独校准。
"""

import argparse
import hashlib
import json
from pathlib import Path
import struct

import cv2
import numpy as np
from PIL import Image


def main():
    asset_root = Path(__file__).resolve().parents[1] / 'assets'
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sources', type=Path, default=asset_root / 'gaze')
    parser.add_argument('--output', type=Path, default=asset_root / 'gaze-flow.bin')
    args = parser.parse_args()
    landmarks = json.loads((args.sources / 'landmarks.json').read_text(encoding='utf-8'))
    width, height = 384, 288
    yy, xx = np.mgrid[:height, :width].astype(np.float32)
    xy = np.stack([xx, yy], axis=2) / 2
    views, sources = [], {}
    for i in range(17):
        path = args.sources / f'pose-{i:02}.png'
        image = np.array(Image.open(path).convert('RGBA'))
        if image.shape != (624, 576, 4):
            raise ValueError(f'原画尺寸不正确：{path.name}')
        image = image[:432]
        alpha = image[:, :, 3:].astype(np.float32) / 255
        rgb = image[:, :, :3].astype(np.float32)
        # 灰色背景让白发与透明区有区别，避免背景被误配到头发内部。
        view = np.clip(rgb * alpha + 96 * (1 - alpha), 0, 255).astype(np.uint8)
        views.append(cv2.resize(cv2.cvtColor(view, cv2.COLOR_RGB2GRAY),
                                (width, height), interpolation=cv2.INTER_AREA))
        sources[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()

    flows = []
    for i in range(16):
        # 每个原画分别向待机图、下一个原画建立直接对应，减少绕经待机造成的误差。
        for j in [16, (i + 1) % 16]:
            source_mesh = np.array(landmarks['poses'][i], dtype=np.float32)
            target_mesh = np.array(landmarks['neutral'] if j == 16 else landmarks['poses'][j],
                                   dtype=np.float32)
            initial = xy.copy()
            for triangle in landmarks['triangles']:
                a, b, c = source_mesh[triangle]
                divisor = ((b[1] - c[1]) * (a[0] - c[0])
                           + (c[0] - b[0]) * (a[1] - c[1]))
                if abs(divisor) < 1e-4:
                    continue
                u = ((b[1] - c[1]) * (xy[:, :, 0] - c[0])
                     + (c[0] - b[0]) * (xy[:, :, 1] - c[1])) / divisor
                v = ((c[1] - a[1]) * (xy[:, :, 0] - c[0])
                     + (a[0] - c[0]) * (xy[:, :, 1] - c[1])) / divisor
                w = 1 - u - v
                mask = (u >= -1e-5) & (v >= -1e-5) & (w >= -1e-5)
                mapped = (u[:, :, None] * target_mesh[triangle[0]]
                          + v[:, :, None] * target_mesh[triangle[1]]
                          + w[:, :, None] * target_mesh[triangle[2]])
                initial[mask] = mapped[mask]
            initial *= 2
            warped = cv2.remap(views[j], initial[:, :, 0], initial[:, :, 1],
                               cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=96)
            engine = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
            engine.setFinestScale(0)
            engine.setPatchSize(8)
            engine.setPatchStride(3)
            engine.setGradientDescentIterations(100)
            engine.setVariationalRefinementIterations(30)
            residual = engine.calc(views[i], warped, None)
            residual = cv2.GaussianBlur(np.clip(residual, -16, 16), (0, 0), 1.2)
            final = cv2.remap(initial, xx + residual[:, :, 0], yy + residual[:, :, 1],
                              cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
            flow = (final - np.stack([xx, yy], axis=2)) / 2
            flow = cv2.resize(flow, (192, 144), interpolation=cv2.INTER_AREA)
            flow = cv2.GaussianBlur(flow, (0, 0), .65)
            if not np.isfinite(flow).all() or np.abs(flow).max() > 48:
                raise ValueError(f'第{i}个原画的位移超出有效范围')
            flows.append(flow.astype('<f4'))
        print(f'已制作转头原画 {i + 1}/16', flush=True)
    data = struct.pack('<4sIII', b'XGH2', 192, 144, 32) + b''.join(f.tobytes() for f in flows)
    args.output.write_bytes(data)
    metadata = {
        'format': 'XGH2', 'opencv': cv2.__version__, 'grid_step': 3,
        'algorithm': 'DIS optical flow after landmark alignment',
        'source': 'https://github.com/opencv/opencv', 'images_sha256': sources,
        'landmarks_sha256': hashlib.sha256((args.sources / 'landmarks.json').read_bytes()).hexdigest(),
        'sha256': hashlib.sha256(data).hexdigest(),
    }
    args.output.with_suffix('.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2),
                                               encoding='utf-8')


if __name__ == '__main__':
    main()
