# 困困打盹密集原画提示词

制作日期：2026-10-21。

生成方式：内置 image_gen 工具；所有调用均设置 `transparent_background=true`，使用本地图片参考；未使用 CLI 或外部模型。

原始输出保留在 `C:/Users/47354/.codex/generated_images/01a15eb1-a80d-7641-a9e6-5f48f77ab2c2/`；项目副本为本目录的 `raise-atlas.png`、`rub-atlas.png`、`lower-atlas.png`、`settle-atlas.png`。

以下是生成时原样传入的完整提示词。图集均为候选原画，不代表每格已通过裁切、比例或实时播放验收；其中 lower 最后两格和 settle 存在手臂比例偏长，未采用。

## raise

原始文件：`exec-cb96bb7b-dca1-47bc-943d-576786b875ac.png`。

输入图片（顺序）：`upper-reference.png`、`../../raw/sleepy.png`。

```text
Use case: identity-preserve. Asset: hand-drawn cel animation character pose sheet for an existing chibi girl.
Image 1 is the exact character design and upper-body crop. Image 2 is an older sleepy gesture sheet showing only the intended action and materials.
Generate a transparent PNG pose sheet with EXACTLY EIGHT isolated UPPER-BODY character drawings in a 4 columns x 2 rows grid. Generous transparent space between drawings, no grid lines or text. Framing matches Image 1: whole horns, hair, face, both wings, both arms, waist / top of skirt visible; no legs needed. Keep the SAME front-view head, shoulder height, torso and other arm in all eight cells. Do not tilt the head, do not shift torso, do not change expression yet: eyes open.
This is a consecutive hand-drawn animation sequence: the SCREEN-LEFT arm lifts slowly from a relaxed lowered hand toward the outer corner of the SCREEN-LEFT eye to rub it sleepily. The arm must be naturally attached to the same shoulder, with a smoothly bending elbow and a relaxed wrist. White/lavender bell sleeve is soft fabric, changes folds with gravity, lags behind the hand slightly.
Read left to right, top to bottom:
1: exact relaxed arms-down reference pose.
2: tiny anticipation, fingers loosen, elbow begins bending.
3: hand lifts just above waist, wrist curves inward.
4: loosely closed hand midway between waist and cheek, elbow moves slightly outward.
5: hand reaches lower cheek / jaw height, elbow is lower and outward.
6: hand near cheek, wrist gently flexed inward, sleeve still settling.
7: hand just below outer corner of screen-left eye, knuckles almost touch cheek.
8: loosely closed hand gently touches outer corner of screen-left eye. Eyes remain open, face is uncompressed.
All eight poses genuinely different, neighboring drawings change only a little. Preserve chibi anatomy, consistent hand size, skin color, costume seams, fixed shoulder root. The fingers and elbow should trace organic curved paths, not rigid rod rotation. No extra fingers, no melting face, no face deformation, no floating arms. Crisp anime illustration, exactly the reference lavender/white color palette, fine clean dark outline. True alpha transparency; no painted checkerboard or shadow background.
```

## rub

原始文件：`exec-79b7ac04-07b1-456e-8bc3-89fa659859ce.png`。

输入图片（顺序）：`eye-reference.png`、`../../../thinking/pose-00.png`。

```text
Use case: identity-preserve. Production cel animation pose sheet.
Image 1 is the exact character at the eye-touch pose. Image 2 is the original style/model reference.
Create a transparent PNG with EXACTLY EIGHT upper-body drawings in a 4 columns x 2 rows grid, read left-to-right then top-to-bottom. Same framing as Image 1: whole horns, whole head, both arms and wings, waist visible, no legs. Large enough transparent margins between cells. No labels, no grid lines, no shadow background.
These eight consecutive drawings animate ONE small sleepy eye rub with the SCREEN-LEFT hand: keep the same shoulder root and upper body fixed. A loosely closed soft fist gently touches the OUTER CORNER of the screen-left eye. All cells should have eyes gently closed for sleepiness, same uncompressed face and same mouth. The hand moves only several pixels on the character, not across the entire eye or face. Tiny wrist and finger roll with a small elbow response; sleeve folds lag naturally. Organic curved motions, not rigid rods.
1 fist just touches lower outer eye corner.
2 wrist relaxes, knuckles roll slightly upward.
3 knuckles move a tiny bit inward and upward.
4 soft highest point of the rub, elbow subtly adjusts.
5 knuckles roll gently outward.
6 wrist loosens, hand lowers a very little.
7 fist lightly presses lower outer eye corner again.
8 hand relaxes against cheek just below eye, ready to leave.
All EIGHT drawings genuinely different, but only small continuous changes between neighboring cells. Fixed head, torso, shoulder height, other arm. Preserve white/lavender dress, bare upper arm, detached bell sleeve, chibi anatomy, hand size, line weight. True alpha transparency. Do not deform the cheek or eyelid around the hand, no melting face, no extra fingers, no floating shoulder. Crisp high quality painted anime cels.
```

