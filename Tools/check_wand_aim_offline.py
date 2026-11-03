import math
import re
from pathlib import Path
from mathutils import Quaternion, Vector

root = Path(__file__).resolve().parents[1]
base = root / "Assets/Weapons/Basic Wand/Animations"
paths = ["WizardModel/Armature", "WizardModel/Armature/Hips",
         "WizardModel/Armature/Hips/Spine02", "WizardModel/Armature/Hips/Spine02/Spine01",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm/RightHand"]


def qvalue(raw):
    v = [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", raw)]
    return Quaternion((v[3], v[0], v[1], v[2]))


def curves(filename):
    txt = (base / filename if not Path(filename).is_absolute() else Path(filename)).read_text(encoding="utf-8")
    section = txt.split("  m_RotationCurves:\n", 1)[1].split("  m_CompressedRotationCurves:", 1)[0]
    result = {}
    for block in re.split(r"(?=^  - curve:\n)", section, flags=re.M):
        p = re.search(r"(?m)^    path: (.+)$", block)
        if p:
            result[p.group(1)] = [(float(t), qvalue(v)) for t, v in re.findall(
                r"(?m)^        time: ([^\n]+)\n        value: (\{[^\n]+\})", block)]
    return result


def positions(filename):
    txt = (base / filename if not Path(filename).is_absolute() else Path(filename)).read_text(encoding="utf-8")
    section = txt.split("  m_PositionCurves:\n", 1)[1].split("  m_ScaleCurves:", 1)[0]
    result = {}
    for block in re.split(r"(?=^  - curve:\n)", section, flags=re.M):
        p = re.search(r"(?m)^    path: (.+)$", block)
        if p:
            result[p.group(1)] = [(float(t), Vector([float(v) for v in re.findall(
                r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", raw)][:3]))
                for t, raw in re.findall(r"(?m)^        time: ([^\n]+)\n        value: (\{[^\n]+\})", block)]
    return result


def sample(keys, t):
    if t <= keys[0][0]: return keys[0][1]
    for i in range(1, len(keys)):
        if t <= keys[i][0] + 1e-6:
            a, b = keys[i - 1], keys[i]
            u = (t - a[0]) / max(1e-9, b[0] - a[0])
            u = max(0, min(1, u))
            return a[1].slerp(b[1], u) if isinstance(a[1], Quaternion) else a[1].lerp(b[1], u)
    return keys[-1][1]


source_file = root / "Assets/Player/Animations/WizardAnimated/WizardStaffCast.anim"
source = curves(source_file)
idle = curves("WizardCombatIdle_BasicWandGrip.anim")
report = []
for file, local in [("BasicWandAttack.anim", (0.115895, 0.861220, 0.226995, -0.439708)),
                    ("BasicWandAttack_DummyGrip.anim", (0.077292, 0.877672, 0.151386, -0.448108))]:
    data = curves(file)
    pos = positions(file)
    unchanged = ("Hips", "Spine02", "Spine01", "Spine", "LeftUpLeg", "LeftLeg", "LeftFoot",
                 "RightUpLeg", "RightLeg", "RightFoot", "LeftArm", "neck", "Head")
    checked = 0
    for path, original in source.items():
        if path.split("/")[-1] not in unchanged: continue
        actual = data[path]
        assert len(actual) == len(original), (file, path, "key count")
        for (time_a, qa), (time_b, qb) in zip(actual, original):
            assert abs(time_a - time_b) < 1e-5 and math.degrees(qa.rotation_difference(qb).angle) < .01, (file, path)
        checked += 1
    for path, values in data.items():
        name = path.split("/")[-1]
        if name in ("RightForeArm", "RightHand") or name.startswith(
                ("RightThumb", "RightIndex", "RightMiddle", "RightRing", "RightLittle")):
            for q in (values[0][1], values[-1][1]):
                assert math.degrees(q.rotation_difference(idle[path][0][1]).angle) < .1, (file, path, "idle endpoint")
    empty_forearm = next(path for path in data if path.endswith("/LeftForeArm"))
    empty_hand = next(path for path in data if path.endswith("/LeftForeArm/LeftHand"))
    empty_forearm_max = max(math.degrees(q.rotation_difference(idle[empty_forearm][0][1]).angle)
                            for _, q in data[empty_forearm])
    empty_hand_max = max(math.degrees(q.rotation_difference(idle[empty_hand][0][1]).angle)
                         for _, q in data[empty_hand])
    empty_forearm_jump = max(math.degrees(a[1].rotation_difference(b[1]).angle)
                             for a, b in zip(data[empty_forearm], data[empty_forearm][1:]))
    source_empty_jump = max(math.degrees(a[1].rotation_difference(b[1]).angle)
                            for a, b in zip(source[empty_forearm], source[empty_forearm][1:]))
    assert empty_forearm_max < 35 and empty_hand_max < .1, (
        file, "empty-hand wrist not relaxed", empty_forearm_max, empty_hand_max)
    assert empty_forearm_jump <= source_empty_jump + .1, (file, "empty forearm key jump")
    max_arm_jump = 0
    jump_at = ""
    source_max_jump = 0
    for path in paths[-3:]:
        keys = data[path]
        for a, b in zip(keys, keys[1:]):
            jump = math.degrees(a[1].rotation_difference(b[1]).angle)
            if jump > max_arm_jump:
                max_arm_jump, jump_at = jump, f"{path.split('/')[-1]} {a[0]:.3f}-{b[0]:.3f}"
        source_max_jump = max(source_max_jump, *(math.degrees(a[1].rotation_difference(b[1]).angle)
                                                   for a, b in zip(source[path], source[path][1:])))
    max_wrist = 0
    max_wrist_time = 0
    for t, _ in data[paths[-1]]:
        world_q, world_p = Quaternion(), Vector((0, 0, 0))
        forearm_p = None
        for path in paths:
            world_p += world_q @ sample(pos[path], t)
            world_q = world_q @ sample(data[path], t)
            if path.endswith("/RightForeArm"):
                forearm_p = world_p.copy()
        wrist_angle = math.degrees((world_q @ Vector((0, 1, 0))).angle(world_p - forearm_p))
        if wrist_angle > max_wrist:
            max_wrist, max_wrist_time = wrist_angle, t
    assert max_wrist < 50, (file, "excessive wrist bend", max_wrist)
    assert max_arm_jump <= source_max_jump + .1, (file, "abrupt arm key", max_arm_jump)
    for time in (0, .2, .65):
        q = Quaternion()
        p = Vector((0, 0, 0))
        elbow = None
        for path in paths:
            p += q @ sample(pos[path], time)
            q = q @ sample(data[path], time)
            if path.endswith("/RightForeArm"):
                elbow = p.copy()
        wand = Quaternion((local[3], *local[:3]))
        forward = q @ wand @ Vector((0, 0, 1))
        error = math.degrees(forward.angle(Vector((0, 0, 1))))
        wrist = math.degrees((q @ Vector((0, 1, 0))).angle(p - elbow))
        print(file, "time", time, "aim error", round(error, 3), "wrist", round(wrist, 2))
        if time == .2:
            assert error < 1 and wrist < 45, (file, error, wrist)
            report.append(f"{file}: source body/leg curves {checked} unchanged; empty LeftForeArm max {empty_forearm_max:.2f} deg, key jump {empty_forearm_jump:.2f} deg (source {source_empty_jump:.2f}); LeftHand local max {empty_hand_max:.2f} deg; release aim {error:.3f} deg; right wrist {wrist:.2f} deg; max right wrist {max_wrist:.2f} deg at {max_wrist_time:.3f}s; max right arm key jump {max_arm_jump:.2f} deg at {jump_at} (source {source_max_jump:.2f} deg)")
    txt = (base / file).read_text(encoding="utf-8")
    assert re.findall(r"(?m)^    functionName: (.+)$", txt) == ["FireMagic", "EndAttack"]
    assert re.findall(r"(?m)^  - time: (.+)$", txt)[-2:] == ["0.2", "0.65"]
report.append("PASS: Lightning source remains separate; only standard FireMagic/EndAttack events in wand copies.")
(root / "Docs/BasicWand/Attack/lightning-offline-validation.txt").write_text("\n".join(report) + "\n", encoding="utf-8")
print("\n".join(report))
