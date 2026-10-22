# 灵光一闪 · 实画姿势提示词

使用内置 image_gen，透明背景；成品仅提取手臂，人物脸与身体沿用旧底图。

## 试制24格（未采用）

Use case: identity-preserve. Production 2D CEL ANIMATION sprite sheet.
Create a very high resolution 3072 x 2048 transparent PNG, EXACTLY 6 columns x 4 rows = 24 equal square cells. Each cell shows the SAME chibi white dragon girl from head through hips, centered at the SAME size and position. The input first image is her exact identity and painting style. Input second image shows the old six rough key poses of the intended "I have an idea!" arm gesture. Now DRAW THE MISSING INTERMEDIATE POSES, not merely repeat those six keys.
Camera is front orthographic and completely locked. Her white hair, lavender eyes, cute face, jaw, head, neck, torso, shoulder attachment, wings, tail and waist stay motionless and identical in all cells; both shoulders stay level. Mouth remains her tiny neutral open mouth. No head turns, no eye animation. Only her SCREEN-RIGHT arm, relaxed fingers and lavender-white bell sleeve move. SCREEN-LEFT arm remains down. Show the upper arm actually connected to shoulder, a real bent elbow, forearm and hand. Preserve anatomy and volume; no extra hands.
Her right hand should rise naturally on an outward curved arc, then extend an index finger beside her ear, then close and return gently to her side. The elbow leads the lifting a little, wrist follows softly, cloth lags. Arm and sleeve need organic shapes, drawn changing cloth folds, and freely bending elbow positions, NOT straight rigid sticks rotating around a single point. A small soft gesture, never waving or pumping.
Twenty-four consecutive DRAWN poses, read each row left to right:
01 rest, hand at side.
02 tiny anticipation.
03 starts lifting, wrist just above hip.
04 wrist lifts one more small step and moves outward.
05 wrist between hip and waist.
06 wrist at waist level, elbow flexes.
07 wrist a little above waist.
08 forearm tilts up, fist still relaxed.
09 fist reaches lower-chest level beside body.
10 fist rises to shoulder level.
11 wrist near cheek height, index starts unfolding.
12 index halfway extended.
13 index fully up beside ear.
14 a tiny upward index emphasis.
15 emphasis settles and cloth catches up.
16 index starts curling back into a loose hand.
17 index folded, palm turns sideward, arm still raised.
18 hand begins lowering past lower cheek.
19 hand down at shoulder level, elbow relaxes outward.
20 hand at upper-waist level.
21 hand at waist.
22 hand almost at hip, relaxed fingers.
23 hand arrives at original rest, cloth still slightly raised.
24 same settled rest as 01.
The raising and lowering drawings must have evenly progressive positions with visible in-between anatomy. No sudden jumps from down to up. No duplicated rows or columns. Maintain the exact character outfit, same pale ivory skin and white/light lavender shading. All 24 subjects visible, no inter-cell overlap, no labels, no text, no border lines, no scenery. Real alpha transparency; no painted checkerboard. The generous high resolution is needed to preserve fingers and sleeve linework.

## 抬手8格

Use case: identity-preserve. 2D hand-drawn animation sprite sheet.
Input image is the exact identity/outfit/painted style and upper-body crop of XiaoG, the white-haired chibi dragon girl. Produce ONE transparent atlas, EXACTLY 4 columns x 2 rows = EIGHT equal square cells, in chronological order. High resolution. Every cell contains the same character from hair tips to below hips, fixed frontal camera, head, tiny open mouth, eyes, neck, torso, wings, left arm all COMPLETELY STILL and aligned. Don't crop either hand.
This is ONLY THE SLOW LIFTING phase of the SCREEN-RIGHT arm for a cute "I have an idea" gesture. All eight hands are a soft relaxed loose fist, no extended index finger yet. Draw real inbetween anatomy with natural bending shoulder/elbow/wrist, gentle curved wrist path. Both shoulder roots stay in the SAME place. Include upper arm connected to shoulder, sleeve and hand.
Each cell must have a DIFFERENT precisely progressive wrist height. In the input reference's coordinate system (400x410), the screen-right wrist initially lies near (269,358); the shoulder root near (229,281).
01 wrist (269,358), same resting down arm.
02 wrist (275,350), slightly starts lifting.
03 wrist (279,339), small outward elbow bend.
04 wrist (281,325), forearm gradually inclines up.
05 wrist (280,310), elbow flexes farther.
06 wrist (275,294), hand now near shoulder height.
07 wrist (267,280), hand beside lower cheek.
08 wrist (260,270), relaxed fist near the lower cheek, prepared to extend index next.
These eight drawings must NOT jump straight from rest to raised, do NOT skip the middle wrist heights. The hanging bell sleeve changes its folds and follows with slight lag, relaxed hand knuckles turn slightly inward. Natural soft curves, no rigid sticks. All other parts of the character identical to input. Pale warm skin and ivory/lavender cloth, fine clean anime outline. No text, labels, grid lines, background, scenery or checkerboard; true alpha transparency.

