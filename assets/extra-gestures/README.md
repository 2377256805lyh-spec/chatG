# 肢体原画与历史制作资料

- `raw/`：ImageGen 生成的 3 张关键姿势图和 1 张双臂遮挡底图，保留透明通道。
- `aligned/`：按原人物特征对齐的原画，仅用于提取肢体；未用这些图替换原来的脸和身体。
- `regions.json`：每个姿势的肢体轮廓、肩肘腕指端位置、动作时间表。
- `layers/`：独立的手臂前景和补出旧手臂遮挡区域后的身体底图。
- `alignment.json`：原画对齐的位置资料。
- `generation-prompts.md`：内置 ImageGen 的完整提示词。

以下六姿势蒙皮流程是历史制作方式，不是当前播放流程。当前生产请在仓库根目录运行 `python scripts/make-painted-gestures.py`，时间表为 `painted/sequence.json`，仅包含困困打盹与灵光一闪。害羞晃晃已移除，其原画保留。

历史动作由 6 个关键姿势构成。抬手和放手均有独立阶段；上臂、前臂沿关节转动，手掌尽量整体转动，袖子在相邻重画姿势之间改变褶皱。生成器仅处理手臂所在的画面区域，固定身体底图不会被肢体的位置对应拖动。

以下命令仅用于复现历史制作步骤，需要 Python、numpy、Pillow、opencv-python-headless，桌面版播放不需要安装这些依赖。在仓库根目录运行；`frames` 会覆盖当前帧包，不能用于发布当前版本：

```powershell
python scripts/make-extra-gestures.py --stage align
python scripts/make-extra-gestures.py --stage layers
python scripts/make-extra-gestures.py --stage preview
```

`preview` 阶段生成相邻姿势的审核图，审核后才制作完整帧包。`frames` 生成 `extra-gestures.zip`、校验清单以及 `artwork/extra-*-body.png`，原动作帧包保持不动。后续由 `build.ps1` 校验并编译。
