"""Inspect the in-game staff cast wrist and staff direction with Blender Python."""
import math
import re
from pathlib import Path

from mathutils import Quaternion, Vector

root = Path(__file__).resolve().parents[1]
clip = root / "Assets/Weapons/Common/BaseCast.anim"
text = clip.read_text(encoding="utf-8")
chain = [
    "WizardModel/Armature",
    "WizardModel/Armature/Hips",
    "WizardModel/Armature/Hips/Spine02",
    "WizardModel/Armature/Hips/Spine02/Spine01",
    "WizardModel/Armature/Hips/Spine02/Spine01/Spine",
    "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder",
    "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm",
    "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm",
    "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder/RightArm/RightForeArm/RightHand",
]


def parse(start, end):
    section = text.split(start, 1)[1].split(end, 1)[0]
    result = {}
    for block in re.split(r"(?=^  - curve:\n)", section, flags=re.M):
        path = re.search(r"(?m)^    path: (.+)$", block)
        if path:
            result[path.group(1)] = [
                (float(t), [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", value)])
                for t, value in re.findall(r"(?m)^        time: ([^\n]+)\n        value: (\{[^\n]+\})", block)
            ]
    return result


rotation = parse("  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
position = parse("  m_PositionCurves:\n", "  m_ScaleCurves:")


def sample(data, path, time, rot=False):
    values = data[path]
    if time <= values[0][0]:
        raw = values[0][1]
    elif time >= values[-1][0]:
        raw = values[-1][1]
    else:
        for (t0, a), (t1, b) in zip(values, values[1:]):
            if t0 <= time <= t1:
                u = (time - t0) / (t1 - t0)
                if rot:
                    return Quaternion((a[3], *a[:3])).slerp(Quaternion((b[3], *b[:3])), u)
                return Vector(a[:3]).lerp(Vector(b[:3]), u)
    return Quaternion((raw[3], *raw[:3])) if rot else Vector(raw[:3])


staff = Quaternion((0.75100106, -0.60682046, 0.18719545, 0.18089838))
shaft_local = staff @ Vector((0, 1, 0))


def project(v, axis):
    return v - axis * v.dot(axis)


maximum = (0, 0)
for time, _ in rotation[chain[-1]]:
    q, p = Quaternion(), Vector((0, 0, 0))
    forearm = None
    for path in chain:
        p += q @ sample(position, path, time)
        q = q @ sample(rotation, path, time, True)
        if path.endswith("/RightForeArm"):
            forearm = p.copy()
    socket_path = chain[-1] + "/WeaponSocket"
    socket = sample(rotation, socket_path, time, True)
    axis = (q @ socket @ shaft_local).normalized()
    arm = (p - forearm).normalized()
    up = q @ Vector((0, 1, 0))
    wrist = math.degrees(up.angle(arm))
    maximum = max(maximum, (wrist, time))
    if abs(time - .2) < 1e-4:
        aim = math.degrees(axis.angle(Vector((0, 0, 1))))
        print(f"release wrist={wrist:.2f} degrees, staff aim error={aim:.4f} degrees")
        assert wrist < 25 and aim < .01

print(f"largest wrist angle={maximum[0]:.2f} degrees at {maximum[1]:.3f}s")
assert maximum[0] < 45
events = re.findall(r"(?m)^    functionName: (.+)$", text)
assert events == ["FireMagic", "EndAttack"], events
for endpoint in (0, .65):
    socket_rotation = sample(rotation, socket_path, endpoint, True)
    assert math.degrees(socket_rotation.angle) < .01
for path in chain[-3:] + [socket_path]:
    keys = rotation[path]
    jump = max((math.degrees(Quaternion((a[1][3], *a[1][:3])).rotation_difference(
        Quaternion((b[1][3], *b[1][:3]))).angle), a[0], b[0])
        for a, b in zip(keys, keys[1:]))
    print(f"{path.split('/')[-1]} largest key rotation={jump[0]:.2f} degrees at {jump[1]:.3f}-{jump[2]:.3f}s")
    assert jump[0] < 22