## 展指与收指8格

Use case: identity-preserve. Production 2D CEL ANIMATION upper-body sprite sheet.
Image 1 is the fixed original character. Image 2 is the accepted lifting sequence: the arm on the VIEWER'S LEFT SIDE of the image has lifted into a loose fist beside her LEFT cheek. Continue exactly THAT same arm and pose. The arm on the VIEWER'S RIGHT stays down.
Create exactly 8 consecutive drawings in a 4 columns by 2 rows atlas, true alpha transparency. Same upper-body crop, same character size, locked front camera. All faces, hair, eyes, tiny neutral open mouth, neck, torso, wings, shoulder roots stay unchanged. Only the arm on the LEFT HALF OF EACH CELL and its hand and sleeve move; the other arm stays resting. Pale skin and white lavender bell sleeve match references. No text.
This sheet is ONLY the "index finger unfolds / little idea / finger curls back" stage. Fist is already raised beside the cheek and the wrist barely moves. Depict all the finger articulation in distinct real drawings:
1 loose raised fist beside cheek, all fingers folded;
2 index first knuckle loosens and index is short/partly bent;
3 index opens halfway upward with a clearly bent upper joint;
4 index fully extends, pointing upward beside the cheek, happy little idea gesture;
5 small relaxed emphasis of the fully raised index, wrist rises only a few pixels;
6 wrist settles, index relaxes slightly curved;
7 index folds halfway back into the hand;
8 index completely folded into loose fist, wrist turns slightly sideward ready to lower.
The pointed fingertip stays outside the face silhouette, close beside her ear. Upper arm, shoulder and elbow stay naturally connected in every cell. The broad sleeve hangs under the bent forearm with soft fabric folds and little cloth lag, not a rigid cone. Do NOT replace the raised arm with another hand or move the face. Fine clean anime linework, no scenery, no grids, no borders, no fake checkerboard.

## 独立收手8格

Use case: identity-preserve. Production 2D CEL ANIMATION sheet.
Image 1 is the exact fixed original character and upper-body style. Image 2 is the gesture's end: the arm on the VIEWER'S LEFT SIDE has a raised fist beside the LEFT cheek. Now draw only its independent natural LOWERING motion.
Transparent PNG atlas of EXACTLY 8 equal square cells, 4 columns x 2 rows, chronological left-to-right. Same high-resolution painted anime style. All frames show the same fixed centered chibi dragon-girl upper body from hair tips to below hips. Head, lavender eyes, neutral tiny open mouth, jaw, neck, torso, shoulder roots, wings and arm on viewer's RIGHT remain absolutely motionless/identical. Only the arm on viewer's LEFT and its loose bell sleeve change. Anatomically correct elbow/hand, white-lavender cloth, warm pale skin. No labels or text.
01 starts with a relaxed closed fist beside LEFT lower cheek, wrist nearly shoulder height.
02 hand has turned so thumb/side/back is visible, wrist drops just below shoulder height.
03 hand lowers only one more small step, elbow bends less and moves out a little; sleeve lags.
04 hand at middle chest level to the left side of torso, fingers softly loosen.
05 hand at upper waist level, relaxed hand side visible.
06 hand at waist level, elbow opening more; cloth falls softly.
07 hand near hip level, same natural downward hand direction as original rest.
08 reaches exactly original down-rest arm position, sleeve settled.
Distinct small evenly spaced changes in wrist height in ALL eight frames, no skipping from face to waist. Do not loop the hand across her chest and do not keep the palm facing camera as it lowers. The hand follows a soft outward arc back to side. The shoulder is fixed, elbow shifts naturally, sleeve has changing folds and follow-through. Draw connected upper arm, elbow, forearm and hand in every frame, preserving volume. Nothing else moves. True alpha transparency, no checkerboard, no grid lines, no scenery, no extra limbs.
