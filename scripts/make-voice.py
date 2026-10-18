"""制作小G的本地角色配音；桌面程序只播放提前生成的WAV。

依赖：qwen-tts==0.1.1、transformers==4.57.3、accelerate==1.12.0，
以及兼容显卡的torch/torchaudio、numpy、soundfile、librosa等模型依赖。
用法：python make-voice.py --models <Qwen3-TTS-12Hz-0.6B-Base目录>
可用--output指定制作目录；12句全部完成后再替换正式素材并构建。
--reference-dir指定已确认的声线目录；该目录须包含reference.wav和voice-design.json。
声线采用voice/reference.wav与voice/voice-design.json中记录的合成角色样本。
加上--design-models <1.7B-VoiceDesign目录>可先按保存的描述重新制作声线样本。
配音默认用FFmpeg保持音调放慢到93%；--ffmpeg指定工具，--tempo指定速度比例。
"""

import argparse
import gc
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import wave

import numpy as np

def adjust_tempo(file, ratio, executable):
    """保持音调调整节奏，输出播放组件要求的标准44字节PCM头。"""
    if not 0.5 <= ratio <= 2:
        raise ValueError("配音速度比例须在0.5至2之间。")
    if ratio == 1:
        with wave.open(str(file), "rb") as reader:
            return round(reader.getnframes() / reader.getframerate(), 4)
    result = subprocess.run(
        [str(executable), "-nostdin", "-hide_banner", "-loglevel", "error", "-i", str(file),
         "-af", f"atempo={ratio:.9f}", "-ac", "1", "-ar", "24000", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
        creationflags=0x08000000 if os.name == "nt" else 0,
    )
    pcm = result.stdout
    if len(pcm) % 2 or not 48000 <= len(pcm) <= 48000 * 30:
        raise ValueError("变速后配音的长度或采样格式不正确：" + file.name)
    with wave.open(str(file), "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(24000)
        writer.writeframes(pcm)
    return round(len(pcm) / 48000, 4)


def save_pcm(file, samples, rate):
    """统一素材格式和响度，保留模型原本的音调与语速。"""
    samples = np.asarray(samples, dtype=np.float32).reshape(-1)
    if rate != 24000 or not np.isfinite(samples).all() or not rate <= len(samples) <= rate * 15:
        raise ValueError("配音长度、采样率或采样值不正确：" + file.name)
    # 用短时间内的声音能量识别首尾，保留轻声起音及自然的句尾。
    window = max(1, int(rate * 0.01))
    energy = np.sqrt(np.convolve(samples * samples, np.ones(window) / window, mode="same"))
    audible = np.flatnonzero(energy > 0.006)
    if not len(audible):
        raise ValueError("配音没有有效声音：" + file.name)
    samples = samples[max(0, audible[0] - int(rate * 0.08)):min(len(samples), audible[-1] + int(rate * 0.17))].copy()
    rms = float(np.sqrt(np.mean(samples * samples)))
    peak = float(np.max(np.abs(samples)))
    samples *= min(0.14 / max(rms, 0.001), 0.82 / max(peak, 0.001))
    fade = min(int(rate * 0.012), len(samples) // 4)
    samples[:fade] *= np.linspace(0, 1, fade)
    samples[-fade:] *= np.linspace(1, 0, fade)
    samples = np.concatenate((np.zeros(int(rate * 0.04)), samples, np.zeros(int(rate * 0.15))))
    pcm = np.clip(np.rint(samples * 32767), -32768, 32767).astype("<i2")
    with wave.open(str(file), "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(rate)
        writer.writeframes(pcm.tobytes())
    return round(len(pcm) / rate, 4)


def main():
    script_folder = Path(__file__).resolve().parent
    root = script_folder.parent if (script_folder.parent / "src" / "Main.cs").exists() else script_folder
    voice = root / "assets" / "voice" if (root / "assets").exists() else root / "voice"
    main_file = root / "src" / "Main.cs" if (root / "src").exists() else root / "Main.cs"
    parser = argparse.ArgumentParser(description="沿用小G合成角色声线，制作12句本地配音")
    parser.add_argument("--models", type=Path, required=True)
    parser.add_argument("--design-models", type=Path)
    parser.add_argument("--output", type=Path, default=voice)
    parser.add_argument("--reference-dir", type=Path, default=voice)
    parser.add_argument("--device", default="cuda:0")
    parser.add_argument("--tempo", type=float, default=0.93)
    parser.add_argument("--ffmpeg", type=Path)
    args = parser.parse_args()
    if not 0.5 <= args.tempo <= 2:
        parser.error("配音速度比例须在0.5至2之间。")
    ffmpeg = args.ffmpeg or shutil.which("ffmpeg")
    if args.tempo != 1 and ffmpeg is None:
        parser.error("放慢配音需要FFmpeg，请用--ffmpeg指定工具路径；--tempo 1可保留原速。")
    import torch
    from qwen_tts import Qwen3TTSModel
    if args.device == "cpu":
        torch.set_num_threads(min(8, os.cpu_count() or 1))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    voice = args.reference_dir.resolve()
    design = json.loads((voice / "voice-design.json").read_text(encoding="utf-8"))
    reference = voice / design["file"]
    if args.design_models is not None:
        design_model = Qwen3TTSModel.from_pretrained(
            str(args.design_models.resolve()), device_map=args.device,
            dtype=torch.bfloat16 if args.device.startswith("cuda") else torch.float32,
            attn_implementation="sdpa",
        )
        if design.get("seed") is not None:
            torch.manual_seed(design["seed"])
        wavs, rate = design_model.generate_voice_design(
            text=design["text"], language="Chinese", instruct=design["instruct"], max_new_tokens=512,
        )
        design["seconds"] = save_pcm(reference, wavs[0], rate)
        design["sha256"] = hashlib.sha256(reference.read_bytes()).hexdigest()
        revision_file = args.design_models / "source-revision.txt"
        design["model_revision"] = revision_file.read_text(encoding="utf-8").strip()
        design["created_with"] = "本机离线声音设计"
        design["origin"] = "依据保留的角色描述在本机重新生成的声线。"
        for field in ("voice", "reference_processing", "source_reference_sha256", "service_source", "service_code_revision"):
            design.pop(field, None)
        (voice / "voice-design.json").write_text(json.dumps(design, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"角色声线样本 {design['seconds']:.2f}s 已生成", flush=True)
        del design_model, wavs
        gc.collect()
        if args.device.startswith("cuda"):
            torch.cuda.empty_cache()
    if hashlib.sha256(reference.read_bytes()).hexdigest() != design["sha256"]:
        raise ValueError("角色声音样本与记录不一致。")
    block = re.search(r'string\[\] phrases\s*=\s*\{([\s\S]*?)\};', main_file.read_text(encoding="utf-8-sig"))
    if block is None:
        raise ValueError("没有找到小G语录。")
    phrases = re.findall(r'"([^"\n]*)"', block.group(1))
    if not phrases:
        raise ValueError("小G语录为空。")
    model = Qwen3TTSModel.from_pretrained(
        str(args.models.resolve()), device_map=args.device,
        dtype=torch.bfloat16 if args.device.startswith("cuda") else torch.float32,
        attn_implementation="sdpa",
    )
    prompt = model.create_voice_clone_prompt(
        ref_audio=str(reference), ref_text=design["text"], x_vector_only_mode=False,
    )
    clips = []
    for index, text in enumerate(phrases):
        # 固定每句的生成起点，方便日后重新制作同一套声音。
        torch.manual_seed(8407 + index)
        wavs, rate = model.generate_voice_clone(
            text=text, language="Chinese", voice_clone_prompt=prompt, max_new_tokens=256,
        )
        file = output / f"{index:02d}.wav"
        seconds = save_pcm(file, wavs[0], rate)
        source_seconds = seconds
        source_sha256 = hashlib.sha256(file.read_bytes()).hexdigest()
        seconds = adjust_tempo(file, args.tempo, ffmpeg)
        clips.append({"file": file.name, "text": text, "seconds": seconds,
                      "source_seconds": source_seconds, "source_sha256": source_sha256,
                      "sha256": hashlib.sha256(file.read_bytes()).hexdigest()})
        print(f"{file.name} {seconds:.2f}s {text}", flush=True)
    revision_file = args.models / "source-revision.txt"
    catalog = {
        "model": "Qwen3-TTS-12Hz-0.6B-Base", "model_revision": revision_file.read_text(encoding="utf-8").strip() if revision_file.exists() else None,
        "voice": design.get("voice", "小G·清甜自然的软萌少女声线"), "reference": design,
        "source": "https://github.com/QwenLM/Qwen3-TTS",
        "model_source": "https://huggingface.co/Qwen/Qwen3-TTS-12Hz-0.6B-Base",
        "pitch_ratio": 1.0, "tempo_ratio": args.tempo, "tempo_processing": "FFmpeg atempo" if args.tempo != 1 else None,
        "seed_base": 8407,
        "format": "PCM WAV, mono, 24000 Hz, 16 bit", "clips": clips,
    }
    (output / "catalog.json").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
