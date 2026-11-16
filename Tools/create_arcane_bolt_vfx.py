"""Create isolated Basic Wand VFX assets from Eric VFX Studio's FX_Fireball.

Run with Blender's bundled Python (no Blender scene is opened or saved):
  blender -b --python Tools/create_arcane_bolt_vfx.py
"""

from __future__ import annotations

import hashlib
import re
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/ThirdParty/EricVFXStudio"
TARGET = ROOT / "Assets/Game/Weapons/BasicWand/Art/VFX"
PREFAB_SOURCE = SOURCE / "Free Game VFX/Prefab/FX_Fireball.prefab"
MATERIAL_SOURCE = SOURCE / "Resource/Materials"
SHADER_SOURCE = SOURCE / "Resource/Shader"
NAMESPACE = uuid.UUID("8eae99d7-73bf-4f63-9eaa-48b54aa0ab72")

MATERIALS = {
    "decal_fire10": ("ArcaneCrackLightning", (0.72, 0.60, 1.0, 0.90)),
    "Aura_Flame_001": ("ArcaneFlow", (0.72, 0.60, 1.0, 0.36)),
    "particle_sprite_droplet": ("ArcaneSparks", (0.72, 0.60, 1.0, 0.70)),
    "firerock 2": ("ArcaneCrackedCore", (0.97, 0.96, 1.0, 0.95)),
    "Gradient_Beam_007": ("ArcaneBeam", (0.72, 0.60, 1.0, 0.45)),
    "ring_03a": ("ArcaneRim", (0.72, 0.60, 1.0, 0.55)),
    "firerock 3": ("ArcaneInnerShell", (0.62, 0.47, 0.88, 0.67)),
    "glow_ball2_grey_3 1": ("ArcaneWhiteCore", (0.97, 0.96, 1.0, 0.44)),
}

SHADERS = {
    "AdditiveFlow": ("ArcaneAdditiveFlow", "BasicWand/ArcaneAdditiveFlow"),
    "AlphaBlendFlow": ("ArcaneAlphaBlendFlow", "BasicWand/ArcaneAlphaBlendFlow"),
}


def guid_for(path: Path) -> str:
    return uuid.uuid5(NAMESPACE, path.relative_to(ROOT).as_posix()).hex


def meta_guid(path: Path) -> str:
    match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", path.with_name(path.name + ".meta").read_text(encoding="utf-8"))
    assert match, path
    return match.group(1)


def write_asset(source: Path, target: Path, body: str) -> tuple[str, str]:
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(body, encoding="utf-8", newline="\n")
    old_guid, new_guid = meta_guid(source), guid_for(target)
    meta = source.with_name(source.name + ".meta").read_text(encoding="utf-8")
    assert meta.count(old_guid) == 1, source
    target.with_name(target.name + ".meta").write_text(meta.replace(old_guid, new_guid), encoding="utf-8", newline="\n")
    return old_guid, new_guid


def write_folder_meta(folder: Path) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    meta = folder.with_name(folder.name + ".meta")
    if not meta.exists():
        meta.write_text(
            "fileFormatVersion: 2\n"
            f"guid: {guid_for(folder)}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n",
            encoding="utf-8", newline="\n")


PALETTE_FUNCTION = """
            // Recolor only warm source pixels. Texture alpha, moving UVs,
            // cracks, dark rock detail, and noise remain exactly as authored.
            float3 ArcanePalette(float3 sourceRGB) {
                float peak = max(sourceRGB.r, max(sourceRGB.g, sourceRGB.b));
                float warm = smoothstep(0.035, 0.18, (sourceRGB.r - sourceRGB.b) / max(peak, 0.001));
                warm *= smoothstep(0.45, 0.75, sourceRGB.r / max(peak, 0.001));
                float3 violet = lerp(float3(0.18, 0.10, 0.34), float3(0.718, 0.604, 1.0), smoothstep(0.04, 0.70, peak));
                violet = lerp(violet, float3(0.973, 0.961, 1.0), smoothstep(0.70, 1.0, peak));
                return lerp(sourceRGB, violet, warm);
            }
"""


def create_shaders() -> dict[str, str]:
    replacements = {}
    for source_name, (target_name, shader_name) in SHADERS.items():
        src = SHADER_SOURCE / f"{source_name}.shader"
        dst = TARGET / "Shaders" / f"{target_name}.shader"
        data = src.read_text(encoding="utf-8-sig")
        data = re.sub(r'^Shader "[^"]+"', f'Shader "{shader_name}"', data, count=1)
        assert data.count("float4 frag(Varyings input) : SV_Target") == 1, src
        data = data.replace("float4 frag(Varyings input) : SV_Target", PALETTE_FUNCTION + "\n            float4 frag(Varyings input) : SV_Target", 1)
        needle = "float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv * _MainTex_ST.xy + _MainTex_ST.zw + _FlowSpeed.xy * _Time.y + uvOff);"
        assert data.count(needle) == 1, src
        data = data.replace(needle, needle + "\n                tex.rgb = ArcanePalette(tex.rgb);", 1)
        old, new = write_asset(src, dst, data)
        replacements[old] = new
    return replacements


def rgba(color: tuple[float, float, float, float]) -> str:
    r, g, b, a = color
    return f"{{r: {r:.6f}, g: {g:.6f}, b: {b:.6f}, a: {a:.6f}}}"


