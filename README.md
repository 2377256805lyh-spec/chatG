# 小G

一个陪你学习的白发龙娘 Q 版 Windows 桌面挂件，支持动作互动、鼠标注视、花饰短句气泡和本地角色配音。

## 互动方式

| 操作 | 反应 |
|---|---|
| 单击人物 | 约 280 毫秒内没有第二次点击时起跳 |
| 双击人物 | 依次切换踮脚、思考、摇头、沮丧、等待、检查、困困打盹、灵光一闪 |
| 按住左右拖动 | 朝拖动方向奔跑，松手停止 |
| 鼠标靠近头部 | 轻轻转头注视；离开时平顺回正 |
| 累计互动 5～7 次 | 随机显示一句短句，并播放对应配音 |
| 右键人物或通知区域图标 | 调整大小、置顶、语音、音量、启动联动和动作 |

单击和双击各算一次互动，拖动不计数。气泡里的文字和配音对应，当前语录不会被连续点击打断。音量提供 25%、55%、80% 三档，只影响小G。

现用配音采用确认后的清澈软萌声线，仅保留这一款。所有配音提前生成并嵌入程序；日常显示、互动、语录和声音播放均在本机完成，不发送聊天请求、不消耗模型额度。

动作使用高清原画和预制过渡画面，转头另有细节位移数据；思考将手臂与身体分层，摇头保留颈部和衣领。程序按动作素材的采样频率取图，最高每秒更新 120 次，两种新动作每秒更新 60 次；实际流畅度受原画数量、屏幕刷新率和系统负载影响。

困困打盹与灵光一闪使用逐格实画的手臂和袖子，固定脸、脖子与身体。打盹抬手稍慢，揉眼轻揉两次；灵光一闪举指冒星光时多停留约 0.3 秒。害羞晃晃已移除，原画只作为历史制作资料保留。奔跑沿用原有整个人物姿势，交叉步观感仍待后续改进。

## 下载与编译

使用 64 位 Windows，需要 Git、Git LFS（Git Large File Storage，Git 大文件存储），以及 Windows 的 .NET Framework C# 编译器与 WPF（Windows Presentation Foundation，Windows 界面绘制框架）。已有素材可直接编译，无需语音模型或 Python。

```powershell
git clone https://github.com/2377256805lyh-spec/chatG.git
Set-Location chatG
git lfs install --local
git lfs pull
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\小G.exe
```

构建会校验动作帧包、新动作身体底图、转头、思考分层和配音素材；配音还会核对文本、顺序和文件哈希。随后将素材嵌入根目录的 `小G.exe`。主动画帧包约 457 MB，使用 Git LFS 保存；若文件只有几行文字，请先运行 `git lfs pull`。

编译出的程序不提交到源码仓库。便携运行只需 `小G.exe`；更新时退出旧程序、替换可执行文件，并保留原来的 `settings.json`。

## 设置与启动

窗口位置、大小、置顶、语音开关、音量等保存在程序旁的 `settings.json`，该个人设置文件不提交到仓库。找不到人物时，可从通知区域图标选择找回，或双击图标将人物移回主屏幕。

在右键菜单开启“随桌面 Codex 启动”后，程序在当前用户的 Windows 启动文件夹创建快捷方式，隐藏等待进程检测到桌面 Codex 窗口后打开小G。手动退出小G后，不会在同一次 Codex 打开期间反复拉起。可在同一菜单关闭此功能。

## 项目结构

| 路径 | 内容 |
|---|---|
| `src/` | 12 个 C# 源文件与应用清单 |
| `assets/artwork/` | 程序使用的高清动作图与思考分层图 |
| `assets/gaze/` | 已对齐的转头原画和定位点 |
| `assets/thinking/` | 思考原画、遮挡底图与手臂轮廓 |
| `assets/voice/` | 唯一正式声线、12 段配音及制作记录 |
| `assets/animation-frames.zip` | 预制动画帧包，使用 Git LFS |
| `assets/extra-gestures/` | 新动作的原画、透明肢体层、提示词与曝光时间表 |
| `assets/extra-gestures.zip` | 困困打盹与灵光一闪的透明肢体帧包 |
| `scripts/` | 可选的配音、转头、动作和分层制作入口 |
| `docs/` | 素材来源、第三方许可和动态预览 |
| `build.ps1` | Windows 构建入口 |

## 可选的素材重制

普通编译和日常运行不需要这些制作环境。

- `scripts/make-voice.py`：使用保留的 `assets/voice/reference.wav` 和声音设计记录制作全部语录。需要 Qwen3-TTS 与兼容模型环境，具体依赖见脚本开头。现用声线以确认样本的自然节奏为准，重制时使用 `--tempo 1`；调整节奏才需要 FFmpeg。
- `scripts/make-gaze.py`：从 `assets/gaze/` 生成转头位移文件和校验清单。
- `scripts/make-thinking-layers.py`：从 `assets/thinking/` 生成手臂和身体遮挡底图。
- `scripts/make-painted-gestures.py`：按 `assets/extra-gestures/painted/sequence.json` 打包两种新动作，需要 NumPy、Pillow；已有原画不再计算肢体变形。
- `scripts/export-extra-actions.ps1`：从编译好的程序导出与实际绘制一致的完整 PNG 动作序列；用 `-AssemblyPath .\小G.exe -Output <输出目录>` 指定输入输出。

转头与思考分层制作需要 NumPy、Pillow 与 OpenCV。`make-extra-gestures.py` 是历史六姿势制作入口，其 `frames` 阶段不能覆盖当前实画帧包。声音制作和素材重制完成后需重新构建程序。

## 动态预览

[动作预览](docs/previews/小G-动作动态预览.gif) · [转头预览](docs/previews/小G-转头动态预览.gif) · [颈部与思考分层修复](docs/previews/小G-颈部与思考修复预览.gif)

[困困打盹](docs/previews/新增动作/困困打盹.webp) · [灵光一闪](docs/previews/新增动作/灵光一闪.webp) · [新动作制作说明](docs/previews/新增动作/制作说明.md)

## 素材来源

角色参考来自 [fywdy/chatgpt-chibi-pets](https://github.com/fywdy/chatgpt-chibi-pets)。本项目是非官方同人创作，与 OpenAI 等品牌方没有官方关联。原美术素材及改编需遵守署名与非商业使用条件，详见 [素材说明](docs/ASSET-SOURCES.md) 和 [原项目许可](docs/licenses/fywdy/)。

声音是为该角色制作的合成声线，不声称复刻现实人物。声音样本来源、去沙处理、生成模型与各句校验值保存在 `assets/voice/` 中。
