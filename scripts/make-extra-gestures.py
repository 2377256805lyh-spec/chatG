"""新动作素材制作：定位原画、分离肢体、只对肢体补帧，不改动其他动作。

制作依赖 numpy、Pillow、opencv-python-headless；桌面程序不需要这些依赖。
"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

W, H = 576, 624
PROJECT_ROOT = Path(__file__).resolve().parents[1]
ROOT = PROJECT_ROOT / 'assets'
ASSETS = ROOT / 'extra-gestures'


def load(path, normalize=True):
    image = np.array(Image.open(path).convert('RGBA'))
    if normalize:
        image[image[:, :, 3] < 24] = 0
        image[image[:, :, 3] > 247, 3] = 255
    return image


def align(image, reference):
    sift = cv2.SIFT_create(nfeatures=5000, contrastThreshold=.015)
    kp, desc = sift.detectAndCompute(cv2.cvtColor(image[:, :, :3], cv2.COLOR_RGB2GRAY), image[:, :, 3])
    mask = reference[:, :, 3].copy()
    mask[360:480] = 0
    mask[:, :155] = 0
    mask[:, 415:] = 0
    rp, rd = sift.detectAndCompute(cv2.cvtColor(reference[:, :, :3], cv2.COLOR_RGB2GRAY), mask)
    matches = [] if desc is None else [a for a, b in cv2.BFMatcher().knnMatch(desc, rd, k=2) if a.distance < .78 * b.distance]
    accepted = 0
    matrix = None
    if len(matches) >= 5:
        a = np.float32([kp[m.queryIdx].pt for m in matches])
        b = np.float32([rp[m.trainIdx].pt for m in matches])
        matrix, inliers = cv2.estimateAffinePartial2D(a, b, method=cv2.RANSAC, ransacReprojThreshold=3.0,
                                                    maxIters=5000, confidence=.999)
        accepted = int(inliers.sum()) if inliers is not None else 0
    if matrix is None or accepted < 5 or abs(matrix[0, 1]) > .09:
        ys, xs = np.nonzero(image[:, :, 3] > 220)
        ry, rx = np.nonzero(reference[:, :, 3] > 220)
        scale = (ry.max() - ry.min()) / (ys.max() - ys.min())
        matrix = np.array([[scale, 0, (rx.min() + rx.max() - scale * (xs.min() + xs.max())) / 2],
                           [0, scale, ry.max() - scale * ys.max()]], np.float64)
        accepted = 0
    else:
        # 固定正面视角，不因原画画线的微小差异引入身体旋转。
        scale = float(np.hypot(matrix[0, 0], matrix[1, 0]))
        matrix[0, 0] = matrix[1, 1] = scale
        matrix[0, 1] = matrix[1, 0] = 0
    output = cv2.warpAffine(image, matrix, (W, H), flags=cv2.INTER_LANCZOS4)
    output[output[:, :, 3] < 24] = 0
    return output, {'inliers': accepted, 'matrix': matrix.tolist()}


def alignment_stage():
    reference = load(ROOT / 'thinking' / 'pose-00.png', False)
    folder = ASSETS / 'aligned'
    folder.mkdir(exist_ok=True)
    metadata = {}
    for name in ['shy', 'sleepy', 'idea', 'clean']:
        source = load(ASSETS / 'raw' / (name + '.png'))
        count = 1 if name == 'clean' else 6
        for i in range(count):
            if count == 1:
                crop = source
            else:
                x0, x1 = round(i % 3 * source.shape[1] / 3), round((i % 3 + 1) * source.shape[1] / 3)
                y0, y1 = round(i // 3 * source.shape[0] / 2), round((i // 3 + 1) * source.shape[0] / 2)
                crop = source[y0:y1, x0:x1]
            output, info = align(crop, reference)
            filename = f'{name}-{i:02}.png'
            Image.fromarray(output).save(folder / filename)
            metadata[filename] = info
            print(filename, 'inliers', info['inliers'], flush=True)
    (ASSETS / 'alignment.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    font = ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc', 14)
    canvas = Image.new('RGB', (1152, 1035), '#efe9f4')
    draw = ImageDraw.Draw(canvas)
    for row, name in enumerate(['shy', 'sleepy', 'idea']):
        for col, index in enumerate([0, 1, 3, 4]):
            image = Image.open(folder / f'{name}-{index:02}.png').convert('RGBA')
            image = image.resize((288, 312), Image.Resampling.LANCZOS)
            canvas.paste(image, (col * 288, row * 345 + 25), image)
            draw.text((col * 288 + 12, row * 345 + 4), f'{name} {index}', fill='#594368', font=font)
    canvas.save(ASSETS / '姿势定位.png')


def polygon_mask(points):
    canvas = Image.new('L', (W * 3, H * 3))
    ImageDraw.Draw(canvas).polygon([(round(x * 3), round(y * 3)) for x, y in points], fill=255)
    return np.array(canvas.resize((W, H), Image.Resampling.LANCZOS))


def premultiplied(image):
    result = image.astype(np.float32)
    result[:, :, :3] *= result[:, :, 3:] / 255
    return result


def straight(image):
    output = image.copy()
    output[:, :, :3] *= 255 / np.maximum(output[:, :, 3:], 1)
    output[output[:, :, 3] < 8] = 0
    return np.rint(output).clip(0, 255).astype(np.uint8)


def over(foreground, background):
    return foreground + background * (1 - foreground[:, :, 3:] / 255)


def extraction_stage():
    spec = json.loads((ASSETS / 'regions.json').read_text(encoding='utf-8'))
    reference = load(ROOT / 'thinking' / 'pose-00.png', False)
    clean = load(ASSETS / 'aligned' / 'clean-00.png')
    output = ASSETS / 'layers'
    output.mkdir(exist_ok=True)
    original_masks = {side: polygon_mask(info['polygon']) for side, info in spec['original'].items()}
    font = ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc', 16)
    for name, info in spec['actions'].items():
        hole = np.maximum.reduce([original_masks[side] for side in info['sides']])
        # 仅补出旧手臂遮住的底图。五官、衣领、裙摆和翅膀其余部分完全保留原像素。
        hole = cv2.dilate(hole, np.ones((3, 3), np.uint8))
        hole = cv2.GaussianBlur(hole, (3, 3), .5)[:, :, None].astype(np.float32) / 255
        body = np.rint(reference * (1 - hole) + clean * hole).astype(np.uint8)
        Image.fromarray(body).save(output / f'{name}-body.png')
        canvas = Image.new('RGB', (1152, 1320), '#ede7f3')
        for i in range(6):
            image = reference if i == 0 else load(ASSETS / 'aligned' / f'{name}-{i:02}.png')
            parts = []
            for side in info['sides']:
                mask = polygon_mask(info['polygons'][side][i])
                part = image.copy()
                part[:, :, 3] = np.rint(part[:, :, 3].astype(float) * mask / 255).astype(np.uint8)
                part[part[:, :, 3] < 8] = 0
                count, labels, stats, _ = cv2.connectedComponentsWithStats((part[:, :, 3] > 32).astype(np.uint8))
                for component in range(1, count):
                    if stats[component, cv2.CC_STAT_AREA] < 32: part[labels == component] = 0
                Image.fromarray(part).save(output / f'{name}-{side}-{i:02}.png')
                parts.append(premultiplied(part))
            foreground = parts[0]
            for part in parts[1:]: foreground = over(part, foreground)
            pose = Image.fromarray(straight(over(foreground, premultiplied(body))))
            x, y = i % 3 * 384, i // 3 * 660
            view = pose.resize((384, 416), Image.Resampling.LANCZOS)
            canvas.paste(view, (x, y + 28), view)
            arm = Image.fromarray(straight(foreground)).crop((150, 285, 400, 490)).resize((330, 200))
            canvas.paste(arm, (x + 20, y + 448), arm)
            ImageDraw.Draw(canvas).text((x + 12, y + 4), f'{name} {i}', font=font, fill='#594368')
        canvas.save(ASSETS / f'{name}-肢体分离.png')
    print('已分离新动作的手臂与遮挡底图。', flush=True)


class ArmTransition:
    def __init__(self, first, second, first_joints, second_joints):
        self.first, self.second = first, second
        # 只在手臂所在的区域计算位置对应，其他位置本来就是透明像素。
        self.grid = np.stack(np.meshgrid(np.arange(140, 410), np.arange(280, 500)), axis=2).astype(np.float32)
        self.joints = [np.asarray(first_joints, np.float32), np.asarray(second_joints, np.float32)]
        self.distances = []
        for image in [first, second]:
            mask = (image[:, :, 3] > 127).astype(np.uint8)
            self.distances.append(cv2.distanceTransform(mask, cv2.DIST_L2, 5)
                                  - cv2.distanceTransform(1 - mask, cv2.DIST_L2, 5))

    def target_joints(self, amount):
        a, b = self.joints
        result = [a[0] * (1 - amount) + b[0] * amount]
        for i in range(3):
            da, db = a[i + 1] - a[i], b[i + 1] - b[i]
            aa, ab = np.arctan2(da[1], da[0]), np.arctan2(db[1], db[0])
            angle = aa + ((ab - aa + np.pi) % (2 * np.pi) - np.pi) * amount
            length = np.linalg.norm(da) * (1 - amount) + np.linalg.norm(db) * amount
            result.append(result[-1] + np.array([np.cos(angle), np.sin(angle)]) * length)
        return np.asarray(result, np.float32)

    def mapping(self, source, target):
        # 上臂、前臂分别跟随肩肘腕的转动；手掌以整块刚性旋转为主，避免手指被网格拉长。
        maps, weights = [], []
        for i in range(3):
            a, b = source[i + 1] - source[i], target[i + 1] - target[i]
            scale = np.linalg.norm(b) / max(np.linalg.norm(a), 1) if i < 2 else 1
            angle = np.arctan2(a[1], a[0]) - np.arctan2(b[1], b[0])
            cosine, sine = np.cos(angle), np.sin(angle)
            d = self.grid - target[i]
            mapped = np.stack((cosine * d[:, :, 0] - sine * d[:, :, 1],
                               sine * d[:, :, 0] + cosine * d[:, :, 1]), axis=2) / scale + source[i]
            position = np.clip(np.sum(d * b, axis=2) / max(float(np.dot(b, b)), 1), 0, 1)
            nearest = target[i] + position[:, :, None] * b
            distance = np.sum((self.grid - nearest) ** 2, axis=2)
            weight = np.exp(-distance / 150) * (1.3 if i == 2 else 1)
            maps.append(mapped)
            weights.append(weight)
        total = np.maximum(sum(weights), 1e-20)
        mapping = sum(m * w[:, :, None] for m, w in zip(maps, weights)) / total[:, :, None]
        return mapping.astype(np.float32)

    @staticmethod
    def warp(image, mapped, border=0):
        return cv2.remap(image, mapped[:, :, 0], mapped[:, :, 1], cv2.INTER_LINEAR,
                         borderMode=cv2.BORDER_CONSTANT, borderValue=border)

    def render(self, amount):
        if amount <= 0: return self.first
        if amount >= 1: return self.second
        target = self.target_joints(amount)
        ma, mb = self.mapping(self.joints[0], target), self.mapping(self.joints[1], target)
        a = self.warp(self.first, ma)
        b = self.warp(self.second, mb)
        da = self.warp(self.distances[0], ma, -64)
        db = self.warp(self.distances[1], mb, -64)
        alpha = np.clip(da * (1 - amount) + db * amount + .5, 0, 1) * 255
        blend = amount ** 3 / (amount ** 3 + (1 - amount) ** 3)
        mixed = a * (1 - blend) + b * blend
        mixed[:, :, :3] *= alpha[:, :, None] / np.maximum(mixed[:, :, 3:], 1)
        mixed[:, :, 3] = alpha
        mixed[:, :, :3] = np.minimum(mixed[:, :, :3], mixed[:, :, 3:])
        result = np.zeros((H, W, 4), np.float32)
        result[280:500, 140:410] = mixed.clip(0, 255)
        return result


class Gesture:
    def __init__(self, name, info):
        self.name, self.info = name, info
        self.layers = {side: [premultiplied(load(ASSETS / 'layers' / f'{name}-{side}-{i:02}.png', False)) for i in range(6)]
                       for side in info['sides']}
        self.transitions = {}

    def sample(self, seconds):
        keys = self.info['keys']
        index = next((i for i in range(1, len(keys)) if seconds <= keys[i][0]), len(keys) - 1)
        t0, first = keys[index - 1]
        t1, second = keys[index]
        amount = np.clip((seconds - t0) / (t1 - t0), 0, 1)
        amount = amount * amount * (3 - 2 * amount)
        result = np.zeros((H, W, 4), np.float32)
        for side in self.info['sides']:
            a, b = min(first, second), max(first, second)
            if a == b:
                layer = self.layers[side][a]
            else:
                key = (side, a, b)
                if key not in self.transitions:
                    self.transitions[key] = ArmTransition(self.layers[side][a], self.layers[side][b],
                        self.info['joints'][side][a], self.info['joints'][side][b])
                layer = self.transitions[key].render(amount if first == a else 1 - amount)
            result = over(layer, result)
        return result


def frames_stage(preview_only=False):
    spec = json.loads((ASSETS / 'regions.json').read_text(encoding='utf-8'))
    output = ROOT / 'extra-gestures.zip'
    archive = None if preview_only else zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED, compresslevel=5)
    metadata = {'rate': 60, 'size': [W, H], 'method': 'isolated painted limb poses with shoulder elbow wrist fingertip guides; fixed original body', 'clips': {}}
    seen = set()
    font = ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc', 15)
    for name, info in spec['actions'].items():
        gesture = Gesture(name, info)
        body = premultiplied(load(ASSETS / 'layers' / f'{name}-body.png', False))
        if preview_only:
            sample_times = np.linspace(0, info['duration'], 12)
            canvas = Image.new('RGB', (1152, 1005), '#efe8f5')
            for i, seconds in enumerate(sample_times):
                layer = gesture.sample(seconds)
                image = Image.fromarray(straight(over(layer, body))).resize((288, 312), Image.Resampling.LANCZOS)
                canvas.paste(image, (i % 4 * 288, i // 4 * 335 + 20), image)
                ImageDraw.Draw(canvas).text((i % 4 * 288 + 12, i // 4 * 335 + 1), f'{seconds:.2f}s', font=font, fill='#594368')
            canvas.save(ASSETS / f'{name}-过渡审核.png')
            print(name, 'preview ready', flush=True)
            continue
        timeline = []
        for i in range(round(info['duration'] * 60) + 1):
            layer = np.rint(gesture.sample(i / 60)).clip(0, 255).astype(np.uint8)
            data = layer[:, :, [2, 1, 0, 3]].tobytes()
            key = hashlib.sha256(data).hexdigest()[:24]
            if key not in seen:
                archive.writestr('frames/' + key + '.bgra', data)
                seen.add(key)
            timeline.append(key)
        archive.writestr('timelines/' + name + '.txt', '\n'.join(timeline) + '\n')
        archive.writestr('timelines/' + name + '.fps', '60')
        metadata['clips'][name] = {'frames': len(timeline), 'unique': len(set(timeline)), 'duration': info['duration']}
        print(name, metadata['clips'][name], flush=True)
    if archive is not None:
        archive.close()
        metadata['sha256'] = hashlib.sha256(output.read_bytes()).hexdigest()
        metadata['bodies_sha256'] = {}
        for name in spec['actions']:
            src = ASSETS / 'layers' / f'{name}-body.png'
            target = ROOT / 'artwork' / f'extra-{name}-body.png'
            target.write_bytes(src.read_bytes())
            metadata['bodies_sha256'][target.name] = hashlib.sha256(target.read_bytes()).hexdigest()
        sources = list((ASSETS / 'raw').glob('*.png')) + [ASSETS / 'regions.json', ROOT / 'thinking' / 'pose-00.png', Path(__file__)]
        metadata['sources_sha256'] = {p.relative_to(PROJECT_ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
        (ROOT / 'extra-gestures.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage', choices=['align', 'layers', 'preview', 'frames'], required=True)
    args = parser.parse_args()
    if args.stage == 'align': alignment_stage()
    if args.stage == 'layers': extraction_stage()
    if args.stage == 'preview': frames_stage(True)
    if args.stage == 'frames': frames_stage()


if __name__ == '__main__': main()
