import math
import re
from pathlib import Path
from mathutils import Quaternion, Vector

root = Path(__file__).resolve().parents[1]
folder = root / "Assets/Game/Weapons/BasicWand/Art/Animations"
source = root / "Assets/Game/Player/Art/Animations/WizardAnimated/WizardStaffCast.anim"
chain = ["WizardModel/Armature", "WizardModel/Armature/Hips",
         "WizardModel/Armature/Hips/Spine02", "WizardModel/Armature/Hips/Spine02/Spine01",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/LeftShoulder",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/LeftShoulder/LeftArm",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/LeftShoulder/LeftArm/LeftForeArm",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/LeftShoulder/LeftArm/LeftForeArm/LeftHand"]


def parse(file, heading, ending, vector=False):
    txt = Path(file).read_text(encoding="utf-8")
    body = txt.split(heading, 1)[1].split(ending, 1)[0]
    out = {}
    for block in re.split(r"(?=^  - curve:\n)", body, flags=re.M):
        p = re.search(r"(?m)^    path: (.+)$", block)
        if not p: continue
        values = []
        for t, raw in re.findall(r"(?m)^        time: ([^\n]+)\n        value: \{([^\n]+)\}", block):
            nums = [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", raw)]
            q = Vector(nums[:3]) if vector else Quaternion((nums[3], *nums[:3]))
            values.append((float(t), q))
        out[p.group(1)] = values
    return out


def sample(keys, t):
    if t <= keys[0][0]: return keys[0][1]
    for a, b in zip(keys, keys[1:]):
        if t <= b[0] + 1e-6:
            u = max(0, min(1, (t - a[0]) / max(1e-9, b[0] - a[0])))
            return a[1].slerp(b[1], u) if isinstance(a[1], Quaternion) else a[1].lerp(b[1], u)
    return keys[-1][1]


idle = parse(folder / "WizardCombatIdle_BasicWandGrip.anim", "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
for label, file in (("lightning", source), ("wand", folder / "BasicWandAttack.anim")):
    rot = parse(file, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
    pos = parse(file, "  m_PositionCurves:\n", "  m_ScaleCurves:", vector=True)
    print(label)
    for path in chain[-3:]:
        base = idle[path][0][1]
        angles = [(t, math.degrees(q.rotation_difference(base).angle)) for t, q in rot[path]]
        print(path.split("/")[-1], "max local deviation", max(angles, key=lambda x: x[1]))
    angles = []
    for i in range(66):
        t = i / 100
        q, p = Quaternion(), Vector((0, 0, 0))
        forearm = None
        for path in chain:
            p += q @ sample(pos[path], t)
            q = q @ sample(rot[path], t)
            if path.endswith("/LeftForeArm"): forearm = p.copy()
        wrist = math.degrees((q @ Vector((0, 1, 0))).angle(p - forearm))
        angles.append((t, wrist))
    print("wrist world angle max", max(angles, key=lambda x: x[1]), "min", min(angles, key=lambda x: x[1]))
