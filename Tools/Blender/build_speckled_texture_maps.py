from pathlib import Path
import sys

import numpy as np
from PIL import Image, ImageFilter


def main(source_path: str, output_directory: str) -> None:
    source = Path(source_path)
    destination = Path(output_directory)
    destination.mkdir(parents=True, exist_ok=True)

    base = Image.open(source).convert("RGB").resize((1024, 1024), Image.Resampling.LANCZOS)
    base_path = destination / "Drone_PlasticSpeckled_BaseColor.png"
    base.save(base_path, optimize=True)

    luminance = np.asarray(base.convert("L"), dtype=np.float32) / 255.0
    blurred = np.asarray(base.convert("L").filter(ImageFilter.GaussianBlur(5.0)), dtype=np.float32) / 255.0
    detail = np.clip(luminance - blurred, -0.22, 0.22)

    roughness = np.clip(0.56 + detail * 0.42, 0.46, 0.68)
    roughness_image = Image.fromarray(np.uint8(roughness * 255.0), mode="L")
    roughness_image.save(destination / "Drone_PlasticSpeckled_Roughness.png", optimize=True)

    height = detail
    gradient_y, gradient_x = np.gradient(height)
    strength = 3.2
    normal_x = -gradient_x * strength
    normal_y = -gradient_y * strength
    normal_z = np.ones_like(height)
    length = np.sqrt(normal_x * normal_x + normal_y * normal_y + normal_z * normal_z)
    normal = np.stack(
        (
            normal_x / length * 0.5 + 0.5,
            normal_y / length * 0.5 + 0.5,
            normal_z / length * 0.5 + 0.5,
        ),
        axis=-1,
    )
    Image.fromarray(np.uint8(np.clip(normal, 0.0, 1.0) * 255.0), mode="RGB").save(
        destination / "Drone_PlasticSpeckled_Normal.png",
        optimize=True,
    )

    print(base_path)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: build_speckled_texture_maps.py SOURCE_IMAGE OUTPUT_DIRECTORY")
    main(sys.argv[1], sys.argv[2])
