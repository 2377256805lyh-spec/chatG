"""从保留的思考原画制作手臂和遮挡底图；制作依赖 numpy、Pillow、opencv-python-headless。"""

import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image


def main():
    root = Path(__file__).resolve().parents[1] / 'assets'
    sources = root / 'thinking'
    polygons = json.loads((sources / 'arm-regions.json').read_text(encoding='utf-8'))['polygons']
    images, masks = [], []
    for i, polygon in enumerate(polygons):
        image = np.array(Image.open(sources / f'pose-{i:02}.png').convert('RGBA'))
        images.append(image)
        mask = np.zeros(image.shape[:2], np.uint8)
        cv2.fillPoly(mask, [np.rint(np.array(polygon) * 3).astype(np.int32)], 255)
        labels = np.zeros(mask.shape, np.uint8)
        labels[cv2.dilate(mask, np.ones((3, 3), np.uint8)) > 0] = cv2.GC_PR_BGD
        labels[mask > 0] = cv2.GC_PR_FGD
        labels[cv2.erode(mask, np.ones((7, 7), np.uint8)) > 0] = cv2.GC_FGD
        cv2.grabCut(image[:, :, :3], labels, None, np.zeros((1, 65)), np.zeros((1, 65)),
                    4, cv2.GC_INIT_WITH_MASK)
        mask = np.where((labels == cv2.GC_FGD) | (labels == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
        masks.append(cv2.GaussianBlur(mask, (3, 3), .55))

    clean = np.array(Image.open(sources / 'clean-body-aligned.png').convert('RGBA'))
    sift = cv2.SIFT_create(nfeatures=2500)
    reference = images[1]
    keypoints, descriptors = sift.detectAndCompute(cv2.cvtColor(reference[:, :, :3], cv2.COLOR_RGB2GRAY), reference[:, :, 3])
    yy, xx = np.mgrid[:624, :576].astype(np.float32)
    arms, backdrops = [], []
    for image, mask in zip(images, masks):
        arm = image.copy()
        arm[:, :, 3] = np.rint(image[:, :, 3].astype(float) * mask / 255).astype(np.uint8)
        arms.append(arm)
        target_points, target_descriptors = sift.detectAndCompute(
            cv2.cvtColor(image[:, :, :3], cv2.COLOR_RGB2GRAY), image[:, :, 3])
        matches = [a for a, b in cv2.BFMatcher().knnMatch(descriptors, target_descriptors, k=2) if a.distance < .7 * b.distance]
        source_points = np.float32([keypoints[m.queryIdx].pt for m in matches])
        destination_points = np.float32([target_points[m.trainIdx].pt for m in matches])
        matrices = []
        for head in [True, False]:
            valid = source_points[:, 1] < 330 if head else ((source_points[:, 1] > 345) & ((source_points[:, 0] > 315) | (source_points[:, 0] < 140)))
            matrix, accepted = cv2.estimateAffinePartial2D(source_points[valid], destination_points[valid],
                method=cv2.RANSAC, ransacReprojThreshold=2.5)
            if matrix is None or int(accepted.sum()) < 6:
                raise ValueError('思考底图缺少足够的定位点')
            matrices.append(cv2.invertAffineTransform(matrix))
        head_weight = np.clip((405 - yy) / 65, 0, 1)
        head_weight = head_weight * head_weight * (3 - 2 * head_weight)
        maps = []
        for matrix in matrices:
            maps.append(np.stack([matrix[0, 0] * xx + matrix[0, 1] * yy + matrix[0, 2],
                                  matrix[1, 0] * xx + matrix[1, 1] * yy + matrix[1, 2]], axis=2))
        mapped = (maps[0] * head_weight[:, :, None] + maps[1] * (1 - head_weight[:, :, None])).astype(np.float32)
        aligned = cv2.remap(clean, mapped[:, :, 0], mapped[:, :, 1], cv2.INTER_LINEAR)
        # 只修补运动区域，其他像素仍来自原画。
        hole = cv2.GaussianBlur(cv2.dilate(mask, np.ones((3, 3), np.uint8)), (3, 3), .6)
        weight = hole[:, :, None].astype(float) / 255
        body = np.rint(image * (1 - weight) + aligned * weight).astype(np.uint8)
        backdrops.append(body)
    for name, items in [('thinking-arms', arms), ('thinking-backdrops', backdrops)]:
        atlas = Image.new('RGBA', (1728, 1872))
        for i, image in enumerate(items):
            atlas.paste(Image.fromarray(image), (i % 3 * 576, i // 3 * 624))
        atlas.save(root / 'artwork' / f'{name}.png')
    paths = sorted(sources.glob('*.png')) + [sources / 'arm-regions.json']
    metadata = {
        'opencv': cv2.__version__,
        'method': 'separate arm foreground from body; repair hidden areas with retained generated plate',
        'source': 'https://github.com/lisiyao21/AnimeInterp',
        'note': '借鉴分区域匹配的思路，未使用该项目模型或代码。',
        'sources_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in paths},
        'atlases_sha256': {name: hashlib.sha256((root / 'artwork' / name).read_bytes()).hexdigest()
                          for name in ['thinking-arms.png', 'thinking-backdrops.png']},
    }
    (root / 'artwork' / 'thinking-layers.json').write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
