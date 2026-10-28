# 素材来源与使用说明

小G是个人学习用途的非官方白发龙娘桌面形象，与 OpenAI 等品牌方没有官方关联。

## 角色原始素材

- 作者与原项目：[fywdy / chatgpt-chibi-pets](https://github.com/fywdy/chatgpt-chibi-pets)。
- 角色目录：[chatgpt-white-dragon-chibi-v5](https://github.com/fywdy/chatgpt-chibi-pets/tree/main/chatgpt-white-dragon-chibi-v5)。
- 本项目沿用该角色参考形象和原始图集，并对透明背景、高清动作、转头、眨眼、踮脚、思考、沮丧和动画补帧进行了修改或重新生成。
- 原项目美术素材采用 [CC BY-NC 4.0（署名—非商业性使用）](https://creativecommons.org/licenses/by-nc/4.0/)，原许可与声明保存在 [licenses/fywdy](licenses/fywdy/)。原素材及其改编素材需保留原署名和使用限制。

## 目录与文件

- `assets/xiaog-atlas.png`：角色图集和原始位置标尺。
- `assets/artwork/`：当前程序使用的高清动作原画。
- `assets/animation-frames.zip`：预制动画帧包，使用 Git LFS 管理；SHA-256 为 `d7b9e4debb85e0dde9a6b3e0ca781bd153c056edcfb9ba5e654d2134e37dbc3a`，各动作信息见 `assets/animation-frames.json`。
- `assets/extra-gestures/painted/`：新动作的逐格原画、肢体层、定位记录与完整 ImageGen 提示词；`sequence.json` 记录曝光时间。`shy/` 为已移除动作的历史素材，不嵌入程序。
- `assets/extra-gestures.zip`：困困打盹与灵光一闪的透明肢体帧包；身体底图和来源文件校验值见 `assets/extra-gestures.json`。
- `assets/gaze/`、`assets/gaze-flow.bin` 及清单：转头原画、定位点与位移数据。
- `assets/thinking/`：思考分层的输入原画、遮挡底图和手臂轮廓。
- `assets/xiaog.ico`：程序图标。
- `docs/previews/`：动作与鼠标转头的动态预览。
- `assets/voice/00.wav` 至 `assets/voice/11.wav`：12 段预先合成的角色配音。
- `assets/voice/reference.wav`、`assets/voice/voice-design.json`：合成声线样本及设计描述。
- `assets/voice/catalog.json`：配音文本、模型来源、时长和校验信息。

## 配音

唯一正式声线是确认后的“清澈软萌（去沙版）”。最初通过 [Qwen3-TTS VoiceDesign](https://github.com/QwenLM/Qwen3-TTS) 生成声音样本，再使用 [noisereduce](https://github.com/timsainb/noisereduce) 进行轻度去沙，保护声音主体。确认后的样本保存在 `assets/voice/reference.wav`，处理参数与来源记录在 `voice-design.json`。

12 句语录用同一声音样本和 Qwen3-TTS 0.6B Base 生成，保留自然音调与样本节奏，只统一响度和首尾；不叠加其他人声或后期变调。具体模型版本、文本顺序、时长与文件校验值以 `catalog.json` 为准。模型和制作环境无需随程序安装，运行时只播放预制音频。

## 转头与思考制作

`assets/gaze/` 保留 17 张转头与待机原画、定位点，`gaze-flow.bin` 是预先计算的位移数据。制作时使用 [OpenCV](https://github.com/opencv/opencv) 的位置对应方法，运行时直接读取结果。

`assets/thinking/` 保留思考原画、手臂轮廓和补制的遮挡底图；`artwork/thinking-layers.json` 保存输入及分层图校验值。借鉴 [AnimeInterp](https://github.com/lisiyao21/AnimeInterp) 按区域处理位置对应的思路，未使用其代码或模型。生成提示词随原画保留。

困困打盹与灵光一闪使用内置 ImageGen 制作逐格肢体原画，提取手臂、手和袖子，身体与脸固定。随后只对独立肢体层增加连续过渡，保留揉眼与举指停留的时间表，装饰由程序绘制。最初六姿势蒙皮流程仅作为历史制作资料保留，未用于当前两种动作的肢体补帧。

## 交互实现参考

- [PixelPaws 的 PetEngine](https://github.com/jordansbc/pixelpaws/blob/main/src/DesktopPet/Engine/PetEngine.cs)：参考其集中处理状态进入、动作结束与拖动收尾的组织方式，小G将回到待机时的注视、眨眼、装饰和肢体图层复位合并处理。
- [大肥鱼桌宠](https://github.com/1190fasheqi/dafeiyu-pet/blob/main/%E6%A1%8C%E5%AE%A0.py)：参考其人物菜单和托盘复用设置构建入口的做法，小G的托盘增加通往现有完整菜单的入口。

以上为组织方式与交互思路参考，由本项目在现有 WPF 实现中独立完成。气泡上下避让、指向翻转和长句分行使用本项目现有花饰样式。

全动作播放时序参考了 [WPF 的逐帧绘制说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget) 与 [RenderingEventArgs 的呈现时间定义](https://source.dot.net/PresentationCore/System/Windows/Media/RenderingEventArgs.cs.html)，并对照 [PixelPaws 的 SpriteAnimator](https://github.com/jordansbc/pixelpaws/blob/main/src/DesktopPet/Engine/SpriteAnimator.cs) 的按时间推进方式。小G用实际呈现时间推进动作，重复回调只处理一次；未复制这些项目的代码。相邻原帧的加密采用 OpenCV 的小位移匹配，限制位移并保留原透明像素，不将增加帧数等同于重画动作。

2026-10-28 的运行逻辑更新进一步参考 [VPet 的 PNGAnimation 后台取图与播放控制检查](https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Core/Graph/PNGAnimation.cs#L250-L289)：将计算与解压移出界面回调，转头只保留最新待执行请求，切换动作后拒绝旧会话的结果；参考 [VPet 的动作收尾衔接](https://github.com/LorisYounger/VPet/blob/main/VPet-Simulator.Core/Display/MainDisplay.cs#L88-L107)，让跳跃落地后再接最近一次动作选择。该做法在小G现有结构中独立实现，没有引入 VPet 框架、素材或逐帧睡眠循环。线程间图像使用 [WPF 官方说明的冻结对象共享机制](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/freezable-objects-overview)。

本次未增加动画采样或修改人物原画。拖动步频、停顿后继续步态、专注计时及计时通知由本项目现有交互与声音资源实现。奔跑交叉步和等待、检查的旧轮廓重叠仍属于原画问题。

## 获取完整素材

安装 Git LFS 后，在仓库目录运行 `git lfs pull` 可下载动画帧包的实际内容。
