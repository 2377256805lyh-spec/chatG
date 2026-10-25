"""制作动作帧包：双向位置对应后补帧，保留踮脚与思考的原有画面。

依赖 numpy、Pillow、opencv-python-headless；只在制作时使用。
先用 export-animation-poses.ps1 从现有程序导出对齐原画，再运行本入口。
奔跑沿用原有完整人物姿势的补帧画面；左向逐像素镜像。其他动作使用位置对应补帧。
"""

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import zipfile

import numpy as np
from PIL import Image, ImageDraw
PERIOD = .68


WIDTH, HEIGHT = 576, 624


def load_pose(path):
    rgba = np.asarray(Image.open(path).convert('RGBA'), dtype=np.float32)
    if rgba.shape != (HEIGHT, WIDTH, 4):
        raise ValueError(f'原画尺寸错误：{path}')
    bgra = rgba[:, :, [2, 1, 0, 3]].copy()
    bgra[:, :, :3] *= bgra[:, :, 3:] / 255
    bgra[bgra[:,:,3]<24]=0
    return np.rint(bgra).astype(np.uint8)


def view(image):
    # 透明区使用同一个灰底；半透明边缘按实际显示颜色匹配。
    visible = image[:, :, :3].astype(np.float32) + 96 * (1 - image[:, :, 3:] / 255)
    gray = cv2.cvtColor(np.clip(visible, 0, 255).astype(np.uint8), cv2.COLOR_BGR2GRAY)
    return cv2.resize(gray, (384, 416), interpolation=cv2.INTER_AREA)


def face_pose(image):
    b,g,r,alpha=np.moveaxis(image.astype(np.int16),2,0)
    skin=((alpha>210)&(r>175)&(r-g>5)&(g-b>3)&(r-b>20)).astype(np.uint8)
    skin[:90]=0;skin[492:]=0;skin[:,:168]=0;skin[:,450:]=0
    count,labels,stats,centers=cv2.connectedComponentsWithStats(skin)
    if count<2: raise ValueError('未能找到人物面部，不能安全补帧')
    index=1+int(np.argmax(stats[1:,cv2.CC_STAT_AREA])); x,y,w,h,_=stats[index]
    eyes=((alpha>210)&(b-r>12)&(b-g>24)&(r>55)).astype(np.uint8)
    eyes[:max(0,y-12)]=0;eyes[y+h+1:]=0;eyes[:,:x]=0;eyes[:,x+w+1:]=0
    ec,_,es,ep=cv2.connectedComponentsWithStats(eyes)
    candidates=[i for i in range(1,ec) if es[i,cv2.CC_STAT_AREA]>30]
    candidates=sorted(candidates,key=lambda i:es[i,cv2.CC_STAT_AREA],reverse=True)[:2]
    center=centers[index].astype(np.float32);angle=0
    if len(candidates)==2:
        left,right=sorted([ep[i] for i in candidates],key=lambda p:p[0])
        if right[0]-left[0]>24:
            angle=float(np.arctan2(right[1]-left[1],right[0]-left[0]));center=((left+right)/2).astype(np.float32)
    return center/1.5, angle, (y+h+8)/1.5


def head_guide(first,second,grid):
    a,aa,chin=face_pose(first);b,ba,_=face_pose(second)
    delta=np.clip(ba-aa,-.45,.45)
    rotation=np.array([[np.cos(delta),-np.sin(delta)],[np.sin(delta),np.cos(delta)]],dtype=np.float32)
    displacement=(grid-a)@rotation.T+b-grid
    weight=np.clip((396-grid[:,:,1])/(396-chin),0,1)
    return (displacement*weight[:,:,None]).astype(np.float32)


