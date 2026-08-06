from pathlib import Path
import sys

import numpy as np
from PIL import Image


MATERIAL_COLORS = {
    "Armor_Highlight": (0.58, 0.46, 0.33),
    "Armor_Sand": (0.39, 0.30, 0.21),
    "Chassis_Blue_Shadow": (0.13, 0.29, 0.33),
    "Chassis_Pale_Blue": (0.29, 0.57, 0.63),
    "Rotor_Graphite": (0.035, 0.045, 0.05),
    "Soft_White": (0.72, 0.85, 0.87),
}


def linear_to_srgb(value):
    return np.where(
        value <= 0.0031308,
        value * 12.92,
        1.055 * np.power(value, 1.0 / 2.4) - 0.055,
    )


def main(source_path: str, output_path: str, material_directory: str | None = None) -> None:
    source = Image.open(source_path).convert("L")
    luminance = np.asarray(source, dtype=np.float32) / 255.0
    low = float(np.percentile(luminance, 2.0))
    high = float(np.percentile(luminance, 99.0))
    normalized = np.clip((luminance - low) / max(high - low, 1e-5), 0.0, 1.0)
    normalized = np.power(normalized, 0.82)
    output = Image.fromarray(np.uint8(normalized * 255.0), mode="L")
    destination = Path(output_path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    output.save(destination, optimize=True)

    if material_directory is not None:
        material_destination = Path(material_directory)
        material_destination.mkdir(parents=True, exist_ok=True)
        variation = 0.65 + normalized[..., None] * 0.35
        for material_name, linear_color in MATERIAL_COLORS.items():
            base = np.asarray(linear_color, dtype=np.float32)[None, None, :]
            textured_linear = np.clip(base * variation, 0.0, 1.0)
            textured_srgb = linear_to_srgb(textured_linear)
            texture = Image.fromarray(np.uint8(textured_srgb * 255.0), mode="RGB")
            texture.save(
                material_destination / f"Drone_Plastic006_{material_name}_BaseColor.jpg",
                quality=92,
                optimize=True,
            )
    print(destination)


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4):
        raise SystemExit(
            "Usage: build_plastic006_pattern_mask.py SOURCE_IMAGE OUTPUT_MASK [MATERIAL_DIRECTORY]"
        )
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) == 4 else None)
