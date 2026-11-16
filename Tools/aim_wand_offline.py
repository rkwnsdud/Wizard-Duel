"""Apply a short right-arm aiming correction to retargeted Unity wand clips."""
import math
import re
from pathlib import Path
from mathutils import Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
FOLDER = ROOT / "Assets/Game/Weapons/BasicWand/Art/Animations"
CHAIN = ["WizardModel/Armature", "WizardModel/Armature/Hips",
         "WizardModel/Armature/Hips/Spine02", "WizardModel/Armature/Hips/Spine02/Spine01",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine",
         "WizardModel/Armature/Hips/Spine02/Spine01/Spine/RightShoulder"]
ARM = CHAIN[-1] + "/RightArm"
FOREARM = ARM + "/RightForeArm"
HAND = FOREARM + "/RightHand"
TARGET_PATHS = (ARM, FOREARM, HAND)
KEY = re.compile(r"(?m)(^      - serializedVersion: 3\n        time: ([^\n]+)\n"
                 r"        value: )([^\n]+)(\n        inSlope: )([^\n]+)"
                 r"(\n        outSlope: )([^\n]+)")


def numbers(value):
    return [float(v) for v in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", value)]


def quat(raw):
    v = numbers(raw)
    return Quaternion((v[3], v[0], v[1], v[2]))


def vector(raw):
    return Vector(numbers(raw)[:3])


def fmtq(q):
    return "{x: %.9g, y: %.9g, z: %.9g, w: %.9g}" % (q.x, q.y, q.z, q.w)


def split_section(text, start, end):
    a = text.index(start) + len(start)
    b = text.index(end, a)
    return text[a:b], a, b


def blocks(text, start, end, prefix):
    section, a, b = split_section(text, start, end)
    return re.split(r"(?=^" + re.escape(prefix) + r")", section, flags=re.M), a, b


def name_of(block):
    p = re.search(r"(?m)^    path: (.+)$", block)
    return p.group(1) if p else None


def data_of(text, start, end):
    pieces, _, _ = blocks(text, start, end, "  - curve:\n")
    out = {}
    for piece in pieces:
        name = name_of(piece)
        if name:
            out[name] = [(float(m.group(2)), m.group(3)) for m in KEY.finditer(piece)]
    return out


def sample(values, t, parser):
    parsed = [(time, parser(raw)) for time, raw in values]
    if t <= parsed[0][0]: return parsed[0][1]
    for i in range(1, len(parsed)):
        if t <= parsed[i][0] + 1e-6:
            lo, hi = parsed[i - 1], parsed[i]
            u = max(0, min(1, (t - lo[0]) / max(1e-9, hi[0] - lo[0])))
            return lo[1].slerp(hi[1], u) if isinstance(lo[1], Quaternion) else lo[1].lerp(hi[1], u)
    return parsed[-1][1]


def smooth(a, b, t):
    u = max(0, min(1, (t - a) / (b - a)))
    return u * u * (3 - 2 * u)


def project(v, axis):
    return v - axis * v.dot(axis)


def pose(t, rotations, positions, wand_local, target_offset=None, pole=None):
    world_q, world_p = Quaternion(), Vector((0, 0, 0))
    for path in CHAIN:
        world_p += world_q @ sample(positions[path], t, vector)
        world_q = world_q @ sample(rotations[path], t, quat)
    shoulder_q, shoulder_p = world_q.copy(), world_p.copy()
    arm_local = sample(rotations[ARM], 0, quat)
    elbow_local = sample(rotations[FOREARM], 0, quat)
    hand_local = sample(rotations[HAND], 0, quat)
    arm_p = shoulder_p + shoulder_q @ sample(positions[ARM], t, vector)
    arm_q = shoulder_q @ arm_local
    elbow_offset = sample(positions[FOREARM], t, vector)
    hand_offset = sample(positions[HAND], t, vector)
    elbow_p = arm_p + arm_q @ elbow_offset
    elbow_q = arm_q @ elbow_local
    hand_p = elbow_p + elbow_q @ hand_offset
    # The idle grip already has a straight elbow and neutral wrist. Raise the
    # entire limb at the shoulder; never aim by twisting the wrist itself.
    aim_axis = Vector((0, 0, 1))
    arm_q = (hand_p - arm_p).rotation_difference(aim_axis) @ arm_q
    hand_q = arm_q @ elbow_local @ hand_local
    back = project(hand_q @ Vector((1, 0, 0)), aim_axis)
    outward = Vector((1, 0, 0))
    roll = math.atan2(aim_axis.dot(back.cross(outward)), back.dot(outward))
    arm_q = Quaternion(aim_axis, roll) @ arm_q
    arm_local_aim = shoulder_q.inverted() @ arm_q
    elbow_local_aim, hand_local_aim = elbow_local.copy(), hand_local.copy()
    wrist = math.degrees((hand_local @ Vector((0, 1, 0))).angle(hand_offset))
    deviation = sum(math.degrees(a.rotation_difference(b).angle)
                    for a, b in zip((arm_local, elbow_local, hand_local),
                                    (arm_local_aim, elbow_local_aim, hand_local_aim)))
    return ((arm_local, elbow_local, hand_local),
            (arm_local_aim, elbow_local_aim, hand_local_aim), wrist, deviation,
            math.degrees(roll), wrist)


def rewrite_rotation(text, new_samples):
    pieces, a, b = blocks(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:", "  - curve:\n")
    out = []
    for piece in pieces:
        path = name_of(piece)
        if path not in new_samples:
            out.append(piece)
            continue
        values = new_samples[path]
        def replace(m):
            i = replace.index
            replace.index += 1
            q = values[i][1]
            before, after = values[max(0, i - 1)], values[min(len(values) - 1, i + 1)]
            dt = after[0] - before[0]
            slope = Quaternion(tuple((after[1][j] - before[1][j]) / dt if dt else 0
                                     for j in range(4)))
            return m.group(1) + fmtq(q) + m.group(4) + fmtq(slope) + m.group(6) + fmtq(slope)
        replace.index = 0
        out.append(KEY.sub(replace, piece))
    return text[:a] + "".join(out) + text[b:]


def rewrite_editor(text, new_samples):
    pieces, a, b = blocks(text, "  m_EditorCurves:\n", "  m_HasGenericRootTransform:",
                           "  - serializedVersion: 2\n")
    out = []
    for piece in pieces:
        path = name_of(piece)
        attr_match = re.search(r"(?m)^    attribute: (.+)$", piece)
        if path not in new_samples or not attr_match:
            out.append(piece)
            continue
        attr = attr_match.group(1)
        if not (attr.startswith("m_LocalRotation.") or attr.startswith("localEulerAngles.")):
            out.append(piece)
            continue
        keys = list(KEY.finditer(piece))
        times = [float(m.group(2)) for m in keys]
        qs = [sample(new_samples[path], t, lambda x: x) for t in times]
        if attr.startswith("m_LocalRotation."):
            values = [getattr(q, attr[-1]) for q in qs]
        else:
            axis = "xyz".index(attr[-1])
            eulers = []
            for q in qs:
                eulers.append(q.to_euler("ZXY", eulers[-1]) if eulers else q.to_euler("ZXY"))
            values = [e[axis] * 180 / math.pi for e in eulers]
        def replace(m):
            i = replace.index
            replace.index += 1
            lo, hi = max(0, i - 1), min(len(values) - 1, i + 1)
            dt = times[hi] - times[lo]
            slope = (values[hi] - values[lo]) / dt if dt else 0
            return (m.group(1) + "%.9g" % values[i] + m.group(4) +
                    "%.9g" % slope + m.group(6) + "%.9g" % slope)
        replace.index = 0
        out.append(KEY.sub(replace, piece))
    return text[:a] + "".join(out) + text[b:]


for filename, local in (("BasicWandAttack.anim", (.115895, .861220, .226995, -.439708)),):
    file = FOLDER / filename
    text = file.read_text(encoding="utf-8")
    rotations = data_of(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
    positions = data_of(text, "  m_PositionCurves:\n", "  m_ScaleCurves:")
    wand = Quaternion((local[3], *local[:3]))
    target, pole = None, None
    _, _, release_wrist, release_deviation, release_roll, pre_roll_wrist = pose(.2, rotations, positions, wand, target, pole)
    print(filename, "selected target", target, "pole", pole,
          "release wrist", round(release_wrist, 2), "joint change", round(release_deviation, 1),
          "hand roll", round(release_roll, 1), "pre-roll wrist", round(pre_roll_wrist, 1))
    _, release_aim, _, _, _, _ = pose(.2, rotations, positions, wand, target, pole)
    times = [t for t, _ in rotations[ARM]]
    wand_path = HAND + "/WeaponSocket/BasicWand"
    # Fit the wand along the neutral hand for the extended cast. Its grip
    # origin stays inside the hand, and the normal idle attachment returns.
    cast_wand = (wand @ Vector((0, 0, 1))).rotation_difference(Vector((0, 1, 0))) @ wand
    if wand_path not in rotations:
        parts, a, b = blocks(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:", "  - curve:\n")
        template = next(part for part in parts if name_of(part) == HAND)
        text = text[:b] + template.replace("    path: " + HAND + "\n", "    path: " + wand_path + "\n") + text[b:]
        parts, a, b = blocks(text, "  m_EditorCurves:\n", "  m_HasGenericRootTransform:", "  - serializedVersion: 2\n")
        templates = [part.replace("    path: " + HAND + "\n", "    path: " + wand_path + "\n")
                     for part in parts if name_of(part) == HAND and
                     ("attribute: m_LocalRotation." in part or "attribute: localEulerAngles." in part)]
        text = text[:b] + "".join(templates) + text[b:]
    values = {path: [] for path in (*TARGET_PATHS, wand_path)}
    for t in times:
        source, aim, _, _, _, _ = pose(t, rotations, positions, wand, target, pole)
        weight = smooth(0, .18, t) * (1 - smooth(.3, .6, t))
        for path, a, b in zip(TARGET_PATHS, source, aim):
            q = a.slerp(b, weight)
            if values[path] and values[path][-1][1].dot(q) < 0: q.negate()
            values[path].append((t, q))
        values[wand_path].append((t, wand.slerp(cast_wand, weight)))
    text = rewrite_rotation(text, values)
    text = rewrite_editor(text, values)
    file.write_text(text, encoding="utf-8", newline="\n")
    print(filename, "aimed arm curves", len(times), "release weight", smooth(.11, .2, .2))
