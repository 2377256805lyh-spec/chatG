# 小G挥手与思考原画记录

这是首版原画记录。当前追加的独立收手姿势和替换的思考眼睑见`gesture-refinement-prompts.md`；下述旧矩形眨眼替换方式已被替换。

本次使用内置 image_gen 工具，采用两张透明的1536×1024图，每张4列×2行，共8个完整人物姿势。程序使用配套的手、袖子、头肩和表情原画；每张按原来脚底位置导入到576×624画布。历史素材和v8备份保留。

## 挥手：waving-coordinated.png

最终原图：`C:\Users\47354\.codex\generated_images\01a11549-0893-7713-8442-0d0dcdac42ff\exec-385292d1-dcda-4755-a831-1e5a92678654.png`。

初次生成有边缘噪点和留白不足，经下面的定向修正版清理后采用。

### 初次生成提示词

Use case: identity-preserve.
Asset type: production transparent 2D animation key-pose sprite sheet for XiaoG, an existing desktop character.
Input image 1 is the EXACT current character and neutral pose. Input image 2 is a supporting example of the existing natural coordinated animation style. Preserve image 1 identity and detailed outfit.
Create ONE sheet of EXACTLY EIGHT full-body consecutive wave poses, four equal columns and two rows, on real transparent alpha, large detailed landscape canvas. No grid, labels, captions, ground shadow or painted checkerboard. Generous transparent space between figures, no tails or wings crossing a cell.
Keep gray-white hair, lavender eyes, ribbed white horns, single ahoge, viewer-right knot ornament and tassel, pointed ears, white Chinese-style dress, lavender wings, curled white tail. Same head-to-body proportions, linework, colors and sharp detail. Fixed feet, same overall scale and front-facing camera in all eight cells. The gestures should be coordinated and understated like the existing character's waiting/review animations, not a rigid arm puppet.
READ LEFT TO RIGHT:
1 exactly neutral pose, two hands relaxed by hips.
2 viewer-left arm smoothly starts lifting: relaxed OPEN palm just above hip, elbow still low.
3 elbow bends outward gently, the same OPEN palm at lower chest level.
4 hand at shoulder height, palm visible, forearm naturally bent, sleeve hanging by gravity.
5 open palm at lower-cheek height, positioned OUTSIDE the face on viewer-left with a clear visible gap from hair/cheek, forearm angled slightly outward.
6 small wrist wave outward, palm moves only a little horizontally, elbow stays at same comfortable height; tiny friendly head inclination and closed small smile.
7 wrist wave inward, same palm shape and fingers, elbow and sleeve connected naturally, tiny matching shoulder response; gentle smile with both eyes open.
8 wrist returns to the frame-5 position, smile relaxed.
The wave is made by wrist and forearm, not by changing the fingers from a fist to a palm. All eight moving hands have the SAME relaxed spread of fingers, no sudden hand-type changes. Keep right-side arm relaxed and naturally attached. Sleeve folds are redrawn coherently following elbow bending; no floating sleeve, stretched cloth, melting background, extra arms, duplicate hands, deformed face or warped cheek. Avoid any hand touching or covering the face. Head silhouette and chin shape stay rigid, facial expression changes only via eyes and mouth. Preserve crisp subtle lilac palette, no circuits.

### 最终清理提示词

Use case: precise-object-edit.
Edit target image 1: the exact EIGHT-pose 4-column x 2-row XiaoG wave animation sheet. Supporting image 2: clean linework and character detail reference.
KEEP exactly the existing 8 poses, all character features, arm positions, matching scale and transparent background, grid order, and facial expressions of target image 1.
REPAIR ONLY these quality problems: remove ALL loose white, pink, magenta, cyan and black specks or stray colored pixels around the characters and between cells. Replace jagged pink/white color fringes with clean thin gray/lilac anime outlines and smooth antialiasing. Background must be genuinely transparent, including gaps inside hair, wings and curled tail. Use the clean outline and restrained coloring quality of supporting image 2.
Fit the 4-by-2 arrangement into a spacious 1536x1024 landscape canvas with at least 24 pixels of real transparent margin around every figure; every full ahoge, horn, hair strand, wing, skirt, feet and tail must be fully inside its cell, no clipping, no overlaps. Do not crop the tops of the first-row heads.
Maintain eight consistent full-body drawings, never stretch the face or alter the chin outline. Exactly two arms and two hands per figure; sleeves and wrists anatomically connected, no extra arms or floating cuffs. No labels, grid lines, backdrop, shadows, checkerboard pattern, noise, texture grain, motion blur or ghost contours. This is an outline cleanup and margin repair, not new poses or a redesign.

