"""保留整个人物奔跑姿势，替换奔跑帧；其他动作按原压缩数据保留。"""

import argparse
import copy
import hashlib
import json
from pathlib import Path
import zipfile

import numpy as np
from PIL import Image

WIDTH, HEIGHT = 576, 624
PERIOD = .68


def copy_entries(old, new, names):
    # ZIP局部文件记录直接复制，不重新压缩几千张未修改画面。
    # 中央目录由zipfile重新生成；数据描述符包含在完整局部记录中。
    records = sorted(old.infolist(), key=lambda info: info.header_offset)
    for index, info in enumerate(records):
        if info.filename not in names:
            continue
        end = records[index+1].header_offset if index+1 < len(records) else old.start_dir
        item = copy.copy(info)
        item.header_offset = new.fp.tell()
        old.fp.seek(info.header_offset)
        remaining = end-info.header_offset
        while remaining:
            block = old.fp.read(min(4*1024*1024, remaining))
            if not block: raise ValueError('原帧包的局部记录不完整')
            new.fp.write(block)
            remaining -= len(block)
        new.filelist.append(item)
        new.NameToInfo[item.filename] = item
        new.start_dir = new.fp.tell()
        new._didModify = True


def load_frames(folder=None, recovery=None):
    if recovery:
        with zipfile.ZipFile(recovery) as original:
            keys = original.read('timelines/run-right.txt').decode().splitlines()
            if keys[-1] == keys[0]: keys.pop()
            return [np.frombuffer(original.read('frames/'+key+'.bgra'), np.uint8).reshape(HEIGHT,WIDTH,4).copy() for key in keys]
    result = []
    for path in sorted(folder.glob('*.png')):
        rgba = np.asarray(Image.open(path).convert('RGBA'), dtype=float)
        if rgba.shape != (HEIGHT,WIDTH,4): raise ValueError('奔跑画面尺寸不一致')
        rgba[:, :, :3] *= rgba[:, :, 3:]/255
        result.append(rgba[:,:,[2,1,0,3]].round().clip(0,255).astype(np.uint8))
    if not result: raise ValueError('没有奔跑画面')
    return result


def write_previews(frames, folder):
    folder.mkdir(parents=True, exist_ok=True)
    step = max(1, round(len(frames)/34))
    views = []
    for data in frames[::step]:
        rgb = data[:,:,[2,1,0]].astype(float)+(1-data[:,:,3:]/255)*np.array((232,225,234))
        views.append(Image.fromarray(rgb.round().clip(0,255).astype(np.uint8)).resize((288,312), Image.Resampling.LANCZOS))
    for name, images in [('run-right',views),('run-left',[im.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for im in views])]:
        duration = [round((i+1)*PERIOD*100/len(images))*10-round(i*PERIOD*100/len(images))*10 for i in range(len(images))]
        images[0].save(folder/(name+'.gif'), save_all=True, append_images=images[1:], duration=duration, loop=0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--oldpack', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--preview', type=Path, required=True)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument('--frames', type=Path)
    inputs.add_argument('--restorepack', type=Path)
    args = parser.parse_args()
    if args.oldpack.resolve() == args.output.resolve(): parser.error('请使用新输出路径')
    frames = load_frames(args.frames, args.restorepack)
    write_previews(frames,args.preview)
    with zipfile.ZipFile(args.oldpack) as old, zipfile.ZipFile(args.output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as new:
        metadata = json.loads(old.read('animation-info.json'))
        timelines = [name for name in old.namelist() if name.startswith('timelines/') and Path(name).stem not in ('run-right','run-left')]
        keep = set(timelines)
        for name in timelines:
            if name.endswith('.txt'): keep.update('frames/'+key+'.bgra' for key in old.read(name).decode().splitlines())
        copy_entries(old,new,keep)
        keys = {'run-right':[], 'run-left':[]}
        for frame in frames:
            for name, pixels in [('run-right',frame),('run-left',frame[:,::-1].copy())]:
                raw = pixels.tobytes();key=hashlib.sha256(raw).hexdigest()
                path = 'frames/'+key+'.bgra'
                if path not in new.NameToInfo: new.writestr(path,raw)
                keys[name].append(key)
        for name, items in keys.items():
            new.writestr('timelines/'+name+'.txt','\n'.join(items)+'\n')
            new.writestr('timelines/'+name+'.fps',f'{len(items)/PERIOD:.12f}\n')
            metadata['clips'][name] = {'frames':len(items),'unique':len(set(items)),'rate':len(items)/PERIOD}
        metadata['running'] = {'method':'original whole-character running poses; no limb rig or reconstructed gait',
                               'mode':'restored' if args.restorepack else 'interpolated whole frames', 'period':PERIOD}
        if args.restorepack: metadata['running']['source_pack_sha256']=hashlib.sha256(args.restorepack.read_bytes()).hexdigest()
        elif (args.frames/'running-source.json').exists(): metadata['running']=json.loads((args.frames/'running-source.json').read_text(encoding='utf-8'))
        metadata['preserved']=sorted(Path(n).stem for n in timelines if n.endswith('.txt'))
        new.writestr('animation-info.json',json.dumps(metadata,ensure_ascii=False,indent=2)+'\n')
    metadata['sha256']=hashlib.sha256(args.output.read_bytes()).hexdigest()
    args.output.with_suffix('.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('running frames',len(frames),'preserved actions',len(metadata['preserved']),flush=True)


if __name__ == '__main__': main()
