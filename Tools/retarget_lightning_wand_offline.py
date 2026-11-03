"""Blender Python fallback for retargeting Unity's serialized Lightning clip.

Keeps source body curves and events; offsets only rotations needed to start/end
at the wand idle grip. Unity's full authoring pass adds exact release aiming.
"""
import hashlib
import math
import re
from pathlib import Path
from mathutils import Quaternion

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Player/Animations/WizardAnimated/WizardStaffCast.anim"
IDLE = ROOT / "Assets/Weapons/Basic Wand/Animations/WizardCombatIdle_BasicWandGrip.anim"
OUTPUT = ROOT / "Assets/Weapons/Basic Wand/Animations"
SOURCE_BYTES = SOURCE.read_bytes()
SOURCE_SHA = hashlib.sha256(SOURCE_BYTES).hexdigest()
src = SOURCE_BYTES.decode("utf-8").replace("\r\n", "\n")
idle = IDLE.read_text(encoding="utf-8")


def section(text, start, end):
    a = text.index(start) + len(start)
    b = text.index(end, a)
    return text[a:b], a, b


def rot_blocks(text):
    body, _, _ = section(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
    return re.split(r"(?=^  - curve:\n)", body, flags=re.M)


def path_of(block):
    found = re.search(r"(?m)^    path: (.+)$", block)
    return found.group(1) if found else None


def q_of(value):
    nums = [float(v) for v in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", value)]
    return Quaternion((nums[3], nums[0], nums[1], nums[2]))


def fmtq(q):
    return "{x: %.9g, y: %.9g, z: %.9g, w: %.9g}" % (q.x, q.y, q.z, q.w)


key_re = re.compile(
    r"(?m)(^      - serializedVersion: 3\n        time: ([^\n]+)\n"
    r"        value: )([^\n]+)(\n        inSlope: )([^\n]+)"
    r"(\n        outSlope: )([^\n]+)"
)

idle_q = {}
for block in rot_blocks(idle):
    path = path_of(block)
    match = key_re.search(block)
    if path and match:
        idle_q[path] = q_of(match.group(3))

adjusted = {}
rot_body, a, b = section(src, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
new_blocks = []
for block in re.split(r"(?=^  - curve:\n)", rot_body, flags=re.M):
    path = path_of(block)
    if not path or path not in idle_q:
        new_blocks.append(block)
        continue
    matches = list(key_re.finditer(block))
    if not matches:
        new_blocks.append(block)
        continue
    original0 = q_of(matches[0].group(3))
    target0 = idle_q[path]
    offset = target0 @ original0.inverted()
    finger = re.search(r"/Right(?:Thumb|Index|Middle|Ring|Little)", path) is not None
    if offset.rotation_difference(Quaternion()).angle < 1e-4 and not finger:
        new_blocks.append(block)
        continue
    samples = []
    for match in matches:
        t = float(match.group(2))
        q = target0 if finger else offset @ q_of(match.group(3))
        # The cast ends on the wand idle grip to avoid a transition pop.
        if t > .57:
            u = min(1.0, max(0.0, (t - .57) / .08))
            u = u * u * (3 - 2 * u)
            q = q.slerp(target0, u)
        q.normalize()
        samples.append((t, q))
    adjusted[path] = samples
    def replace_key(match):
        nonlocal_index = replace_key.index
        replace_key.index += 1
        q = samples[nonlocal_index][1]
        if finger:
            slope = fmtq(Quaternion((0, 0, 0, 0)))
        else:
            before = samples[max(0, nonlocal_index - 1)]
            after = samples[min(len(samples) - 1, nonlocal_index + 1)]
            dt = after[0] - before[0]
            slope = fmtq(Quaternion(tuple((after[1][j] - before[1][j]) / dt if dt else 0
                                          for j in range(4))))
        return match.group(1) + fmtq(q) + match.group(4) + slope + match.group(6) + slope
    replace_key.index = 0
    new_blocks.append(key_re.sub(replace_key, block))
src = src[:a] + "".join(new_blocks) + src[b:]


def interpolate(samples, t):
    if t <= samples[0][0]:
        return samples[0][1].copy()
    for i in range(1, len(samples)):
        if t <= samples[i][0] + 1e-6:
            lo, hi = samples[i - 1], samples[i]
            u = (t - lo[0]) / max(1e-9, hi[0] - lo[0])
            return lo[1].slerp(hi[1], min(1, max(0, u)))
    return samples[-1][1].copy()


editor, a, b = section(src, "  m_EditorCurves:\n", "  m_HasGenericRootTransform:")
new_editor = []
for block in re.split(r"(?=^  - serializedVersion: 2\n)", editor, flags=re.M):
    path = path_of(block)
    attr_match = re.search(r"(?m)^    attribute: (.+)$", block)
    if not path or path not in adjusted or not attr_match:
        new_editor.append(block)
        continue
    attr = attr_match.group(1)
    if not (attr.startswith("m_LocalRotation.") or attr.startswith("localEulerAngles.")):
        new_editor.append(block)
        continue
    axis = "xyzw".index(attr[-1]) if attr.startswith("m_LocalRotation.") else "xyz".index(attr[-1])
    keys = list(key_re.finditer(block))
    times = [float(m.group(2)) for m in keys]
    quats = [interpolate(adjusted[path], t) for t in times]
    if attr.startswith("m_LocalRotation."):
        values = [q[axis] for q in quats]
    else:
        eulers = []
        for q in quats:
            eulers.append(q.to_euler("ZXY", eulers[-1]) if eulers else q.to_euler("ZXY"))
        values = [e[axis] * 180 / math.pi for e in eulers]
    def replace_editor(match):
        i = replace_editor.index
        replace_editor.index += 1
        before, after = max(0, i - 1), min(len(values) - 1, i + 1)
        dt = times[after] - times[before]
        slope = (values[after] - values[before]) / dt if dt else 0
        return (match.group(1) + "%.9g" % values[i] + match.group(4) +
                "%.9g" % slope + match.group(6) + "%.9g" % slope)
    replace_editor.index = 0
    new_editor.append(key_re.sub(replace_editor, block))
src = src[:a] + "".join(new_editor) + src[b:]

for filename, name in (("BasicWandAttack.anim", "BasicWandAttack"),
                       ("BasicWandAttack_DummyGrip.anim", "BasicWandAttack_DummyGrip")):
    result = src.replace("m_Name: WizardStaffCast", "m_Name: " + name, 1)
    (OUTPUT / filename).write_bytes(result.encode("utf-8"))
assert hashlib.sha256(SOURCE.read_bytes()).hexdigest() == SOURCE_SHA
print("RETARGETED_BONES", len(adjusted), "SOURCE_SHA256", SOURCE_SHA)
