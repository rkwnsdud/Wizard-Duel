# Tools

Unity 밖에서 돌리는 보조 스크립트다. 게임 빌드에는 들어가지 않는다. 명령은 저장소 루트에서 실행한다. 검사 보고서는 git에 올라가지 않는 `Logs/` 아래에 남긴다.

| 스크립트 | 실행 | 하는 일 |
|---|---|---|
| `WeaponMesh/decimate_weapon.py` | `blender -b --factory-startup --python Tools/WeaponMesh/decimate_weapon.py -- <in.fbx> <out.fbx> <목표 삼각형 수> <비교 이미지 폴더> <이름>` | 무기 FBX를 목표 삼각형 수로 줄여 같은 오브젝트·재질 이름으로 다시 내보낸다. `BasicWand_Low.fbx`와 `LightningStaff_Low.fbx`를 이것으로 만들었다. |
| `create_arcane_bolt_vfx.py` | `blender -b --python Tools/create_arcane_bolt_vfx.py` | Eric VFX Studio의 `FX_Fireball`에서 기본 완드 마력탄 VFX를 만든다. 다시 돌릴 때의 GUID 주의는 [VFX 문서](../Assets/Game/Weapons/BasicWand/Art/VFX/README_KO.md)에 있다. |
| `fix_wand_attack_pose.cjs` | `node Tools/fix_wand_attack_pose.cjs` | 기본 완드 공격 클립(`BasicWandAttack.anim`)의 오른팔 회전 트랙을 지금 자세로 다시 쓰고 `Logs/BasicWand/neutral-wrist-validation.txt`에 검사 결과를 남긴다. |
| `aim_wand_offline.py` | Blender의 Python으로 실행(`mathutils` 필요) | 같은 공격 클립에 오른팔 조준 보정을 넣는다. 지금 자세보다 앞선 방식이다. |
| `neutralize_offhand_wrist.py` | Blender의 Python으로 실행 | 같은 공격 클립에서 빈 왼손 아래팔·손목을 중립으로 둔다. 보고서는 `Logs/BasicWand/offhand-wrist-validation.txt`. |
| `check_wand_aim_offline.py` | Blender의 Python으로 실행 | 공격 클립의 조준 오차·손목 각도·이벤트를 검사한다. 클립은 바꾸지 않는다. 보고서는 `Logs/BasicWand/lightning-offline-validation.txt`. |
| `preview_wand_pose.py` | `blender -b --python Tools/preview_wand_pose.py -- <클립> <출력 이미지> [시각]` | 손 보정 메시(`Wizard_BasicWandGrip.asset`)를 실제 스킨 가중치로 Blender에서 렌더해 본다. `aim_wand_offline.py`의 계산 함수를 불러 쓴다. |
| `UnityStaffWindow.ps1` | `powershell -NoProfile -File Tools/UnityStaffWindow.ps1 [-Refresh] [-TogglePlay]` | 열린 Wizard-Duel 에디터 창을 앞으로 가져와 `Logs/UnityWindow.png`로 찍는다. `-Refresh`는 Ctrl+R, `-TogglePlay`는 Ctrl+P(플레이 모드 켜고 끄기)를 먼저 보낸다. 에디터 창이 맨 앞으로 오지 않으면 찍지 않는다. |

`fix_wand_attack_pose.cjs`, `aim_wand_offline.py`, `neutralize_offhand_wrist.py`는 `Assets/Game/Weapons/BasicWand/Art/Animations/`의 공격 클립을 직접 덮어쓴다. 지금 클립의 오른팔 자세는 `fix_wand_attack_pose.cjs`가 마지막으로 쓴 것이므로, 자세를 다시 만들 때는 이 스크립트만 돌린다. 다른 두 스크립트를 돌리면 지금 자세가 바뀐다. 완드 자세가 이렇게 된 이유는 [기본 완드 에셋](../Docs/BasicWand/README_KO.md)에 있다.
