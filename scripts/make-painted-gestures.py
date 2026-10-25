"""把实画肢体原画制成连续过渡；只处理透明手臂前景，不改变人物底图。"""
import hashlib
import json
from pathlib import Path
import zipfile
import numpy as np
from PIL import Image
import cv2

PROJECT_ROOT=Path(__file__).resolve().parents[1]
ROOT=PROJECT_ROOT/'assets'
ASSETS=ROOT/'extra-gestures/painted'
W,H=576,624
RATE=120


class LimbTransition:
    def __init__(self, first, second):
        self.first,self.second=first,second
        self.same=np.array_equal(first,second)
        if self.same:return
        self.flows=[];self.distances=[];views=[]
        for image in (first,second):
            visible=image[:,:,:3].astype(np.float32)+112*(1-image[:,:,3:]/255)
            gray=cv2.cvtColor(np.clip(visible,0,255).astype(np.uint8),cv2.COLOR_BGR2GRAY)
            views.append(cv2.resize(gray,(W//2,H//2),interpolation=cv2.INTER_AREA))
            mask=(image[:,:,3]>127).astype(np.uint8)
            self.distances.append(cv2.distanceTransform(mask,cv2.DIST_L2,cv2.DIST_MASK_PRECISE)
                                  -cv2.distanceTransform(1-mask,cv2.DIST_L2,cv2.DIST_MASK_PRECISE))
        for source,target in (views,views[::-1]):
            engine=cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
            engine.setFinestScale(0);engine.setPatchSize(8);engine.setPatchStride(3)
            engine.setVariationalRefinementIterations(25)
            flow=engine.calc(source,target,None)
            flow=cv2.resize(cv2.GaussianBlur(flow,(0,0),.65),(W,H),interpolation=cv2.INTER_LINEAR)*2
            if not np.isfinite(flow).all() or np.abs(flow).max()>W/4:
                raise ValueError('相邻肢体原画位移过大，请补画关键姿势')
            self.flows.append(flow)
        yy,xx=np.mgrid[:H,:W].astype(np.float32)
        self.grid=np.stack((xx,yy),axis=2)

    def warp(self,image,flow,amount,border=0):
        position=self.grid-flow*amount
        for _ in range(6):
            sample=cv2.remap(flow,position[:,:,0],position[:,:,1],cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
            position=(position+self.grid-sample*amount)*.5
        return cv2.remap(image,position[:,:,0],position[:,:,1],cv2.INTER_LINEAR,
                         borderMode=cv2.BORDER_CONSTANT,borderValue=border).astype(np.float32)

    def render(self,t):
        if t<=0 or self.same:return self.first
        if t>=1:return self.second
        a=self.warp(self.first,self.flows[0],t);b=self.warp(self.second,self.flows[1],1-t)
        distance=self.warp(self.distances[0],self.flows[0],t,-64)*(1-t)+self.warp(self.distances[1],self.flows[1],1-t,-64)*t
        alpha=np.clip(distance+.5,0,1)*255
        mixed=a*(1-t)+b*t
        result=np.empty_like(mixed)
        result[:,:,:3]=mixed[:,:,:3]/np.maximum(mixed[:,:,3:],1)*alpha[:,:,None]
        result[:,:,3]=alpha;result[:,:,:3]=np.minimum(result[:,:,:3],result[:,:,3:])
        return np.rint(result).clip(0,255).astype(np.uint8)


def main():
    plan=json.loads((ASSETS/'sequence.json').read_text(encoding='utf-8'))
    cv2.setNumThreads(4)
    metadata={'rate':RATE,'size':[W,H],
              'method':'painted limb-only cels; bidirectional small-step correspondence; fixed body; expressive holds retained',
              'clips':{},'bodies_sha256':{},'sources_sha256':{}}
    seen=set()
    output=ROOT/'extra-gestures.zip'
    temporary=output.with_suffix('.building.zip')
    with zipfile.ZipFile(temporary,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as archive:
        for name,clip in plan.items():
            timeline=[]; sources=[];images=[]
            for exposure in clip['exposures']:
                path=ASSETS/name/'cels'/exposure['file']
                rgba=np.array(Image.open(path).convert('RGBA'))
                if rgba.shape!=(H,W,4): raise ValueError('实画帧尺寸不一致：'+str(path))
                pixels=rgba.astype(np.uint16)
                pixels[:,:,:3]=(pixels[:,:,:3]*pixels[:,:,3:]+127)//255
                images.append(pixels.astype(np.uint8)[:,:,[2,1,0,3]])
                ticks=exposure['ticks']
                if not isinstance(ticks,int) or ticks<1: raise ValueError('曝光时间必须为正整数')
                sources.append(path)
            for index,exposure in enumerate(clip['exposures']):
                ticks=exposure['ticks']*RATE//60
                transition=LimbTransition(images[index],images[min(index+1,len(images)-1)])
                # 指尖亮起和中性收尾仍停留；揉眼在原来的慢节奏中连续经过小姿势。
                moving=min(ticks,12) if exposure['ticks']>=24 else ticks
                for tick in range(ticks):
                    t=max(0,(tick-(ticks-moving))/moving)
                    if index==0 or index==len(images)-2:t=t*t*(3-2*t)
                    data=transition.render(t).tobytes();key=hashlib.sha256(data).hexdigest()[:24]
                    if key not in seen:
                        archive.writestr('frames/'+key+'.bgra',data);seen.add(key)
                    timeline.append(key)
            if len(timeline)!=round(clip['duration']*RATE): raise ValueError('动作时长与曝光表不一致：'+name)
            archive.writestr('timelines/'+name+'.txt','\n'.join(timeline)+'\n')
            archive.writestr('timelines/'+name+'.fps',str(RATE)+'\n')
            motion_ticks=sorted({e['ticks'] for e in clip['exposures'][1:-1] if e['ticks']<=8 or e.get('phase')=='rub'})
            metadata['clips'][name]={'frames':len(timeline),'unique':len(set(timeline)),
                'painted_cels':len({p.name for p in sources}),'duration':clip['duration'],
                'motion_exposure_ticks':motion_ticks,
                'motion_cels_per_second_range':[round(60/max(motion_ticks),2),round(60/min(motion_ticks),2)]}
            body=ROOT/'artwork'/('extra-'+name+'-body.png')
            metadata['bodies_sha256'][body.name]=hashlib.sha256(body.read_bytes()).hexdigest()
            sources.extend([ASSETS/name/'metadata.json'])
            for path in sources:
                metadata['sources_sha256'][path.relative_to(PROJECT_ROOT).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
            print(name,metadata['clips'][name],flush=True)
    temporary.replace(output)
    metadata['sha256']=hashlib.sha256(output.read_bytes()).hexdigest()
    for path in [Path(__file__),ASSETS/'sequence.json',ROOT/'thinking/pose-00.png']:
        metadata['sources_sha256'][path.relative_to(PROJECT_ROOT).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
    (ROOT/'extra-gestures.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')


if __name__=='__main__': main()
