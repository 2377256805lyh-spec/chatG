"""把审核后的实画肢体原画打包为定时曝光帧；不计算肢体变形或混合画面。"""
import hashlib
import json
from pathlib import Path
import zipfile
import numpy as np
from PIL import Image

PROJECT_ROOT=Path(__file__).resolve().parents[1]
ROOT=PROJECT_ROOT/'assets'
ASSETS=ROOT/'extra-gestures/painted'
W,H=576,624


def main():
    plan=json.loads((ASSETS/'sequence.json').read_text(encoding='utf-8'))
    metadata={'rate':60,'size':[W,H],
              'method':'individually painted limb cels; timed held exposures; no joint interpolation',
              'clips':{},'bodies_sha256':{},'sources_sha256':{}}
    seen=set()
    output=ROOT/'extra-gestures.zip'
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as archive:
        for name,clip in plan.items():
            timeline=[]; sources=[]
            for exposure in clip['exposures']:
                path=ASSETS/name/'cels'/exposure['file']
                rgba=np.array(Image.open(path).convert('RGBA'))
                if rgba.shape!=(H,W,4): raise ValueError('实画帧尺寸不一致：'+str(path))
                pixels=rgba.astype(np.uint16)
                pixels[:,:,:3]=(pixels[:,:,:3]*pixels[:,:,3:]+127)//255
                data=pixels.astype(np.uint8)[:,:,[2,1,0,3]].tobytes()
                key=hashlib.sha256(data).hexdigest()[:24]
                if key not in seen:
                    archive.writestr('frames/'+key+'.bgra',data);seen.add(key)
                ticks=exposure['ticks']
                if not isinstance(ticks,int) or ticks<1: raise ValueError('曝光时间必须为正整数')
                timeline.extend([key]*ticks)
                sources.append(path)
            if len(timeline)!=round(clip['duration']*60): raise ValueError('动作时长与曝光表不一致：'+name)
            archive.writestr('timelines/'+name+'.txt','\n'.join(timeline)+'\n')
            archive.writestr('timelines/'+name+'.fps','60')
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
    metadata['sha256']=hashlib.sha256(output.read_bytes()).hexdigest()
    for path in [Path(__file__),ASSETS/'sequence.json',ROOT/'thinking/pose-00.png']:
        metadata['sources_sha256'][path.relative_to(PROJECT_ROOT).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
    (ROOT/'extra-gestures.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')


if __name__=='__main__': main()