class Transition:
    def __init__(self, first, second):
        self.first, self.second = first, second
        self.same = np.array_equal(first, second)
        if self.same:
            return
        self.distances=[]
        for image in (first,second):
            mask=(image[:,:,3]>127).astype(np.uint8)
            self.distances.append(cv2.distanceTransform(mask,cv2.DIST_L2,cv2.DIST_MASK_PRECISE)
                                  -cv2.distanceTransform(1-mask,cv2.DIST_L2,cv2.DIST_MASK_PRECISE))
        a, b = view(first), view(second)
        self.flows = []
        yy, xx = np.mgrid[:416,:384].astype(np.float32)
        grid = np.stack((xx,yy),axis=2)
        for direction, (source, target) in enumerate(((a, b), (b, a))):
            engine = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
            engine.setFinestScale(0)
            engine.setPatchSize(8)
            engine.setPatchStride(3)
            engine.setGradientDescentIterations(100)
            engine.setVariationalRefinementIterations(30)
            guide=head_guide(first,second,grid) if direction==0 else head_guide(second,first,grid)
            initial = grid+guide
            aligned = cv2.remap(target,initial[:,:,0],initial[:,:,1],cv2.INTER_LINEAR,
                                borderMode=cv2.BORDER_CONSTANT,borderValue=96)
            residual = engine.calc(source,aligned,None)
            residual = cv2.GaussianBlur(residual,(0,0),.8)
            mapped = cv2.remap(initial,xx+residual[:,:,0],yy+residual[:,:,1],
                              cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
            flow = mapped-grid
            # 头部沿两眼与面部整体定位，只做整块位移和转动；不让像素匹配拉动五官。
            _,_,chin = face_pose(first if direction == 0 else second)
            head = np.clip((chin+8-yy)/12,0,1)[:,:,None].astype(np.float32)
            flow = flow*(1-head)+guide*head
            flow = cv2.GaussianBlur(flow, (0, 0), 0.65)
            flow = cv2.resize(flow, (WIDTH, HEIGHT), interpolation=cv2.INTER_LINEAR) * 1.5
            if not np.isfinite(flow).all() or np.abs(flow).max() > WIDTH / 3:
                raise ValueError('动作原画的位置对应超出范围')
            self.flows.append(flow)
        yy, xx = np.mgrid[:HEIGHT, :WIDTH].astype(np.float32)
        self.grid = np.stack((xx, yy), axis=2)

    def warped(self, image, flow, amount, border=0):
        # 反求当前位置在原图的位置，而不是整张图在半程突然换一张。
        position = self.grid - flow * amount
        for _ in range(8):
            sample = cv2.remap(flow, position[:, :, 0], position[:, :, 1],
                               cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
            position = (position + self.grid - sample * amount) * .5
        return cv2.remap(image, position[:, :, 0], position[:, :, 1],
                         cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT,borderValue=border).astype(np.float32)

    def render(self, amount):
        if amount <= 0 or self.same:
            return self.first
        if amount >= 1:
            return self.second
        a = self.warped(self.first, self.flows[0], amount)
        b = self.warped(self.second, self.flows[1], 1 - amount)
        # 用轮廓到边缘的距离决定新边缘，避免两张半透明人物叠在一起。
        da=self.warped(self.distances[0],self.flows[0],amount,-64)
        db=self.warped(self.distances[1],self.flows[1],1-amount,-64)
        distance=da*(1-amount)+db*amount
        alpha=np.clip(distance+.5,0,1)*255
        blend=amount**3/(amount**3+(1-amount)**3)
        mixed=a*(1-blend)+b*blend
        result=np.empty_like(mixed)
        result[:,:,:3]=mixed[:,:,:3]/np.maximum(mixed[:,:,3:],1)*alpha[:,:,None]
        result[:,:,3]=alpha
        result[:, :, :3] = np.minimum(result[:, :, :3], result[:, :, 3:])
        return np.clip(result, 0, 255).astype(np.uint8)


def ease(value):
    return value * value * (3 - 2 * value)


class SmallTransition:
    """只细化已经密集的相邻画面；不再推断整张脸的关键点或重建透明轮廓。"""
    def __init__(self, first, second):
        self.first,self.second=first,second
        self.same=np.array_equal(first,second)
        if self.same:return
        self.flows=[]
        a,b=view(first),view(second)
        for source,target in ((a,b),(b,a)):
            engine=cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
            engine.setFinestScale(0)
            flow=engine.calc(source,target,None)
            flow=cv2.resize(cv2.GaussianBlur(flow,(0,0),1.2),(WIDTH,HEIGHT),interpolation=cv2.INTER_LINEAR)*1.5
            # 原画间隔只有约1/60秒，大幅位移不可信，不能因此拉动五官或袖口。
            self.flows.append(np.clip(flow,-4,4))
        yy,xx=np.mgrid[:HEIGHT,:WIDTH].astype(np.float32)
        self.grid=np.stack((xx,yy),axis=2)

    def render(self,t):
        if t<=0 or self.same:return self.first
        if t>=1:return self.second
        a=Transition.warped(self,self.first,self.flows[0],t)
        b=Transition.warped(self,self.second,self.flows[1],1-t)
        result=a*(1-t)+b*t
        first=self.first.astype(np.float32);second=self.second.astype(np.float32)
        still=cv2.erode((np.max(np.abs(first-second),axis=2)<=1).astype(np.uint8),np.ones((5,5),np.uint8))>0
        result[still]=first[still]
        distance=np.mean(np.abs(first-second))
        # 匹配失败时只在本来相邻的两帧之间取像素，不放大原来极小的画面变化。
        if np.mean(np.abs(first-result))+np.mean(np.abs(result-second))>distance*1.6+.04:
            result=first*(1-t)+second*t
        return np.rint(result).clip(0,255).astype(np.uint8)


def replace_shake(args):
    paths = sorted(args.shake_frames.glob('*.png'))
    if len(paths) != 313 or args.rate != 120:
        raise ValueError('摇头须由生产导出器提供313帧，120帧/秒')
    spec = importlib.util.spec_from_file_location('xiaog_running_pack', Path(__file__).with_name('make-running.py'))
    pack_writer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(pack_writer)
    args.preview.mkdir(parents=True, exist_ok=True)
    previews = []
    with zipfile.ZipFile(args.oldpack) as old, zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as new:
        metadata = json.loads(old.read('animation-info.json'))
        metadata['preserved'] = sorted(Path(n).stem for n in old.namelist()
                                       if n.startswith('timelines/') and n.endswith('.txt') and Path(n).stem != 'shake')
        keep = {n for n in old.namelist() if n.startswith('timelines/') and Path(n).stem != 'shake'}
        for name in list(keep):
            if name.endswith('.txt'):
                keep.update('frames/'+key+'.bgra' for key in old.read(name).decode().splitlines())
        pack_writer.copy_entries(old, new, keep)
        old_keys = old.read('timelines/shake.txt').decode().splitlines()
        neutral = np.frombuffer(old.read('frames/'+old_keys[0]+'.bgra'), np.uint8).reshape(HEIGHT, WIDTH, 4)
        keys = []
        for index, path in enumerate(paths):
            rgba = np.asarray(Image.open(path).convert('RGBA'), dtype=float)
            if rgba.shape != (HEIGHT, WIDTH, 4):
                raise ValueError('摇头画面尺寸不一致')
            rgba[:,:,:3] *= rgba[:,:,3:]/255
            image = np.rint(rgba[:,:,[2,1,0,3]]).clip(0,255).astype(np.uint8)
            # 身体始终沿用正式待机像素，PNG往返也不会改变裙摆和脚的位置/颜色。
            image[134*3:] = neutral[134*3:]
            if index == 0 or index == len(paths)-1:
                image = neutral.copy()
            data = image.tobytes(); key = hashlib.sha256(data).hexdigest()
            entry = 'frames/'+key+'.bgra'
            if entry not in new.NameToInfo:
                new.writestr(entry, data)
            keys.append(key)
            if index % 2 == 0:
                rgb = image[:,:,[2,1,0]].astype(float)+(1-image[:,:,3:]/255)*np.array((232,225,234))
                previews.append(Image.fromarray(np.rint(rgb).clip(0,255).astype(np.uint8)))
        new.writestr('timelines/shake.txt', '\n'.join(keys)+'\n')
        new.writestr('timelines/shake.fps', '120\n')
        metadata['clips']['shake'] = {'frames':len(keys), 'unique':len(set(keys)), 'rate':120}
        metadata['shake'] = {'method':'damped asymmetric yaw with small pitch; jaw-following neck boundary', 'duration':2.6,
                             'previous_pack_sha256':hashlib.sha256(args.oldpack.read_bytes()).hexdigest(),
                             'source_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}}
        new.writestr('animation-info.json', json.dumps(metadata,ensure_ascii=False,indent=2)+'\n')
    durations = [round((i+1)*1000/60)-round(i*1000/60) for i in range(len(previews))]
    previews[0].save(args.preview/'摇头.webp',save_all=True,append_images=previews[1:],duration=durations,loop=0,lossless=True)
    gif = [im.resize((288,312),Image.Resampling.LANCZOS) for im in previews[::3]]
    gif[0].save(args.preview/'摇头.gif',save_all=True,append_images=gif[1:],duration=50,loop=0)
    metadata['sha256'] = hashlib.sha256(args.output.read_bytes()).hexdigest()
    args.output.with_suffix('.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('仅更新摇头：313帧；其余动作原压缩记录保留。',flush=True)


def smooth_existing(args):
    global cv2
    import cv2
    cv2.setNumThreads(4)
    spec=importlib.util.spec_from_file_location('xiaog_running_pack',Path(__file__).with_name('make-running.py'))
    writer=importlib.util.module_from_spec(spec);spec.loader.exec_module(writer)
    args.preview.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(args.oldpack) as old,zipfile.ZipFile(args.output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as new:
        metadata=json.loads(old.read('animation-info.json'))
        names=['tiptoe','thinking','run-right','run-left']
        skip={'animation-info.json'}|{'timelines/'+name+suffix for name in names for suffix in ('.txt','.fps')}
        writer.copy_entries(old,new,set(old.namelist())-skip)
        runs=[]
        for name in ['tiptoe','thinking','run-right']:
            keys=old.read('timelines/'+name+'.txt').decode().splitlines()
            rate=float(old.read('timelines/'+name+'.fps'))
            if not 59<=rate<=61:raise ValueError('现有加密入口只接受原有60Hz动作')
            loop=name=='run-right';count=len(keys)*2 if loop else len(keys)*2-1
            output=[];views=[];previous=None
            for index in range(count):
                a=index//2;b=(a+1)%len(keys) if loop else min(a+1,len(keys)-1)
                if previous!=a:
                    first=np.frombuffer(old.read('frames/'+keys[a]+'.bgra'),np.uint8).reshape(HEIGHT,WIDTH,4).copy()
                    second=np.frombuffer(old.read('frames/'+keys[b]+'.bgra'),np.uint8).reshape(HEIGHT,WIDTH,4).copy()
                    transition=None;previous=a
                if index%2 and keys[a]!=keys[b]:
                    if transition is None:transition=SmallTransition(first,second)
                    frame=transition.render(.5)
                else:frame=first
                data=frame.tobytes();key=hashlib.sha256(data).hexdigest();entry='frames/'+key+'.bgra'
                if entry not in new.NameToInfo:new.writestr(entry,data)
                output.append(key)
                if loop:
                    data=frame[:,::-1].copy().tobytes();mirror=hashlib.sha256(data).hexdigest();entry='frames/'+mirror+'.bgra'
                    if entry not in new.NameToInfo:new.writestr(entry,data)
                    runs.append(mirror)
                if index%5==0:
                    rgb=frame[:,:,[2,1,0]].astype(float)+(1-frame[:,:,3:]/255)*np.array((232,225,234))
                    views.append(Image.fromarray(np.rint(rgb).clip(0,255).astype(np.uint8)).resize((288,312),Image.Resampling.LANCZOS))
            for clip,items in [(name,output)]+([('run-left',runs)] if loop else []):
                new.writestr('timelines/'+clip+'.txt','\n'.join(items)+'\n')
                new.writestr('timelines/'+clip+'.fps',f'{rate*2:.12f}\n')
                metadata['clips'][clip]={'frames':len(items),'unique':len(set(items)),'rate':rate*2}
            views[0].save(args.preview/(name+'.gif'),save_all=True,append_images=views[1:],duration=round(5000/(rate*2)),loop=0)
            if loop:
                mirrored=[im.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for im in views]
                mirrored[0].save(args.preview/'run-left.gif',save_all=True,append_images=mirrored[1:],duration=round(5000/(rate*2)),loop=0)
            print('已加密',name,len(keys),'->',len(output),'帧，原帧逐张保留',flush=True)
        metadata['smoothing']={'method':'bounded small-step midpoint correspondence; stationary pixels fixed; premultiplied alpha retained; original samples and 120Hz clips retained',
                               'source_pack_sha256':hashlib.sha256(args.oldpack.read_bytes()).hexdigest(),
                               'updated':['tiptoe','thinking','run-right','run-left']}
        metadata['preserved']=[name for name in metadata['clips'] if name not in names]
        new.writestr('animation-info.json',json.dumps(metadata,ensure_ascii=False,indent=2)+'\n')
    metadata['sha256']=hashlib.sha256(args.output.read_bytes()).hexdigest()
    args.output.with_suffix('.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--oldpack', type=Path, required=True)
    parser.add_argument('--poses', type=Path, help='重制原有过渡时的对齐原画目录；仅替换摇头时不需要')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--preview', type=Path, required=True)
    parser.add_argument('--rate', type=int, default=120)
    parser.add_argument('--running-frames', type=Path, help='已经审阅的完整人物奔跑画面目录；默认保留当前奔跑')
    parser.add_argument('--running-only', action='store_true', help='仅重制左右奔跑，原样保留其余动作')
    parser.add_argument('--shake-frames', type=Path, help='仅替换生产导出器生成的完整摇头画面')
    parser.add_argument('--smooth-existing', action='store_true', help='加密现有60Hz动作，120Hz画面和所有原采样保持不变')
    args = parser.parse_args()
    if args.oldpack.resolve() == args.output.resolve():
        parser.error('输出请使用新路径；全部完成后再替换正式帧包。')
    if not 60 <= args.rate <= 144:
        parser.error('制作频率需在60到144之间。')
    if args.smooth_existing:
        if args.shake_frames or args.running_only or args.running_frames:
            parser.error('现有动作加密不能与重制姿势同时使用')
        smooth_existing(args)
        return
    if args.shake_frames:
        if args.running_only or args.running_frames:
            parser.error('摇头替换不能同时重制奔跑')
        replace_shake(args)
        return
    if args.poses is None:
        parser.error('重制原有过渡需要 --poses 指定对齐原画目录')
    global cv2
    import cv2
    cv2.setNumThreads(4)
    neutral = load_pose(args.poses / 'neutral-00.png')
    sources = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in args.poses.glob('*.png')}
    metadata = {'format': 2, 'algorithm': 'face and leg alignment / bidirectional DIS / signed-distance silhouettes; existing action samples retained',
                'opencv': cv2.__version__, 'source': 'https://github.com/opencv/opencv',
                'original_pack_sha256': hashlib.sha256(args.oldpack.read_bytes()).hexdigest(),
                'poses_sha256': sources, 'preserved': ['tiptoe', 'thinking'], 'clips': {}}
    written = set()
    previews = {}
    args.preview.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.oldpack) as old, zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as new:
        def emit(image):
            data = image.tobytes()
            key = hashlib.sha256(data).hexdigest()
            if key not in written:
                new.writestr('frames/' + key + '.bgra', data)
                written.add(key)
            return key

        def timeline(name, keys, rate):
            new.writestr('timelines/' + name + '.txt', '\n'.join(keys) + '\n')
            new.writestr('timelines/' + name + '.fps', f'{rate:.12f}\n')
            metadata['clips'][name] = {'frames': len(keys), 'unique': len(set(keys)), 'rate': rate}

        preserved = metadata['preserved'] if not args.running_only else [Path(n).stem for n in old.namelist()
                    if n.startswith('timelines/') and n.endswith('.txt') and Path(n).stem not in ('run-left','run-right')]
        for name in preserved:
            keys = old.read('timelines/' + name + '.txt').decode().splitlines()
            for key in dict.fromkeys(keys):
                if key not in written:
                    new.writestr('frames/' + key + '.bgra', old.read('frames/' + key + '.bgra'))
                    written.add(key)
            rate = float(old.read('timelines/'+name+'.fps')) if 'timelines/'+name+'.fps' in old.namelist() else 60
            timeline(name, keys, rate)

        # 默认保留帧包中已有奔跑画面；只在明确提供已审阅素材时替换。
        if args.running_frames:
            paths = sorted(args.running_frames.glob('*.png'))
            if not paths: raise ValueError('指定的奔跑画面目录为空')
            frames = [load_pose(path) for path in paths]
            rate = len(frames)/PERIOD
            metadata['running'] = {'method':'explicitly supplied whole-character frames','period':PERIOD}
        else:
            running_keys = old.read('timelines/run-right.txt').decode().splitlines()
            frames = [np.frombuffer(old.read('frames/'+key+'.bgra'),np.uint8).reshape(HEIGHT,WIDTH,4).copy() for key in running_keys]
            rate = float(old.read('timelines/run-right.fps')) if 'timelines/run-right.fps' in old.namelist() else 60
            existing_info = json.loads(old.read('animation-info.json')) if 'animation-info.json' in old.namelist() else {}
            metadata['running'] = existing_info.get('running',{'method':'existing whole-character running frames preserved'})
        keys = [emit(image) for image in frames]
        timeline('run-right',keys,rate)
        previews['run-right'] = frames[::max(1,round(rate/25))]
        left = []
        for key in keys:
            a = np.frombuffer(new.read('frames/' + key + '.bgra'), np.uint8).reshape(HEIGHT, WIDTH, 4)
            left.append(emit(a[:, ::-1].copy()))
        timeline('run-left', left, rate)
        previews['run-left'] = [a[:, ::-1].copy() for a in previews['run-right']]
        if not args.running_only:
            # 已确认的完整画面不重做姿势；只在其相邻采样之间增加画面。
            # 新帧发生错误时不会污染原来的动作关键帧。
            for name,duration in [('jump',.95),('failed',4.10),('waiting',4.84),('review',5.06),('shake',2.60)]:
                old_keys=old.read('timelines/'+name+'.txt').decode().splitlines()
                old_rate=float(old.read('timelines/'+name+'.fps')) if 'timelines/'+name+'.fps' in old.namelist() else 60
                keys,samples=[],[]
                previous_pair=None;transition=None
                for i in range(int(np.ceil(duration*args.rate))+1):
                    position=min(len(old_keys)-1,i*old_rate/args.rate)
                    a=int(position);b=min(len(old_keys)-1,a+1);amount=position-a
                    if previous_pair!=(a,b):
                        previous_pair=(a,b)
                        images=[np.frombuffer(old.read('frames/'+old_keys[j]+'.bgra'),np.uint8).reshape(HEIGHT,WIDTH,4).copy() for j in (a,b)]
                        transition=None
                    if amount and old_keys[a]!=old_keys[b]:
                        if transition is None:
                            clean=[image.copy() for image in images]
                            for image in clean: image[image[:,:,3]<24]=0
                            transition=Transition(*clean)
                        image=transition.render(amount)
                    else: image=images[0]
                    keys.append(emit(image))
                    if i%5==0:samples.append(image.copy())
                timeline(name,keys,args.rate)
                previews[name]=samples
                print(f'已加密 {name}: {len(keys)} 帧，原有采样画面保留',flush=True)
        new.writestr('animation-info.json', json.dumps(metadata, ensure_ascii=False, indent=2)+'\n')
    for name, samples in previews.items():
        images=[]
        for data in samples:
            canvas = np.full((HEIGHT,WIDTH,3), (232,225,234), dtype=np.float32)
            rgb = data[:,:,[2,1,0]].astype(np.float32) + canvas*(1-data[:,:,3:]/255)
            images.append(Image.fromarray(np.rint(rgb).clip(0,255).astype(np.uint8)).resize((288,312),Image.Resampling.LANCZOS))
        images[0].save(args.preview/(name+'.gif'),save_all=True,append_images=images[1:],duration=40,loop=0)
        sheet=Image.new('RGB',(8*192,3*228),(232,225,234)); draw=ImageDraw.Draw(sheet)
        indices=np.linspace(0,len(images)-1,24).round().astype(int)
        for k,index in enumerate(indices):
            x=k%8*192;y=k//8*228
            sheet.paste(images[index].resize((192,208)),(x,y));draw.text((x+5,y+210),f'{name} {index}',fill='black')
        sheet.save(args.preview/(name+'.png'))
    metadata['sha256'] = hashlib.sha256(args.output.read_bytes()).hexdigest()
    args.output.with_suffix('.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('动作帧包已制作完成；踮脚与思考保留原文件。',flush=True)


if __name__ == '__main__':
    main()