## 思考：thinking-coordinated.png

最终原图：`C:\Users\47354\.codex\generated_images\01a11549-0893-7713-8442-0d0dcdac42ff\exec-7261325b-9da8-4397-9063-86d710a52e37.png`。

第7张眨眼在导入后只替换第6张的眼部像素，手、肩膀和身体保留第6张，以免眨眼时抖动。程序将动作安排为抬手、思考停留、短眨眼、收手。

### 提示词

Use case: identity-preserve.
Asset type: production 2D animation key-pose sheet for XiaoG desktop companion.
Input image 1 is the exact cleaned XiaoG character identity, linework and outfit reference. Input image 2 is a supporting natural head/shoulder animation reference.
Create ONE new crisp EIGHT-pose full-body THINKING animation sheet on genuinely transparent alpha, four columns and two rows in a 1536x1024 landscape canvas. Maintain the same anatomical scale, fixed foot baseline in each row, exact same colors and original front-facing proportions. At least 24 px transparent safety margin around EACH character, especially full ahoge, horns, wings, tail, feet. No captions, grid, background or cast shadows.
Match image 1 precisely: gray-white hair, purple eyes, white segmented horns, single ahoge, viewer-right white knot ornament with tassel, white/lilac Chinese dress, exposed shoulders with detached bell sleeves, pale lavender wings, curled white tail. Clean thin lilac-gray linework, clear detailed eyes and outfit; no speckles or colored fringes.
Eight consecutive states, left-to-right:
1 NEUTRAL: hands relaxed down by hips, same as idle.
2 viewer-left hand starts lifting slightly above hip, relaxed loose fist, elbow begins bending.
3 same loose fist reaches lower chest, elbow bends naturally, sleeve hangs below.
4 knuckles reach upper chest, still below chin, sleeve folds follow elbow.
5 loose fist rests JUST BELOW the chin, with a visible narrow gap between hand and chin; head inclined only about 2 degrees toward viewer-left, thoughtful open eyes.
6 same hand held BELOW chin, eyes glance slightly upward, head tilts back only a tiny amount, shoulders respond subtly; no arm pumping.
7 same thinking hand position, one gentle blink, face shape and chin unchanged.
8 same thinking hand position, eyes reopen with a small thoughtful smile.
The final 4 poses are a quiet thinking hold with small head/eye changes, not a series of different arm gestures. Her hand NEVER touches the lips or enters the cheek, never eats a finger. Maintain the chin silhouette in every frame; no melting cheeks, stretched neck, squashy/jelly face. The foreground hand and sleeve must be separate visually from the face. Natural connected shoulder-elbow-wrist anatomy, exactly two arms and hands, right-side hand remains relaxed near hip. No spare hip hands left behind, no extra wrists, no floating cuffs or ghost outlines. Keep wings, dress and tail stable with only subtle natural pose response. Do not change character identity, face proportions or clothing. High detail, sharp edges, not pixel art or 3D.

## 动作过渡

两张原画分别算向下一张、向上一张的位移，让手部在中间位置对齐；脸和下巴取同一张原画的完整形状，单次绘制只使用一套纹理。中间画面最多每秒更新30次，与旧版的点击、双击、拖动及设置规则相同。

额外尝试的6姿势抬手图、分离手臂和遮挡补图未采用，工作区副本与临时预览已清理。
