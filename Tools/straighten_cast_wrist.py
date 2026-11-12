"""Straighten the right wrist in the shared staff cast without moving the weapon.

Run with Blender's Python. The two-bone arm keeps the hand at the original
position; a counter-rotation on WeaponSocket keeps the staff's world pose.
"""
import math
import re
from pathlib import Path

from mathutils import Quaternion, Vector

ROOT = Path(__file__).resolve().parents[1]
FILE = ROOT / "Assets/Weapons/Common/BaseCast.anim"
PATHS = [
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
ARM, FOREARM, HAND = PATHS[-3:]
SOCKET = HAND + "/WeaponSocket"
KEY = re.compile(r"(?m)(^      - serializedVersion: 3\n        time: ([^\n]+)\n"
                 r"        value: )([^\n]+)(\n        inSlope: )([^\n]+)"
                 r"(\n        outSlope: )([^\n]+)")


def numbers(raw):
    return [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", raw)]


def quat(raw):
    x, y, z, w = numbers(raw)
    return Quaternion((w, x, y, z))


def vec(raw):
    return Vector(numbers(raw)[:3])


def fmtq(q):
    return "{x: %.9g, y: %.9g, z: %.9g, w: %.9g}" % (q.x, q.y, q.z, q.w)


def section(text, first, last):
    a = text.index(first) + len(first)
    b = text.index(last, a)
    return text[a:b], a, b


def path_of(block):
    match = re.search(r"(?m)^    path: (.+)$", block)
    return match.group(1) if match else None


def blocks(text, first, last, prefix):
    body, a, b = section(text, first, last)
    return re.split(r"(?=^" + re.escape(prefix) + r")", body, flags=re.M), a, b


def curves(text, first, last):
    pieces, _, _ = blocks(text, first, last, "  - curve:\n")
    return {path_of(block): [(float(m.group(2)), m.group(3)) for m in KEY.finditer(block)]
            for block in pieces if path_of(block)}


def sample(keys, time, parser):
    if time <= keys[0][0]:
        return parser(keys[0][1])
    for (t0, a), (t1, b) in zip(keys, keys[1:]):
        if time <= t1 + 1e-6:
            u = min(1, max(0, (time - t0) / max(1e-9, t1 - t0)))
            a, b = parser(a), parser(b)
            return a.slerp(b, u) if isinstance(a, Quaternion) else a.lerp(b, u)
    return parser(keys[-1][1])


def smooth(a, b, t):
    u = min(1, max(0, (t - a) / (b - a)))
    return u * u * (3 - 2 * u)


def joints_at(time, rotations, positions):
    joints = {}
    world_q, world_p = Quaternion(), Vector((0, 0, 0))
    for path in PATHS:
        local_q = sample(rotations[path], time, quat)
        world_p = world_p + world_q @ sample(positions[path], time, vec)
        world_q = world_q @ local_q
        joints[path] = (world_p.copy(), world_q.copy(), local_q)
    return joints


def elbow_circle(joints):
    shoulder, elbow, hand = (joints[p][0] for p in (ARM, FOREARM, HAND))
    upper, lower = (elbow - shoulder).length, (hand - elbow).length
    reach = hand - shoulder
    along = (upper * upper - lower * lower + reach.length_squared) / (2 * reach.length)
    center = shoulder + reach.normalized() * along
    radius = math.sqrt(max(0, upper * upper - along * along))
    basis = (elbow - center).normalized()
    other = reach.normalized().cross(basis)
    return shoulder, elbow, hand, center, radius, basis, other


def release_elbow_reference(rotations, positions):
    joints = joints_at(.2, rotations, positions)
    shoulder, elbow, hand, center, radius, basis, other = elbow_circle(joints)
    hand_up = joints[HAND][1] @ Vector((0, 1, 0))
    best = None
    for degree in range(-180, 181, 2):
        radians = math.radians(degree)
        candidate = center + radius * (math.cos(radians) * basis + math.sin(radians) * other)
        wrist = math.degrees(hand_up.angle((hand - candidate).normalized()))
        displacement = (candidate - elbow).length
        # Prefer the straightest feasible wrist while limiting sleeve movement.
        score = wrist + 18 * displacement
        if best is None or score < best[0]:
            best = score, degree

    radians = math.radians(max(-140, min(140, best[1])))
    return center + radius * (math.cos(radians) * basis + math.sin(radians) * other) - shoulder


def solve(time, rotations, positions, release_reference):
    joints = joints_at(time, rotations, positions)
    shoulder, elbow, hand, center, radius, basis, other = elbow_circle(joints)
    hand_up = joints[HAND][1] @ Vector((0, 1, 0))
    desired = shoulder + release_reference
    plane = desired - center
    plane -= (hand - shoulder).normalized() * plane.dot((hand - shoulder).normalized())
    angle = math.atan2(plane.dot(other), plane.dot(basis))
    weight = min(1, max(0, (time - .035) / .165)) * (1 - smooth(.29, .50, time))
    radians = angle * weight
    elbow_target = center + radius * (math.cos(radians) * basis + math.sin(radians) * other)
    old_hand_world = joints[HAND][1]
    shoulder_world = joints[PATHS[-4]][1]
    arm_world = ((elbow - shoulder).rotation_difference(elbow_target - shoulder)
                 @ joints[ARM][1])
    rotated_forearm = arm_world @ joints[FOREARM][2]
    rotated_lower = rotated_forearm @ sample(positions[HAND], time, vec)
    forearm_world = (rotated_lower.rotation_difference(hand - elbow_target)
                     @ rotated_forearm)
    # A 45 degree palm correction follows the elbow adjustment. The socket
    # counter-rotates so the held weapon keeps its original world orientation.
    target_up = (hand - elbow_target).normalized()
    correction = hand_up.rotation_difference(target_up)
    correction = Quaternion().slerp(correction, min(1, math.radians(45) * weight / max(1e-8, correction.angle)))
    new_hand_world = correction @ old_hand_world
    result = {
        ARM: shoulder_world.inverted() @ arm_world,
        FOREARM: arm_world.inverted() @ forearm_world,
        HAND: forearm_world.inverted() @ new_hand_world,
        SOCKET: new_hand_world.inverted() @ old_hand_world,
    }
    wrist = math.degrees((new_hand_world @ Vector((0, 1, 0))).angle(target_up))
    # The IK keeps both bone lengths and the hand position fixed.
    rebuilt_elbow = shoulder + arm_world @ sample(positions[FOREARM], time, vec)
    rebuilt_hand = rebuilt_elbow + forearm_world @ sample(positions[HAND], time, vec)
    if (rebuilt_hand - hand).length > 1e-4:
        raise AssertionError((time, "hand moved", (rebuilt_hand - hand).length))
    if math.degrees((new_hand_world @ result[SOCKET]).rotation_difference(old_hand_world).angle) > .01:
        raise AssertionError((time, "weapon rotated"))
    return result, wrist


def rewrite_keys(block, values):
    def replace(match):
        i = replace.index
        replace.index += 1
        before, after = values[max(0, i - 1)], values[min(len(values) - 1, i + 1)]
        dt = after[0] - before[0]
        slope = Quaternion(tuple((after[1][j] - before[1][j]) / dt if dt else 0 for j in range(4)))
        return (match.group(1) + fmtq(values[i][1]) + match.group(4) + fmtq(slope) +
                match.group(6) + fmtq(slope))
    replace.index = 0
    result = KEY.sub(replace, block)
    if replace.index != len(values):
        raise AssertionError(("wrong key count", replace.index, len(values)))
    return result


def rewrite_editor_block(block, values):
    attr = re.search(r"(?m)^    attribute: (.+)$", block).group(1)
    keys = list(KEY.finditer(block))
    times = [float(m.group(2)) for m in keys]
    quats = [sample(values, t, lambda q: q) for t in times]
    if attr.startswith("m_LocalRotation."):
        axis = "xyzw".index(attr[-1])
        scalars = [q[axis] for q in quats]
    elif attr.startswith("localEulerAngles."):
        axis = "xyz".index(attr[-1])
        eulers = []
        for q in quats:
            eulers.append(q.to_euler("ZXY", eulers[-1]) if eulers else q.to_euler("ZXY"))
        scalars = [math.degrees(e[axis]) for e in eulers]
    else:
        return block

    def replace(match):
        i = replace.index
        replace.index += 1
        lo, hi = max(0, i - 1), min(len(keys) - 1, i + 1)
        dt = times[hi] - times[lo]
        slope = (scalars[hi] - scalars[lo]) / dt if dt else 0
        return (match.group(1) + "%.9g" % scalars[i] + match.group(4) +
                "%.9g" % slope + match.group(6) + "%.9g" % slope)
    replace.index = 0
    return KEY.sub(replace, block)


def main():
    text = FILE.read_text(encoding="utf-8")
    rotations = curves(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:")
    positions = curves(text, "  m_PositionCurves:\n", "  m_ScaleCurves:")
    if SOCKET in rotations:
        raise RuntimeError("WeaponSocket already has cast animation curves; refusing a second pass")
    times = [t for t, _ in rotations[HAND]]
    release_reference = release_elbow_reference(rotations, positions)
    samples = {p: [] for p in (ARM, FOREARM, HAND, SOCKET)}
    wrist_at_release = None
    for time in times:
        pose, wrist = solve(time, rotations, positions, release_reference)
        if abs(time - .2) < 1e-4:
            wrist_at_release = wrist
        for path, q in pose.items():
            if samples[path] and samples[path][-1][1].dot(q) < 0:
                q.negate()
            samples[path].append((time, q))
    if wrist_at_release is None or wrist_at_release > 25:
        raise AssertionError(("release wrist", wrist_at_release))

    parts, a, b = blocks(text, "  m_RotationCurves:\n", "  m_CompressedRotationCurves:", "  - curve:\n")
    hand_template = next(part for part in parts if path_of(part) == HAND)
    changed = [rewrite_keys(part, samples[path_of(part)]) if path_of(part) in (ARM, FOREARM, HAND)
               else part for part in parts]
    socket_block = re.sub(r"(?m)^    path: .+$", "    path: " + SOCKET,
                          rewrite_keys(hand_template, samples[SOCKET]), count=1)
    text = text[:a] + "".join(changed) + socket_block + text[b:]

    parts, a, b = blocks(text, "  m_EditorCurves:\n", "  m_HasGenericRootTransform:",
                         "  - serializedVersion: 2\n")
    changed, socket_blocks = [], []
    for part in parts:
        path = path_of(part)
        attr = re.search(r"(?m)^    attribute: (.+)$", part)
        if path in (ARM, FOREARM, HAND) and attr:
            changed.append(rewrite_editor_block(part, samples[path]))
            if path == HAND and (attr.group(1).startswith("m_LocalRotation.") or
                                 attr.group(1).startswith("localEulerAngles.")):
                copy = re.sub(r"(?m)^    path: .+$", "    path: " + SOCKET, part, count=1)
                socket_blocks.append(rewrite_editor_block(copy, samples[SOCKET]))
        else:
            changed.append(part)
    if len(socket_blocks) != 7:
        raise AssertionError(("socket editor curves", len(socket_blocks)))
    text = text[:a] + "".join(changed) + "".join(socket_blocks) + text[b:]
    FILE.write_text(text, encoding="utf-8", newline="\n")
    print(f"release wrist {wrist_at_release:.2f} degrees; hand and staff world poses preserved")


if __name__ == "__main__":
    main()
