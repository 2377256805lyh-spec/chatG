# 小G实画动作素材

使用内置 image_gen 生成原画，固定原脸与身体，只提取手臂、手和袖子。

- 灵光一闪：idea/generation-prompts.md
- 历史素材（已移除的害羞晃晃）：shy/generation-prompt.md 和 shy/additional-prompts.md
- 困困打盹：sleepy/generation-prompts.md

生产打包：在仓库根目录运行 `python scripts/make-painted-gestures.py`；曝光时序：`sequence.json`。
绘图原片、人物对齐、手工边界和透明肢体层保存在各动作子目录。
运动阶段按各姿势分别设置曝光；过渡画面不连续长停，完整表意姿势及中性收尾才停留。揉眼往返复用已绘制原画，不生成关节蒙皮补帧。

`idea/prepare.py` 与 `shy/extract-cels.py` 可重导切图；sleepy 的来源、边界和整层定位在metadata.json。
旧的 make-extra-gestures.py 用于最初六张原画的定位与分层，不能用其frames阶段覆盖当前实画帧包。

当前运行与导出只包含sleepy/idea；shy目录仅保留原画与制作记录，不嵌入程序。