## lower

原始文件：`exec-5259a6f0-6281-4c0c-9c4c-56c80191533e.png`。

输入图片（顺序）：`../../../thinking/pose-00.png`、`rub-atlas.png`。

```text
Use case: identity-preserve. Asset: consecutive hand-painted cel animation upper-body sheet for this exact chibi dragon girl.
Image 1 is the exact model reference. Image 2 is the previous eye-rubbing pose sequence for continuity. Generate EXACTLY EIGHT upper-body cels arranged 4 columns x 2 rows, read left to right then top to bottom. Transparent PNG, complete head with horns, wings, shoulders, both arms and waist / top of skirt visible. Same fixed frontal head, shoulder height, torso and other arm in every cell. Keep eyes softly half closed, mouth neutral. No labels, no grid, no background shadows.
Action is the SCREEN-LEFT hand naturally LEAVING THE EYE AND LOWERING BACK TO REST. Draw a different pose for every cell. This is an independent natural lowering movement, not an exact reversal of raising: elbow relaxes first, wrist remains soft, loosely closed hand turns to a relaxed inward / side facing position early, then fingers loosen by the end. Cloth follows with a slight lag.
1: fist lightly against cheek just below outer eye corner, at end of rubbing.
2: hand leaves cheek, wrist lowers a little to lower-cheek height, elbow relaxes outward.
3: hand descends to jaw / upper-chest height, wrist turns inward.
4: hand at chest height, elbow still softly bent, hanging sleeve folds gather.
5: hand slightly above waist, elbow opens, fingers loosen.
6: hand nearly reaches relaxed down position, sleeve has not quite settled.
7: arm reaches neutral arms-down position matching Image 1, hand at hip.
8: same natural hand-down ending with sleeve finally settled.
Preserve anatomy and volumes, same connected shoulder root, natural organic arcs and clearly articulated upper arm/elbow/forearm/hand. White and lavender detached bell sleeve, fine clean outlines and reference coloring. Do not distort face, neck, shoulder, torso, costume or wings. No extra limbs/fingers or floating arm. True alpha transparency, no checkerboard pattern.
```

## settle

原始文件：`exec-a5df6ae9-9ae3-4e48-8ecd-27f968e9c78e.png`。

输入图片（顺序）：`../../../thinking/pose-00.png`、`lower-atlas.png`。

```text
Use case: identity-preserve. Cel animation production sheet, exact existing character.
Image 1 is the exact model. Image 2 is a previous hand-lowering pose sheet. We ONLY need the FINAL QUARTER of the SCREEN-LEFT arm lowering motion, from hand slightly in front of waist to the natural down-at-the-side resting hand seen in Image 1.
Generate EIGHT consecutive upper-body cels in a 4 columns x 2 rows grid. Transparent background, no text/grid. Complete head/horns, shoulders, both arms, wings and hip visible in each cell. Fixed frontal head, body, shoulder root and screen-right arm. Eyes half-open and sleepy.
The SCREEN-LEFT arm stays short and naturally proportioned to Image 1; do not lengthen the arm! Wrist height at the end must match Image 1.
Sequence, with SMALL differences in each successive cell:
1: screen-left hand loosely relaxed just in front of the LEFT HALF of the waist, elbow slightly bent. Hand is NOT at the chest or face.
2: wrist sinks slightly and moves outward a little toward the screen-left hip.
3: elbow opens softly, hand drifts down and outward.
4: relaxed side of the hand visible, wrist moves farther toward screen-left hip.
5: almost rest, elbow nearly extended, hand just above its original resting height.
6: wrist at the exact original resting hand position, sleeve still gently swinging.
7: hand fully relaxed at its original down-at-side position, sleeve beginning to settle.
8: exact resting arm / sleeve silhouette from Image 1.
Natural organic arcs, changing cloth folds, tiny overlap/lag in white/lavender bell sleeve. SAME SHOULDER POSITION and SAME total arm length in every frame. Preserve reference hand size, anatomy, clean dark outline, white/lavender/skin palette. No rigid rod look, no melting face or body, no fingers merging. Real alpha transparency.
```
