"""Relax the empty left forearm and keep its wrist neutral during the cast."""
import math
import re
from pathlib import Path
from mathutils import Quaternion

root = Path(__file__).resolve().parents[1]
folder = root / "Assets/Game/Weapons/BasicWand/Art/Animations"
idle_file = folder / "WizardCombatIdle_BasicWandGrip.anim"
hand_path = "WizardModel/Armature/Hips/Spine02/Spine01/Spine/LeftShoulder/LeftArm/LeftForeArm/LeftHand"
forearm_path = hand_path.rsplit("/", 1)[0]
FOREARM_WEIGHT = .4
key_re = re.compile(r"(?m)(^      - serializedVersion: 3\n        time: ([^\n]+)\n"
                    r"        value: )([^\n]+)(\n        inSlope: )([^\n]+)"
                    r"(\n        outSlope: )([^\n]+)")


def sections(text, begin, end, marker):
    a = text.index(begin) + len(begin)
    b = text.index(end, a)
    return re.split(r"(?=^" + re.escape(marker) + r")", text[a:b], flags=re.M), a, b


def path(block):
    match = re.search(r"(?m)^    path: (.+)$", block)
    return match.group(1) if match else None


def numbers(raw):
    return [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", raw)]


def quaternion(raw):
    v = numbers(raw)
    return Quaternion((v[3], v[0], v[1], v[2]))


def format_quaternion(q):
    return "{x: %.9g, y: %.9g, z: %.9g, w: %.9g}" % (q.x, q.y, q.z, q.w)


def sample(values, time):
    if time <= values[0][0]: return values[0][1]
    for a, b in zip(values, values[1:]):
        if time <= b[0] + 1e-6:
            u = max(0, min(1, (time - a[0]) / max(1e-9, b[0] - a[0])))
            return a[1].slerp(b[1], u)
    return values[-1][1]


idle = idle_file.read_text(encoding="utf-8")
blocks, _, _ = sections(idle, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:", "  - curve:\n")
idle_rotations = {}
for block in blocks:
    if path(block) in (hand_path, forearm_path):
        idle_rotations[path(block)] = quaternion(key_re.search(block).group(3))
idle_hand = idle_rotations[hand_path]
idle_forearm = idle_rotations[forearm_path]
idle_values = {axis: idle_hand["wxyz".index(axis)] for axis in "xyzw"}
idle_euler = idle_hand.to_euler("ZXY")
euler_values = dict(zip("xyz", [math.degrees(v) for v in idle_euler]))
zero_quat = "{x: 0, y: 0, z: 0, w: 0}"
neutral_quat = "{x: %.9g, y: %.9g, z: %.9g, w: %.9g}" % (
    idle_hand.x, idle_hand.y, idle_hand.z, idle_hand.w)
report = []

for filename in ("BasicWandAttack.anim", "BasicWandAttack_DummyGrip.anim"):
    file = folder / filename
    text = file.read_text(encoding="utf-8")
    curve_blocks, a, b = sections(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:", "  - curve:\n")
    maximum_hand = 0
    maximum_forearm = 0
    forearm_samples = []
    for i, block in enumerate(curve_blocks):
        bone = path(block)
        if bone == hand_path:
            keys = list(key_re.finditer(block))
            maximum_hand = max(math.degrees(quaternion(m.group(3)).rotation_difference(idle_hand).angle)
                               for m in keys)
            curve_blocks[i] = key_re.sub(lambda m: m.group(1) + neutral_quat + m.group(4) +
                                        zero_quat + m.group(6) + zero_quat, block)
        elif bone == forearm_path:
            keys = list(key_re.finditer(block))
            for key in keys:
                original = quaternion(key.group(3))
                maximum_forearm = max(maximum_forearm,
                                      math.degrees(original.rotation_difference(idle_forearm).angle))
                q = idle_forearm.slerp(original, FOREARM_WEIGHT)
                if forearm_samples and q.dot(forearm_samples[-1][1]) < 0: q.negate()
                forearm_samples.append((float(key.group(2)), q))
            def replace_forearm(key):
                index = replace_forearm.index
                replace_forearm.index += 1
                before = forearm_samples[max(0, index - 1)]
                after = forearm_samples[min(len(forearm_samples) - 1, index + 1)]
                dt = after[0] - before[0]
                slope = Quaternion(tuple((after[1][j] - before[1][j]) / dt if dt else 0
                                         for j in range(4)))
                return (key.group(1) + format_quaternion(forearm_samples[index][1]) +
                        key.group(4) + format_quaternion(slope) +
                        key.group(6) + format_quaternion(slope))
            replace_forearm.index = 0
            curve_blocks[i] = key_re.sub(replace_forearm, block)
    assert maximum_forearm > 40 and forearm_samples, (filename, "Expected Lightning forearm motion")
    text = text[:a] + "".join(curve_blocks) + text[b:]
    editor_blocks, a, b = sections(text, "  m_EditorCurves:\n", "  m_HasGenericRootTransform:",
                                   "  - serializedVersion: 2\n")
    for i, block in enumerate(editor_blocks):
        bone = path(block)
        if bone not in (hand_path, forearm_path): continue
        attribute = re.search(r"(?m)^    attribute: (.+)$", block)
        if not attribute: continue
        name = attribute.group(1)
        if bone == hand_path:
            if name.startswith("m_LocalRotation."):
                value = idle_values[name[-1]]
            elif name.startswith("localEulerAngles."):
                value = euler_values[name[-1]]
            else:
                continue
            editor_blocks[i] = key_re.sub(lambda m: m.group(1) + "%.9g" % value + m.group(4) +
                                          "0" + m.group(6) + "0", block)
        else:
            if not (name.startswith("m_LocalRotation.") or name.startswith("localEulerAngles.")):
                continue
            keys = list(key_re.finditer(block))
            times = [float(key.group(2)) for key in keys]
            quats = [sample(forearm_samples, time) for time in times]
            if name.startswith("m_LocalRotation."):
                axis = "xyzw".index(name[-1])
                values = [q["wxyz".index(name[-1])] for q in quats]
            else:
                axis = "xyz".index(name[-1])
                eulers = []
                for q in quats:
                    eulers.append(q.to_euler("ZXY", eulers[-1]) if eulers else q.to_euler("ZXY"))
                values = [math.degrees(e[axis]) for e in eulers]
            def replace_editor(key):
                index = replace_editor.index
                replace_editor.index += 1
                lo, hi = max(0, index - 1), min(len(values) - 1, index + 1)
                dt = times[hi] - times[lo]
                slope = (values[hi] - values[lo]) / dt if dt else 0
                return (key.group(1) + "%.9g" % values[index] + key.group(4) +
                        "%.9g" % slope + key.group(6) + "%.9g" % slope)
            replace_editor.index = 0
            editor_blocks[i] = key_re.sub(replace_editor, block)
    text = text[:a] + "".join(editor_blocks) + text[b:]
    file.write_text(text, encoding="utf-8", newline="\n")
    new_forearm_max = max(math.degrees(q.rotation_difference(idle_forearm).angle)
                          for _, q in forearm_samples)
    line = (f"{filename}: empty-hand LeftForeArm motion reduced {maximum_forearm:.2f} -> "
            f"{new_forearm_max:.2f} deg; LeftHand local rotation held at idle "
            f"(source deviation {maximum_hand:.2f} deg); upper arm/body/legs/weapon unchanged.")
    report.append(line)
    print(line)

report_file = root / "Logs/BasicWand/offhand-wrist-validation.txt"  # git-ignored
report_file.parent.mkdir(parents=True, exist_ok=True)
report_file.write_text("\n".join(report) + "\n", encoding="utf-8")