def create_materials(shader_replacements: dict[str, str]) -> dict[str, str]:
    replacements = {}
    for source_name, (target_name, tint) in MATERIALS.items():
        src = MATERIAL_SOURCE / f"{source_name}.mat"
        dst = TARGET / "Materials" / f"{target_name}.mat"
        data = src.read_text(encoding="utf-8-sig")
        data = re.sub(r"(?m)^  m_Name: .+$", f"  m_Name: {target_name}", data, count=1)
        for old, new in shader_replacements.items():
            data = data.replace(f"guid: {old}", f"guid: {new}")
        assert any(f"guid: {new}" in data for new in shader_replacements.values()), src
        data, n = re.subn(r"(?m)^    - _TintColor: \{[^}]+\}$", f"    - _TintColor: {rgba(tint)}", data)
        assert n == 1, src
        data = re.sub(r"(?m)^    - _Color: \{[^}]+\}$", "    - _Color: {r: 1, g: 1, b: 1, a: 1}", data)
        data = re.sub(r"(?m)^    - _DissolveEdgeColor: \{[^}]+\}$", "    - _DissolveEdgeColor: {r: 0.475, g: 0.329, b: 0.788, a: 1}", data)
        data = re.sub(r"(?m)^(    - _Brightness: )([0-9.]+)$", lambda m: m.group(1) + str(min(float(m.group(2)), 1.5)), data)
        old, new = write_asset(src, dst, data)
        replacements[old] = new
    return replacements


COLOR = re.compile(r"\{r: ([0-9.eE+-]+), g: ([0-9.eE+-]+), b: ([0-9.eE+-]+), a: ([0-9.eE+-]+)\}")


def recolor_particle(match: re.Match) -> str:
    r, g, b, a = map(float, match.groups())
    peak = max(r, g, b)
    if peak < 0.001 or r - b < 0.08 or r < g * 0.8:
        return match.group(0)
    # Keep the existing alpha gradient; map emission warmth to the violet range.
    intensity = min(1.0, peak)
    dark = (0.475, 0.329, 0.788)
    outer = (0.718, 0.604, 1.0)
    blend = max(0.0, min(1.0, intensity))
    return rgba(tuple(d * (1 - blend) + o * blend for d, o in zip(dark, outer)) + (a,))


def edit_particle_block(chunk: str, name: str) -> str:
    if name in {"Particle_up", "Particle_up (1)"}:
        # Ember flecks are reduced and expire sooner, while their motion stays.
        rate = 18 if name == "Particle_up" else 12
        chunk, n = re.subn(
            r"(  EmissionModule:\n    enabled: 1\n    serializedVersion: 4\n    rateOverTime:\n      serializedVersion: 2\n      minMaxState: 0\n      scalar: )([0-9.]+)",
            lambda m: m.group(1) + str(rate), chunk, count=1)
        assert n == 1, name
        chunk, n = re.subn(
            r"(    startLifetime:\n      serializedVersion: 2\n      minMaxState: 3\n      scalar: )0\.7\n      minScalar: 0\.8000001",
            r"\g<1>0.48\n      minScalar: 0.55", chunk, count=1)
        assert n == 1, name
    if name == "ArcaneBoltVFX":
        chunk, n = re.subn(r"m_LocalScale: \{x: 1, y: 1, z: 1\}",
                           "m_LocalScale: {x: 0.7, y: 0.7, z: 0.7}", chunk, count=1)
        assert n == 1, name
    if name.startswith("aura2") or name == "half":
        # These mesh emitters form the long tail; retain their shape and flow.
        def shorten(m: re.Match) -> str:
            return "z: " + f"{float(m.group(1)) * 0.68:.6f}"
        chunk, n = re.subn(r"z: (-[0-9.]+)(?=\}\n  m_LocalScale:)", shorten, chunk, count=1)
        assert n == 1, name
    return chunk


def create_prefab(material_replacements: dict[str, str]) -> None:
    data = PREFAB_SOURCE.read_text(encoding="utf-8-sig")
    for old, new in material_replacements.items():
        count = data.count(f"guid: {old}")
        assert count, old
        data = data.replace(f"guid: {old}", f"guid: {new}")
    data, n = re.subn(r"(?m)^  m_Name: FX_Fireball$", "  m_Name: ArcaneBoltVFX", data)
    assert n == 1
    chunks = re.split(r"(?=^--- !u!1 &)", data, flags=re.M)
    names = []
    for i, chunk in enumerate(chunks):
        match = re.search(r"(?m)^  m_Name: (.+)$", chunk)
        if match:
            names.append(match.group(1))
            chunks[i] = edit_particle_block(chunk, match.group(1))
    assert len(names) == 11, names
    data = "".join(chunks)
    data = COLOR.sub(recolor_particle, data)
    dst = TARGET / "ArcaneBoltVFX.prefab"
    write_asset(PREFAB_SOURCE, dst, data)
    assert not any(f"guid: {old}" in data for old in material_replacements)


def main() -> None:
    assert PREFAB_SOURCE.exists(), PREFAB_SOURCE
    source_files = [PREFAB_SOURCE] + [MATERIAL_SOURCE / f"{name}.mat" for name in MATERIALS] + [SHADER_SOURCE / f"{name}.shader" for name in SHADERS]
    before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}
    for folder in [TARGET, TARGET / "Materials", TARGET / "Shaders"]:
        write_folder_meta(folder)
    shaders = create_shaders()
    materials = create_materials(shaders)
    create_prefab(materials)
    assert before == {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}
    print(f"Created {TARGET / 'ArcaneBoltVFX.prefab'} with {len(MATERIALS)} isolated materials and {len(SHADERS)} isolated URP shaders")
    print("Source files unchanged; original textures remain shared read-only and their RGB is recolored only in the isolated shaders.")


if __name__ == "__main__":
    main()
