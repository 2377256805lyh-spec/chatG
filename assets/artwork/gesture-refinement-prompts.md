# 小G收手与思考眨眼修正

这是上一版修正记录。思考眨眼继续沿用本文方案；收手已改为沿身侧直接下降，当前素材与完整提示词见`wave-direct-return-prompts.md`。

本次使用内置 image_gen 工具，新增两张1536×1024、4列×2行、透明背景原画。

- 收手素材：`artwork/waving-return.png`。生成原图：`exec-0ac65e07-d506-42b1-8837-102de4b20cd9.png`。
- 眼睑素材：`artwork/thinking-blink.png`。生成原图：`exec-223a1b9d-ae6a-4916-ab10-53caf1d4065f.png`。

收手使用原来挥手结束姿势和挥手原画作参考；眼睑使用原来思考停留姿势作参考。参考单帧由v9备份中的程序直接导出，临时导出文件在收尾时清理。

## 收手完整提示词

Use case: identity-preserve, precise-object-edit.
Asset: eight full-body animation drawings for the END of a friendly wave. The user finds a reverse-played open palm descending unnatural. Fix the return action anatomically.
Reference image 1 is the EXISTING character, pose, scale, colors, linework and wave-ending starting position. Image 2 is supporting consistent art / detail reference of the same character.
Create ONE crisp 1536x1024 landscape transparent-alpha sprite sheet: exactly four equal columns by two rows; eight FULL figures, left-to-right then next row. Fixed feet on each row and identical overall height, generous separation and margin. No panels, labels, background, shadows, checkerboard, noise or extraneous speckles.
Preserve XiaoG exactly: gray-white hair, lavender eyes, segmented white horns, single ahoge, viewer-right white knot hair ornament and tassel, white/lilac Chinese dress, lilac batlike wings, curled white tail, same chibi proportions. Keep the subtle lilac colors and smooth sharp lines of reference image 2. Head, face, clothing, feet, still arm, wings and tail stable. Keep mouth a small friendly relaxed smile in all eight poses. Eyes OPEN in every pose. Two arms only.
Moving hand is on the VIEWER'S LEFT (same side as reference image 1), near her own right shoulder. Required eight closely spaced RETURN key poses:
1 EXACT reference image 1 end-of-wave open palm pose, palm facing viewer at shoulder/lower-cheek height.
2 hand still at that height, wrist turns inward about 25 degrees, fingers relax together, no continuing wave.
3 hand slightly lower, wrist has turned 55 degrees inward; palm almost EDGE-ON, fingers gently curved, thumb anatomically connected.
4 hand at upper chest outside torso, wrist now 80-90 degrees, back/outer edge of hand visible; palm faces her torso, elbow begins lowering, fingertips point diagonally inward/down.
5 elbow lowers and opens slightly; hand reaches mid chest outside torso, fingers loosely curled, BACK/OUTER side of hand visible; NO front-facing open palm.
6 hand at lower chest/waist beside torso, relaxed fingers loosely curled, back of hand visible, natural sleeve falls with gravity.
7 hand just above hip, relaxed small loose fist, back/outer side of hand visible, elbow near resting position.
8 NEUTRAL: hand relaxed at hip (matching the neutral figure in reference image 2); small loosely curled fingers, palm toward thigh. Entire arm and sleeve returned naturally.
Essential: show actual wrist rotation in frames 2-4 BEFORE lowering substantially. Do not keep the palm facing the viewer on the way down. Use distinct but gradual hand orientation cues, knuckles and correct thumb location. Redraw sleeve folds coherently with bending elbow and gravity; hand/wrist/sleeve continuous, not mechanical cutouts. No extra hands, original resting hand left behind, floating cuffs, stretched cloth, deformed face or duplicated edges. Do not let hand touch or warp the cheek. Clean transparent antialiasing. The first and last poses must fit the existing action endpoints.

## 眨眼完整提示词

Use case: precise-object-edit, identity-preserve.
Edit target is the provided EXACT existing XiaoG thinking-hold illustration. Need registered blink key frames of this exact still, not new character drawings.
Create one clean transparent-alpha 1536x1024 sheet, EXACTLY four equal columns by two rows, eight identical full-body instances of this input. Fixed foot baseline, identical size, identical HEAD POSITION, identical bangs/hair, identical face/jaw/nose/mouth/cheeks, identical fist under chin, arms, shoulders, clothing, wings, horns, tail and ornament in EVERY cell. Sharp clear clean linework; remove stray background specks from input, preserve true alpha. Keep all figures within cells, at least 20px margins, no clipping, no labels/grid/shadows/backdrop.
ONLY eyelids and irises change. Most essential: treat the input as a single locked drawing that is copied; do NOT redraw or shift bangs, facial skin or head outline. Keep the SAME quiet thoughtful tiny open mouth throughout, do NOT add a smile, do NOT move eyebrows.
Read left-to-right:
1 exactly OPEN thoughtful eyes of reference, no changes.
2 same eye shape, UPPER EYELID lowered about one-third, lower lid remains fixed; iris partly occluded, single upper lash.
3 UPPER EYELID lowered two-thirds, thin iris sliver, lower lid fixed; one upper lash, no duplicate lines.
4 FULLY CLOSED relaxed blink: one simple fine dark eyelid curve per eye at the naturally closed position, no iris and no remnants of the open upper lash. No heavy doubled black band.
5 identical FULLY CLOSED eyes as 4.
6 identical TWO-THIRDS closed as 3.
7 identical ONE-THIRD closed as 2.
8 identical OPEN as 1.
Natural downward blink, not closed-eye laughing smile. The hair/bangs silhouette above and between eyes is LOCKED and never shifts, no pieces of eyelashes left above the closed eye, no patched skin rectangle, no moving chin or cheeks. Exactly one outline per eyelid. All non-eye parts should be identical copies of the reference. Preserve subdued gray-white / lilac palette and detailed Chinese dress, no circuit patterns. Two arms exactly, fist remains just below chin.

## 程序如何使用

挥手前8张原画保持不变，追加8张收手姿势。手腕先从正面转到侧面，手指放松，再降低肘部并回到身旁；收手不再倒放抬手原画。整体时长仍为3.4秒。

眼睑素材以原思考停留姿势为位置标尺导入。程序始终复制同一张思考停留画面，仅在左右两个完整眼睛轮廓内更新眼睑，并保护从上沿及侧边连续延伸进来的灰白刘海；其余画面保持原有像素。眨眼独立播放半闭、接近闭合、闭合、接近闭合、半闭、睁开，约0.26秒，不经过身体位移网格。整体思考时长仍为4.4秒。

原来的矩形替换范围只覆盖闭眼线，遗漏了上方的睁眼睫毛，因此产生双层睫毛；直接扩大矩形又会带入另一张原画的刘海和肤色边界。本版使用匹配的眼睑原画、左右眼轮廓及刘海保护，避免这两类问题。

现有高清画布仍为576×624，姿势画面每秒最多更新30次；点击、双击、拖动、语录、设置和启动联动规则保持原有实现。旧原画和本次修改前的程序、源码保存在`outputs/xiaog-desktop-v9.zip`。
